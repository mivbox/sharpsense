namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed record TypeScriptReExport(string FilePath, string Source, string? ImportedName, string? ExportedName);
