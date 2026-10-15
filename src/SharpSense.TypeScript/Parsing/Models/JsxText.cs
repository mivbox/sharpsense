namespace SharpSense.TypeScript.Parsing.Models;

public class JsxText(string text, bool containsOnlyTriviaWhiteSpaces)
    : LiteralLikeNode(SyntaxKind.JsxText, text), IJsxChild
{
    public bool ContainsOnlyTriviaWhiteSpaces { get; } = containsOnlyTriviaWhiteSpaces;
}
