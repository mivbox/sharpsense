using JetBrains.Annotations;

namespace SharpSense.Application.Features.Indexing.Contracts;

/// <summary>
/// Discovers files that are safe for indexers to consume under a target directory after applying both configured include
/// globs and repository-level ignore rules. Implementations are responsible for returning normalized repository-relative
/// paths alongside absolute file paths so downstream extractors can index content without recalculating discovery metadata
/// or reapplying path filters.
/// </summary>
[PublicAPI]
public interface IWorkspaceFileDiscoverer
{
    /// <summary>
    /// Returns the files allowed for indexing beneath the supplied target directory after include glob evaluation and
    /// optional gitignore filtering have been applied. The returned collection must contain absolute file paths together
    /// with normalized repository-relative paths suitable for persistence and code node identity generation.
    /// </summary>
    /// <param name="targetDirectory">Absolute target directory used as the root for include-glob evaluation.</param>
    /// <param name="includeGlobs">Configured include globs relative to the target directory.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current discovery operation.</param>
    /// <returns>The discovered files allowed for indexing.</returns>
    Task<IReadOnlyList<DiscoveredFile>> GetAllowedFiles(
        string targetDirectory,
        IReadOnlyList<string> includeGlobs,
        CancellationToken ct);
}
