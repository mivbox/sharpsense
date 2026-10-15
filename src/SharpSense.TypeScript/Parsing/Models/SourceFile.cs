namespace SharpSense.TypeScript.Parsing.Models;

public class SourceFile : Node, IDeclaration, IBlockLike, IGetRestChildren
{
    public SourceFile()
        : base(SyntaxKind.SourceFile)
    {
    }

    internal SourceFile(
        string sourceText,
        string fileName,
        ScriptTarget languageVersion,
        ScriptKind scriptKind,
        bool isDeclarationFile,
        NodeArray<IStatement> statements,
        EndOfFileToken endOfFileToken,
        NodeFlags flags) : this()
    {
        // code from createNode is inlined here so createNode won't have to deal with special case of creating source files
        // this is quite rare comparing to other nodes and createNode should be as fast as possible
        Statements = statements;
        EndOfFileToken = endOfFileToken;
        Flags = flags;
        Utilities.SetTextRangePosWidth(this, 0, sourceText.Length);

        Text = sourceText;
        LanguageVersion = languageVersion;
        FileName = fileName;
        LanguageVariant = Utilities.GetLanguageVariant(scriptKind);
        IsDeclarationFile = isDeclarationFile;
        ScriptKind = scriptKind;
    }

    public NodeArray<IStatement>? Statements { get; internal set; }

    public Token? EndOfFileToken { get; internal set; }

    public string? FileName { get; internal set; }

    public LanguageVariant LanguageVariant { get; internal set; }

    public ScriptTarget LanguageVersion { get; internal set; }

    public bool IsDeclarationFile { get; internal set; }

    public ScriptKind ScriptKind { get; internal set; }

    public INode? ExternalModuleIndicator { get; internal set; }

    internal List<Diagnostic>? ParseDiagnostics { get; set; }

    public string? Text { get; internal set; }

    internal IEnumerable<CommentDirective>? CommentDirectives { get; set; }

    internal JSDocParsingMode JsDocParsingMode { get; set; }

    public IEnumerable<INode?> GetRestChildren()
    {
        if (Statements != null)
        {
            foreach (var x in Statements)
            {
                yield return x;
            }
        }

        yield return EndOfFileToken;
    }
}
