namespace SharpSense.TypeScript.Parsing.Models;

/// <summary>A parsed source file and its diagnostics. Syntax node offsets use UTF-16 code units.</summary>
public sealed class TypeScriptSyntaxTree
{
    private readonly int[] _lineStarts;

    internal TypeScriptSyntaxTree(SourceFile root, string sourceText, CancellationToken cancellationToken)
    {
        Root = root;
        SourceText = sourceText;
        var lineStarts = new List<int> { 0 };
        for (var index = 0; index < sourceText.Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (sourceText[index] == '\r' && index + 1 < sourceText.Length && sourceText[index + 1] == '\n')
            {
                index++;
                lineStarts.Add(index + 1);
            }
            else if (sourceText[index] is '\r' or '\n' or '\u2028' or '\u2029')
            {
                lineStarts.Add(index + 1);
            }
        }

        _lineStarts = [.. lineStarts];
        Diagnostics = (root.ParseDiagnostics ?? [])
            .Select(diagnostic =>
            {
                var position = GetPosition(diagnostic.Start);
                var message = diagnostic.Message?.Message ?? "Invalid TypeScript syntax.";
                if (diagnostic.Argument is not null)
                {
                    message = message.Replace(
                        "{0}",
                        Convert.ToString(diagnostic.Argument, CultureInfo.InvariantCulture),
                        StringComparison.Ordinal);
                }

                return new ParseDiagnostic(
                    diagnostic.Message?.Code ?? 0,
                    message,
                    diagnostic.Start,
                    diagnostic.Length,
                    position.Line,
                    position.Column);
            })
            .ToList()
            .AsReadOnly();
    }

    public SourceFile Root { get; }

    public string SourceText { get; }

    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Count > 0;

    public int LineCount => _lineStarts.Length;

    /// <summary>Returns a one-based source line without its line terminator or copying the text.</summary>
    public ReadOnlySpan<char> GetLineText(int line)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(line, LineCount);

        var start = _lineStarts[line - 1];
        var end = line < LineCount ? _lineStarts[line] : SourceText.Length;
        while (end > start && SourceText[end - 1] is '\r' or '\n' or '\u2028' or '\u2029')
        {
            end--;
        }

        return SourceText.AsSpan(start, end - start);
    }

    public int GetStart(INode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return GetTokenStart(node.Pos ?? 0);
    }

    /// <summary>Skips whitespace and comments from a UTF-16 source offset.</summary>
    public int GetTokenStart(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, SourceText.Length);

        return Scanner.SkipTrivia(SourceText, offset) ?? offset;
    }

    public string GetText(INode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return SourceText[GetStart(node)..(node.End ?? SourceText.Length)];
    }

    public SourcePosition GetPosition(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, SourceText.Length);

        var lineIndex = Array.BinarySearch(_lineStarts, offset);
        if (lineIndex < 0)
        {
            lineIndex = ~lineIndex - 1;
        }

        return new SourcePosition(lineIndex + 1, offset - _lineStarts[lineIndex] + 1);
    }
}
