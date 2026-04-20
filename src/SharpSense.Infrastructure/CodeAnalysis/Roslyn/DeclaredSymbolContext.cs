using Microsoft.CodeAnalysis;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed record DeclaredSymbolContext(
    string NodeId,
    ISymbol Symbol,
    SyntaxNode DeclarationSyntax,
    SemanticModel SemanticModel);
