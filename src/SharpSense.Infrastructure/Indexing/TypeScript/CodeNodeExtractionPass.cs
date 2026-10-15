using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.TypeScript.Parsing.Abstractions;
using SharpSense.TypeScript.Parsing.Models;
using System.Security.Cryptography;
using System.Text;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

/// <summary>Indexes exported declarations and members; export aliases reference their original declaration.</summary>
internal sealed class CodeNodeExtractionPass : ITypeScriptExtractionPass
{
    public void Execute(TypeScriptPassContext context)
    {
        foreach (var file in context.ParsedFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var path = file.DiscoveredFile.RelativeFilePath;
            var exports = context.ExportsByFile[path] = new(StringComparer.Ordinal);
            var declarations = new Dictionary<string, Declaration>(StringComparer.Ordinal);
            var importedBindings = ReadImportedBindings(file.RootNode);
            var statements = file.RootNode.Statements ?? [];
            foreach (var declaration in ReadDeclarations(statements, file, context.CancellationToken))
            {
                // TypeScript has separate type and value namespaces. The graph keeps one symbol
                // identity, so a value declaration owns its body and outgoing calls.
                if (!declarations.TryGetValue(declaration.Name, out var existing) ||
                    PreferDeclaration(declaration.Node, existing.Node))
                {
                    declarations[declaration.Name] = declaration;
                }
            }

            foreach (var statement in statements)
            {
                if (statement is ExportDeclaration export)
                {
                    if (TypeScriptSyntax.GetModuleSpecifier(export) is { } source)
                    {
                        AddReExports(context, path, export, source);
                    }
                    else if (export.ExportClause is NamedExports clause)
                    {
                        foreach (var specifier in clause.Elements)
                        {
                            var local = TypeScriptSyntax.GetName(specifier.PropertyName ?? specifier.Name);
                            var alias = TypeScriptSyntax.GetName(specifier.Name);
                            AddExport(local, alias);
                        }
                    }

                    continue;
                }

                if (statement is ExportAssignment { IsExportEquals: false, Expression: { } value })
                {
                    if (value is Identifier identifier)
                    {
                        AddExport(identifier.Text, "default");
                    }
                    else if (IsFunction(value))
                    {
                        exports["default"] = AddNode(
                            context,
                            file,
                            value,
                            "default",
                            FunctionKind(file, "default", value, context.CancellationToken),
                            statement);
                    }

                    continue;
                }

                if (!statement.HasModifier(SyntaxKind.ExportKeyword))
                {
                    continue;
                }

                foreach (var declaration in ReadDeclarations([statement], file, context.CancellationToken))
                {
                    var selected = declarations.GetValueOrDefault(declaration.Name, declaration);
                    var id = AddNode(context, file, selected.Node, selected.Name, selected.Kind, statement);
                    exports[statement.HasModifier(SyntaxKind.DefaultKeyword) ? "default" : selected.Name] = id;
                    AddMembers(context, file, selected, id);
                }
            }

            void AddExport(string? local, string? alias)
            {
                if (local is null || alias is null)
                {
                    return;
                }

                if (declarations.TryGetValue(local, out var declaration))
                {
                    exports[alias] = AddNode(context, file, declaration.Node, declaration.Name, declaration.Kind, declaration.Node);
                    AddMembers(context, file, declaration, exports[alias]);
                }
                else if (importedBindings.TryGetValue(local, out var imported))
                {
                    context.ReExports.Add(new(path, imported.Source, imported.ImportedName, alias));
                }
            }
        }
    }

    private static Dictionary<string, (string Source, string? ImportedName)> ReadImportedBindings(SourceFile root)
    {
        var bindings = new Dictionary<string, (string Source, string? ImportedName)>(StringComparer.Ordinal);
        foreach (var import in (root.Statements ?? []).OfType<ImportDeclaration>())
        {
            if (TypeScriptSyntax.GetModuleSpecifier(import) is not { } source)
            {
                continue;
            }

            foreach (var binding in TypeScriptSyntax.ReadImports(import))
            {
                bindings[binding.LocalName] = (source, binding.IsNamespaceImport ? null : binding.ExportName ?? "default");
            }
        }

        return bindings;
    }

