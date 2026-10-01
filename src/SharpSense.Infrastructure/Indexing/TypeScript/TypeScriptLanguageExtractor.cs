using FluentResults;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Errors;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using TreeSitter;

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

        try
        {
            if (!fileSystem.Directory.Exists(context.TargetPath) && !fileSystem.File.Exists(context.TargetPath))
            {
                throw new FileNotFoundException(
                    $"Selected TypeScript source '{context.TargetPath}' does not exist.",
                    context.TargetPath);
            }

            context.Progress?.Report(new Application.Shared.Models.IndexingProgress(
                "Discovering TypeScript files...",
                0,
                1));
            var discoveredFiles = await sourceDiscoverer.Discover(context.TargetPath, ct);

            return Result.Ok(await ExecutePasses(context.Progress, discoveredFiles, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sourceDiscoverer.ClearCache();
            throw;
        }
        catch (Exception exception)
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
    }

    private async Task<ExtractedNodes> ExecutePasses(
        IProgress<Application.Shared.Models.IndexingProgress>? progress,
        IReadOnlyList<DiscoveredFile> discoveredFiles,
        CancellationToken ct)
    {
        if (discoveredFiles.Count == 0)
        {
            return new ExtractedNodes([], [], [], []);
        }

        using var typeScriptLanguage = new Language("TypeScript");
        using var tsxLanguage = new Language("TSX");
        using var typeScriptParser = new Parser(typeScriptLanguage);
        using var tsxParser = new Parser(tsxLanguage);
        var parsedFiles = new List<TypeScriptParsedFile>(discoveredFiles.Count);
        var orderedPasses = extractionPasses
            .OrderBy(GetPassOrder)
            .ToArray();
        var totalSteps = discoveredFiles.Count + orderedPasses.Length;
        var completedSteps = 0;

        try
        {
            foreach (var discoveredFile in discoveredFiles)
            {
                ct.ThrowIfCancellationRequested();

                string sourceText;
                try
                {
                    sourceText = await fileSystem.File.ReadAllTextAsync(discoveredFile.AbsolutePath, ct);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new TypeScriptSourceException(discoveredFile.RelativeFilePath, exception.Message, exception);
                }
                var parser = discoveredFile.RelativeFilePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                    ? tsxParser
                    : typeScriptParser;
                var syntaxTree = parser.Parse(sourceText)
                    ?? throw new InvalidOperationException($"Tree-sitter returned no syntax tree for '{discoveredFile.RelativeFilePath}'.");

                if (syntaxTree.RootNode.HasError)
                {
                    syntaxTree.Dispose();
                    throw new TypeScriptSourceException(
                        discoveredFile.RelativeFilePath,
                        $"TypeScript syntax errors in '{discoveredFile.RelativeFilePath}'; previous index preserved.");
                }

                parsedFiles.Add(new TypeScriptParsedFile(
                    discoveredFile,
                    sourceText,
                    syntaxTree));
                completedSteps++;
                progress?.Report(new Application.Shared.Models.IndexingProgress(
                    $"Parsing {discoveredFile.RelativeFilePath}...",
                    completedSteps,
                    totalSteps));
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

            return new ExtractedNodes(
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
                    .. discoveredFiles
                        .Select(static file => file.AbsolutePath)
                        .Concat(sourceDiscoverer.ResolutionInputPaths)
                        .Distinct(FileSystemPaths.Comparer)
                ],
                CanReuseForDocumentationChanges: true);
        }
        finally
        {
            foreach (var parsedFile in parsedFiles)
            {
                parsedFile.Dispose();
            }
        }
    }

    private sealed class TypeScriptSourceException(string filePath, string message, Exception? innerException = null)
        : Exception(message, innerException)
    {
        public string FilePath { get; } = filePath;
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
