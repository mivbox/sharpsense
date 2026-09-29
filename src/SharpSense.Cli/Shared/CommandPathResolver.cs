namespace SharpSense.Cli.Shared;

internal static class CommandPathResolver
{
    public static string ResolveWorkspaceRoot(string? workspaceRoot)
    {
        var workingDirectory = Environment.CurrentDirectory;
        var rootCandidate = string.IsNullOrWhiteSpace(workspaceRoot)
            ? workingDirectory
            : ResolvePath(workingDirectory, workspaceRoot);

        return NormalizeDirectory(rootCandidate);
    }

    private static string ResolvePath(string basePath, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));

    private static string NormalizeDirectory(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
