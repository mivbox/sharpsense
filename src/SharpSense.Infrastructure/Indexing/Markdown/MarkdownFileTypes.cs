namespace SharpSense.Infrastructure.Indexing.Markdown;

internal static class MarkdownFileTypes
{
    private static readonly string[] _extensions = [".md", ".markdown", ".mdown", ".mkd"];

    public static bool IsMarkdown(string path)
        => _extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
