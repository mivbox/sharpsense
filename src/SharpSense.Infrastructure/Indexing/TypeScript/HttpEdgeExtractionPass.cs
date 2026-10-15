using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.TypeScript.Parsing.Abstractions;
using SharpSense.TypeScript.Parsing.Models;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class HttpEdgeExtractionPass : ITypeScriptExtractionPass
{
    public void Execute(TypeScriptPassContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seenEdges = context.Edges
            .Select(static edge => (edge.CallerId, edge.CalleeId, edge.EdgeType))
            .ToHashSet();
        var codeNodesByFile = context.CodeNodes.ToLookup(static node => node.RelativeFilePath, StringComparer.Ordinal);

        foreach (var parsedFile in context.ParsedFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var fileCodeNodes = codeNodesByFile[parsedFile.DiscoveredFile.RelativeFilePath]
                .Where(codeNode => context.DeclarationNodes.ContainsKey(codeNode.CanonicalId))
                .OrderBy(codeNode => context.DeclarationNodes[codeNode.CanonicalId].End - context.DeclarationNodes[codeNode.CanonicalId].Pos)
                .ThenBy(codeNode => codeNode.StartLine)
                .ToArray();
            if (fileCodeNodes.Length == 0)
            {
                continue;
            }

            foreach (var call in parsedFile.RootNode.DescendantsAndSelf(context.CancellationToken).OfType<CallExpression>())
            {
                if (!TryBuildHttpRequest(call, parsedFile.SyntaxTree, out var method, out var url))
                {
                    continue;
                }

                var callerNode = fileCodeNodes
                    .FirstOrDefault(codeNode => context.DeclarationNodes[codeNode.CanonicalId].Pos <= call.Pos &&
                        context.DeclarationNodes[codeNode.CanonicalId].End >= call.End);
                if (callerNode is null)
                {
                    continue;
                }

                var calleeId = HttpNodeIdentity.CreateCanonicalId(method, url);
                if (seenEdges.Add((callerNode.CanonicalId, calleeId, EdgeType.HttpRequest)))
                {
                    context.Edges.Add(new IndexedDependency(callerNode.CanonicalId, calleeId, EdgeType.HttpRequest, url));
                }
            }
        }
    }

    private static bool TryBuildHttpRequest(
        CallExpression call,
        TypeScriptSyntaxTree tree,
        out string method,
        out string url)
    {
        method = string.Empty;
        url = string.Empty;
        if (call.Arguments is not { Count: > 0 } arguments || !TryExtractStringLiteral(arguments[0], out url))
        {
            return false;
        }

        if (call.Expression is Identifier { Text: "fetch" })
        {
            if (arguments.Count > 1)
            {
                return TryExtractFetchMethod(arguments[1], tree, out method);
            }

            method = "GET";

            return true;
        }

        if (call.Expression is not PropertyAccessExpression { Expression: Identifier { Text: "axios" }, Name.Text: { } propertyName })
        {
            return false;
        }

        method = propertyName.ToUpperInvariant();

        return method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS";
    }

    private static bool TryExtractStringLiteral(INode node, out string value)
    {
        value = node is ILiteralLikeNode literal && node.Kind is SyntaxKind.StringLiteral or SyntaxKind.NoSubstitutionTemplateLiteral
            ? literal.Text ?? string.Empty
            : string.Empty;

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryExtractFetchMethod(INode options, TypeScriptSyntaxTree tree, out string method)
    {
        method = "GET";
        if (options is not ObjectLiteralExpression literal)
        {
            return false;
        }

        foreach (var property in literal.Properties)
        {
            var key = (property as IHasNameProperty)?.Name;
            var name = TypeScriptSyntax.GetName(key);
            // Spreads, computed keys and prototype setters can introduce an unknown method.
            // Inspect only direct properties: nested headers and comments are unrelated.
            if (key is null || name is null || name == "__proto__" || tree.GetText(key).Contains('\\'))
            {
                return false;
            }

            if (name != "method")
            {
                continue;
            }

            if (property is not PropertyAssignment { Initializer: { } value } ||
                !TryExtractStringLiteral(value, out var verb) || !verb.All(char.IsAsciiLetter))
            {
                return false;
            }

            // JavaScript object literals retain the last explicitly declared property value.
            method = verb.ToUpperInvariant();
        }

        return true;
    }
}
