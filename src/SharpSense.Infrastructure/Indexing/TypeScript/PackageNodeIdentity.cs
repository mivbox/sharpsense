namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal static class PackageNodeIdentity
{
    public static string CreateCanonicalId(
        string packageName,
        string? exportName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        var encodedPackageName = Uri.EscapeDataString(packageName);

        return string.IsNullOrWhiteSpace(exportName)
            ? $"package:{encodedPackageName}"
            : $"package:{encodedPackageName}:{Uri.EscapeDataString(exportName)}";
    }

    public static bool IsPlaceholderId(string canonicalId)
        => canonicalId.StartsWith("package:", StringComparison.Ordinal);

    public static bool TryParse(
        string canonicalId,
        out string packageName,
        out string? exportName)
    {
        packageName = string.Empty;
        exportName = null;

        if (!IsPlaceholderId(canonicalId))
        {
            return false;
        }

        var encodedPayload = canonicalId["package:".Length..];
        if (string.IsNullOrWhiteSpace(encodedPayload))
        {
            return false;
        }

        var separatorIndex = encodedPayload.IndexOf(':');
        if (separatorIndex < 0)
        {
            packageName = Uri.UnescapeDataString(encodedPayload);

            return !string.IsNullOrWhiteSpace(packageName);
        }

        packageName = Uri.UnescapeDataString(encodedPayload[..separatorIndex]);
        exportName = Uri.UnescapeDataString(encodedPayload[(separatorIndex + 1)..]);

        return !string.IsNullOrWhiteSpace(packageName) &&
            !string.IsNullOrWhiteSpace(exportName);
    }
}
