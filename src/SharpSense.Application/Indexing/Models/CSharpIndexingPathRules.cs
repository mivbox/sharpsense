namespace SharpSense.Application.Indexing.Models;

public static class CSharpIndexingPathRules
{
    private static readonly HashSet<string> ConfigurationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
        "global.json", ".editorconfig"
    };

    public static bool IsWorkspaceTarget(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsConfigurationPath(string path)
        => IsWorkspaceTarget(path) || ConfigurationNames.Contains(Path.GetFileName(path)) ||
           Path.GetExtension(path).Equals(".props", StringComparison.OrdinalIgnoreCase) ||
           Path.GetExtension(path).Equals(".targets", StringComparison.OrdinalIgnoreCase);

    public static bool IsRelevantChangePath(string path)
        => !WorkspaceIndexingPathRules.IsIgnoredPath(path) &&
           (Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
            IsConfigurationPath(path));
}
