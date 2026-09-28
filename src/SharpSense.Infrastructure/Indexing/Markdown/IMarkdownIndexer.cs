namespace SharpSense.Infrastructure.Indexing.Markdown;

/// <summary>
/// Converts raw Markdown text plus its canonical repository-relative path into the document code nodes and dependency
/// edges that the indexing pipeline persists. Implementations own Markdown parsing, heading chunking, and document-link
/// resolution so discovery code can remain focused on locating and reading files.
/// </summary>
internal interface IMarkdownIndexer
{
    /// <summary>
    /// Indexes the supplied Markdown payload into document nodes and document-link or hierarchy edges using the supplied
    /// repository-relative path as the canonical identity root.
    /// </summary>
    /// <param name="rawText">The raw Markdown content to index.</param>
    /// <param name="relativeFilePath">The canonical repository-relative file path for the Markdown document.</param>
    /// <returns>The Markdown indexing result for the supplied document.</returns>
    MarkdownIndexResult Index(
        string rawText,
        string relativeFilePath);
}
