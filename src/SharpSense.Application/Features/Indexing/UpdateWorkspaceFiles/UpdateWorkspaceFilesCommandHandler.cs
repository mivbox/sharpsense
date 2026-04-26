using SharpSense.Application.Features.Indexing.Infrastructure;
using SharpSense.Application.Shared.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Application.Features.Indexing.UpdateWorkspaceFiles;

[ExcludeFromCodeCoverage(Justification = "Thin wrapper around the indexing service, no logic to test.")]
public sealed class UpdateWorkspaceFilesCommandHandler(IKnowledgeGraphIndexing indexing)
    : ICommandHandler<UpdateWorkspaceFilesCommand>
{
    public Task Handle(UpdateWorkspaceFilesCommand command, CancellationToken ct)
        => indexing.UpdateIncremental(command, ct);
}
