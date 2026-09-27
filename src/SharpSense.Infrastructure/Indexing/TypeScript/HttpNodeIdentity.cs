namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal static class HttpNodeIdentity
{
    public static string CreateCanonicalId(
        string method,
        string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return $"http:{method.ToUpperInvariant()}:{Uri.EscapeDataString(url)}";
    }

    public static bool IsPlaceholderId(string canonicalId)
        => canonicalId.StartsWith("http:", StringComparison.Ordinal);

    public static bool TryParse(
        string canonicalId,
        out string method,
        out string url)
    {
        method = string.Empty;
        url = string.Empty;

        if (!IsPlaceholderId(canonicalId))
        {
            return false;
        }

        var separatorIndex = canonicalId.IndexOf(':', "http:".Length);
        if (separatorIndex < 0)
        {
            return false;
        }

        method = canonicalId["http:".Length..separatorIndex];
        url = Uri.UnescapeDataString(canonicalId[(separatorIndex + 1)..]);

        return !string.IsNullOrWhiteSpace(method) &&
            !string.IsNullOrWhiteSpace(url);
    }
}
