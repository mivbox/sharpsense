using Microsoft.CodeAnalysis;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed class SymbolNodeResolver(
    Solution solution,
    IReadOnlyDictionary<ProjectId, string> projectIds,
    IReadOnlyDictionary<string, string> symbolNodeIds)
{
    public bool TryGetNodeId(ISymbol symbol, out string nodeId)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var canonicalSymbol = RoslynSymbolUtilities.Canonicalize(symbol);
        var assembly = canonicalSymbol.ContainingAssembly;
        var project = assembly is null ? null : solution.GetProject(assembly);
        if (project is null || !projectIds.TryGetValue(project.Id, out var projectId))
        {
            nodeId = string.Empty;

            return false;
        }

        var canonicalId = RoslynSymbolUtilities.GetCanonicalId(projectId, canonicalSymbol);
        // Incremental extraction can reference declarations outside the current batch.
        // Their identity belongs to the declaring project, never to a name match.
        nodeId = symbolNodeIds.GetValueOrDefault(canonicalId, canonicalId);

        return true;
    }
}
