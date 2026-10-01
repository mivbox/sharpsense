using Microsoft.CodeAnalysis;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed class SymbolNodeResolver(
    Solution solution,
    IReadOnlyDictionary<ProjectId, string> projectIds)
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

        // Identity belongs to the declaring project; names may repeat across projects.
        nodeId = RoslynSymbolUtilities.GetCanonicalId(projectId, canonicalSymbol);

        return true;
    }
}
