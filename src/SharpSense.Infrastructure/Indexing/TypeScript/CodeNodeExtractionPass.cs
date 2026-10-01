using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TreeSitter;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

/// <summary>Indexes exported declarations and members; export aliases reference their original declaration.</summary>
internal sealed partial class CodeNodeExtractionPass : ITypeScriptExtractionPass
{
    public void Execute(TypeScriptPassContext context)
    {
        foreach (var file in context.ParsedFiles)
        {
            var path = file.DiscoveredFile.RelativeFilePath;
            var exports = context.ExportsByFile[path] = new(StringComparer.Ordinal);
            var declarations = new Dictionary<string, Declaration>(StringComparer.Ordinal);
            var importedBindings = ReadImportedBindings(file.RootNode);
            var exportNodes = file.RootNode.NamedChildren
                .Where(node => node.Type == "export_statement")
                .ToArray();
            foreach (var node in file.RootNode.NamedChildren)
            {
                foreach (var declaration in ReadDeclarations(
                    node.Type == "export_statement" ? node.NamedChildren : [node],
                    file))
                {
                    // Prefer implementation over overload signature with same symbol name.
                    if (!declarations.TryGetValue(declaration.Name, out var existing) ||
                        declaration.Node.GetChildForField("body") is not null || existing.Node.Type == "function_signature")
                    {
                        declarations[declaration.Name] = declaration;
                    }
                }
            }

            foreach (var export in exportNodes)
            {
                var source = export.GetChildForField("source");
                if (source is not null)
                {
                    AddReExports(context, path, export, source.Text.Trim('\'', '"'));
                    continue;
                }

                var isDefault = DefaultExportRegex()
                    .IsMatch(export.Text);
                foreach (var declaration in ReadDeclarations(export.NamedChildren, file))
                {
                    var selected = declarations.GetValueOrDefault(declaration.Name, declaration);
                    var id = AddNode(context, file, selected.Node, selected.Name, selected.Kind, export);
                    exports[isDefault ? "default" : selected.Name] = id;
                    AddMembers(context, file, selected, id);
                }

                foreach (var specifier in export.NamedChildren
                    .Where(node => node.Type == "export_clause")
                    .SelectMany(node => node.NamedChildren)
                    .Where(node => node.Type == "export_specifier"))
                {
                    var local = specifier.GetChildForField("name")?.Text;
                    var alias = specifier.GetChildForField("alias")?.Text.Trim('\'', '"') ?? local;
                    if (local is not null && alias is not null && declarations.TryGetValue(local, out var declaration))
                    {
                        exports[alias] = AddNode(
                            context,
                            file,
                            declaration.Node,
                            declaration.Name,
                            declaration.Kind,
                            declaration.Node);
                        AddMembers(context, file, declaration, exports[alias]);
                    }
                    else if (local is not null && alias is not null && importedBindings.TryGetValue(
                        local,
                        out var imported))
                    {
                        context.ReExports.Add(new(path, imported.Source, imported.ImportedName, alias));
                    }
                }

                if (isDefault)
                {
                    var value = export.GetChildForField("value");
                    if (value?.Type == "identifier" && declarations.TryGetValue(value.Text, out var declaration))
                    {
                        exports["default"] = AddNode(
                            context,
                            file,
                            declaration.Node,
                            declaration.Name,
                            declaration.Kind,
                            declaration.Node);
                        AddMembers(context, file, declaration, exports["default"]);
                    }
                    else if (value?.Type == "identifier" && importedBindings.TryGetValue(value.Text, out var imported))
                    {
                        context.ReExports.Add(new(path, imported.Source, imported.ImportedName, "default"));
                    }
                    else if (value is not null && IsFunction(value))
                    {
                        exports["default"] = AddNode(
                            context,
                            file,
                            value,
                            "default",
                            FunctionKind(
                                file,
                                "default",
                                value),
                            export);
                    }
                }
            }
        }
    }

