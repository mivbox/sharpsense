namespace SharpSense.Application.Indexing.Abstractions;

/// <summary>
/// Resolves target and repository-relative paths for the indexing slice without leaking Infrastructure workspace types
/// into the Application layer. Implementations own repository-root awareness and path normalization semantics.
/// </summary>
public interface IIndexingWorkspacePaths
{
    /// <summary>
    /// Gets the normalized absolute repository root used by the current indexing session.
    /// </summary>
    string RootPath
    {
        get;
    }

    /// <summary>
    /// Resolves the configured target path into an absolute path for extraction.
    /// </summary>
    /// <param name="targetPath">The configured target path, absolute or repository-relative.</param>
    /// <returns>The absolute target path.</returns>
    string GetRequiredTargetPath(string targetPath);

    /// <summary>
    /// Converts the supplied path into a normalized repository-relative path or throws when the path is outside the
    /// repository root.
    /// </summary>
    /// <param name="filePath">The file path to convert.</param>
    /// <returns>The normalized repository-relative path.</returns>
    string ToRepositoryRelativePath(string? filePath);

    /// <summary>
    /// Attempts to convert the supplied path into a normalized repository-relative path.
    /// </summary>
    /// <param name="filePath">The file path to convert.</param>
    /// <param name="relativePath">The normalized repository-relative path when conversion succeeds.</param>
    /// <returns><c>true</c> when conversion succeeds; otherwise <c>false</c>.</returns>
    bool TryToRepositoryRelativePath(
        string? filePath,
        out string relativePath);
}