    private static IEnumerable<Declaration> ReadDeclarations(
        IEnumerable<INode> nodes,
        TypeScriptParsedFile file,
        CancellationToken ct)
    {
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            if (node is VariableStatement statement)
            {
                foreach (var variable in statement.DeclarationList.Declarations ?? [])
                {
                    if (variable.Name is not Identifier { Text: { } name })
                    {
                        continue;
                    }

                    yield return new(
                        name,
                        variable,
                        variable.Initializer is { } value && IsFunction(value) ? FunctionKind(file, name, value, ct) : NodeType.Field);
                }

                continue;
            }

            var declarationName = TypeScriptSyntax.GetName((node as IHasNameProperty)?.Name) ?? "default";
            var kind = node switch
            {
                FunctionDeclaration => FunctionKind(file, declarationName, node, ct),
                ClassDeclaration or ClassExpression or EnumDeclaration => NodeType.Class,
                InterfaceDeclaration or TypeAliasDeclaration => NodeType.Interface,
                _ => (NodeType?)null
            };
            if (kind is not null)
            {
                yield return new(declarationName, node, kind.Value);
            }
        }
    }

    private static void AddMembers(
        TypeScriptPassContext context,
        TypeScriptParsedFile file,
        Declaration declaration,
        string parentId)
    {
        IEnumerable<INode> members = declaration.Node switch
        {
            IClassLikeDeclaration { Members: { } values } => values,
            InterfaceDeclaration { Members: { } values } => values,
            EnumDeclaration { Members: { } values } => values,
            _ => []
        };
        foreach (var member in members)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var name = TypeScriptSyntax.GetName((member as IHasNameProperty)?.Name);
            if (member is ConstructorDeclaration)
            {
                name = "constructor";
            }

            var kind = member switch
            {
                MethodDeclaration or MethodSignature or ConstructorDeclaration => NodeType.Method,
                PropertyDeclaration or PropertySignature or GetAccessorDeclaration or SetAccessorDeclaration => NodeType.Property,
                EnumMember { Initializer: not null } => NodeType.Field,
                _ => (NodeType?)null
            };
            if (name is null || kind is null)
            {
                continue;
            }

            var accessor = member.Kind switch
            {
                SyntaxKind.GetAccessor => "get",
                SyntaxKind.SetAccessor => "set",
                _ => null
            };
            var memberKind = (member.HasModifier(SyntaxKind.StaticKeyword), accessor) switch
            {
                (true, not null) => $"static:{accessor}",
                (true, null) => "static",
                (false, not null) => accessor,
                _ => null
            };
            var id = AddNode(context, file, member, $"{declaration.Name}.{name}", kind.Value, member, memberKind);
            if (context.ParentEdges.Add((parentId, id)))
            {
                context.Edges.Add(new(parentId, id, EdgeType.ParentOf));
            }
        }
    }

    private static string AddNode(
        TypeScriptPassContext context,
        TypeScriptParsedFile file,
        INode node,
        string name,
        NodeType kind,
        INode documentationNode,
        string? memberKind = null)
    {
        var path = file.DiscoveredFile.RelativeFilePath;
        var id = TypeScriptNodeIdentity.CreateCanonicalId(path, name) + (memberKind is null ? string.Empty : $":{memberKind}");
        var hasExisting = context.CodeNodeIndexes.TryGetValue(id, out var existingIndex);
        if (hasExisting)
        {
            var existing = context.DeclarationNodes[id];
            if (!TypeScriptSyntax.HasBody(node) || TypeScriptSyntax.HasBody(existing))
            {
                return id;
            }
        }

        var symbolName = memberKind is null ? name : $"{name} [{memberKind}]";
        var display = TypeScriptNodeIdentity.CreateDisplayName(symbolName, kind);
        var summary = ReadDocumentation(file, documentationNode);
        var start = TypeScriptSyntax.GetDeclarationStart(file.SyntaxTree, node);
        var declarationText = file.SourceText[start..(node.End ?? file.SourceText.Length)];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(declarationText)))
            .ToLowerInvariant();
        context.DeclarationNodes[id] = node;
        var codeNode = new IndexedCodeNode(
            id,
            null,
            TypeScriptNodeIdentity.CreateFullyQualifiedName(path, symbolName),
            display,
            kind,
            path,
            file.SyntaxTree.GetPosition(start).Line,
            file.SyntaxTree.GetPosition(node.End ?? file.SourceText.Length).Line,
            summary,
            TypeScriptNodeIdentity.CreateSearchText(display, summary),
            hash);
        if (hasExisting)
        {
            // Method overloads share one symbol; the implementation owns location, hash and calls.
            context.CodeNodes[existingIndex] = codeNode;
        }
        else
        {
            context.CodeNodeIndexes.Add(id, context.CodeNodes.Count);
            context.CodeNodes.Add(codeNode);
        }

        return id;
    }

    private static string ReadDocumentation(TypeScriptParsedFile file, INode declaration)
    {
        var tree = file.SyntaxTree;
        var line = tree.GetPosition(tree.GetStart(declaration)).Line - 1;
        while (line > 0 && tree.GetLineText(line).IsWhiteSpace())
        {
            line--;
        }

        if (line == 0 || !tree.GetLineText(line).TrimEnd().EndsWith("*/", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var end = line;
        while (line > 0 && !tree.GetLineText(line).Contains("/**", StringComparison.Ordinal))
        {
            line--;
        }

        if (line == 0)
        {
            return string.Empty;
        }

        var summary = new StringBuilder();
        for (var current = line; current <= end; current++)
        {
            if (current > line)
            {
                summary.Append('\n');
            }

            summary.Append(tree.GetLineText(current).Trim().TrimStart('/').Trim("*/ "));
        }

        return summary.ToString().Trim();
    }

    private static void AddReExports(
        TypeScriptPassContext context,
        string path,
        ExportDeclaration export,
        string source)
    {
        if (export.ExportClause is NamedExports clause)
        {
            foreach (var binding in clause.Elements)
            {
                var name = TypeScriptSyntax.GetName(binding.PropertyName ?? binding.Name);
                var alias = TypeScriptSyntax.GetName(binding.Name);
                if (name is not null && alias is not null)
                {
                    context.ReExports.Add(new(path, source, name, alias));
                }
            }
        }
        else
        {
            var alias = export.ExportClause is NamespaceExport namespaceExport ? TypeScriptSyntax.GetName(namespaceExport.Name) : null;
            context.ReExports.Add(new(path, source, null, alias));
        }
    }

    private static NodeType FunctionKind(TypeScriptParsedFile file, string name, INode node, CancellationToken ct)
        => file.DiscoveredFile.RelativeFilePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) &&
           (name == "default" || name.Length > 0 && char.IsUpper(name[0])) &&
           node.DescendantsAndSelf(ct).Any(child => child is JsxElement or JsxSelfClosingElement or JsxFragment)
            ? NodeType.Component
            : NodeType.Method;

    private static bool IsFunction(INode node) => node is ArrowFunction or FunctionExpression or FunctionDeclaration;

    private static bool PreferDeclaration(INode candidate, INode existing)
    {
        var candidateIsTypeOnly = candidate is InterfaceDeclaration or TypeAliasDeclaration;
        var existingIsTypeOnly = existing is InterfaceDeclaration or TypeAliasDeclaration;
        if (candidateIsTypeOnly != existingIsTypeOnly)
        {
            return !candidateIsTypeOnly;
        }

        // Prefer an implementation over an overload signature with the same symbol name.
        return TypeScriptSyntax.HasBody(candidate) || !TypeScriptSyntax.HasBody(existing);
    }

    private sealed record Declaration(string Name, INode Node, NodeType Kind);
}
