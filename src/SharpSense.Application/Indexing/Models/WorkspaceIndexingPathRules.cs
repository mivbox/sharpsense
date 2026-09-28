namespace SharpSense.Application.Indexing.Models;

public static class WorkspaceIndexingPathRules
{
    private static readonly HashSet<string> _ignoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        ".vs",
        ".idea",
        "node_modules",
        "TestResults"
    };

    public static bool IsIgnoredPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(_ignoredDirectoryNames.Contains);
    }
}
