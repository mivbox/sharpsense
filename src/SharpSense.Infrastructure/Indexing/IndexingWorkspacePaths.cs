using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing;

public sealed class IndexingWorkspacePaths(IRepositoryWorkspace repositoryWorkspace) : IIndexingWorkspacePaths
{
    public string RootPath => repositoryWorkspace.RootPath;

    public string GetRequiredTargetPath(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        return Path.IsPathRooted(targetPath)
            ? Path.GetFullPath(targetPath)
            : Path.GetFullPath(Path.Combine(repositoryWorkspace.RootPath, targetPath));
    }

    public string ToRepositoryRelativePath(string? filePath)
        => repositoryWorkspace.ToRepositoryRelativePath(filePath);

    public bool TryToRepositoryRelativePath(
        string? filePath,
        out string relativePath)
        => repositoryWorkspace.TryToRepositoryRelativePath(filePath, out relativePath);
}
