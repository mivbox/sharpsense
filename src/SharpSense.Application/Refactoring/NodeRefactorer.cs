using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Application.Refactoring;

public sealed class NodeRefactorer(
    IRefactorTargetLookup targetLookup,
    IWorkspaceRefactorer workspaceRefactorer)
    : INodeRefactorer
{
    public async Task<RefactorResult> RefactorNode(
        int nodeId,
        string newSourceCode,
        string? targetPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newSourceCode);

        if (nodeId <= 0)
        {
            return Failure("Node id must be greater than zero.");
        }

        var target = await targetLookup.GetTarget(nodeId, ct);
        if (target is null)
        {
            return Failure($"No persisted node exists for id {nodeId}.");
        }

        return await workspaceRefactorer.RefactorNode(
            target,
            newSourceCode,
            targetPath,
            ct);
    }

    private static RefactorResult Failure(string errorMessage)
        => new(
            false,
            [],
            errorMessage);
}
