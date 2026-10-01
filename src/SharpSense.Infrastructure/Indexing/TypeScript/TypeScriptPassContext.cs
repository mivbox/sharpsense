using SharpSense.Application.Indexing.Models;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TypeScriptPassContext
{
    public TypeScriptPassContext(
        IReadOnlyList<TypeScriptParsedFile> parsedFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsedFiles);

        ParsedFiles = parsedFiles;
        CancellationToken = cancellationToken;
    }

    public IReadOnlyList<TypeScriptParsedFile> ParsedFiles { get; }

    public CancellationToken CancellationToken { get; }

    public List<IndexedCodeNode> CodeNodes { get; } = [];

    public List<IndexedDependency> Edges { get; } = [];

    public List<string> Diagnostics { get; } = [];

    internal Dictionary<string, Dictionary<string, string>> ExportsByFile { get; } = new(StringComparer.Ordinal);

    internal List<TypeScriptReExport> ReExports { get; } = [];

    internal Dictionary<string, TreeSitter.Node> DeclarationNodes { get; } = new(StringComparer.Ordinal);
}
