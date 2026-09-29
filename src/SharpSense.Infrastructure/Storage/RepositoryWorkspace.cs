using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Storage;

internal sealed class RepositoryWorkspace : IRepositoryWorkspace
{
    private readonly IFileSystem _fileSystem;

    internal RepositoryWorkspace(
        string rootPath,
        string databasePath,
        IFileSystem fileSystem,
        WorkspaceDefinition? definition = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        RootPath = NormalizeRootPath(rootPath, _fileSystem);
        DatabasePath = _fileSystem.Path.GetFullPath(databasePath);
        Definition = definition;
    }

    public string RootPath
    {
        get;
    }

    public string DatabasePath
    {
        get;
    }

    public Guid? WorkspaceId => Definition?.Id;

    public string? WorkspaceName => Definition?.Name;

    public WorkspaceDefinition? Definition
    {
        get;
    }

    public string ToRepositoryRelativePath(string? filePath) =>
        !TryToRepositoryRelativePath(filePath, out var relativePath)
        ?
            throw new InvalidOperationException($"File path '{filePath}' must be located under repository root '{RootPath}'.")
        :
            relativePath;

    public string GetRequiredTargetDirectoryPath(string targetPath)
    {
        var absoluteTargetPath = ResolveTargetPath(targetPath);

        if (_fileSystem.Directory.Exists(absoluteTargetPath))
        {
            return absoluteTargetPath;
        }

        return _fileSystem.Path.GetDirectoryName(absoluteTargetPath)
            ?? throw new InvalidOperationException($"Unable to determine the target directory for '{absoluteTargetPath}'.");
    }

    public bool TryToRepositoryRelativePath(string? filePath, out string relativePath)
    {
        relativePath = string.Empty;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var absolutePath = NormalizeRootPath(
            _fileSystem.Path.IsPathRooted(filePath)
                ? filePath
                : _fileSystem.Path.Combine(RootPath, filePath),
            _fileSystem);

        if (!IsSameOrSubPath(absolutePath))
        {
            return false;
        }

        var computedRelativePath = _fileSystem.Path.GetRelativePath(RootPath, absolutePath);
        relativePath = computedRelativePath == "."
            ? string.Empty
            : NormalizeDirectorySeparators(computedRelativePath);

        return true;
    }

    public bool IsSameOrSubPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var absolutePath = NormalizeRootPath(
            _fileSystem.Path.IsPathRooted(filePath)
                ? filePath
                : _fileSystem.Path.Combine(RootPath, filePath),
            _fileSystem);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPathWithSeparator = RootPath.EndsWith(Path.DirectorySeparatorChar) ||
                                    RootPath.EndsWith(Path.AltDirectorySeparatorChar)
            ? RootPath
            : RootPath + Path.DirectorySeparatorChar;

        return string.Equals(RootPath, absolutePath, comparison) ||
            absolutePath.StartsWith(rootPathWithSeparator, comparison);
    }

    public string NormalizeDirectorySeparators(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Replace('\\', '/');
    }

    private string ResolveTargetPath(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var absoluteTargetPath = _fileSystem.Path.IsPathRooted(targetPath)
            ? _fileSystem.Path.GetFullPath(targetPath)
            : _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(RootPath, targetPath));

        if (!IsSameOrSubPath(absoluteTargetPath))
        {
            throw new InvalidOperationException($"Target path '{absoluteTargetPath}' must be located under repository root '{RootPath}'.");
        }

        if (!_fileSystem.File.Exists(absoluteTargetPath) &&
            !_fileSystem.Directory.Exists(absoluteTargetPath))
        {
            throw new FileNotFoundException(
                $"Target path '{absoluteTargetPath}' was not found.",
                absoluteTargetPath);
        }

        return absoluteTargetPath;
    }

    internal static string NormalizeRootPath(
        string path,
        IFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = fileSystem.Path.GetFullPath(path);
        var resolvedPath = ResolveExistingPath(fullPath, fileSystem);

        return TrimEndingDirectorySeparatorPreservingRoot(fileSystem.Path.GetFullPath(resolvedPath));
    }

    private static string ResolveExistingPath(
        string fullPath,
        IFileSystem fileSystem)
    {
        var root = fileSystem.Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return fullPath;
        }

        var resolvedPath = root;
        var segments = fullPath[root.Length..]
            .Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var candidatePath = fileSystem.Path.Combine(resolvedPath, segment);
            resolvedPath = TryResolveLinkTarget(candidatePath, fileSystem) ?? candidatePath;
        }

        return resolvedPath;
    }

    private static string? TryResolveLinkTarget(
        string path,
        IFileSystem fileSystem)
    {
        if (fileSystem.Directory.Exists(path) &&
            fileSystem.DirectoryInfo.New(path) is FileSystemInfoBase directoryInfo)
        {
            return TryResolveLinkTarget(directoryInfo, fileSystem);
        }

        if (fileSystem.File.Exists(path) &&
            fileSystem.FileInfo.New(path) is FileSystemInfoBase fileInfo)
        {
            return TryResolveLinkTarget(fileInfo, fileSystem);
        }

        return null;
    }

    private static string? TryResolveLinkTarget(
        FileSystemInfoBase fileSystemInfo,
        IFileSystem fileSystem)
    {
        try
        {
            return fileSystemInfo.ResolveLinkTarget(returnFinalTarget: true) is { } target
                ? ResolveExistingPath(target.FullName, fileSystem)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string TrimEndingDirectorySeparatorPreservingRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(fullPath, root, comparison)
            ? fullPath
            : Path.TrimEndingDirectorySeparator(fullPath);
    }
}
