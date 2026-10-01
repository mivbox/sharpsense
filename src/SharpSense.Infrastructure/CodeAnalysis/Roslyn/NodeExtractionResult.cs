using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed record NodeExtractionResult(
    IReadOnlyList<CodeNode> CodeNodes,
    IReadOnlyList<DeclaredSymbolContext> DeclaredSymbols);
