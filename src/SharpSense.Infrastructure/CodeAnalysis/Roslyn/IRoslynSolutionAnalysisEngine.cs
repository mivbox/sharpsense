using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

public interface IRoslynSolutionAnalysisEngine
{
    Task<KnowledgeGraphExtractionPayload> Extract(
        string solutionPath,
        IRepositoryWorkspace repositoryWorkspace,
        RoslynWorkspaceOptions? options = null,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default);
}
