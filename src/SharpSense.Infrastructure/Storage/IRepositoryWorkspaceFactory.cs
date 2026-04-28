namespace SharpSense.Infrastructure.Storage;

/// <summary>
/// Creates repository workspaces from a current working directory so callers do not need to know how repository roots,
/// normalized paths, and database locations are derived from the filesystem boundary.
/// </summary>
public interface IRepositoryWorkspaceFactory
{
    /// <summary>
    /// Creates a repository workspace from the supplied working directory.
    /// </summary>
    /// <param name="workingDirectory">The working directory used to locate the repository root.</param>
    /// <returns>The repository workspace rooted at the nearest repository ancestor.</returns>
    IRepositoryWorkspace CreateFromWorkingDirectory(string workingDirectory);
}
