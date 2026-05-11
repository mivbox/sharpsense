namespace SharpSense.Infrastructure.HybridSearch;

internal static class HybridSearchTokenizer
{
    private static readonly char[] SearchTokenSeparators =
    [
        ' ',
        '\t',
        '\r',
        '\n',
        '.',
        ':',
        '-',
        '_',
        '/',
        '\\',
        '(',
        ')',
        '[',
        ']',
        '<',
        '>',
        ',',
        ';'
    ];

    public static string[] Tokenize(string searchText)
        => searchText
            .Split(SearchTokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

