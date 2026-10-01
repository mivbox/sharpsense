namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal static class TypeScriptSyntax
{
    public static TypeScriptImportBinding[] ReadImports(TreeSitter.Node importNode)
    {
        var clause = importNode.NamedChildren.FirstOrDefault(node => node.Type == "import_clause");
        if (clause is null)
        {
            return [];
        }

        var bindings = new List<TypeScriptImportBinding>();
        foreach (var child in clause.NamedChildren)
        {
            if (child.Type == "identifier")
            {
                bindings.Add(new(null, child.Text, false));
            }
            else if (child.Type == "namespace_import")
            {
                var local = child.NamedChildren.FirstOrDefault(node => node.Type == "identifier")?.Text;
                if (local is not null)
                {
                    bindings.Add(new(null, local, true));
                }
            }
            else if (child.Type == "named_imports")
            {
                foreach (var specifier in child.NamedChildren.Where(node => node.Type == "import_specifier"))
                {
                    var name = specifier.GetChildForField("name")?.Text.Trim('\'', '"');
                    var local = specifier.GetChildForField("alias")?.Text ?? name;
                    if (name is not null && local is not null)
                    {
                        bindings.Add(new(name, local, false));
                    }
                }
            }
        }

        return [.. bindings];
    }

    public static IEnumerable<TreeSitter.Node> EnumerateDescendants(TreeSitter.Node node)
    {
        yield return node;

        foreach (var childNode in node.NamedChildren)
        {
            foreach (var descendantNode in EnumerateDescendants(childNode))
            {
                yield return descendantNode;
            }
        }
    }
}
