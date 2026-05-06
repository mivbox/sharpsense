using Microsoft.Extensions.Options;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Indexing.IndexTarget;

public sealed class IndexTargetCommandHandler(
    IEnumerable<ILanguageExtractor> extractors,
    IEmbeddingGenerator embeddingGenerator,
    IKnowledgeGraphRepository knowledgeGraphRepository,
    IIndexingWorkspacePaths workspacePaths,
    IOptions<SharpSenseCliOptions> cliOptions)
    : ICommandHandler<IndexTargetCommand>
{
    private readonly SharpSenseCliOptions _cliOptions = cliOptions?.Value ?? throw new ArgumentNullException(nameof(cliOptions));

    public async Task Handle(IndexTargetCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var absoluteTargetPath = workspacePaths.GetRequiredTargetPath(GetRequiredTargetPath());

        using var trace = SharpSenseTraceSpan.Start("index.target");
        trace.AddTag("target.path", absoluteTargetPath);
        trace.AddTag("repository.root", workspacePaths.RootPath);
        trace.AddTag("index.include_embeddings", !_cliOptions.SkipEmbeddings);

        try
        {
            using var extractActivity = SharpSenseTraceSpan.Start("index.extract");
            var extractedNodes = await Extract(
                new ExtractionContext(absoluteTargetPath, command.Progress),
                extractActivity,
                ct);
            var projectCount = extractedNodes.Projects.Count;
            var documentNodeCount = extractedNodes.CodeNodes.Count(static codeNode => codeNode.NodeType == NodeType.Document);

            extractActivity.AddTag("index.project.count", extractedNodes.Projects.Count);
            extractActivity.AddTag("index.code_node.count", extractedNodes.CodeNodes.Count);
            extractActivity.AddTag("index.document_node.count", documentNodeCount);
            extractActivity.AddTag("index.dependency.count", extractedNodes.Edges.Count);
            extractActivity.AddTag("index.diagnostic.count", extractedNodes.Diagnostics.Count);

            if (extractedNodes.CodeNodes.Count > 0)
            {
                if (!_cliOptions.SkipEmbeddings)
                {
                    command.Progress?.Report(new IndexingProgress("Embedding phase...", projectCount, projectCount));
                }

                var persistedCodeNodes = _cliOptions.DisableEmbeddingCache
                    ? []
                    : await knowledgeGraphRepository.GetPersistedCodeNodes(ct);
                extractedNodes = extractedNodes with
                {
                    CodeNodes = await CodeNodeEmbeddingCoordinator.Populate(
                        extractedNodes.CodeNodes,
                        persistedCodeNodes,
                        _cliOptions.SkipEmbeddings,
                        _cliOptions.DisableEmbeddingCache,
                        embeddingGenerator,
                        command.EmbeddingProgress,
                        ct)
                };
            }

            extractedNodes = NormalizePersistedPaths(extractedNodes);
            command.Progress?.Report(new IndexingProgress("Persisting index...", projectCount, projectCount));

            await knowledgeGraphRepository.ReplaceTarget(extractedNodes, ct);

            trace.AddTag("index.project.count", extractedNodes.Projects.Count);
            trace.AddTag("index.code_node.count", extractedNodes.CodeNodes.Count);
            trace.AddTag("index.document_node.count", documentNodeCount);
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
        ExtractionContext context,
        SharpSenseTraceSpan extractActivity,
        CancellationToken ct)
    {
        var aggregatedProjects = new List<IndexedProject>();
        var aggregatedCodeNodes = new List<IndexedCodeNode>();
        var aggregatedEdges = new List<IndexedDependency>();
        var aggregatedDiagnostics = new List<string>();

        foreach (var extractor in extractors)
        {
            var extractedNodes = await extractor.Extract(context, ct);

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

    private string GetRequiredTargetPath()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(_cliOptions.TargetPath);
        return _cliOptions.TargetPath;
    }
}
