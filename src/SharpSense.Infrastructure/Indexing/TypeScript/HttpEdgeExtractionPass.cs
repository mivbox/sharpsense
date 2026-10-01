using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using TreeSitter;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class HttpEdgeExtractionPass : ITypeScriptExtractionPass
{
    public void Execute(TypeScriptPassContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seenEdges = context.Edges
            .Select(static edge => (edge.CallerId, edge.CalleeId, edge.EdgeType))
            .ToHashSet();

        foreach (var parsedFile in context.ParsedFiles)
        {
            var fileCodeNodes = context.CodeNodes
                .Where(codeNode => string.Equals(
                    codeNode.RelativeFilePath,
                    parsedFile.DiscoveredFile.RelativeFilePath,
                    StringComparison.Ordinal))
                .ToArray();
            if (fileCodeNodes.Length == 0)
            {
                continue;
            }

            foreach (var callExpression in TypeScriptSyntax.EnumerateDescendants(parsedFile.RootNode)
                .Where(static node => node.Type == "call_expression"))
            {
                if (!TryBuildHttpRequest(callExpression, out var method, out var url))
                {
                    continue;
                }

                var callerNode = fileCodeNodes
                    .Where(codeNode =>
                        context.DeclarationNodes.TryGetValue(codeNode.CanonicalId, out var declaration) &&
                        declaration.StartIndex <= callExpression.StartIndex &&
                        declaration.EndIndex >= callExpression.EndIndex)
                    .OrderBy(codeNode => context.DeclarationNodes[codeNode.CanonicalId].EndIndex - context.DeclarationNodes[codeNode.CanonicalId].StartIndex)
                    .ThenBy(codeNode => codeNode.StartLine)
                    .FirstOrDefault();
                if (callerNode is null)
                {
                    continue;
                }

                var calleeId = HttpNodeIdentity.CreateCanonicalId(method, url);
                if (!seenEdges.Add((callerNode.CanonicalId, calleeId, EdgeType.HttpRequest)))
                {
                    continue;
                }

                context.Edges.Add(new IndexedDependency(
                    callerNode.CanonicalId,
                    calleeId,
                    EdgeType.HttpRequest,
                    url));
            }
        }
    }

    private static bool TryBuildHttpRequest(
        Node callExpression,
        out string method,
        out string url)
    {
        method = string.Empty;
        url = string.Empty;

        var functionNode = callExpression.GetChildForField("function");
        var argumentsNode = callExpression.GetChildForField("arguments");
        if (functionNode is null ||
            argumentsNode is null)
        {
            return false;
        }

        var argumentNodes = argumentsNode.NamedChildren
            .Where(static node => node.Type != "comment")
            .ToArray();
        if (functionNode.Text == "fetch")
        {
            if (argumentNodes.Length == 0 ||
                !TryExtractStringLiteral(argumentNodes[0], out url))
            {
                return false;
            }

            if (argumentNodes.Length > 1)
            {
                return TryExtractFetchMethod(argumentNodes[1], out method);
            }

            method = "GET";

            return true;
        }

        if (functionNode.Type != "member_expression")
        {
            return false;
        }

        var objectNode = functionNode.GetChildForField("object");
        var propertyNode = functionNode.GetChildForField("property");
        if (objectNode is null ||
            propertyNode is null ||
            objectNode.Text != "axios" ||
            !TryExtractAxiosMethod(propertyNode.Text, out method) ||
            argumentNodes.Length == 0 ||
            !TryExtractStringLiteral(argumentNodes[0], out url))
        {
            return false;
        }

        return true;
    }

    private static bool TryExtractStringLiteral(
        Node node,
        out string value)
    {
        value = string.Empty;
        if (node.Type is not ("string" or "template_string"))
        {
            return false;
        }

        var text = node.Text;
        if (string.IsNullOrWhiteSpace(text) ||
            text.Length < 2)
        {
            return false;
        }

        if (text[0] is '\'' or '"' &&
            text[^1] == text[0])
        {
            value = text[1..^1];

            return !string.IsNullOrWhiteSpace(value);
        }

        if (text[0] == '`' &&
            text[^1] == '`' &&
            !text.Contains("${", StringComparison.Ordinal))
        {
            value = text[1..^1];

            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static bool TryExtractFetchMethod(
        Node options,
        out string method)
    {
        method = "GET";
        if (options.Type != "object")
        {
            return false;
        }

        foreach (var property in options.NamedChildren)
        {
            if (property.Type == "comment")
            {
                continue;
            }

            var key = property.Type switch
            {
                "pair" => property.GetChildForField("key"),
                "method_definition" => property.GetChildForField("name"),
                "shorthand_property_identifier" => property,
                _ => null
            };
            // Spreads, computed names and prototype setters can introduce an unknown method.
            // Inspect only direct object properties: nested headers and comments are unrelated.
            if (key is null || !TryExtractPropertyName(key, out var name) || name == "__proto__")
            {
                return false;
            }

            if (name != "method")
            {
                continue;
            }

            var value = property.Type == "pair" ? property.GetChildForField("value") : null;
            if (value is null || !TryExtractStringLiteral(value, out var literal) || !literal.All(char.IsAsciiLetter))
            {
                return false;
            }

            // JavaScript object literals retain the last explicitly declared property value.
            method = literal.ToUpperInvariant();
        }

        return true;
    }

    private static bool TryExtractPropertyName(Node node, out string name)
    {
        name = node.Text;
        if (node.Type == "string")
        {
            if (!TryExtractStringLiteral(node, out name))
            {
                return false;
            }
        }
        else if (node.Type is not ("property_identifier" or "identifier" or "shorthand_property_identifier" or "number"))
        {
            return false;
        }

        // Escaped keys need JavaScript decoding before we could prove they are not "method".
        return !name.Contains('\\');
    }

    private static bool TryExtractAxiosMethod(
        string propertyName,
        out string method)
    {
        method = propertyName.ToUpperInvariant();

        return method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS";
    }
}
