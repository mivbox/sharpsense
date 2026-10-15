namespace SharpSense.TypeScript.Parsing.Models;

public class ModifierToken : Token, IModifier
{
    public ModifierToken(SyntaxKind kind)
        : base(kind)
    {
    }

    public ModifierToken()
        : base(SyntaxKind.Unknown)
    {
    }
}
