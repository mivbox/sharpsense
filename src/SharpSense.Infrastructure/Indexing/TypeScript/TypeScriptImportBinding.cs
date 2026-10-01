namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed record TypeScriptImportBinding(
    string? ExportName,
    string LocalName,
    bool IsNamespaceImport);
