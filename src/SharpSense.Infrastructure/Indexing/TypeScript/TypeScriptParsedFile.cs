using SharpSense.Application.Indexing.Models;
using SharpSense.TypeScript.Parsing.Models;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TypeScriptParsedFile(DiscoveredFile discoveredFile, TypeScriptSyntaxTree syntaxTree)
{
    public DiscoveredFile DiscoveredFile { get; } = discoveredFile;

    public TypeScriptSyntaxTree SyntaxTree { get; } = syntaxTree;

    public string SourceText => SyntaxTree.SourceText;

    public SourceFile RootNode => SyntaxTree.Root;
}
