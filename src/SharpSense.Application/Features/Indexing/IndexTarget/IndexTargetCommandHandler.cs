using SharpSense.Application.Features.Indexing.Infrastructure;
using SharpSense.Application.Shared.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Application.Features.Indexing.IndexTarget;

[ExcludeFromCodeCoverage(Justification = "Thin wrapper around the indexing service, no logic to test.")]
public sealed class IndexTargetCommandHandler(IKnowledgeGraphIndexing indexing)
    : ICommandHandler<IndexTargetCommand>
{
    public Task Handle(IndexTargetCommand command, CancellationToken ct)
        => indexing.Index(command, ct);
}
