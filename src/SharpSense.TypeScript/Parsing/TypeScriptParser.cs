namespace SharpSense.TypeScript.Parsing;

/// <summary>Parses TypeScript or TSX into an owned syntax tree without shared parser state.</summary>
public static class TypeScriptParser
{
    public static TypeScriptSyntaxTree Parse(
        string fileName,
        string sourceText,
        CancellationToken cancellationToken = default)
        => Parse(fileName, sourceText, new TypeScriptParseOptions(), cancellationToken);

    /// <summary>Parses a file within the supplied resource limits; cancellation and limit failures propagate.</summary>
    public static TypeScriptSyntaxTree Parse(
        string fileName,
        string sourceText,
        TypeScriptParseOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceText.Length > options.MaxSourceLength)
        {
            throw new TypeScriptParseLimitException(nameof(options.MaxSourceLength), options.MaxSourceLength);
        }

        var parser = new Parser(new ParseBudget(options, cancellationToken));
        var sourceFile = parser.ParseSourceFile(fileName, sourceText);

        return new TypeScriptSyntaxTree(sourceFile, sourceText, cancellationToken);
    }
}
