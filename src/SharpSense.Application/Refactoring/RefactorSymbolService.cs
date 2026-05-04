using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Application.Refactoring;

public sealed class RefactorSymbolService(
    IRefactorTargetLookup targetLookup,
    IWorkspaceRenamer workspaceRenamer)
    : IRefactorSymbolService
{
    public async Task<RefactorResult> RenameSymbol(
        int nodeId,
        string newName,
        string? targetPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newName);

        if (nodeId <= 0)
        {
            return Failure("Node id must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            return Failure("New name must not be empty.");
        }

        var target = await targetLookup.GetTarget(nodeId, ct);
        if (target is null)
        {
            return Failure($"No persisted node exists for id {nodeId}.");
        }

        return await workspaceRenamer.RenameSymbol(
            target,
            newName,
            targetPath,
            ct);
    }

    private static RefactorResult Failure(string errorMessage)
        => new(
            false,
            [],
            errorMessage);
}
