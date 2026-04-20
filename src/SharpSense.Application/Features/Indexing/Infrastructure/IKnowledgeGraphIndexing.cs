using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Features.Indexing.IndexSolution;

namespace SharpSense.Application.Features.Indexing.Infrastructure;

public interface IKnowledgeGraphIndexing
{
    /// <summary>
    /// Index a solution storing all the nodes and edges in the knowledge graph.
    /// </summary>
    /// <param name="command">The command for indexing</param>
    /// <param name="ct"><see cref="CancellationToken"/></param>
    /// <returns><see cref="IndexingSummary"/></returns>
    Task<IndexingSummary> Index(
        IndexSolutionCommand command,
        CancellationToken ct);
}
