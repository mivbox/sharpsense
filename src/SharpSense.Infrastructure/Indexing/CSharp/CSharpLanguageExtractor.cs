using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing;

public sealed class CSharpLanguageExtractor(
    IRoslynSolutionAnalysisEngine analysisEngine,
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
}