    private static Dictionary<string, (string Source, string? ImportedName)> ReadImportedBindings(Node root)
    {
        var bindings = new Dictionary<string, (string Source, string? ImportedName)>(StringComparer.Ordinal);
        foreach (var import in root.NamedChildren.Where(node => node.Type == "import_statement"))
        {
            var source = import.GetChildForField("source")?.Text.Trim('\'', '"');
            if (source is null)
            {
                continue;
            }

            foreach (var binding in TypeScriptSyntax.ReadImports(import))
            {
                var importedName = binding.IsNamespaceImport ? null : binding.ExportName ?? "default";
                bindings[binding.LocalName] = (source, importedName);
            }
        }

        return bindings;
    }

    private static IEnumerable<Declaration> ReadDeclarations(IEnumerable<Node> nodes, TypeScriptParsedFile file)
    {
        foreach (var node in nodes)
        {
            if (node.Type == "ambient_declaration")
            {
                foreach (var declaration in ReadDeclarations(node.NamedChildren, file))
                {
                    yield return declaration;
                }
                continue;
            }

            if (node.Type is "lexical_declaration" or "variable_declaration")
            {
                // Only direct declarators: local variables inside exported functions are not exports.
                foreach (var variable in node.NamedChildren.Where(child => child.Type == "variable_declarator"))
                {
                    var name = variable.GetChildForField("name");
                    var value = variable.GetChildForField("value");
                    if (name?.Type != "identifier")
                    {
                        continue;
                    }
                    yield return new(
                        name.Text,
                        variable,
                        value is not null && IsFunction(value) ? FunctionKind(file, name.Text, value) : NodeType.Field);
                }
                continue;
            }

            var nameNode = node.GetChildForField("name");
            var kind = node.Type switch
            {
                "function_declaration" or "generator_function_declaration" or "function_signature" =>
                    FunctionKind(file, nameNode?.Text ?? "default", node),
                "class_declaration" or "abstract_class_declaration" or "class" => NodeType.Class,
                "interface_declaration" or "type_alias_declaration" => NodeType.Interface,
                "enum_declaration" => NodeType.Class,
                _ => (NodeType?)null
            };
            if (kind is not null)
            {
                yield return new(nameNode?.Text ?? "default", node, kind.Value);
            }
        }
    }

    private static void AddMembers(
        TypeScriptPassContext context,
        TypeScriptParsedFile file,
        Declaration declaration,
        string parentId)
    {
        if (declaration.Kind is not NodeType.Class and not NodeType.Interface ||
            declaration.Node.GetChildForField("body") is not
            { } body)
        {
            return;
        }

        foreach (var member in body.NamedChildren)
        {
            var name = member.GetChildForField("name");
            var kind = member.Type switch
            {
                "method_definition" or "method_signature" or "abstract_method_signature" => NodeType.Method,
                "public_field_definition" or "property_signature" => NodeType.Property,
                "enum_assignment" => NodeType.Field,
                _ => (NodeType?)null
            };
            if (name is null || kind is null || name.Type is "computed_property_name" or "private_property_identifier")
            {
                continue;
            }

            var isStatic = member.Children.Any(child => child.Type == "static");
            var accessor = member.Children.FirstOrDefault(child => child.Type is "get" or "set")?.Type;
            var memberKind = (isStatic, accessor) switch
            {
                (true, not null) => $"static:{accessor}",
                (true, null) => "static",
                (false, not null) => accessor,
                _ => null
            };
            var id = AddNode(
                context,
                file,
                member,
                $"{declaration.Name}.{name.Text.Trim('\'', '"')}",
                accessor is null ? kind.Value : NodeType.Property,
                member,
                memberKind);
            if (!context.Edges
                .Any(edge => edge.CallerId == parentId && edge.CalleeId == id && edge.EdgeType == EdgeType.ParentOf))
            {
                context.Edges.Add(new(parentId, id, EdgeType.ParentOf));
            }
        }
    }

