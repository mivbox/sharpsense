namespace SharpSense.Infrastructure.Storage;

public sealed class RepositoryWorkspace : IRepositoryWorkspace
{
    private RepositoryWorkspace(string rootPath, string databasePath)
    {
        RootPath = NormalizeRootPath(rootPath);
        DatabasePath = Path.GetFullPath(databasePath);

        var databaseDirectory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("The repository workspace database path must include a directory.");

        Directory.CreateDirectory(databaseDirectory);
    }

    public string RootPath { get; }

    public string DatabasePath { get; }

    public static RepositoryWorkspace CreateFromWorkingDirectory(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var rootPath = ResolveRootPathFromWorkingDirectory(workingDirectory);
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(userProfilePath))
        {
            throw new InvalidOperationException("Unable to resolve the local application data directory for SharpSense storage.");
        }

        var repositoryHash = RepositoryHashCalculator.ComputeHash(rootPath);
        var databasePath = Path.Combine(userProfilePath, ".SharpSense", $"{repositoryHash}.db");

        return new RepositoryWorkspace(rootPath, databasePath);
    }

    public string ToRepositoryRelativePath(string? filePath) =>
        !TryToRepositoryRelativePath(filePath, out var relativePath) ?
            throw new InvalidOperationException($"File path '{filePath}' must be located under repository root '{RootPath}'.") :
            relativePath;

    public bool TryToRepositoryRelativePath(string? filePath, out string relativePath)
    {
        relativePath = string.Empty;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var absolutePath = Path.IsPathRooted(filePath)
            ? Path.GetFullPath(filePath)
            : Path.GetFullPath(Path.Combine(RootPath, filePath));

        if (!IsSameOrSubPath(absolutePath))
        {
            return false;
        }

        var computedRelativePath = Path.GetRelativePath(RootPath, absolutePath);
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

        var absolutePath = Path.IsPathRooted(filePath)
            ? Path.GetFullPath(filePath)
            : Path.GetFullPath(Path.Combine(RootPath, filePath));
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

    private static string ResolveRootPathFromWorkingDirectory(string workingDirectory)
    {
        var currentDirectory = new DirectoryInfo(Path.GetFullPath(workingDirectory));

        while (currentDirectory is not null)
        {
            var gitPath = Path.Combine(currentDirectory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return NormalizeRootPath(currentDirectory.FullName);
            }

            currentDirectory = currentDirectory.Parent;
        }

        return NormalizeRootPath(workingDirectory);
    }

    private static string NormalizeRootPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
