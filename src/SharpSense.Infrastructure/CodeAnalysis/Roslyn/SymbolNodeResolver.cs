using Microsoft.CodeAnalysis;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed class SymbolNodeResolver
{
    private readonly IReadOnlyDictionary<string, string> _symbolNodeIds;
    private readonly IReadOnlyDictionary<string, string> _projectIdsByDocumentPath;

    public SymbolNodeResolver(
        Solution solution,
        IReadOnlyDictionary<ProjectId, string> projectIds,
        IReadOnlyDictionary<string, string> symbolNodeIds)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(projectIds);
        ArgumentNullException.ThrowIfNull(symbolNodeIds);

        _symbolNodeIds = symbolNodeIds;
        _projectIdsByDocumentPath = BuildProjectIdsByDocumentPath(solution, projectIds);
    }

    public bool TryGetNodeId(ISymbol symbol, out string nodeId)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var canonicalSymbol = RoslynSymbolUtilities.Canonicalize(symbol);
        if (TryGetMappedNodeId(symbol, canonicalSymbol, out nodeId))
        {
            return true;
        }

        return TryGetSourceNodeId(symbol, canonicalSymbol, out nodeId);
    }

    private bool TryGetMappedNodeId(
        ISymbol symbol,
        ISymbol canonicalSymbol,
        out string nodeId)
    {
        var canonicalLookupKey = RoslynSymbolUtilities.GetLookupKey(canonicalSymbol);
        if (_symbolNodeIds.TryGetValue(canonicalLookupKey, out var canonicalNodeId))
        {
            nodeId = canonicalNodeId;
            return true;
        }

        var symbolLookupKey = RoslynSymbolUtilities.GetLookupKey(symbol);
        if (_symbolNodeIds.TryGetValue(symbolLookupKey, out var symbolNodeId))
        {
            nodeId = symbolNodeId;
            return true;
        }

        nodeId = string.Empty;
        return false;
    }

    private bool TryGetSourceNodeId(
        ISymbol symbol,
        ISymbol canonicalSymbol,
        out string nodeId)
    {
        foreach (var sourcePath in EnumerateSourcePaths(canonicalSymbol).Concat(EnumerateSourcePaths(symbol)))
        {
            if (!_projectIdsByDocumentPath.TryGetValue(sourcePath, out var projectId))
            {
                continue;
            }

            nodeId = RoslynSymbolUtilities.GetCanonicalId(projectId, canonicalSymbol);
            return true;
        }

        nodeId = string.Empty;
        return false;
    }

    private static IReadOnlyDictionary<string, string> BuildProjectIdsByDocumentPath(
        Solution solution,
        IReadOnlyDictionary<ProjectId, string> projectIds)
    {
        var comparer = GetPathComparer();
        var projectIdsByDocumentPath = new Dictionary<string, string>(comparer);

        foreach (var project in solution.Projects)
        {
            if (!projectIds.TryGetValue(project.Id, out var projectId))
            {
                continue;
            }

            foreach (var document in project.Documents)
            {
                if (string.IsNullOrWhiteSpace(document.FilePath))
                {
                    continue;
                }

                projectIdsByDocumentPath[Path.GetFullPath(document.FilePath)] = projectId;
            }
        }

        return projectIdsByDocumentPath;
    }

    private static IEnumerable<string> EnumerateSourcePaths(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || string.IsNullOrWhiteSpace(location.SourceTree?.FilePath))
            {
                continue;
            }

            yield return Path.GetFullPath(location.SourceTree.FilePath);
        }
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
