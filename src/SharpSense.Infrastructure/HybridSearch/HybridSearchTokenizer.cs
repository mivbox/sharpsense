namespace SharpSense.Infrastructure.HybridSearch;

internal static class HybridSearchTokenizer
{
    private static readonly char[] _searchTokenSeparators =
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
        ';',
        '*'
    ];

    public static string[] Tokenize(string searchText)
        => searchText
            .Split(_searchTokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

