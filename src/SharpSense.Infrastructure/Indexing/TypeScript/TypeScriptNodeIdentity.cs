namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal static class TypeScriptNodeIdentity
{
    public static string CreateCanonicalId(
        string relativeFilePath,
        string exportName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportName);

        return $"code:ts:{Normalize(relativeFilePath)}:{exportName}";
    }

    public static string CreateDisplayName(
        string exportName,
        Domain.KnowledgeGraph.Enums.NodeType nodeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportName);

        return nodeType == Domain.KnowledgeGraph.Enums.NodeType.Method
            ? $"{exportName}()"
            : exportName;
    }

    public static string CreateSearchText(
        string displayName,
        string summary,
        string? relativeFilePath = null,
        string? signatureContext = null)
    {
        var header = string.IsNullOrWhiteSpace(signatureContext)
            ? displayName
            : $"{displayName} {signatureContext}";
        var pathContext = FormatPathContext(relativeFilePath);
        var parts = new List<string>(3)
        {
            header
        };
        if (!string.IsNullOrWhiteSpace(pathContext))
        {
            parts.Add(pathContext);
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            parts.Add(summary);
        }

        return string.Join('\n', parts);
    }

    private static string? FormatPathContext(string? relativeFilePath)
    {
        if (string.IsNullOrWhiteSpace(relativeFilePath))
        {
            return null;
        }

        var normalized = Normalize(relativeFilePath);
        var extension = Path.GetExtension(normalized);

        return string.IsNullOrEmpty(extension)
            ? normalized
            : normalized[..^extension.Length];
    }

    private static string Normalize(string relativeFilePath)
        => relativeFilePath.Replace('\\', '/');

    public static string CreateFullyQualifiedName(string relativeFilePath, string symbolName)
        => $"ts:{Normalize(relativeFilePath)}::{symbolName}";
}
