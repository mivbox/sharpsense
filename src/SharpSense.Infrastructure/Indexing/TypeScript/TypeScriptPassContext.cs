using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

public sealed class TypeScriptPassContext
{
    public TypeScriptPassContext(
        string targetPath,
        IReadOnlyList<TypeScriptParsedFile> parsedFiles,
        bool isIncremental,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(parsedFiles);

        TargetPath = targetPath;
        ParsedFiles = parsedFiles;
        DiscoveredFiles =
        [
            .. parsedFiles.Select(static parsedFile => parsedFile.DiscoveredFile)
        ];
        IsIncremental = isIncremental;
        Progress = progress;
        CancellationToken = cancellationToken;
    }

    public string TargetPath { get; }

    public IReadOnlyList<TypeScriptParsedFile> ParsedFiles { get; }

    public IReadOnlyList<DiscoveredFile> DiscoveredFiles { get; }

    public bool IsIncremental { get; }

    public IProgress<IndexingProgress>? Progress { get; }

    public CancellationToken CancellationToken { get; }

    public List<IndexedProject> Projects { get; } = [];

    public List<IndexedCodeNode> CodeNodes { get; } = [];

    public List<IndexedDependency> Edges { get; } = [];

    public List<string> Diagnostics { get; } = [];

    internal Dictionary<string, Dictionary<string, string>> ExportsByFile { get; } = new(StringComparer.Ordinal);

    internal List<TypeScriptReExport> ReExports { get; } = [];

    internal Dictionary<string, TreeSitter.Node> DeclarationNodes { get; } = new(StringComparer.Ordinal);
}

internal sealed record TypeScriptReExport(string FilePath, string Source, string? ImportedName, string? ExportedName);