    private static string AddNode(
        TypeScriptPassContext context,
        TypeScriptParsedFile file,
        Node node,
        string name,
        NodeType kind,
        Node documentationNode,
        string? memberKind = null)
    {
        var path = file.DiscoveredFile.RelativeFilePath;
        var id = TypeScriptNodeIdentity.CreateCanonicalId(path, name) +
                 (memberKind is null
                     ? string.Empty
                     : $":{memberKind}");
        var existingIndex = context.CodeNodes.FindIndex(candidate => candidate.CanonicalId == id);
        if (existingIndex >= 0)
        {
            var existing = context.DeclarationNodes[id];
            if (node.GetChildForField("body") is null || existing.GetChildForField("body") is not null)
            {
                return id;
            }
            // Method overloads share one symbol; implementation owns hash, location and outgoing calls.
            context.CodeNodes.RemoveAt(existingIndex);
        }

        var symbolName = memberKind is null
            ? name
            : $"{name} [{memberKind}]";
        var display = TypeScriptNodeIdentity.CreateDisplayName(symbolName, kind);
        var summary = ReadDocumentation(file.SourceText, documentationNode);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.Text)))
            .ToLowerInvariant();
        context.DeclarationNodes[id] = node;
        context.CodeNodes.Add(new IndexedCodeNode(
            id,
            null,
            TypeScriptNodeIdentity.CreateFullyQualifiedName(path, symbolName),
            display,
            kind,
            path,
            node.StartPosition.Row + 1,
            node.EndPosition.Row + 1,
            summary,
            TypeScriptNodeIdentity.CreateSearchText(display, summary),
            hash));

        return id;
    }

    private static string ReadDocumentation(string source, Node declaration)
    {
        // Positions count UTF-8 bytes; line indexing avoids slicing .NET UTF-16 by byte offset.
        var lines = source.Split('\n');
        var line = Math.Min(declaration.StartPosition.Row - 1, lines.Length - 1);
        while (line >= 0 && string.IsNullOrWhiteSpace(lines[line]))
        {
            line--;
        }
        if (line < 0 || !lines[line].TrimEnd()
            .EndsWith("*/", StringComparison.Ordinal))
        {
            return string.Empty;
        }
        var end = line;
        while (line >= 0 && !lines[line].Contains("/**", StringComparison.Ordinal))
        {
            line--;
        }
        if (line < 0)
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            lines[line..(end + 1)]
                .Select(text => text.Trim()
                    .TrimStart('/')
                    .Trim('*', '/', ' ')))
            .Trim();
    }

    private static void AddReExports(TypeScriptPassContext context, string path, Node export, string source)
    {
        var clause = export.NamedChildren.FirstOrDefault(node => node.Type == "export_clause");
        if (clause is null)
        {
            var namespaceExport = export.NamedChildren.FirstOrDefault(node => node.Type == "namespace_export");
            var alias = namespaceExport?.NamedChildren.LastOrDefault()?.Text;
            context.ReExports.Add(new(path, source, null, alias));

            return;
        }

        foreach (var binding in clause.NamedChildren.Where(node => node.Type == "export_specifier"))
        {
            var name = binding.GetChildForField("name")?.Text.Trim('\'', '"');
            var alias = binding.GetChildForField("alias")?.Text.Trim('\'', '"') ?? name;
            if (name is not null && alias is not null)
            {
                context.ReExports.Add(new(path, source, name, alias));
            }
        }
    }

    private static NodeType FunctionKind(TypeScriptParsedFile file, string name, Node node)
        => file.DiscoveredFile.RelativeFilePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) &&
           (name == "default" || char.IsUpper(name[0])) &&
           Descendants(node)
               .Any(child => child.Type is "jsx_element" or "jsx_self_closing_element" or "jsx_fragment")
            ? NodeType.Component
            : NodeType.Method;

    private static bool IsFunction(Node node)
        => node.Type is "arrow_function" or "function_expression" or "function_declaration" or "generator_function";

    internal static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (var child in node.NamedChildren)
        {
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    [GeneratedRegex(@"^export\s+default\b")]
    private static partial Regex DefaultExportRegex();

    private sealed record Declaration(string Name, Node Node, NodeType Kind);
}
