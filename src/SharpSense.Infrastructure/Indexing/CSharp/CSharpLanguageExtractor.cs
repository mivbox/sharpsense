using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing.CSharp;

public sealed class CSharpLanguageExtractor(
    IRoslynTargetAnalysisEngine analysisEngine,
    IRepositoryWorkspace repositoryWorkspace)
    : ILanguageExtractor
{
    public string ExtractorName => "csharp";

    public async Task<ExtractedNodes> Extract(
        ExtractionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TargetPath);

        var extractionPayload = await analysisEngine.Extract(
            context.TargetPath,
            repositoryWorkspace,
            new RoslynWorkspaceOptions(),
            context.Progress,
            ct);

        return new ExtractedNodes(
            extractionPayload.Projects,
            extractionPayload.CodeNodes,
            extractionPayload.Edges,
            extractionPayload.Diagnostics);
    }

    public async Task<ExtractedNodes> ExtractIncremental(
        IncrementalExtractionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TargetPath);
        ArgumentNullException.ThrowIfNull(context.ChangedFiles);

        var cSharpChanges = context.ChangedFiles
            .Where(static changedFile => changedFile.GetAffectedPaths().Any(IsCSharpFilePath))
            .ToArray();
        if (cSharpChanges.Length == 0)
        {
            return new ExtractedNodes([], [], [], []);
        }

        var extractionPayload = await analysisEngine.ExtractIncremental(
            context.TargetPath,
            repositoryWorkspace,
            cSharpChanges,
            context.Progress,
            ct);

        return new ExtractedNodes(
            extractionPayload.Projects,
            extractionPayload.CodeNodes,
            extractionPayload.Edges,
            extractionPayload.Diagnostics);
    }

    private static bool IsCSharpFilePath(string path)
        => string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);
}
