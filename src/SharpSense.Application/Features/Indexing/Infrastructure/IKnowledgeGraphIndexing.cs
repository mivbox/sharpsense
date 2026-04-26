using SharpSense.Application.Features.Indexing.IndexTarget;
using SharpSense.Application.Features.Indexing.UpdateWorkspaceFiles;

namespace SharpSense.Application.Features.Indexing.Infrastructure;

public interface IKnowledgeGraphIndexing
{
    /// <summary>
    /// Index a target storing all the nodes and edges in the knowledge graph.
    /// </summary>
    /// <param name="command">The command for indexing</param>
    /// <param name="ct"><see cref="CancellationToken"/></param>
    Task Index(
        IndexTargetCommand command,
        CancellationToken ct);

    Task UpdateIncremental(
        UpdateWorkspaceFilesCommand command,
        CancellationToken ct);
}
