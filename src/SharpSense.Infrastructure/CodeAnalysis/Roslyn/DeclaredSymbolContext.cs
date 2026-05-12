using Microsoft.CodeAnalysis;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed record DeclaredSymbolContext(
    string NodeId,
    string ProjectId,
    ISymbol Symbol,
    SyntaxNode DeclarationSyntax,
    SemanticModel SemanticModel);
