namespace SharpSense.Cli.Shared;

internal static class CommandPathResolver
{
    public static string ResolveRepositoryRoot(string? repositoryRoot)
    {
        var workingDirectory = Environment.CurrentDirectory;
        var rootCandidate = string.IsNullOrWhiteSpace(repositoryRoot)
            ? workingDirectory
            : ResolvePath(workingDirectory, repositoryRoot);

        return NormalizeDirectory(rootCandidate);
    }

    private static string ResolvePath(string basePath, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));

    private static string NormalizeDirectory(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
