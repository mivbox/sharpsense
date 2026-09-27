using SharpSense.Application.Indexing.Models;
using TreeSitter;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TypeScriptParsedFile(
    DiscoveredFile discoveredFile,
    string sourceText,
    Tree syntaxTree) : IDisposable
{
    public DiscoveredFile DiscoveredFile
    {
        get;
    } = discoveredFile ?? throw new ArgumentNullException(nameof(discoveredFile));

    public string SourceText
    {
        get;
    } = sourceText ?? throw new ArgumentNullException(nameof(sourceText));

    public Tree SyntaxTree
    {
        get;
    } = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));

    public Node RootNode => SyntaxTree.RootNode;

    public void Dispose()
        => SyntaxTree.Dispose();
}
