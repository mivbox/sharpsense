using SharpSense.Application.Indexing.Models;
using SharpSense.TypeScript.Parsing.Abstractions;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TypeScriptPassContext
{
    public TypeScriptPassContext(
        IReadOnlyList<TypeScriptParsedFile> parsedFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsedFiles);

        ParsedFiles = parsedFiles;
        FilesByPath = parsedFiles.ToDictionary(static file => file.DiscoveredFile.RelativeFilePath, StringComparer.Ordinal);
        CancellationToken = cancellationToken;
    }

    public IReadOnlyList<TypeScriptParsedFile> ParsedFiles { get; }

    public CancellationToken CancellationToken { get; }

    public List<IndexedCodeNode> CodeNodes { get; } = [];

    public List<IndexedDependency> Edges { get; } = [];

    public List<string> Diagnostics { get; } = [];

    internal Dictionary<string, Dictionary<string, string>> ExportsByFile { get; } = new(StringComparer.Ordinal);

    internal List<TypeScriptReExport> ReExports { get; } = [];

    internal Dictionary<string, INode> DeclarationNodes { get; } = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, TypeScriptParsedFile> FilesByPath { get; }

    internal Dictionary<string, int> CodeNodeIndexes { get; } = new(StringComparer.Ordinal);

    internal HashSet<(string ParentId, string MemberId)> ParentEdges { get; } = [];
}
