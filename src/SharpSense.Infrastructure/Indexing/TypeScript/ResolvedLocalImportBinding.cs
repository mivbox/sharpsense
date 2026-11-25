namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed record ResolvedLocalImportBinding(
    string LocalName,
    bool IsNamespaceImport,
    string TargetRelativePath,
    string[] TargetNodeIds);
