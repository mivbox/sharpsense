using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Application.Refactoring.Abstractions;

/// <summary>
/// Coordinates persisted-node lookup plus semantic rename orchestration so every write-capable interface in the product
/// can rename one declared symbol through the same shared Application-layer boundary.
/// </summary>
public interface IRefactorSymbolService
{
    /// <summary>
    /// Renames the declared symbol represented by the persisted node id and reports every repository-relative file that
    /// changed when the operation succeeds.
    /// </summary>
    /// <param name="nodeId">The persisted integer node id to rename.</param>
    /// <param name="newName">The new identifier name for the declared symbol.</param>
    /// <param name="targetPath">An optional explicit workspace target path. When omitted, the implementation resolves the target from persisted metadata and the current repository context.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current rename operation.</param>
    Task<RefactorResult> RenameSymbol(
        int nodeId,
        string newName,
        string? targetPath = null,
        CancellationToken ct = default);
}
