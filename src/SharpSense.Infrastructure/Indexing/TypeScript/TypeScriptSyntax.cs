using SharpSense.TypeScript.Parsing.Abstractions;
using SharpSense.TypeScript.Parsing.Models;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal static class TypeScriptSyntax
{
    public static IEnumerable<TypeScriptImportBinding> ReadImports(ImportDeclaration import)
    {
        var clause = import.ImportClause;
        if (clause?.Name is { Text: { } name })
        {
            yield return new(null, name, false);
        }

        if (clause?.NamedBindings is NamespaceImport { Name.Text: { } namespaceName })
        {
            yield return new(null, namespaceName, true);
        }
        else if (clause?.NamedBindings is NamedImports namedImports)
        {
            foreach (var specifier in namedImports.Elements)
            {
                if (specifier.Name?.Text is { } local)
                {
                    yield return new(GetName(specifier.PropertyName) ?? local, local, false);
                }
            }
        }
    }

    public static string? GetName(INode? node)
        => node switch
        {
            PrivateIdentifier => null,
            Identifier identifier => identifier.Text,
            StringLiteral literal => literal.Text,
            NumericLiteral literal => literal.Text,
            _ => null
        };

    public static string? GetModuleSpecifier(INode node)
        => node switch
        {
            ImportDeclaration { ModuleSpecifier: StringLiteral literal } => literal.Text,
            ExportDeclaration { ModuleSpecifier: StringLiteral literal } => literal.Text,
            _ => null
        };

    public static bool HasBody(INode node) => node is FunctionLikeDeclarationBase { Body: not null };

    public static int GetDeclarationStart(TypeScriptSyntaxTree tree, INode node)
    {
        var start = tree.GetStart(node);
        if (node is IHasModifierLike { Modifiers: { } modifiers })
        {
            foreach (var modifier in modifiers)
            {
                if (modifier.Kind is not (SyntaxKind.ExportKeyword or SyntaxKind.DefaultKeyword or SyntaxKind.DeclareKeyword))
                {
                    break;
                }

                start = modifier.End ?? start;
            }
        }

        return tree.GetTokenStart(start);
    }
}
