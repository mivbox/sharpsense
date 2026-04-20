using SharpSense.Application.Features.Indexing.Infrastructure;
using SharpSense.Application.Shared.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Application.Features.Indexing.IndexSolution;

[ExcludeFromCodeCoverage(Justification = "Thin wrapper around the indexing service, no logic to test.")]
public sealed class IndexSolutionCommandHandler(IKnowledgeGraphIndexing indexing)
    : ICommandHandler<IndexSolutionCommand>
{
    public async Task HandleAsync(IndexSolutionCommand command, CancellationToken ct) =>
        await indexing.Index(command, ct);
}
