namespace SharpSense.Infrastructure.Storage;

/// <summary>
/// Filesystem path comparisons follow the platform policy used by discovery, watching and workspace storage.
/// </summary>
internal static class FileSystemPaths
{
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
