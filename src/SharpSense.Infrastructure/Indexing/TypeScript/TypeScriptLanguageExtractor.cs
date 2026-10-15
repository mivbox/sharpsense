using FluentResults;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Errors;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.Text.Json;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TypeScriptLanguageExtractor(
    TypeScriptSourceDiscoverer sourceDiscoverer,
    IFileSystem fileSystem,
    IEnumerable<ITypeScriptExtractionPass> extractionPasses)
    : ILanguageExtractor
{
    public WorkspaceSourceKind SourceKind => WorkspaceSourceKind.TypeScript;

    public async Task<Result<ExtractedNodes>> Extract(
        ExtractionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TargetPath);
        ct.ThrowIfCancellationRequested();

        if (!fileSystem.Directory.Exists(context.TargetPath) && !fileSystem.File.Exists(context.TargetPath))
        {
            return Result.Fail(new ServiceError(
                ServiceErrorCode.ThirdPartyError,
                $"Selected TypeScript source '{context.TargetPath}' does not exist."));
        }

        context.Progress?.Report(new Application.Shared.Models.IndexingProgress(
            "Discovering TypeScript files...",
            0,
            1));

        IReadOnlyList<TypeScriptParsedFile> parsedFiles;
        try
        {
            parsedFiles = await sourceDiscoverer.Discover(context.TargetPath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sourceDiscoverer.ClearCache();
            throw;
        }
        catch (Exception exception) when (exception is TypeScriptSourceException or IOException
            or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            var error = new ServiceError(
                ServiceErrorCode.ThirdPartyError,
                $"TypeScript extraction failed for '{context.TargetPath}': {exception.Message}");
            if (exception is TypeScriptSourceException sourceException)
            {
                error.Metadata["filePath"] = sourceException.FilePath;
            }

            return Result.Fail(error);
        }

        return ExecutePasses(context.Progress, parsedFiles, ct);
    }

    private Result<ExtractedNodes> ExecutePasses(
        IProgress<Application.Shared.Models.IndexingProgress>? progress,
        IReadOnlyList<TypeScriptParsedFile> parsedFiles,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (parsedFiles.Count == 0)
        {
            return Result.Ok(new ExtractedNodes([], [], [], []));
        }

        var diagnostics = new TypeScriptParseDiagnostics();
        var orderedPasses = extractionPasses
            .OrderBy(GetPassOrder)
            .ToArray();
        var totalSteps = parsedFiles.Count + orderedPasses.Length;
        var completedSteps = 0;

        foreach (var parsedFile in parsedFiles)
        {
            ct.ThrowIfCancellationRequested();

            var discoveredFile = parsedFile.DiscoveredFile;
            var syntaxTree = parsedFile.SyntaxTree;
            if (syntaxTree.HasErrors)
            {
                diagnostics.Add(discoveredFile.RelativeFilePath, syntaxTree.Diagnostics[0]);
            }

            completedSteps++;
            progress?.Report(new Application.Shared.Models.IndexingProgress(
                $"Parsing {discoveredFile.RelativeFilePath}...",
                completedSteps,
                totalSteps));
        }

        ct.ThrowIfCancellationRequested();
        if (diagnostics.HasErrors)
        {
            return Result.Fail(diagnostics.GetErrors());
        }

        var passContext = new TypeScriptPassContext(parsedFiles, ct);
        foreach (var extractionPass in orderedPasses)
        {
            ct.ThrowIfCancellationRequested();
            extractionPass.Execute(passContext);
            completedSteps++;
            progress?.Report(new Application.Shared.Models.IndexingProgress(
                GetPassDescription(extractionPass),
                completedSteps,
                totalSteps));
        }

        return Result.Ok(new ExtractedNodes(
            [],
            [
                .. passContext.CodeNodes
                    .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
                    .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            ],
            [
                .. passContext.Edges
                    .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.EdgeType)
            ],
            [.. passContext.Diagnostics],
            [
                .. parsedFiles
                    .Select(static file => file.DiscoveredFile.AbsolutePath)
                    .Concat(sourceDiscoverer.ResolutionInputPaths)
                    .Distinct(FileSystemPaths.Comparer)
            ],
            CanReuseForDocumentationChanges: true));
    }

    private static int GetPassOrder(ITypeScriptExtractionPass extractionPass)
        => extractionPass switch
        {
            CodeNodeExtractionPass => 0,
            ImportDependencyPass => 1,
            HttpEdgeExtractionPass => 2,
            _ => 100
        };

    private static string GetPassDescription(ITypeScriptExtractionPass extractionPass)
        => extractionPass switch
        {
            CodeNodeExtractionPass => "Extracting TypeScript nodes...",
            ImportDependencyPass => "Resolving TypeScript imports...",
            HttpEdgeExtractionPass => "Extracting TypeScript HTTP edges...",
            _ => "Running TypeScript extraction pass..."
        };
}
