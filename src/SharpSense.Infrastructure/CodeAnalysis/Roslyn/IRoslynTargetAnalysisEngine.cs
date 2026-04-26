using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

public interface IRoslynTargetAnalysisEngine
{
    Task<KnowledgeGraphExtractionPayload> Extract(
        string targetPath,
        IRepositoryWorkspace repositoryWorkspace,
        RoslynWorkspaceOptions? options = null,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default);

    Task<KnowledgeGraphExtractionPayload> ExtractIncremental(
        string targetPath,
        IRepositoryWorkspace repositoryWorkspace,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default);
}
