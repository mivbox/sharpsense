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
        SharpSense.Domain.KnowledgeGraph.Enums.NodeType nodeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportName);

        return nodeType == SharpSense.Domain.KnowledgeGraph.Enums.NodeType.Method
            ? $"{exportName}()"
            : exportName;
    }

    public static string CreateSearchText(
        string displayName,
        string summary)
        => string.IsNullOrWhiteSpace(summary)
            ? displayName
            : $"{displayName}\n{summary}";

    private static string Normalize(string relativeFilePath)
        => relativeFilePath.Replace('\\', '/');

    public static string CreateFullyQualifiedName(string relativeFilePath, string symbolName)
        => $"ts:{Normalize(relativeFilePath)}::{symbolName}";
}
