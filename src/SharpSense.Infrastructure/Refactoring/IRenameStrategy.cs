using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Infrastructure.Refactoring;

internal interface IRenameStrategy
{
    bool CanHandle(DocumentKind kind);

    Task<RefactorResult> RenameAsync(
        NodeRefactorTarget target,
        string newName,
        string? targetPath = null,
        CancellationToken ct = default);
}
