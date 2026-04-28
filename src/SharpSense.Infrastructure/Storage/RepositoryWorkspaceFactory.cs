using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Storage;

public sealed class RepositoryWorkspaceFactory(IFileSystem fileSystem) : IRepositoryWorkspaceFactory
{
    public IRepositoryWorkspace CreateFromWorkingDirectory(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var rootPath = RepositoryWorkspace.ResolveRootPathFromWorkingDirectory(workingDirectory, fileSystem);
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(userProfilePath))
        {
            throw new InvalidOperationException("Unable to resolve the local application data directory for SharpSense storage.");
        }

        var repositoryHash = RepositoryHashCalculator.ComputeHash(rootPath);
        var databasePath = fileSystem.Path.Combine(userProfilePath, ".SharpSense", $"{repositoryHash}.db");

        return new RepositoryWorkspace(rootPath, databasePath, fileSystem);
    }
}
