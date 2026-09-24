using System.IO.Abstractions;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing.CSharp;

public sealed class CSharpWorkspaceTargetResolver(
    IRepositoryWorkspace repositoryWorkspace,
    IFileSystem fileSystem) : ICSharpWorkspaceTargetResolver
{
    public string? ResolveTargetPath(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var absolutePath = fileSystem.Path.GetFullPath(
            fileSystem.Path.IsPathRooted(targetPath)
                ? targetPath
                : fileSystem.Path.Combine(repositoryWorkspace.RootPath, targetPath));
        var extension = fileSystem.Path.GetExtension(absolutePath);
        if (!extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // MSBuild documents and source-change paths must use the same physical root,
        // including repositories reached through directory aliases such as macOS /var.
        return fileSystem.Path.GetFullPath(fileSystem.Path.Combine(repositoryWorkspace.RootPath,
            repositoryWorkspace.ToRepositoryRelativePath(absolutePath)));
    }
}
