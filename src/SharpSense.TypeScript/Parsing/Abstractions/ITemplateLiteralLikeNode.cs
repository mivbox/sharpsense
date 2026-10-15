namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface ITemplateLiteralLikeNode : ILiteralLikeNode
{
    string? RawText { get; }

    TokenFlags? TemplateFlags { get; }
}
