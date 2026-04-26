using Microsoft.Extensions.Options;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;

namespace SharpSense.Application.Indexing.UpdateWorkspaceFiles;

public sealed class UpdateWorkspaceFilesCommandHandler(
    IEnumerable<ILanguageExtractor> extractors,
    IKnowledgeGraphRepository knowledgeGraphRepository,
    IIndexingWorkspacePaths workspacePaths,
    IOptions<SharpSenseCliOptions> cliOptions)
    : ICommandHandler<UpdateWorkspaceFilesCommand>
{
    private static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdown", ".mkd"];
    private readonly SharpSenseCliOptions _cliOptions = cliOptions?.Value ?? throw new ArgumentNullException(nameof(cliOptions));

    public async Task Handle(UpdateWorkspaceFilesCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.ChangedFiles);

        var absoluteTargetPath = workspacePaths.GetRequiredTargetPath(GetRequiredTargetPath());
        var changedFilePaths = GetAffectedRelativePaths(command.ChangedFiles);

        using var trace = SharpSenseTraceSpan.Start("index.target.incremental");
        trace.AddTag("target.path", absoluteTargetPath);
        trace.AddTag("repository.root", workspacePaths.RootPath);
        trace.AddTag("index.change.count", command.ChangedFiles.Count);

        try
        {
            if (changedFilePaths.Length == 0)
            {
                return;
            }

            using var extractActivity = SharpSenseTraceSpan.Start("index.extract.incremental");
            var extractedNodes = await Extract(
                new IncrementalExtractionContext(absoluteTargetPath, command.ChangedFiles, command.Progress),
                extractActivity,
                ct);

            extractedNodes = NormalizePersistedPaths(extractedNodes);
            command.Progress?.Report(new IndexingProgress("Persisting incremental index...", changedFilePaths.Length, changedFilePaths.Length));

            await knowledgeGraphRepository.ReplaceWorkspaceFiles(changedFilePaths, extractedNodes, ct);

            trace.AddTag("index.code_node.count", extractedNodes.CodeNodes.Count);
            trace.AddTag("index.dependency.count", extractedNodes.Edges.Count);
            trace.AddTag("index.diagnostic.count", extractedNodes.Diagnostics.Count);
        }
        catch (Exception exception)
        {
            trace.RecordExceptionAndErrorStatus(exception);
            throw;
        }
    }

    private async Task<ExtractedNodes> Extract(
        IncrementalExtractionContext context,
        SharpSenseTraceSpan extractActivity,
        CancellationToken ct)
    {
        var aggregatedProjects = new List<IndexedProject>();
        var aggregatedCodeNodes = new List<IndexedCodeNode>();
        var aggregatedEdges = new List<IndexedDependency>();
        var aggregatedDiagnostics = new List<string>();

        foreach (var extractor in extractors)
        {
            var extractedNodes = await extractor.ExtractIncremental(context, ct);

            aggregatedProjects.AddRange(extractedNodes.Projects);
            aggregatedCodeNodes.AddRange(extractedNodes.CodeNodes);
            aggregatedEdges.AddRange(extractedNodes.Edges);
            aggregatedDiagnostics.AddRange(extractedNodes.Diagnostics);

            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.project.count", extractedNodes.Projects.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.code_node.count", extractedNodes.CodeNodes.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.dependency.count", extractedNodes.Edges.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.diagnostic.count", extractedNodes.Diagnostics.Count);
        }

        return new ExtractedNodes(
            [
                .. aggregatedProjects
                    .OrderBy(static project => project.Name, StringComparer.Ordinal)
                    .ThenBy(static project => project.Id, StringComparer.Ordinal)
            ],
            [
                .. aggregatedCodeNodes
                    .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
                    .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            ],
            [
                .. aggregatedEdges
                    .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.EdgeType)
            ],
            aggregatedDiagnostics);
    }

    private ExtractedNodes NormalizePersistedPaths(ExtractedNodes extractedNodes)
    {
        return extractedNodes with
        {
            Projects =
            [
                .. extractedNodes.Projects.Select(
                    project => project with
                    {
                        RelativeFilePath = workspacePaths.ToRepositoryRelativePath(project.RelativeFilePath)
                    })
            ],
            CodeNodes =
            [
                .. extractedNodes.CodeNodes.Select(
                    codeNode => codeNode with
                    {
                        RelativeFilePath = workspacePaths.ToRepositoryRelativePath(codeNode.RelativeFilePath)
                    })
            ]
        };
    }

    private string[] GetAffectedRelativePaths(IReadOnlyList<WorkspaceFileChange> changedFiles)
    {
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var relativePaths = new HashSet<string>(pathComparer);

        foreach (var changedFile in changedFiles)
        {
            foreach (var affectedPath in changedFile.GetAffectedPaths())
            {
                if (!workspacePaths.TryToRepositoryRelativePath(affectedPath, out var relativePath) ||
                    !IsIncrementalTargetPath(relativePath))
                {
                    continue;
                }

                relativePaths.Add(relativePath);
            }
        }

        return [.. relativePaths.OrderBy(static path => path, pathComparer)];
    }

    private static bool IsIncrementalTargetPath(string path)
    {
        var extension = Path.GetExtension(path);

        return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
               MarkdownExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private string GetRequiredTargetPath()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(_cliOptions.TargetPath);
        return _cliOptions.TargetPath;
    }
}
