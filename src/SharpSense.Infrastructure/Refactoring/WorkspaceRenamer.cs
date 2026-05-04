using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Infrastructure.Refactoring;

internal sealed class WorkspaceRenamer(IEnumerable<IRenameStrategy> strategies)
    : IWorkspaceRenamer
{
    private readonly IRenameStrategy[] _strategies = [.. strategies];

    public Task<RefactorResult> RenameSymbol(
        NodeRefactorTarget target,
        string newName,
        string? targetPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(newName);

        var strategy = _strategies.FirstOrDefault(candidate => candidate.CanHandle(target.DocumentKind));
        return strategy is null
            ? Task.FromResult(Failure($"Document kind '{target.DocumentKind}' is not supported for semantic rename."))
            : strategy.RenameAsync(
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
