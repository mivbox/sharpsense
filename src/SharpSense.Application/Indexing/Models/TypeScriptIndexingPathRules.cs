namespace SharpSense.Application.Indexing.Models;

public static class TypeScriptIndexingPathRules
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules",
        "dist",
        "build",
        "coverage",
        ".next"
    };

    public static readonly string[] IncludeGlobs = ["**/*.ts", "**/*.tsx"];

    public static bool IsRelevantChangePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsIgnoredPath(path))
        {
            return false;
        }

        // Extended TypeScript configs may use arbitrary JSON names. Without a persisted
        // config dependency graph, conservatively refresh on repository JSON changes.
        return IsTypeScriptFilePath(path) ||
               string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIndexedPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return IsTypeScriptFilePath(path) && !IsIgnoredPath(path);
    }

    public static bool IsTypeScriptFilePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".ts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".tsx", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIgnoredPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = path.Replace('\\', '/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return WorkspaceIndexingPathRules.IsIgnoredPath(normalizedPath) || segments.Any(IgnoredDirectoryNames.Contains);
    }
}
