using SharpSense.Application.Indexing.Models;
using SharpSense.TypeScript.Parsing.Abstractions;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TypeScriptPassContext
{
    public TypeScriptPassContext(
        IReadOnlyList<TypeScriptParsedFile> parsedFiles,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? projectIdByFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(parsedFiles);

        ParsedFiles = parsedFiles;
        FilesByPath = parsedFiles.ToDictionary(static file => file.DiscoveredFile.RelativeFilePath, StringComparer.Ordinal);
        CancellationToken = cancellationToken;
        ProjectIdByFilePath = projectIdByFilePath ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public IReadOnlyList<TypeScriptParsedFile> ParsedFiles { get; }

    public CancellationToken CancellationToken { get; }

    public IReadOnlyDictionary<string, string> ProjectIdByFilePath { get; }

    public List<IndexedCodeNode> CodeNodes { get; } = [];

    public List<IndexedDependency> Edges { get; } = [];

    public List<string> Diagnostics { get; } = [];

    internal Dictionary<string, Dictionary<string, string>> ExportsByFile { get; } = new(StringComparer.Ordinal);

    internal List<TypeScriptReExport> ReExports { get; } = [];

    internal Dictionary<string, INode> DeclarationNodes { get; } = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, TypeScriptParsedFile> FilesByPath { get; }

    internal Dictionary<string, int> CodeNodeIndexes { get; } = new(StringComparer.Ordinal);

    internal HashSet<(string ParentId, string MemberId)> ParentEdges { get; } = [];

    internal Dictionary<string, Dictionary<string, ResolvedLocalImportBinding>> LocalImportsByFile { get; } = new(StringComparer.Ordinal);
}
