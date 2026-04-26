namespace SharpSense.Cli.Shared;

internal static class CommandPathResolver
{
    public static string ResolveRepositoryRoot(string? repositoryRoot)
    {
        var workingDirectory = ResolveWorkingDirectory();
        var rootCandidate = string.IsNullOrWhiteSpace(repositoryRoot)
            ? workingDirectory
            : ResolvePath(workingDirectory, repositoryRoot);

        return NormalizeDirectory(rootCandidate);
    }

    public static string ResolveTargetDirectory(string repositoryRoot, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var absoluteTargetPath = ResolvePath(repositoryRoot, targetPath);
        return Path.GetDirectoryName(absoluteTargetPath) ?? NormalizeDirectory(repositoryRoot);
    }

    private static string ResolveWorkingDirectory()
        => Environment.GetEnvironmentVariable("PWD") ?? Environment.CurrentDirectory;

    private static string ResolvePath(string basePath, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));

    private static string NormalizeDirectory(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
