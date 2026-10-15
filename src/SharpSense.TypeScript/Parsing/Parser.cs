// Initially converted from https://github.com/microsoft/TypeScript/commit/fbcdb8cf4fbbbea0111a9adeb9d0d2983c088b7c on 2023-12-21
// Last updated from commit 9757109cafcb771a35ad9fe09855373cdd82005a on 2024-07-29

using static SharpSense.TypeScript.Parsing.Utilities;
using static SharpSense.TypeScript.Scanning.Scanner;

namespace SharpSense.TypeScript.Parsing;

internal sealed class Parser
{
    private readonly Scanner _scanner;
    private readonly ParseBudget _budget;
    private readonly NodeFlags _disAllowInAndDecoratorContext = NodeFlags.DisallowInContext | NodeFlags.DecoratorContext;
    private string _fileName = null!;
    private NodeFlags _sourceFlags;
    private string? _sourceText;
    private ScriptTarget _languageVersion;
    private LanguageVariant _languageVariant;
    private List<Diagnostic> _parseDiagnostics = null!;
    private ISyntaxCursor? _syntaxCursor;

    private SyntaxKind _currentToken;
    private HashSet<string> _identifiers = null!;

    // TODO(jakebailey): This type is a lie; this value actually contains the result
    // of ORing a bunch of `1 << ParsingContext.XYZ`.
    private ParsingContext _parsingContext;

    private HashSet<int> _notParenthesizedArrow = null!;

    // Flags that dictate what parsing context we're in.  For example:
    // Whether or not we are in strict parsing mode.  All that changes in strict parsing mode is
    // that some tokens that would be considered identifiers may be considered keywords.
    //
    // When adding more parser context flags, consider which is the more common case that the
    // flag will be in.  This should be the 'false' state for that flag.  The reason for this is
    // that we don't store data in our nodes unless the value is in the *non-default* state.  So,
    // for example, more often than code 'allows-in' (or doesn't 'disallow-in').  We opt for
    // 'disallow-in' set to 'false'.  Otherwise, if we had 'allowsIn' set to 'true', then almost
    // all nodes would need extra state on them to store this info.
    //
    // Note: 'allowIn' and 'allowYield' track 1:1 with the [in] and [yield] concepts in the ES6
    // grammar specification.
    //
    // An important thing about these context concepts.  By default they are effectively inherited
    // while parsing through every grammar production.  i.e. if you don't change them, then when
    // you parse a sub-production, it will have the same context values as the parent production.
    // This is great most of the time.  After all, consider all the 'expression' grammar productions
    // and how nearly all of them pass along the 'in' and 'yield' context values:
    //
    // EqualityExpression[In, Yield] :
    //      RelationalExpression[?In, ?Yield]
    //      EqualityExpression[?In, ?Yield] == RelationalExpression[?In, ?Yield]
    //      EqualityExpression[?In, ?Yield] != RelationalExpression[?In, ?Yield]
    //      EqualityExpression[?In, ?Yield] === RelationalExpression[?In, ?Yield]
    //      EqualityExpression[?In, ?Yield] !== RelationalExpression[?In, ?Yield]
    //
    // Where you have to be careful is then understanding what the points are in the grammar
    // where the values are *not* passed along.  For example:
    //
    // SingleNameBinding[Yield,GeneratorParameter]
    //      [+GeneratorParameter]BindingIdentifier[Yield] Initializer[In]opt
    //      [~GeneratorParameter]BindingIdentifier[?Yield]Initializer[In, ?Yield]opt
    //
    // Here this is saying that if the GeneratorParameter context flag is set, that we should
    // explicitly set the 'yield' context flag to false before calling into the BindingIdentifier
    // and we should explicitly unset the 'yield' context flag before calling into the Initializer.
    // production.  Conversely, if the GeneratorParameter context flag is not set, then we
    // should leave the 'yield' context flag alone.
    //
    // Getting this all correct is tricky and requires careful reading of the grammar to
    // understand when these values should be changed versus when they should be inherited.
    //
    // Note: it should not be necessary to save/restore these flags during speculative/lookahead
    // parsing.  These context flags are naturally stored and restored through normal recursive
    // descent parsing and unwinding.
    private NodeFlags _contextFlags;

    // Indicates whether we are currently parsing top-level statements.
    private bool _topLevel = true;

    // Whether or not we've had a parse error since creating the last AST node.  If we have
    // encountered an error, it will be stored on the next AST node we create.  Parse errors
    // can be broken down into three categories:
    //
    // 1) An error that occurred during scanning.  For example, an unterminated literal, or a
    //    character that was completely not understood.
    //
    // 2) A token was expected, but was not present.  This type of error is commonly produced
    //    by the 'parseExpected' function.
    //
    // 3) A token was present that no parsing function was able to consume.  This type of error
    //    only occurs in the 'abortParsingListOrMoveToNextToken' function when the parser
    //    decides to skip the token.
    //
    // In all of these cases, we want to mark the next node as having had an error before it.
    // With this mark, we can know in incremental settings if this node can be reused, or if
    // we have to reparse it.  If we don't keep this information around, we may just reuse the
    // node.  in that event we would then not produce the same errors as we did before, causing
    // significant confusion problems.
    //
    // Note: it is necessary that this value be saved/restored during speculative/lookahead
    // parsing.  During lookahead parsing, we will often create a node.  That node will have
    // this value attached, and then this value will be set back to 'false'.  If we decide to
    // rewind, we must get back to the same value we had prior to the lookahead.
    //
    // Note: any errors at the end of the file that do not precede a regular node, should get
    // attached to the EOF token.
    private bool _parseErrorBeforeNextFinishedNode = false;

    private readonly CancellationToken _cancellationToken;

    public Parser(ParseBudget budget)
    {
        _budget = budget;
        _cancellationToken = budget.CancellationToken;
        _scanner = new Scanner(ScriptTarget.Latest, skipTrivia: true, budget: budget);
    }

    public SourceFile ParseSourceFile(
        string fileName,
        string sourceText,
        ScriptTarget languageVersion = ScriptTarget.Latest,
        ISyntaxCursor? syntaxCursor = null,
        bool setParentNodes = true,
        ScriptKind scriptKind = ScriptKind.Unknown,
        Action<SourceFile>? setExternalModuleIndicatorOverride = null,
        JSDocParsingMode jsDocParsingMode = JSDocParsingMode.ParseNone)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        scriptKind = EnsureScriptKind(fileName, scriptKind);

        if (scriptKind == ScriptKind.JSON)
        {
            throw new ArgumentOutOfRangeException(nameof(scriptKind));
        }

        InitializeState(fileName, sourceText, languageVersion, syntaxCursor, scriptKind, jsDocParsingMode);

        try
        {
            return ParseSourceFileWorker(
                fileName,
                setParentNodes,
                scriptKind,
                setExternalModuleIndicatorOverride ?? SetExternalModuleIndicator,
                jsDocParsingMode);
        }
        finally
        {
            ClearState();
        }
    }

    private void InitializeState(
        string fileName,
        string sourceText,
        ScriptTarget languageVersion,
        ISyntaxCursor? syntaxCursor,
        ScriptKind scriptKind,
        JSDocParsingMode jsDocParsingMode)
    {
        _fileName = NormalizePath(fileName);
        _sourceText = sourceText;
        _languageVersion = languageVersion;
        _syntaxCursor = syntaxCursor;
        _languageVariant = GetLanguageVariant(scriptKind);

        _parseDiagnostics = [];
        _parsingContext = 0;
        _identifiers = [];
        _sourceFlags = 0;
        _topLevel = true;

        _contextFlags = scriptKind switch
        {
            ScriptKind.JS or ScriptKind.JSX => NodeFlags.JavaScriptFile,
            ScriptKind.JSON => NodeFlags.JavaScriptFile | NodeFlags.JsonFile,
            _ => NodeFlags.None,
        };
        _parseErrorBeforeNextFinishedNode = false;

        // Initialize and prime the scanner before parsing the source elements.
        _scanner.SetText(sourceText);
        _scanner.SetOnError(ScanError);
        _scanner.SetScriptTarget(languageVersion);
        _scanner.SetLanguageVariant(_languageVariant);
        _scanner.SetScriptKind(scriptKind);
        _scanner.SetJSDocParsingMode(jsDocParsingMode);
    }

    public void ClearState()
    {
        // Clear out the text the scanner is pointing at, so it doesn't keep anything alive unnecessarily.
        _scanner.ClearCommentDirectives();
        _scanner.SetText("");
        _scanner.SetOnError(null);
        _scanner.SetScriptKind(ScriptKind.Unknown);
        _scanner.SetJSDocParsingMode(JSDocParsingMode.ParseAll);

        // Clear any data.  We don't want to accidentally hold onto it for too long.
        _sourceText = null;
        _languageVersion = ScriptTarget.Latest;
        _syntaxCursor = null;
        _languageVariant = LanguageVariant.Standard;
        _sourceFlags = 0;
        _parseDiagnostics = null!;
        _parsingContext = 0;
        _identifiers = null!;
        _notParenthesizedArrow = null!;
        _topLevel = true;
    }

    private SourceFile ParseSourceFileWorker(
        string fileName,
        bool setParentNodes,
        ScriptKind scriptKind,
        Action<SourceFile> setExternalModuleIndicator,
        JSDocParsingMode jsDocParsingMode)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var isDeclarationFile = IsDeclarationFileName(fileName);
        if (isDeclarationFile)
        {
            _contextFlags |= NodeFlags.Ambient;
        }

        _sourceFlags = _contextFlags;

        // Prime the scanner.
        NextToken();

        var statements = ParseList(ParsingContext.SourceElements, ParseStatement);
        Debug.Assert(Token() == SyntaxKind.EndOfFileToken);
        var endHasJSDoc = HasPrecedingJSDocComment();
        var endOfFileToken = WithJSDoc(ParseTokenNode<EndOfFileToken>(), endHasJSDoc);

        var sourceFile = new SourceFile(
            _sourceText!,
            fileName,
            _languageVersion,
            scriptKind,
            isDeclarationFile,
            statements,
            endOfFileToken,
            _sourceFlags);

        setExternalModuleIndicator(sourceFile);

        sourceFile.CommentDirectives = _scanner.GetCommentDirectives();
        sourceFile.ParseDiagnostics = _parseDiagnostics;
        sourceFile.JsDocParsingMode = jsDocParsingMode;

        if (setParentNodes)
        {
            SetParents(sourceFile);
        }

        return sourceFile;
    }

    private void SetParents(INode rootNode)
    {
        var pending = new Stack<INode>();
        pending.Push(rootNode);
        while (pending.TryPop(out var parent))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            parent.ForEachChild(
                child =>
                {
                    ((Node)child).Parent = parent;
                    pending.Push(child);

                    return null;
                },
                recursively: false);
        }
    }

    private void SetContextFlag(bool val, NodeFlags flag)
    {
        if (val)
        {
            _contextFlags |= flag;
        }
        else
        {
            _contextFlags &= ~flag;
        }
    }

    private static T WithJSDoc<T>(T node, bool _) where T : INode
    {
        // JSDoc is not attached by this parser; extraction reads documentation from the source.
        return node;
    }

    private void SetDisallowInContext(bool val)
    {
        SetContextFlag(val, NodeFlags.DisallowInContext);
    }

    private void SetDecoratorContext(bool val)
    {
        SetContextFlag(val, NodeFlags.DecoratorContext);
    }

    private void SetAwaitContext(bool val)
    {
        SetContextFlag(val, NodeFlags.AwaitContext);
    }

    private void SetYieldContext(bool val)
    {
        SetContextFlag(val, NodeFlags.YieldContext);
    }

    private T DoOutsideOfContext<T>(NodeFlags context, Func<T> func)
    {
        // contextFlagsToClear will contain only the context flags that are
        // currently set that we need to temporarily clear
        // We don't just blindly reset to the previous flags to ensure
        // that we do not mutate cached flags for the incremental
        // parser (ThisNodeHasError, ThisNodeOrAnySubNodesHasError, and
        // HasAggregatedChildData).
        var contextFlagsToClear = context & _contextFlags;
        if (contextFlagsToClear != 0)
        {
            // clear the requested context flags
            SetContextFlag(val: false, contextFlagsToClear);
            var result = func();
            // restore the context flags we just cleared
            SetContextFlag(val: true, contextFlagsToClear);

            return result;
        }

        // no need to do anything special as we are not in any of the requested contexts
        return func();
    }

    private T DoInsideOfContext<T>(NodeFlags context, Func<T> func)
    {
        // contextFlagsToSet will contain only the context flags that
        // are not currently set that we need to temporarily enable.
        // We don't just blindly reset to the previous flags to ensure
        // that we do not mutate cached flags for the incremental
        // Parser (ThisNodeHasError, ThisNodeOrAnySubNodesHasError, and
        // HasAggregatedChildData).
        var contextFlagsToSet = context & ~_contextFlags;
        if (contextFlagsToSet != 0)
        {
            // set the requested context flags
            SetContextFlag(val: true, contextFlagsToSet);
            var result = func();
            // reset the context flags we just set
            SetContextFlag(val: false, contextFlagsToSet);

            return result;
        }

        // no need to do anything special as we are already in all of the requested contexts
        return func();
    }

    private T AllowInAnd<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.DisallowInContext, func);
    }

    private T DisAllowInAnd<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.DisallowInContext, func);
    }

    private T AllowConditionalTypesAnd<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.DisallowConditionalTypesContext, func);
    }

    private T DisallowConditionalTypesAnd<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.DisallowConditionalTypesContext, func);
    }

    private T DoInYieldContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.YieldContext, func);
    }

    private T DoInDecoratorContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.DecoratorContext, func);
    }

    private T DoInAwaitContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.AwaitContext, func);
    }

    private T DoOutsideOfAwaitContext<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.AwaitContext, func);
    }

    private T DoInYieldAndAwaitContext<T>(Func<T> func)
    {
        return DoInsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);
    }

    private T DoOutsideOfYieldAndAwaitContext<T>(Func<T> func)
    {
        return DoOutsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);
    }

    private bool InContext(NodeFlags flags)
    {
        return (_contextFlags & flags) != 0;
    }

    private bool InYieldContext()
    {
        return InContext(NodeFlags.YieldContext);
    }

    private bool InDisallowInContext()
    {
        return InContext(NodeFlags.DisallowInContext);
    }

    private bool InDisallowConditionalTypesContext()
    {
        return InContext(NodeFlags.DisallowConditionalTypesContext);
    }

    private bool InDecoratorContext()
    {
        return InContext(NodeFlags.DecoratorContext);
    }

    private bool InAwaitContext()
    {
        return InContext(NodeFlags.AwaitContext);
    }

    private Diagnostic? ParseErrorAtPosition(int start, int length, DiagnosticMessage message, object? args)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        Diagnostic? result = null;
        // Don't report another error if it would just be at the same position as the last error.
        var lastError = _parseDiagnostics.LastOrDefault();
        if (lastError == null || start != lastError.Start)
        {
            result = CreateDetachedDiagnostic(_fileName, _sourceText!, start, length, message, args);
            _parseDiagnostics.Add(result);
        }

        // Mark that we've encountered an error.  We'll set an appropriate bit on the next
        // node we finish so that it can't be reused incrementally.
        _parseErrorBeforeNextFinishedNode = true;

        return result;
    }

    private Diagnostic? ParseErrorAtCurrentToken(DiagnosticMessage message, object? arg0 = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseErrorAt(_scanner.GetTokenStart(), _scanner.GetTokenEnd(), message, arg0);
    }

    private Diagnostic? ParseErrorAt(int start, int end, DiagnosticMessage message, object? arg0 = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseErrorAtPosition(start, end - start, message, arg0);
    }

    private void ParseErrorAtRange(ITextRange range, DiagnosticMessage message, object? arg0 = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        ParseErrorAt(range.Pos ?? 0, range.End ?? range.Pos ?? 0, message, arg0);
    }

    private void ScanError(DiagnosticMessage message, int length, object? arg0 = null)
    {
        ParseErrorAtPosition(_scanner.GetTokenEnd(), length, message, arg0);
    }

    private int GetNodePos()
    {
        return _scanner.GetTokenFullStart();
    }

    private bool HasPrecedingJSDocComment()
    {
        return _scanner.HasPrecedingJSDocComment();
    }

    // Use this function to access the current token instead of reading the currentToken
    // variable. Since function results aren't narrowed in control flow analysis, this ensures
    // that the type checker doesn't make wrong assumptions about the type of the current
    // token (e.g. a call to nextToken() changes the current token but the checker doesn't
    // reason about this side effect).  Mainstream VMs inline simple functions like this, so
    // there is no performance penalty.
    private SyntaxKind Token()
    {
        return _currentToken;
    }

    private SyntaxKind NextTokenWithoutCheck()
    {
        _cancellationToken.ThrowIfCancellationRequested();

        return _currentToken = _scanner.Scan();
    }

    private T NextTokenAnd<T>(Func<T> func)
    {
        NextToken();

        return func();
    }

    private SyntaxKind NextToken()
    {
        // if the keyword had an escape
        if (IsKeyword(_currentToken) && (_scanner.HasUnicodeEscape() || _scanner.HasExtendedUnicodeEscape()))
        {
            // issue a parse error for the escape
            ParseErrorAt(
                _scanner.GetTokenStart(),
                _scanner.GetTokenEnd(),
                Diagnostics._keywordsCannotContainEscapeCharacters);
        }

        return NextTokenWithoutCheck();
    }

    private SyntaxKind NextTokenJSDoc()
    {
        return _currentToken = _scanner.ScanJsDocToken();
    }

    private SyntaxKind ReScanGreaterToken()
    {
        return _currentToken = _scanner.ReScanGreaterToken();
    }

    private SyntaxKind ReScanSlashToken()
    {
        return _currentToken = _scanner.ReScanSlashToken(reportErrors: false);
    }

    private SyntaxKind ReScanTemplateToken(bool isTaggedTemplate)
    {
        return _currentToken = _scanner.ReScanTemplateToken(isTaggedTemplate);
    }

    private SyntaxKind ReScanLessThanToken()
    {
        return _currentToken = _scanner.ReScanLessThanToken();
    }

    private SyntaxKind ScanJsxIdentifier()
    {
        return _currentToken = _scanner.ScanJsxIdentifier();
    }

    private SyntaxKind ScanJsxText()
    {
        return _currentToken = _scanner.ScanJsxToken();
    }

    private SyntaxKind ScanJsxAttributeValue()
    {
        return _currentToken = _scanner.ScanJsxAttributeValue();
    }

    private T SpeculationHelper<T>(Func<T> callback, SpeculationKind speculationKind)
    {
        // Keep track of the state we'll need to rollback to if lookahead fails (or if the
        // caller asked us to always reset our state).
        var saveToken = _currentToken;
        var saveParseDiagnosticsLength = _parseDiagnostics.Count;
        var saveParseErrorBeforeNextFinishedNode = _parseErrorBeforeNextFinishedNode;

        // Note: it is not actually necessary to save/restore the context flags here.  That's
        // because the saving/restoring of these flags happens naturally through the recursive
        // descent nature of our parser.  However, we still store this here just so we can
        // assert that invariant holds.
        var saveContextFlags = _contextFlags;

        // If we're only looking ahead, then tell the scanner to only lookahead as well.
        // Otherwise, if we're actually speculatively parsing, then tell the scanner to do the
        // same.
        var result = speculationKind != SpeculationKind.TryParse
            ? _scanner.LookAhead(callback)
            : _scanner.TryScan(callback);

        Debug.Assert(saveContextFlags == _contextFlags);

        // If our callback returned something 'falsy' or we're just looking ahead,
        // then unconditionally restore us to where we were.
        if (IsFalsy(result) || speculationKind != SpeculationKind.TryParse)
        {
            _currentToken = saveToken;
            if (speculationKind != SpeculationKind.Reparse)
            {
                while (_parseDiagnostics.Count > saveParseDiagnosticsLength)
                {
                    _parseDiagnostics.RemoveAt(_parseDiagnostics.Count - 1);
                }
            }

            _parseErrorBeforeNextFinishedNode = saveParseErrorBeforeNextFinishedNode;
        }

        return result;
    }

    // Invokes the provided callback then unconditionally restores the parser to the state it
    // was in immediately prior to invoking the callback.  The result of invoking the callback
    // is returned from this function.
    private T LookAhead<T>(Func<T> callback)
    {
        return SpeculationHelper(callback, SpeculationKind.Lookahead);
    }

    // Invokes the provided callback.  If the callback returns something falsy, then it restores
    // the parser to the state it was in immediately prior to invoking the callback.  If the
    // callback returns something truthy, then the parser state is not rolled back.  The result
    // of invoking the callback is returned from this function.
    private T TryParse<T>(Func<T> callback)
    {
        return SpeculationHelper(callback, SpeculationKind.TryParse);
    }

    private bool IsBindingIdentifier()
    {
        if (Token() == SyntaxKind.Identifier)
        {
            return true;
        }

        // `let await`/`let yield` in [Yield] or [Await] are allowed here and disallowed in the binder.
        return Token() > SyntaxKindMarker.LastReservedWord;
    }

    // Ignore strict mode flag because we will report an error in type checker instead.
    private bool IsIdentifier()
    {
        if (Token() == SyntaxKind.Identifier)
        {
            return true;
        }

        // If we have a 'yield' keyword, and we're in the [yield] context, then 'yield' is
        // considered a keyword and is not an identifier.
        if (Token() == SyntaxKind.YieldKeyword && InYieldContext())
        {
            return false;
        }

        // If we have a 'await' keyword, and we're in the [Await] context, then 'await' is
        // considered a keyword and is not an identifier.
        if (Token() == SyntaxKind.AwaitKeyword && InAwaitContext())
        {
            return false;
        }

        return Token() > SyntaxKindMarker.LastReservedWord;
    }

    private bool ParseExpected(SyntaxKind kind, DiagnosticMessage? diagnosticMessage = null, bool shouldAdvance = true)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == kind)
        {
            if (shouldAdvance)
            {
                NextToken();
            }

            return true;
        }

        // Report specific message if provided with one.  Otherwise, report generic fallback message.
        if (diagnosticMessage != null)
        {
            ParseErrorAtCurrentToken(diagnosticMessage);
        }
        else
        {
            ParseErrorAtCurrentToken(Diagnostics._argument0Expected, TokenToString(kind));
        }

        return false;
    }

    // Provides a better error message than the generic "';' expected" if possible for
    // known common variants of a missing semicolon, such as from a misspelled names.
    //
    // @param node Node preceding the expected semicolon location.
    private void ParseErrorForMissingSemicolonAfter(INode node)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // Tagged template literals are sometimes used in places where only simple strings are allowed, i.e.:
        //   module `M1` {
        //   ^^^^^^^^^^^ This block is parsed as a template literal like module`M1`.
        if (node is TaggedTemplateExpression tte)
        {
            ParseErrorAt(
                SkipTrivia(_sourceText!, tte.Template.Pos) ?? 0,
                tte.Template.End ?? 0,
                Diagnostics._moduleDeclarationNamesMayOnlyUseOrQuotedStrings);

            return;
        }

        // Otherwise, if this isn't a well-known keyword-like identifier, give the generic fallback message.
        var expressionText = node is Identifier id ? IdText(id) : null;
        if (string.IsNullOrEmpty(expressionText) || !IsIdentifierText(expressionText!, _languageVersion))
        {
            ParseErrorAtCurrentToken(Diagnostics._argument0Expected, TokenToString(SyntaxKind.SemicolonToken));

            return;
        }

        var pos = SkipTrivia(_sourceText!, node.Pos) ?? 0;

        // Some known keywords are likely signs of syntax being used improperly.
        switch (expressionText)
        {
            case "const":
            case "let":
            case "var":
                ParseErrorAt(pos, node.End ?? 0, Diagnostics._variableDeclarationNotAllowedAtThisLocation);
                return;

            case "declare":
                // If a declared node failed to parse, it would have emitted a diagnostic already.
                return;

            case "interface":
                ParseErrorForInvalidName(
                    Diagnostics._interfaceNameCannotBe0,
                    Diagnostics._interfaceMustBeGivenAName,
                    SyntaxKind.OpenBraceToken);
                return;

            case "is":
                ParseErrorAt(
                    pos,
                    _scanner.GetTokenStart(),
                    Diagnostics._aTypePredicateIsOnlyAllowedInReturnTypePositionForFunctionsAndMethods);
                return;

            case "module":
            case "namespace":
                ParseErrorForInvalidName(
                    Diagnostics._namespaceNameCannotBe0,
                    Diagnostics._namespaceMustBeGivenAName,
                    SyntaxKind.OpenBraceToken);
                return;

            case "type":
                ParseErrorForInvalidName(
                    Diagnostics._typeAliasNameCannotBe0,
                    Diagnostics._typeAliasMustBeGivenAName,
                    SyntaxKind.EqualsToken);
                return;
        }

        // Unknown tokens are handled with their own errors in the scanner
        if (Token() == SyntaxKind.Unknown)
        {
            return;
        }

        // Otherwise, we know this some kind of unknown word, not just a missing expected semicolon.
        ParseErrorAt(pos, node.End ?? 0, Diagnostics._unexpectedKeywordOrIdentifier);
    }

    // Reports a diagnostic error for the current token being an invalid name.
    //
    // @param blankDiagnostic Diagnostic to report for the case of the name being blank (matched tokenIfBlankName).
    // @param nameDiagnostic Diagnostic to report for all other cases.
    // @param tokenIfBlankName Current token if the name was invalid for being blank (not provided / skipped).
    private void ParseErrorForInvalidName(
        DiagnosticMessage nameDiagnostic,
        DiagnosticMessage blankDiagnostic,
        SyntaxKind tokenIfBlankName)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == tokenIfBlankName)
        {
            ParseErrorAtCurrentToken(blankDiagnostic);
        }
        else
        {
            ParseErrorAtCurrentToken(nameDiagnostic, _scanner.GetTokenValue());
        }
    }

    private void ParseSemicolonAfterPropertyName(IPropertyName name, ITypeNode? type, IExpression? initializer)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.AtToken && !_scanner.HasPrecedingLineBreak())
        {
            ParseErrorAtCurrentToken(Diagnostics._decoratorsMustPrecedeTheNameAndAllKeywordsOfPropertyDeclarations);

            return;
        }

        if (Token() == SyntaxKind.OpenParenToken)
        {
            ParseErrorAtCurrentToken(Diagnostics._cannotStartAFunctionCallInATypeAnnotation);
            NextToken();

            return;
        }

        if (type != null && !CanParseSemicolon())
        {
            if (initializer != null)
            {
                ParseErrorAtCurrentToken(Diagnostics._argument0Expected, TokenToString(SyntaxKind.SemicolonToken));
            }
            else
            {
                ParseErrorAtCurrentToken(Diagnostics._expectedForPropertyInitializer);
            }

            return;
        }

        if (TryParseSemicolon())
        {
            return;
        }

        if (initializer != null)
        {
            ParseErrorAtCurrentToken(Diagnostics._argument0Expected, TokenToString(SyntaxKind.SemicolonToken));

            return;
        }

        ParseErrorForMissingSemicolonAfter(name);
    }

    private void ParseExpectedMatchingBrackets(SyntaxKind _1, SyntaxKind closeKind, bool openParsed, int _2)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == closeKind)
        {
            NextToken();

            return;
        }

        ParseErrorAtCurrentToken(Diagnostics._argument0Expected, TokenToString(closeKind));
        if (!openParsed)
        {
            return;
        }
    }

    private bool ParseOptional(SyntaxKind t)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == t)
        {
            NextToken();

            return true;
        }

        return false;
    }

    private T? ParseOptionalToken<T>(SyntaxKind t) where T : class, INode, new()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == t)
        {
            return ParseTokenNode<T>();
        }

        return null;
    }

    private T? ParseExpectedToken<T>(
        SyntaxKind t,
        DiagnosticMessage? diagnosticMessage = null,
        string? arg0 = null) where T : class, INode, new()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseOptionalToken<T>(t) ??
            CreateMissingNode<T>(
                t,
                reportAtCurrentPosition: false,
                diagnosticMessage ?? Diagnostics._argument0Expected,
                arg0 ?? TokenToString(t),
                static () => new T());
    }

    private T ParseTokenNode<T>() where T : INode, new()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var kind = Token();
        NextToken();
        var node = new T();
        ((Node)(INode)node).Kind = kind;

        return FinishNode(node, pos);
    }

    private bool CanParseSemicolon()
    {
        // If there's a real semicolon, then we can always parse it out.
        if (Token() == SyntaxKind.SemicolonToken)
        {
            return true;
        }

        // We can parse out an optional semicolon in ASI cases in the following cases.
        return Token() == SyntaxKind.CloseBraceToken || Token() == SyntaxKind.EndOfFileToken || _scanner.HasPrecedingLineBreak();
    }

    private bool TryParseSemicolon()
    {
        if (!CanParseSemicolon())
        {
            return false;
        }

        if (Token() == SyntaxKind.SemicolonToken)
        {
            // consume the semicolon if it was explicitly provided.
            NextToken();
        }

        return true;
    }

    private bool ParseSemicolon()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return TryParseSemicolon() || ParseExpected(SyntaxKind.SemicolonToken);
    }

    private NodeArray<T> CreateNodeArray<T>(
        IEnumerable<T> elements,
        int pos,
        int? end = null,
        bool hasTrailingComma = false)
        where T : INode
    {
        var array = new NodeArray<T>(elements)
        {
            HasTrailingComma = hasTrailingComma
        };
        SetTextRangePosEnd(array, pos, end ?? _scanner.GetTokenFullStart());

        return array;
    }

    private T FinishNode<T>(T node, int pos, int? end = null) where T : INode
    {
        _budget.CompleteNode(pos);
        SetTextRangePosEnd(node, pos, end ?? _scanner.GetTokenFullStart());
        if (_contextFlags != 0)
        {
            ((Node)(INode)node).Flags |= _contextFlags;
        }

        // Keep track on the node if we encountered an error while parsing it.  If we did, then
        // we cannot reuse the node incrementally.  Once we've marked this node, clear out the
        // flag so that we don't mark any subsequent nodes.
        if (_parseErrorBeforeNextFinishedNode)
        {
            _parseErrorBeforeNextFinishedNode = false;
            ((Node)(INode)node).Flags |= NodeFlags.ThisNodeHasError;
        }

        return node;
    }

    private T CreateMissingNode<T>(
        SyntaxKind kind,
        bool reportAtCurrentPosition,
        DiagnosticMessage? diagnosticMessage = null,
        object? arg0 = null,
        Func<T>? create = null)
        where T : INode
    {
        if (reportAtCurrentPosition)
        {
            ParseErrorAtPosition(_scanner.GetTokenFullStart(), 0, diagnosticMessage!, arg0);
        }
        else if (diagnosticMessage != null)
        {
            ParseErrorAtCurrentToken(diagnosticMessage, arg0);
        }

        var pos = GetNodePos();
        var result = kind == SyntaxKind.Identifier ? new Identifier("", SyntaxKind.Unknown) :
            IsTemplateLiteralKind(kind) ? CreateTemplateLiteralLikeNode(kind, "", "", null) :
            kind == SyntaxKind.NumericLiteral ? new NumericLiteral("") :
            kind == SyntaxKind.StringLiteral ? new StringLiteral("", isSingleQuote: null) :
            kind == SyntaxKind.MissingDeclaration ? new MissingDeclaration() :
            kind == SyntaxKind.CommaToken ? new CommaToken() :
            create is not null ? (INode)create() : throw new InvalidOperationException($"Missing node factory for {kind}.");

        return FinishNode((T)result!, pos);
    }

    private string InternIdentifier(string text)
    {
        if (_identifiers.TryGetValue(text, out var existing))
        {
            return existing;
        }

        _identifiers.Add(text);

        return text;
    }

    // An identifier that starts with two underscores has an extra underscore character prepended to it to avoid issues
    // with magic property names like '__proto__'. The 'identifiers' object is used to share a single string instance for
    // each identifier in order to reduce memory consumption.
    private Identifier CreateIdentifier(
        bool isIdentifier,
        DiagnosticMessage? diagnosticMessage = null,
        DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        if (isIdentifier)
        {
            var pos = _scanner.HasPrecedingJSDocLeadingAsterisks() ? _scanner.GetTokenStart() : GetNodePos();
            // Store original token kind if it is not just an Identifier so we can report appropriate error later in type checker
            var originalKeywordKind = Token();
            var text = InternIdentifier(_scanner.GetTokenValue());
            var hasExtendedUnicodeEscape = _scanner.HasExtendedUnicodeEscape();
            NextTokenWithoutCheck();

            return FinishNode(new Identifier(text, originalKeywordKind, hasExtendedUnicodeEscape), pos);
        }

        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            ParseErrorAtCurrentToken(privateIdentifierDiagnosticMessage ?? Diagnostics._privateIdentifiersAreNotAllowedOutsideClassBodies);

            return CreateIdentifier(isIdentifier: true);
        }

        if (Token() == SyntaxKind.Unknown && _scanner.TryScan(() => _scanner.ReScanInvalidIdentifier() == SyntaxKind.Identifier))
        {
            // Scanner has already recorded an 'Invalid character' error, so no need to add another from the parser.
            return CreateIdentifier(isIdentifier: true);
        }

        // Only for end of file because the error gets reported incorrectly on embedded script tags.
        var reportAtCurrentPosition = Token() == SyntaxKind.EndOfFileToken;

        var isReservedWord = _scanner.IsReservedWord();
        var msgArg = _scanner.GetTokenText();

        var defaultMessage = isReservedWord ?
            Diagnostics._identifierExpected0IsAReservedWordThatCannotBeUsedHere :
            Diagnostics._identifierExpected;

        return CreateMissingNode<Identifier>(
            SyntaxKind.Identifier,
            reportAtCurrentPosition,
            diagnosticMessage ?? defaultMessage,
            msgArg);
    }

    private Identifier ParseBindingIdentifier(DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return CreateIdentifier(IsBindingIdentifier(), diagnosticMessage: null, privateIdentifierDiagnosticMessage);
    }

    private Identifier ParseIdentifier(
        DiagnosticMessage? diagnosticMessage = null,
        DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return CreateIdentifier(IsIdentifier(), diagnosticMessage, privateIdentifierDiagnosticMessage);
    }

    private Identifier ParseIdentifierName(DiagnosticMessage? diagnosticMessage = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return CreateIdentifier(TokenIsIdentifierOrKeyword(Token()), diagnosticMessage);
    }

    private Identifier ParseIdentifierNameErrorOnUnicodeEscapeSequence()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (_scanner.HasUnicodeEscape() || _scanner.HasExtendedUnicodeEscape())
        {
            ParseErrorAtCurrentToken(Diagnostics._unicodeEscapeSequenceCannotAppearHere);
        }

        return CreateIdentifier(TokenIsIdentifierOrKeyword(Token()));
    }

    private bool IsLiteralPropertyName()
    {
        return TokenIsIdentifierOrKeyword(Token()) ||
            Token() == SyntaxKind.StringLiteral ||
            Token() == SyntaxKind.NumericLiteral ||
            Token() == SyntaxKind.BigIntLiteral;
    }

    private bool IsImportAttributeName()
    {
        return TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.StringLiteral;
    }

    private IPropertyName ParsePropertyNameWorker(bool allowComputedPropertyNames)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.StringLiteral || Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral)
        {
            var node = ParseLiteralNode<ILiteralExpression>();
            ((LiteralLikeNode)node!).Text = InternIdentifier(node.Text!);

            return (IPropertyName)node;
        }

        if (allowComputedPropertyNames && Token() == SyntaxKind.OpenBracketToken)
        {
            return ParseComputedPropertyName();
        }

        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            return ParsePrivateIdentifier();
        }

        return ParseIdentifierName();
    }

    private IPropertyName ParsePropertyName()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParsePropertyNameWorker(allowComputedPropertyNames: true);
    }

    private ComputedPropertyName ParseComputedPropertyName()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // PropertyName [Yield]:
        //      LiteralPropertyName
        //      ComputedPropertyName[?Yield]
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBracketToken);
        // We parse any expression (including a comma expression). But the grammar
        // says that only an assignment expression is allowed, so the grammar checker
        // will error if it sees a comma expression.
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.CloseBracketToken);

        return FinishNode(new ComputedPropertyName(expression), pos);
    }

    private PrivateIdentifier ParsePrivateIdentifier()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var node = new PrivateIdentifier(InternIdentifier(_scanner.GetTokenValue()));
        NextToken();

        return FinishNode(node, pos);
    }

    private bool ParseContextualModifier(SyntaxKind t)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return Token() == t && TryParse(NextTokenCanFollowModifier);
    }

    private bool NextTokenIsOnSameLineAndCanFollowModifier()
    {
        NextToken();
        if (_scanner.HasPrecedingLineBreak())
        {
            return false;
        }

        return CanFollowModifier();
    }

    private bool NextTokenCanFollowModifier()
    {
        switch (Token())
        {
            case SyntaxKind.ConstKeyword:
                // 'const' is only a modifier if followed by 'enum'.
                return NextToken() == SyntaxKind.EnumKeyword;
            case SyntaxKind.ExportKeyword:
                NextToken();
                if (Token() == SyntaxKind.DefaultKeyword)
                {
                    return LookAhead(NextTokenCanFollowDefaultKeyword);
                }
                if (Token() == SyntaxKind.TypeKeyword)
                {
                    return LookAhead(NextTokenCanFollowExportModifier);
                }
                return CanFollowExportModifier();
            case SyntaxKind.DefaultKeyword:
                return NextTokenCanFollowDefaultKeyword();
            case SyntaxKind.StaticKeyword:
            case SyntaxKind.GetKeyword:
            case SyntaxKind.SetKeyword:
                NextToken();
                return CanFollowModifier();
            default:
                return NextTokenIsOnSameLineAndCanFollowModifier();
        }
    }

    private bool CanFollowExportModifier()
    {
        return Token() == SyntaxKind.AtToken
            || Token() != SyntaxKind.AsteriskToken
                && Token() != SyntaxKind.AsKeyword
                && Token() != SyntaxKind.OpenBraceToken
                && CanFollowModifier();
    }

    private bool NextTokenCanFollowExportModifier()
    {
        NextToken();

        return CanFollowExportModifier();
    }

    private bool ParseAnyContextualModifier()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return IsModifierKind(Token()) && TryParse(NextTokenCanFollowModifier);
    }

    private NodeArray<TypeParameterDeclaration>? ParseTypeParameters()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.LessThanToken)
        {
            return ParseBracketedList(
                ParsingContext.TypeParameters,
                ParseTypeParameter,
                SyntaxKind.LessThanToken,
                SyntaxKind.GreaterThanToken);
        }

        return null;
    }

    private bool CanFollowModifier()
    {
        return Token() == SyntaxKind.OpenBracketToken
            || Token() == SyntaxKind.OpenBraceToken
            || Token() == SyntaxKind.AsteriskToken
            || Token() == SyntaxKind.DotDotDotToken
            || IsLiteralPropertyName();
    }

    private bool NextTokenCanFollowDefaultKeyword()
    {
        NextToken();

        return Token() == SyntaxKind.ClassKeyword
            || Token() == SyntaxKind.FunctionKeyword
            || Token() == SyntaxKind.InterfaceKeyword
            || Token() == SyntaxKind.AtToken
            || (Token() == SyntaxKind.AbstractKeyword && LookAhead(NextTokenIsClassKeywordOnSameLine))
            || (Token() == SyntaxKind.AsyncKeyword && LookAhead(NextTokenIsFunctionKeywordOnSameLine));
    }

    // True if positioned at the start of a list element
    private bool IsListElement(ParsingContext parsingContext, bool inErrorRecovery)
    {
        var node = CurrentNode(parsingContext);
        if (node != null)
        {
            return true;
        }

        switch (parsingContext)
        {
            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
                // If we're in error recovery, then we don't want to treat ';' as an empty statement.
                // The problem is that ';' can show up in far too many contexts, and if we see one
                // and assume it's a statement, then we may bail out inappropriately from whatever
                // we're parsing.  For example, if we have a semicolon in the middle of a class, then
                // we really don't want to assume the class is over and we're on a statement in the
                // outer module.  We just want to consume and move on.
                return !(Token() == SyntaxKind.SemicolonToken && inErrorRecovery) && IsStartOfStatement();
            case ParsingContext.SwitchClauses:
                return Token() == SyntaxKind.CaseKeyword || Token() == SyntaxKind.DefaultKeyword;
            case ParsingContext.TypeMembers:
                return LookAhead(IsTypeMemberStart);
            case ParsingContext.ClassMembers:
                // We allow semicolons as class elements (as specified by ES6) as long as we're
                // not in error recovery.  If we're in error recovery, we don't want an errant
                // semicolon to be treated as a class member (since they're almost always used
                // for statements.
                return LookAhead(IsClassMemberStart) || (Token() == SyntaxKind.SemicolonToken && !inErrorRecovery);
            case ParsingContext.EnumMembers:
                // Include open bracket computed properties. This technically also lets in indexers,
                // which would be a candidate for improved error reporting.
                return Token() == SyntaxKind.OpenBracketToken || IsLiteralPropertyName();
            case ParsingContext.ObjectLiteralMembers:
                return Token() switch
                {
                    SyntaxKind.OpenBracketToken or SyntaxKind.AsteriskToken or SyntaxKind.DotDotDotToken or SyntaxKind.DotToken => true,
                    _ => IsLiteralPropertyName(),
                };
            case ParsingContext.RestProperties:
                return IsLiteralPropertyName();
            case ParsingContext.ObjectBindingElements:
                return Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.DotDotDotToken || IsLiteralPropertyName();
            case ParsingContext.ImportAttributes:
                return IsImportAttributeName();
            case ParsingContext.HeritageClauseElement:
                // If we see `{ ... }` then only consume it as an expression if it is followed by `,` or `{`
                // That way we won't consume the body of a class in its heritage clause.
                if (Token() == SyntaxKind.OpenBraceToken)
                {
                    return LookAhead(IsValidHeritageClauseObjectLiteral);
                }

                if (!inErrorRecovery)
                {
                    return IsStartOfLeftHandSideExpression() && !IsHeritageClauseExtendsOrImplementsKeyword();
                }
                else
                {
                    // If we're in error recovery we tighten up what we're willing to match.
                    // That way we don't treat something like "this" as a valid heritage clause
                    // element during recovery.
                    return IsIdentifier() && !IsHeritageClauseExtendsOrImplementsKeyword();
                }
            case ParsingContext.VariableDeclarations:
                return IsBindingIdentifierOrPrivateIdentifierOrPattern();
            case ParsingContext.ArrayBindingElements:
                return Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.DotDotDotToken || IsBindingIdentifierOrPrivateIdentifierOrPattern();
            case ParsingContext.TypeParameters:
                return Token() == SyntaxKind.InKeyword || Token() == SyntaxKind.ConstKeyword || IsIdentifier();
            case ParsingContext.ArrayLiteralMembers:
                switch (Token())
                {
                    case SyntaxKind.CommaToken:
                    case SyntaxKind.DotToken: // Not an array literal member, but don't want to close the array (see `tests/cases/fourslash/completionsDotInArrayLiteralInObjectLiteral.ts`)
                        return true;
                }
                goto argumentExpressionsLabel;
            // falls through
            case ParsingContext.ArgumentExpressions:
            argumentExpressionsLabel:
                return Token() == SyntaxKind.DotDotDotToken || IsStartOfExpression();
            case ParsingContext.Parameters:
                return IsStartOfParameter(isJSDocParameter: false);
            case ParsingContext.JSDocParameters:
                return IsStartOfParameter(isJSDocParameter: true);
            case ParsingContext.TypeArguments:
            case ParsingContext.TupleElementTypes:
                return Token() == SyntaxKind.CommaToken || IsStartOfType();
            case ParsingContext.HeritageClauses:
                return IsHeritageClause();
            case ParsingContext.ImportOrExportSpecifiers:
                // bail out if the next token is [FromKeyword StringLiteral].
                // That means we're in something like `import { from "mod"`. Stop here can give better error message.
                if (Token() == SyntaxKind.FromKeyword && LookAhead(NextTokenIsStringLiteral))
                {
                    return false;
                }
                if (Token() == SyntaxKind.StringLiteral)
                {
                    return true; // For "arbitrary module namespace identifiers"
                }
                return TokenIsIdentifierOrKeyword(Token());
            case ParsingContext.JsxAttributes:
                return TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.OpenBraceToken;
            case ParsingContext.JsxChildren:
                return true;
            case ParsingContext.JSDocComment:
                return true;
            case ParsingContext.Count:
                throw Debug.Fail("ParsingContext.Count used as a context", GetNodePos()); // Not a real context, only a marker.
            default:
                throw Debug.Fail("Non-exhaustive case in 'isListElement' {0}.", parsingContext);
        }
    }

    private bool IsValidHeritageClauseObjectLiteral()
    {
        Debug.Assert(Token() == SyntaxKind.OpenBraceToken);
        if (NextToken() == SyntaxKind.CloseBraceToken)
        {
            // if we see "extends {}" then only treat the {} as what we're extending (and not
            // the class body) if we have:
            //
            //      extends {} {
            //      extends {},
            //      extends {} extends
            //      extends {} implements

            var next = NextToken();

            return next == SyntaxKind.CommaToken || next == SyntaxKind.OpenBraceToken || next == SyntaxKind.ExtendsKeyword || next == SyntaxKind.ImplementsKeyword;
        }

        return true;
    }

    private bool NextTokenIsIdentifier()
    {
        NextToken();

        return IsIdentifier();
    }

    private bool NextTokenIsIdentifierOrKeyword()
    {
        NextToken();

        return TokenIsIdentifierOrKeyword(Token());
    }

    private bool NextTokenIsIdentifierOrKeywordOrGreaterThan()
    {
        NextToken();

        return TokenIsIdentifierOrKeywordOrGreaterThan(Token());
    }

    private bool IsHeritageClauseExtendsOrImplementsKeyword()
    {
        if (Token() == SyntaxKind.ImplementsKeyword ||
            Token() == SyntaxKind.ExtendsKeyword)
        {
            return LookAhead(NextTokenIsStartOfExpression);
        }

        return false;
    }

    private bool NextTokenIsStartOfExpression()
    {
        NextToken();

        return IsStartOfExpression();
    }

    private bool NextTokenIsStartOfType()
    {
        NextToken();

        return IsStartOfType();
    }

    // True if positioned at a list terminator
    private bool IsListTerminator(ParsingContext kind)
    {
        if (Token() == SyntaxKind.EndOfFileToken)
        {
            // Being at the end of the file ends all lists.
            return true;
        }

        return kind switch
        {
            ParsingContext.BlockStatements or ParsingContext.SwitchClauses or ParsingContext.TypeMembers or ParsingContext.ClassMembers
                or ParsingContext.EnumMembers or ParsingContext.ObjectLiteralMembers or ParsingContext.ObjectBindingElements
                or ParsingContext.ImportOrExportSpecifiers or ParsingContext.ImportAttributes => Token() == SyntaxKind.CloseBraceToken,
            ParsingContext.SwitchClauseStatements => Token() == SyntaxKind.CloseBraceToken || Token() == SyntaxKind.CaseKeyword || Token() == SyntaxKind.DefaultKeyword,
            ParsingContext.HeritageClauseElement => Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword,
            ParsingContext.VariableDeclarations => IsVariableDeclaratorListTerminator(),
            ParsingContext.TypeParameters => Token() == SyntaxKind.GreaterThanToken || Token() == SyntaxKind.OpenParenToken ||
                                Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword,// Tokens other than '>' are here for better error recovery
            ParsingContext.ArgumentExpressions => Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.SemicolonToken,// Tokens other than ')' are here for better error recovery
            ParsingContext.ArrayLiteralMembers or ParsingContext.TupleElementTypes or ParsingContext.ArrayBindingElements => Token() == SyntaxKind.CloseBracketToken,
            ParsingContext.JSDocParameters or ParsingContext.Parameters or ParsingContext.RestProperties => Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.CloseBracketToken /*|| token == SyntaxKind.OpenBraceToken*/,// Tokens other than ')' and ']' (the latter for index signatures) are here for better error recovery
            ParsingContext.TypeArguments => Token() != SyntaxKind.CommaToken,// All other tokens should cause the type-argument to terminate except comma token
            ParsingContext.HeritageClauses => Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.CloseBraceToken,
            ParsingContext.JsxAttributes => Token() == SyntaxKind.GreaterThanToken || Token() == SyntaxKind.SlashToken,
            ParsingContext.JsxChildren => Token() == SyntaxKind.LessThanToken && LookAhead(NextTokenIsSlash),
            _ => false,
        };
    }

    private bool IsVariableDeclaratorListTerminator()
    {
        // If we can consume a semicolon (either explicitly, or with ASI), then consider us done
        // with parsing the list of variable declarators.
        if (CanParseSemicolon())
        {
            return true;
        }

        // in the case where we're parsing the variable declarator of a 'for-in' statement, we
        // are done if we see an 'in' keyword in front of us. Same with for-of
        if (IsInOrOfKeyword(Token()))
        {
            return true;
        }

        // ERROR RECOVERY TWEAK:
        // For better error recovery, if we see an '=>' then we just stop immediately.  We've got an
        // arrow function here and it's going to be very unlikely that we'll resynchronize and get
        // another variable declaration.
        if (Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            return true;
        }

        // Keep trying to parse out variable declarators.
        return false;
    }

    // True if positioned at element or terminator of the current list or any enclosing list
    private bool IsInSomeParsingContext()
    {
        // We should be in at least one parsing context, be it SourceElements while parsing
        // a SourceFile, or JSDocComment when lazily parsing JSDoc.
        Debug.Assert(_parsingContext != 0, "Missing parsing context");
        for (var kind = 0; kind < (int)ParsingContext.Count; kind++)
        {
            if (((int)_parsingContext & (1 << kind)) != 0)
            {
                if (IsListElement((ParsingContext)kind, inErrorRecovery: true) || IsListTerminator((ParsingContext)kind))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Parses a list of elements
    private NodeArray<T> ParseList<T>(ParsingContext kind, Func<T> parseElement) where T : INode
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var saveParsingContext = _parsingContext;
        _parsingContext |= (ParsingContext)(1 << (int)kind);
        var list = new List<T>();
        var listPos = GetNodePos();

        while (!IsListTerminator(kind))
        {
            if (IsListElement(kind, inErrorRecovery: false))
            {
                list.Add(ParseListElement<T>(kind, parseElement));

                continue;
            }

            if (AbortParsingListOrMoveToNextToken(kind))
            {
                break;
            }
        }

        _parsingContext = saveParsingContext;

        return CreateNodeArray(list, listPos);
    }

    private T ParseListElement<T>(ParsingContext parsingContext, Func<T> parseElement) where T : INode
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var node = CurrentNode(parsingContext);
        if (node != null)
        {
            return (T)ConsumeNode(node);
        }

        return parseElement();
    }

    private INode? CurrentNode(ParsingContext parsingContext, int? pos = null)
    {
        // If we don't have a cursor or the parsing context isn't reusable, there's nothing to reuse.
        //
        // If there is an outstanding parse error that we've encountered, but not attached to
        // some node, then we cannot get a node from the old source tree.  This is because we
        // want to mark the next node we encounter as being unusable.
        //
        // Note: This may be too conservative.  Perhaps we could reuse the node and set the bit
        // on it (or its leftmost child) as having the error.  For now though, being conservative
        // is nice and likely won't ever affect perf.
        if (_syntaxCursor == null || !IsReusableParsingContext(parsingContext) || _parseErrorBeforeNextFinishedNode)
        {
            return null;
        }

        var node = _syntaxCursor.CurrentNode(pos ?? _scanner.GetTokenFullStart());

        // Can't reuse a missing node.
        // Can't reuse a node that intersected the change range.
        // Can't reuse a node that contains a parse error.  This is necessary so that we
        // produce the same set of errors again.
        if (NodeIsMissing(node) || node is IIntersectsIncrementalChange { IntersectsIncrementalChange: true } || ContainsParseError(node) != null)
        {
            return null;
        }

        // We can only reuse a node if it was parsed under the same strict mode that we're
        // currently in.  i.e. if we originally parsed a node in non-strict mode, but then
        // the user added 'using strict' at the top of the file, then we can't use that node
        // again as the presence of strict mode may cause us to parse the tokens in the file
        // differently.
        //
        // Note: we *can* reuse tokens when the strict mode changes.  That's because tokens
        // are unaffected by strict mode.  It's just the parser will decide what to do with it
        // differently depending on what mode it is in.
        //
        // This also applies to all our other context flags as well.
        var nodeContextFlags = node.Flags & NodeFlags.ContextFlags;
        if (nodeContextFlags != _contextFlags)
        {
            return null;
        }

        // Ok, we have a node that looks like it could be reused.  Now verify that it is valid
        // in the current list parsing context that we're currently at.
        if (!CanReuseNode(node, parsingContext))
        {
            return null;
        }

        if (node is IHasJSDoc hasJSDoc && hasJSDoc.JSDoc?.JSDocCache != null)
        {
            // jsDocCache may include tags from parent nodes, which might have been modified.
            hasJSDoc.JSDoc.JSDocCache = null;
        }

        return node;
    }

    private INode ConsumeNode(INode node)
    {
        // Move the scanner so it is after the node we just consumed.
        _scanner.ResetTokenState(node.End ?? 0);
        NextToken();

        return node;
    }

    private static bool IsReusableParsingContext(ParsingContext parsingContext)
    {
        return parsingContext switch
        {
            ParsingContext.ClassMembers or ParsingContext.SwitchClauses or ParsingContext.SourceElements or ParsingContext.BlockStatements
            or ParsingContext.SwitchClauseStatements or ParsingContext.EnumMembers or ParsingContext.TypeMembers
            or ParsingContext.VariableDeclarations or ParsingContext.JSDocParameters or ParsingContext.Parameters => true,
            _ => false,
        };
    }

    private static bool CanReuseNode(INode node, ParsingContext parsingContext)
    {
        return parsingContext switch
        {
            ParsingContext.ClassMembers => IsReusableClassMember(node),
            ParsingContext.SwitchClauses => IsReusableSwitchClause(node),
            ParsingContext.SourceElements or ParsingContext.BlockStatements or ParsingContext.SwitchClauseStatements => IsReusableStatement(node),
            ParsingContext.EnumMembers => IsReusableEnumMember(node),
            ParsingContext.TypeMembers => IsReusableTypeMember(node),
            ParsingContext.VariableDeclarations => IsReusableVariableDeclaration(node),
            ParsingContext.JSDocParameters or ParsingContext.Parameters => IsReusableParameter(node),
            _ => false,
        };
    }

    private static bool IsReusableClassMember(INode node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.Constructor:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.SemicolonClassElement:
                    return true;
                case SyntaxKind.MethodDeclaration:
                    // Method declarations are not necessarily reusable.  An object-literal
                    // may have a method calls "constructor(...)" and we must reparse that
                    // into an actual .ConstructorDeclaration.
                    var methodDeclaration = node as MethodDeclaration;
                    var nameIsConstructor = methodDeclaration!.Name!.Kind == SyntaxKind.Identifier &&
                        methodDeclaration.Name is Identifier { EscapedText: "constructor" };

                    return !nameIsConstructor;
            }
        }

        return false;
    }

    private static bool IsReusableSwitchClause(INode node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.CaseClause:
                case SyntaxKind.DefaultClause:
                    return true;
            }
        }

        return false;
    }

    private static bool IsReusableStatement(INode node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.VariableStatement:
                case SyntaxKind.Block:
                case SyntaxKind.IfStatement:
                case SyntaxKind.ExpressionStatement:
                case SyntaxKind.ThrowStatement:
                case SyntaxKind.ReturnStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.BreakStatement:
                case SyntaxKind.ContinueStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.EmptyStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.LabeledStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.DebuggerStatement:
                case SyntaxKind.ImportDeclaration:
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.ExportDeclaration:
                case SyntaxKind.ExportAssignment:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                    return true;
            }
        }

        return false;
    }

    private static bool IsReusableEnumMember(INode node)
    {
        return node?.Kind == SyntaxKind.EnumMember;
    }

    private static bool IsReusableTypeMember(INode node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.CallSignature:
                    return true;
            }
        }

        return false;
    }

    private static bool IsReusableVariableDeclaration(INode node)
    {
        if (node.Kind != SyntaxKind.VariableDeclaration)
        {
            return false;
        }

        // Very subtle incremental parsing bug.  Consider the following code:
        //
        //      let v = new List < A, B
        //
        // This is actually legal code.  It's a list of variable declarators "v = new List<A"
        // on one side and "B" on the other. If you then change that to:
        //
        //      let v = new List < A, B >()
        //
        // then we have a problem.  "v = new List<A" doesn't intersect the change range, so we
        // start reparsing at "B" and we completely fail to handle this properly.
        //
        // In order to prevent this, we do not allow a variable declarator to be reused if it
        // has an initializer.
        var variableDeclarator = node as VariableDeclaration;

        return variableDeclarator?.Initializer == null;
    }

    private static bool IsReusableParameter(INode node)
    {
        if (node.Kind != SyntaxKind.Parameter)
        {
            return false;
        }

        // See the comment in IsReusableVariableDeclaration for why we do this.
        var parameter = node as ParameterDeclaration;

        return parameter?.Initializer == null;
    }

    // Returns true if we should abort parsing.
    private bool AbortParsingListOrMoveToNextToken(ParsingContext kind)
    {
        ParsingContextErrors(kind);
        if (IsInSomeParsingContext())
        {
            return true;
        }

        NextToken();

        return false;
    }

    private Diagnostic? ParsingContextErrors(ParsingContext context)
    {
        switch (context)
        {
            case ParsingContext.SourceElements:
                return Token() == SyntaxKind.DefaultKeyword
                    ? ParseErrorAtCurrentToken(Diagnostics._argument0Expected, TokenToString(SyntaxKind.ExportKeyword))
                    : ParseErrorAtCurrentToken(Diagnostics._declarationOrStatementExpected);
            case ParsingContext.BlockStatements:
                return ParseErrorAtCurrentToken(Diagnostics._declarationOrStatementExpected);
            case ParsingContext.SwitchClauses:
                return ParseErrorAtCurrentToken(Diagnostics._caseOrDefaultExpected);
            case ParsingContext.SwitchClauseStatements:
                return ParseErrorAtCurrentToken(Diagnostics._statementExpected);
            case ParsingContext.RestProperties: // fallthrough
            case ParsingContext.TypeMembers:
                return ParseErrorAtCurrentToken(Diagnostics._propertyOrSignatureExpected);
            case ParsingContext.ClassMembers:
                return ParseErrorAtCurrentToken(Diagnostics._unexpectedTokenAConstructorMethodAccessorOrPropertyWasExpected);
            case ParsingContext.EnumMembers:
                return ParseErrorAtCurrentToken(Diagnostics._enumMemberExpected);
            case ParsingContext.HeritageClauseElement:
                return ParseErrorAtCurrentToken(Diagnostics._expressionExpected);
            case ParsingContext.VariableDeclarations:
                return IsKeyword(Token())
                    ? ParseErrorAtCurrentToken(Diagnostics._argument0IsNotAllowedAsAVariableDeclarationName, TokenToString(Token()))
                    : ParseErrorAtCurrentToken(Diagnostics._variableDeclarationExpected);
            case ParsingContext.ObjectBindingElements:
                return ParseErrorAtCurrentToken(Diagnostics._propertyDestructuringPatternExpected);
            case ParsingContext.ArrayBindingElements:
                return ParseErrorAtCurrentToken(Diagnostics._arrayElementDestructuringPatternExpected);
            case ParsingContext.ArgumentExpressions:
                return ParseErrorAtCurrentToken(Diagnostics._argumentExpressionExpected);
            case ParsingContext.ObjectLiteralMembers:
                return ParseErrorAtCurrentToken(Diagnostics._propertyAssignmentExpected);
            case ParsingContext.ArrayLiteralMembers:
                return ParseErrorAtCurrentToken(Diagnostics._expressionOrCommaExpected);
            case ParsingContext.JSDocParameters:
                return ParseErrorAtCurrentToken(Diagnostics._parameterDeclarationExpected);
            case ParsingContext.Parameters:
                return IsKeyword(Token())
                    ? ParseErrorAtCurrentToken(Diagnostics._argument0IsNotAllowedAsAParameterName, TokenToString(Token()))
                    : ParseErrorAtCurrentToken(Diagnostics._parameterDeclarationExpected);
            case ParsingContext.TypeParameters:
                return ParseErrorAtCurrentToken(Diagnostics._typeParameterDeclarationExpected);
            case ParsingContext.TypeArguments:
                return ParseErrorAtCurrentToken(Diagnostics._typeArgumentExpected);
            case ParsingContext.TupleElementTypes:
                return ParseErrorAtCurrentToken(Diagnostics._typeExpected);
            case ParsingContext.HeritageClauses:
                return ParseErrorAtCurrentToken(Diagnostics._unexpectedTokenExpected);
            case ParsingContext.ImportOrExportSpecifiers:
                if (Token() == SyntaxKind.FromKeyword)
                {
                    return ParseErrorAtCurrentToken(Diagnostics._argument0Expected, "}");
                }
                return ParseErrorAtCurrentToken(Diagnostics._identifierExpected);
            case ParsingContext.JsxAttributes:
                return ParseErrorAtCurrentToken(Diagnostics._identifierExpected);
            case ParsingContext.JsxChildren:
                return ParseErrorAtCurrentToken(Diagnostics._identifierExpected);
            case ParsingContext.ImportAttributes:
                return ParseErrorAtCurrentToken(Diagnostics._identifierOrStringLiteralExpected);
            case ParsingContext.JSDocComment:
                return ParseErrorAtCurrentToken(Diagnostics._identifierExpected);
            case ParsingContext.Count:
                throw Debug.Fail("ParsingContext.Count used as a context"); // Not a real context, only a marker.
            default:
                throw Debug.Fail($"{context} is not exhaustive in ParsingContextErrors");
        }
    }

    private NodeArray<T>? ParseDelimitedList<T>(
        ParsingContext kind,
        Func<T> parseElement,
        bool considerSemicolonAsDelimiter = false) where T : INode
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var saveParsingContext = _parsingContext;
        _parsingContext |= (ParsingContext)(1 << (int)kind);
        var list = new List<T>();
        var listPos = GetNodePos();

        var commaStart = -1; // Meaning the previous token was not a comma
        while (true)
        {
            if (IsListElement(kind, inErrorRecovery: false))
            {
                var startPos = _scanner.GetTokenFullStart();
                var result = ParseListElement(kind, parseElement);
                if (result == null)
                {
                    _parsingContext = saveParsingContext;

                    return null;
                }

                list.Add(result);
                commaStart = _scanner.GetTokenStart();

                if (ParseOptional(SyntaxKind.CommaToken))
                {
                    // No need to check for a zero length node since we know we parsed a comma
                    continue;
                }

                commaStart = -1; // Back to the state where the last token was not a comma
                if (IsListTerminator(kind))
                {
                    break;
                }

                // We didn't get a comma, and the list wasn't terminated, explicitly parse
                // out a comma so we give a good error message.
                ParseExpected(SyntaxKind.CommaToken, GetExpectedCommaDiagnostic(kind));

                // If the token was a semicolon, and the caller allows that, then skip it and
                // continue.  This ensures we get back on track and don't result in tons of
                // parse errors.  For example, this can happen when people do things like use
                // a semicolon to delimit object literal members.   Note: we'll have already
                // reported an error when we called ParseExpected above.
                if (considerSemicolonAsDelimiter && Token() == SyntaxKind.SemicolonToken && !_scanner.HasPrecedingLineBreak())
                {
                    NextToken();
                }

                if (startPos == _scanner.GetTokenFullStart())
                {
                    // What we're parsing isn't actually remotely recognizable as a element and we've consumed no tokens whatsoever
                    // Consume a token to advance the parser in some way and avoid an infinite loop
                    // This can happen when we're speculatively parsing parenthesized expressions which we think may be arrow functions,
                    // or when a modifier keyword which is disallowed as a parameter name (ie, `static` in strict mode) is supplied
                    NextToken();
                }

                continue;
            }

            if (IsListTerminator(kind))
            {
                break;
            }

            if (AbortParsingListOrMoveToNextToken(kind))
            {
                break;
            }
        }

        _parsingContext = saveParsingContext;

        // Recording the trailing comma is deliberately done after the previous
        // loop, and not just if we see a list terminator. This is because the list
        // may have ended incorrectly, but it is still important to know if there
        // was a trailing comma.
        // Check if the last token was a comma.
        // Always preserve a trailing comma by marking it on the NodeArray
        return CreateNodeArray(list, listPos, hasTrailingComma: commaStart >= 0);
    }

    private static DiagnosticMessage? GetExpectedCommaDiagnostic(ParsingContext kind)
    {
        return kind == ParsingContext.EnumMembers ? Diagnostics._anEnumMemberNameMustBeFollowedByAOr : null;
    }

    private NodeArray<T> CreateMissingList<T>() where T : INode
    {
        var list = CreateNodeArray<T>([], GetNodePos());
        list.IsMissingList = true;

        return list;
    }

    private static bool IsMissingList<T>(NodeArray<T> arr)
    {
        return arr.IsMissingList;
    }

    private NodeArray<T> ParseBracketedList<T>(
        ParsingContext kind,
        Func<T> parseElement,
        SyntaxKind open,
        SyntaxKind close) where T : INode
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (ParseExpected(open))
        {
            var result = ParseDelimitedList(kind, parseElement);
            ParseExpected(close);

            return result!;
        }

        return CreateMissingList<T>();
    }

    private IEntityName ParseEntityName(bool allowReservedWords, DiagnosticMessage? diagnosticMessage = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        IEntityName entity = allowReservedWords ? ParseIdentifierName(diagnosticMessage) : ParseIdentifier(diagnosticMessage);
        while (ParseOptional(SyntaxKind.DotToken))
        {
            if (Token() == SyntaxKind.LessThanToken)
            {
                // The entity is part of a JSDoc-style generic. We will use the gap between `typeName` and
                // `typeArguments` to report it as a grammar error in the checker.
                break;
            }

            entity = CreateQualifiedName(
                entity,
                ParseRightSideOfDot(
                    allowReservedWords,
                    allowPrivateIdentifiers: false,
                    allowUnicodeEscapeSequenceInIdentifierName: true));
        }

        return entity;
    }

    private QualifiedName CreateQualifiedName(IEntityName entity, Identifier name)
    {
        return FinishNode(new QualifiedName(entity, name), entity.Pos ?? 0);
    }

    private Identifier ParseRightSideOfDot(
        bool allowIdentifierNames,
        bool allowPrivateIdentifiers,
        bool allowUnicodeEscapeSequenceInIdentifierName)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // Technically a keyword is valid here as all identifiers and keywords are identifier names.
        // However, often we'll encounter this in error situations when the identifier or keyword
        // is actually starting another valid construct.
        //
        // So, we check for the following specific case:
        //
        //      name.
        //      identifierOrKeyword identifierNameOrKeyword
        //
        // Note: the newlines are important here.  For example, if that above code
        // were rewritten into:
        //
        //      name.identifierOrKeyword
        //      identifierNameOrKeyword
        //
        // Then we would consider it valid.  That's because ASI would take effect and
        // the code would be implicitly: "name.identifierOrKeyword; identifierNameOrKeyword".
        // In the first case though, ASI will not take effect because there is not a
        // line terminator after the identifier or keyword.
        if (_scanner.HasPrecedingLineBreak() && TokenIsIdentifierOrKeyword(Token()))
        {
            var matchesPattern = LookAhead(NextTokenIsIdentifierOrKeywordOnSameLine);

            if (matchesPattern)
            {
                // Report that we need an identifier.  However, report it right after the dot,
                // and not on the next token.  This is because the next token might actually
                // be an identifier and the error would be quite confusing.
                return CreateMissingNode<Identifier>(
                    SyntaxKind.Identifier,
                    reportAtCurrentPosition: true,
                    Diagnostics._identifierExpected);
            }
        }

        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            var node = ParsePrivateIdentifier();

            return allowPrivateIdentifiers ? node : CreateMissingNode<Identifier>(
                SyntaxKind.Identifier,
                reportAtCurrentPosition: true,
                Diagnostics._identifierExpected);
        }

        if (allowIdentifierNames)
        {
            return allowUnicodeEscapeSequenceInIdentifierName ? ParseIdentifierName()
                : ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        }

        return ParseIdentifier();
    }

    private NodeArray<TemplateSpan> ParseTemplateSpans(bool isTaggedTemplate)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var list = new List<TemplateSpan>();
        TemplateSpan node;
        do
        {
            node = ParseTemplateSpan(isTaggedTemplate);
            list.Add(node);
        }
        while (node.Literal.Kind == SyntaxKind.TemplateMiddle);

        return CreateNodeArray(list, pos);
    }

    private TemplateExpression ParseTemplateExpression(bool isTaggedTemplate)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(
            new TemplateExpression(
                ParseTemplateHead(isTaggedTemplate),
                ParseTemplateSpans(isTaggedTemplate)),
            pos);
    }

    private TemplateLiteralTypeNode ParseTemplateType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(
            new TemplateLiteralTypeNode(
                ParseTemplateHead(isTaggedTemplate: false),
                ParseTemplateTypeSpans()),
            pos);
    }

    private NodeArray<TemplateLiteralTypeSpan> ParseTemplateTypeSpans()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var list = new List<TemplateLiteralTypeSpan>();
        TemplateLiteralTypeSpan node;
        do
        {
            node = ParseTemplateTypeSpan();
            list.Add(node);
        }
        while (node.Literal.Kind == SyntaxKind.TemplateMiddle);

        return CreateNodeArray(list, pos);
    }

    private TemplateLiteralTypeSpan ParseTemplateTypeSpan()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(
            new TemplateLiteralTypeSpan(
                ParseType(),
                ParseLiteralOfTemplateSpan(isTaggedTemplate: false)),
            pos);
    }

    private ITemplateLiteralLikeNode ParseLiteralOfTemplateSpan(bool isTaggedTemplate)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.CloseBraceToken)
        {
            ReScanTemplateToken(isTaggedTemplate);

            return ParseTemplateMiddleOrTemplateTail();
        }
        else
        {
            // TODO(rbuckton): Do we need to call `ParseExpectedToken` or can we just call `createMissingNode` directly?
            return CreateMissingNode<TemplateLiteralLikeNode>(
                SyntaxKind.TemplateTail,
                reportAtCurrentPosition: true,
                Diagnostics._argument0Expected,
                TokenToString(SyntaxKind.CloseBraceToken));
        }
    }

    private TemplateSpan ParseTemplateSpan(bool isTaggedTemplate)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(
            new TemplateSpan(
                AllowInAnd(ParseExpression),
                ParseLiteralOfTemplateSpan(isTaggedTemplate)),
            pos);
    }

    private TLiteral ParseLiteralNode<TLiteral>()
        where TLiteral : ILiteralLikeNode
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseLiteralLikeNode<TLiteral>(Token());
    }

    private ITemplateLiteralLikeNode ParseTemplateHead(bool isTaggedTemplate)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (!isTaggedTemplate && (_scanner.GetTokenFlags() & TokenFlags.IsInvalid) != 0)
        {
            ReScanTemplateToken(isTaggedTemplate: false);
        }

        var fragment = ParseLiteralLikeNode<ITemplateLiteralLikeNode>(Token());
        Debug.Assert(fragment.Kind == SyntaxKind.TemplateHead, "Template head has wrong token kind");

        return fragment;
    }

    private ITemplateLiteralLikeNode ParseTemplateMiddleOrTemplateTail()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var fragment = ParseLiteralLikeNode<ITemplateLiteralLikeNode>(Token());
        Debug.Assert(
            fragment.Kind == SyntaxKind.TemplateMiddle || fragment.Kind == SyntaxKind.TemplateTail,
            "Template fragment has wrong token kind");

        return fragment;
    }

    private string GetTemplateLiteralRawText(SyntaxKind kind)
    {
        var isLast = kind == SyntaxKind.NoSubstitutionTemplateLiteral || kind == SyntaxKind.TemplateTail;
        var tokenText = _scanner.GetTokenText();

        return tokenText[1..(tokenText.Length - (_scanner.IsUnterminated() ? 0 : isLast ? 1 : 2))];
    }

    private TLiteral ParseLiteralLikeNode<TLiteral>(SyntaxKind kind)
        where TLiteral : ILiteralLikeNode
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ILiteralLikeNode? node = IsTemplateLiteralKind(kind) ? CreateTemplateLiteralLikeNode(
            kind,
            _scanner.GetTokenValue(),
            GetTemplateLiteralRawText(kind),
            _scanner.GetTokenFlags() & TokenFlags.TemplateLiteralLikeFlags) :
            // Note that theoretically the following condition would hold true literals like 009,
            // which is not octal. But because of how the scanner separates the tokens, we would
            // never get a token like this. Instead, we would get 00 and 9 as two separate tokens.
            // We also do not need to check for negatives because any prefix operator would be part of a
            // parent unary expression.
            kind == SyntaxKind.NumericLiteral ? new NumericLiteral(_scanner.GetTokenValue(), _scanner.GetNumericLiteralFlags()) :
            kind == SyntaxKind.StringLiteral ? new StringLiteral(_scanner.GetTokenValue(), isSingleQuote: null, _scanner.HasExtendedUnicodeEscape()) :
            IsLiteralKind(kind) ? CreateLiteralLikeNode(kind, _scanner.GetTokenValue()) :
            throw Debug.Fail($"{kind} is unknown literal like node");

        if (_scanner.HasExtendedUnicodeEscape())
        {
            ((LiteralLikeNode)node!).HasExtendedUnicodeEscape = true;
        }

        if (_scanner.IsUnterminated())
        {
            ((LiteralLikeNode)node!).IsUnterminated = true;
        }

        NextToken();

        return (TLiteral)FinishNode(node!, pos);
    }

    // TYPES
    private IEntityName ParseEntityNameOfTypeReference()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseEntityName(allowReservedWords: true, Diagnostics._typeExpected);
    }

    private NodeArray<ITypeNode>? ParseTypeArgumentsOfTypeReference()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (!_scanner.HasPrecedingLineBreak() && ReScanLessThanToken() == SyntaxKind.LessThanToken)
        {
            return ParseBracketedList(
                ParsingContext.TypeArguments,
                ParseType,
                SyntaxKind.LessThanToken,
                SyntaxKind.GreaterThanToken);
        }

        return null;
    }

    private TypeReferenceNode ParseTypeReference()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(
            new TypeReferenceNode(
                ParseEntityNameOfTypeReference(),
                ParseTypeArgumentsOfTypeReference()!),
            pos);
    }

    // If true, we should abort parsing an error function.
    private     // If true, we should abort parsing an error function.
static bool TypeHasArrowFunctionBlockingParseError(ITypeNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TypeReference:
                return NodeIsMissing((node as TypeReferenceNode)!.TypeName);
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
                {
                    var n = node as IFunctionOrConstructorTypeNode;

                    return IsMissingList(n!.Parameters!) || TypeHasArrowFunctionBlockingParseError(n!.Type!);
                }
            case SyntaxKind.ParenthesizedType:
                return TypeHasArrowFunctionBlockingParseError((node as ParenthesizedTypeNode)!.Type);
            default:
                return false;
        }
    }

    private TypePredicateNode ParseThisTypePredicate(ThisTypeNode lhs)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        NextToken();

        return FinishNode(new TypePredicateNode(null!, lhs, ParseType()), lhs.Pos ?? 0);
    }

    private ThisTypeNode ParseThisTypeNode()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        NextToken();

        return FinishNode(new ThisTypeNode(), pos);
    }

    private JSDocAllType ParseJSDocAllType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        NextToken();

        return FinishNode(new JSDocAllType(), pos);
    }

    private JSDocNonNullableType ParseJSDocNonNullableType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        NextToken();

        return FinishNode(new JSDocNonNullableType(ParseNonArrayType(), postfix: false), pos);
    }

    private JSDocTypeBase ParseJSDocUnknownOrNullableType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        // skip the ?
        NextToken();

        // Need to LookAhead to decide if this is a nullable or unknown type.

        // Here are cases where we'll pick the unknown type:
        //
        //      Foo(?,
        //      { a: ? }
        //      Foo(?)
        //      Foo<?>
        //      Foo(?=
        //      (?|
        if (
            Token() == SyntaxKind.CommaToken ||
            Token() == SyntaxKind.CloseBraceToken ||
            Token() == SyntaxKind.CloseParenToken ||
            Token() == SyntaxKind.GreaterThanToken ||
            Token() == SyntaxKind.EqualsToken ||
            Token() == SyntaxKind.BarToken
        )
        {
            return FinishNode(new JSDocUnknownType(), pos);
        }
        else
        {
            return FinishNode(new JSDocNullableType(ParseType(), postfix: false), pos);
        }
    }

    private ITypeNode ParseJSDocFunctionType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        if (TryParse(NextTokenIsOpenParen))
        {
            var parameters = ParseParameters(SignatureFlags.Type | SignatureFlags.JSDoc);
            var type = ParseReturnType(SyntaxKind.ColonToken, isType: false);

            return WithJSDoc(FinishNode(new JSDocFunctionType(parameters, type), pos), hasJSDoc);
        }

        return FinishNode(new TypeReferenceNode(ParseIdentifierName(), typeArguments: null!), pos);
    }

    private ParameterDeclaration ParseJSDocParameter()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        Identifier? name = null;
        if (Token() == SyntaxKind.ThisKeyword || Token() == SyntaxKind.NewKeyword)
        {
            name = ParseIdentifierName();
            ParseExpected(SyntaxKind.ColonToken);
        }

        return FinishNode(
            new ParameterDeclaration(
                modifiers: null,
                dotDotDotToken: null,
                // TODO(rbuckton): JSDoc parameters don't have names (except `this`/`new`), should we manufacture an empty identifier?
                name: name!,
                questionToken: null,
                type: ParseJSDocType(),
                initializer: null),
            pos);
    }

    private ITypeNode ParseJSDocType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        _scanner.SetSkipJsDocLeadingAsterisks(true);
        var pos = GetNodePos();
        if (ParseOptional(SyntaxKind.ModuleKeyword))
        {
            // TODO(rbuckton): We never set the type for a JSDocNamepathType. What should we put here?
            var moduleTag = new JSDocNamepathType(type: null!);
            while (true)
            {
                switch (Token())
                {
                    case SyntaxKind.CloseBraceToken:
                    case SyntaxKind.EndOfFileToken:
                    case SyntaxKind.CommaToken:
                    case SyntaxKind.WhitespaceTrivia:
                        goto terminateLabel;
                    default:
                        NextTokenJSDoc();
                        break;
                }
            }

        terminateLabel:

            _scanner.SetSkipJsDocLeadingAsterisks(false);

            return FinishNode(moduleTag, pos);
        }

        var hasDotDotDot = ParseOptional(SyntaxKind.DotDotDotToken);
        var type = ParseTypeOrTypePredicate();
        _scanner.SetSkipJsDocLeadingAsterisks(false);
        if (hasDotDotDot)
        {
            type = FinishNode(new JSDocVariadicType(type), pos);
        }

        if (Token() == SyntaxKind.EqualsToken)
        {
            NextToken();

            return FinishNode(new JSDocOptionalType(type), pos);
        }

        return type;
    }

    private TypeQueryNode ParseTypeQuery()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.TypeOfKeyword);
        var entityName = ParseEntityName(allowReservedWords: true);
        // Make sure we perform ASI to prevent parsing the next line's type arguments as part of an instantiation expression.
        var typeArguments = !_scanner.HasPrecedingLineBreak() ? TryParseTypeArguments() : null;

        return FinishNode(new TypeQueryNode(entityName, typeArguments!), pos);
    }

    private TypeParameterDeclaration ParseTypeParameter()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var modifiers = ParseModifiers(allowDecorators: false, permitConstAsModifier: true);
        var hasOutModifier = false;
        var hasInModifier = false;
        foreach (var modifier in modifiers ?? [])
        {
            if (modifier.Kind == SyntaxKind.InKeyword && hasInModifier ||
                modifier.Kind == SyntaxKind.OutKeyword && hasOutModifier)
            {
                ParseErrorAt(
                    SkipTrivia(_sourceText!, modifier.Pos) ?? 0,
                    modifier.End ?? 0,
                    Diagnostics._varianceModifierAlreadySeen,
                    TokenToString(modifier.Kind));
                break;
            }

            if (modifier.Kind == SyntaxKind.InKeyword && hasOutModifier)
            {
                ParseErrorAt(
                    SkipTrivia(_sourceText!, modifier.Pos) ?? 0,
                    modifier.End ?? 0,
                    Diagnostics._varianceModifierOrder);
                break;
            }

            hasInModifier |= modifier.Kind == SyntaxKind.InKeyword;
            hasOutModifier |= modifier.Kind == SyntaxKind.OutKeyword;
        }

        var name = ParseIdentifier();
        ITypeNode? constraint = null;
        IExpression? expression = null;
        if (ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            // It's not uncommon for people to write improper constraints to a generic.  If the
            // user writes a constraint that is an expression and not an actual type, then parse
            // it out as an expression (so we can recover well), but report that a type is needed
            // instead.
            if (IsStartOfType() || !IsStartOfExpression())
            {
                constraint = ParseType();
            }
            else
            {
                // It was not a type, and it looked like an expression.  Parse out an expression
                // here so we recover well.  Note: it is important that we call parseUnaryExpression
                // and not parseExpression here.  If the user has:
                //
                //      <T extends "">
                //
                // We do *not* want to consume the `>` as we're consuming the expression for "".
                expression = ParseUnaryExpressionOrHigher();
            }
        }

        var defaultType = ParseOptional(SyntaxKind.EqualsToken) ? ParseType() : null;
        var node = new TypeParameterDeclaration(modifiers, name, constraint, defaultType)
        {
            Expression = expression
        };

        return FinishNode(node, pos);
    }

    private bool IsStartOfParameter(bool isJSDocParameter)
    {
        return Token() == SyntaxKind.DotDotDotToken ||
            IsBindingIdentifierOrPrivateIdentifierOrPattern() ||
            IsModifierKind(Token()) ||
            Token() == SyntaxKind.AtToken ||
            IsStartOfType(inStartOfParameter: !isJSDocParameter);
    }

    private IBindingName ParseNameOfParameter(NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // FormalParameter [Yield,Await]:
        //      BindingElement[?Yield,?Await]
        var name = ParseIdentifierOrPattern(Diagnostics._privateIdentifiersCannotBeUsedAsParameters);
        if (GetFullWidth(name) == 0 && (modifiers == null || modifiers.Count == 0) && IsModifierKind(Token()))
        {
            // in cases like
            // 'use strict'
            // function foo(static)
            // isParameter('static') == true, because of isModifier('static')
            // however 'static' is not a legal identifier in a strict mode.
            // so result of this function will be ParameterDeclaration (flags = 0, name = missing, type = undefined, initializer = undefined)
            // and current token will not change => parsing of the enclosing parameter list will last till the end of time (or OOM)
            // to avoid this we'll advance cursor to the next token.
            NextToken();
        }

        return name;
    }

    private bool IsParameterNameStart()
    {
        // Be permissive about await and yield by calling isBindingIdentifier instead of isIdentifier; disallowing
        // them during a speculative parse leads to many more follow-on errors than allowing the function to parse then later
        // complaining about the use of the keywords.
        return IsBindingIdentifier() || Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.OpenBraceToken;
    }

    private ParameterDeclaration ParseParameter(bool inOuterAwaitContext)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseParameterWorker(inOuterAwaitContext);
    }

    private ParameterDeclaration ParseParameterForSpeculation(bool inOuterAwaitContext)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseParameterWorker(inOuterAwaitContext, allowAmbiguity: false);
    }

    private ParameterDeclaration ParseParameterWorker(bool inOuterAwaitContext, bool allowAmbiguity = true)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        // FormalParameter [Yield,Await]:
        //      BindingElement[?Yield,?Await]

        // Decorators are parsed in the outer [Await] context, the rest of the parameter is parsed in the function's [Await] context.
        var modifiers = inOuterAwaitContext ?
            DoInAwaitContext(() => ParseModifiers(allowDecorators: true)) :
            DoOutsideOfAwaitContext(() => ParseModifiers(allowDecorators: true));

        ParameterDeclaration node;
        if (Token() == SyntaxKind.ThisKeyword)
        {
            node = new ParameterDeclaration(
                modifiers,
                dotDotDotToken: null,
                CreateIdentifier(isIdentifier: true),
                questionToken: null,
                type: ParseTypeAnnotation(),
                initializer: null);

            var modifier = modifiers?.FirstOrDefault();
            if (modifier != null)
            {
                ParseErrorAtRange(modifier, Diagnostics._neitherDecoratorsNorModifiersMayBeAppliedToThisParameters);
            }

            return WithJSDoc(FinishNode(node, pos), hasJSDoc);
        }

        var savedTopLevel = _topLevel;
        _topLevel = false;

        var dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);

        if (!allowAmbiguity && !IsParameterNameStart())
        {
            return null!;
        }

        node = WithJSDoc(
            FinishNode(
                new ParameterDeclaration(
                    modifiers,
                    dotDotDotToken,
                    name: ParseNameOfParameter(modifiers),
                    questionToken: ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken),
                    type: ParseTypeAnnotation(),
                    initializer: ParseInitializer()),
                pos),
            hasJSDoc);
        _topLevel = savedTopLevel;

        return node;
    }

    private ITypeNode ParseReturnType(SyntaxKind returnToken, bool isType)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (ShouldParseReturnType(returnToken, isType))
        {
            return AllowConditionalTypesAnd(ParseTypeOrTypePredicate);
        }

        return null!;
    }

    private bool ShouldParseReturnType(SyntaxKind returnToken, bool isType)
    {
        if (returnToken == SyntaxKind.EqualsGreaterThanToken)
        {
            ParseExpected(returnToken);

            return true;
        }
        else if (ParseOptional(SyntaxKind.ColonToken))
        {
            return true;
        }
        else if (isType && Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            // This is easy to get backward, especially in type contexts, so parse the type anyway
            ParseErrorAtCurrentToken(Diagnostics._argument0Expected, TokenToString(SyntaxKind.ColonToken));
            NextToken();

            return true;
        }

        return false;
    }

    private NodeArray<ParameterDeclaration> ParseParametersWorker(SignatureFlags flags, bool allowAmbiguity)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // FormalParameters [Yield,Await]: (modified)
        //      [empty]
        //      FormalParameterList[?Yield,Await]
        //
        // FormalParameter[Yield,Await]: (modified)
        //      BindingElement[?Yield,Await]
        //
        // BindingElement [Yield,Await]: (modified)
        //      SingleNameBinding[?Yield,?Await]
        //      BindingPattern[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        //
        // SingleNameBinding [Yield,Await]:
        //      BindingIdentifier[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        var savedYieldContext = InYieldContext();
        var savedAwaitContext = InAwaitContext();

        SetYieldContext((flags & SignatureFlags.Yield) != 0);
        SetAwaitContext((flags & SignatureFlags.Await) != 0);

        var parameters = (flags & SignatureFlags.JSDoc) != 0 ?
            ParseDelimitedList(ParsingContext.JSDocParameters, ParseJSDocParameter) :
            ParseDelimitedList(
                ParsingContext.Parameters,
                () => allowAmbiguity ? ParseParameter(savedAwaitContext)
                : ParseParameterForSpeculation(savedAwaitContext));

        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return parameters!;
    }

    private NodeArray<ParameterDeclaration> ParseParameters(SignatureFlags flags)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // FormalParameters [Yield,Await]: (modified)
        //      [empty]
        //      FormalParameterList[?Yield,Await]
        //
        // FormalParameter[Yield,Await]: (modified)
        //      BindingElement[?Yield,Await]
        //
        // BindingElement [Yield,Await]: (modified)
        //      SingleNameBinding[?Yield,?Await]
        //      BindingPattern[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        //
        // SingleNameBinding [Yield,Await]:
        //      BindingIdentifier[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        if (!ParseExpected(SyntaxKind.OpenParenToken))
        {
            return CreateMissingList<ParameterDeclaration>();
        }

        var parameters = ParseParametersWorker(flags, allowAmbiguity: true);
        ParseExpected(SyntaxKind.CloseParenToken);

        return parameters;
    }

    private void ParseTypeMemberSemicolon()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // We allow type members to be separated by commas or (possibly ASI) semicolons.
        // First check if it was a comma.  If so, we're done with the member.
        if (ParseOptional(SyntaxKind.CommaToken))
        {
            return;
        }

        // Didn't have a comma.  We must have a (possible ASI) semicolon.
        ParseSemicolon();
    }

    private ITypeElement ParseSignatureMember(SyntaxKind kind)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        if (kind == SyntaxKind.ConstructSignature)
        {
            ParseExpected(SyntaxKind.NewKeyword);
        }

        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(SignatureFlags.Type);
        var type = ParseReturnType(SyntaxKind.ColonToken, isType: true);
        ParseTypeMemberSemicolon();
        ITypeElement node = kind == SyntaxKind.CallSignature
            ? new CallSignatureDeclaration(typeParameters!, parameters, type)
            : new ConstructSignatureDeclaration(typeParameters!, parameters, type);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private bool IsIndexSignature()
    {
        return Token() == SyntaxKind.OpenBracketToken && LookAhead(IsUnambiguouslyIndexSignature);
    }

    private bool IsUnambiguouslyIndexSignature()
    {
        // The only allowed sequence is:
        //
        //   [id:
        //
        // However, for error recovery, we also check the following cases:
        //
        //   [...
        //   [id,
        //   [id?,
        //   [id?:
        //   [id?]
        //   [public id
        //   [private id
        //   [protected id
        //   []
        //
        NextToken();
        if (Token() == SyntaxKind.DotDotDotToken || Token() == SyntaxKind.CloseBracketToken)
        {
            return true;
        }

        if (IsModifierKind(Token()))
        {
            NextToken();
            if (IsIdentifier())
            {
                return true;
            }
        }
        else if (!IsIdentifier())
        {
            return false;
        }
        else
        {
            // Skip the identifier
            NextToken();
        }

        // A colon signifies a well formed indexer
        // A comma should be a badly formed indexer because comma expressions are not allowed
        // in computed properties.
        if (Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken)
        {
            return true;
        }

        // Question mark could be an indexer with an optional property,
        // or it could be a conditional expression in a computed property.
        if (Token() != SyntaxKind.QuestionToken)
        {
            return false;
        }

        // If any of the following tokens are after the question mark, it cannot
        // be a conditional expression, so treat it as an indexer.
        NextToken();

        return Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.CloseBracketToken;
    }

    private IndexSignatureDeclaration ParseIndexSignatureDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var parameters = ParseBracketedList(
            ParsingContext.Parameters,
            () => ParseParameter(inOuterAwaitContext: false),
            SyntaxKind.OpenBracketToken,
            SyntaxKind.CloseBracketToken);
        var type = ParseTypeAnnotation();
        ParseTypeMemberSemicolon();
        var node = new IndexSignatureDeclaration(modifiers, parameters, type);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ITypeElement ParsePropertyOrMethodSignature(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var name = ParsePropertyName();
        var questionToken = ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken);
        ITypeElement node;
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            // Method signatures don't exist in expression contexts.  So they have neither
            // [Yield] nor [Await]
            var typeParameters = ParseTypeParameters();
            var parameters = ParseParameters(SignatureFlags.Type);
            var type = ParseReturnType(SyntaxKind.ColonToken, isType: true);
            node = new MethodSignature(modifiers, name, questionToken, typeParameters, parameters, type);
        }
        else
        {
            var type = ParseTypeAnnotation();
            node = new PropertySignature(modifiers, name, questionToken, type);
            // Although type literal properties cannot not have initializers, we attempt
            // to parse an initializer so we can report in the checker that an interface
            // property or type literal property cannot have an initializer.
            if (Token() == SyntaxKind.EqualsToken)
            {
                (node as PropertySignature)!.Initializer = ParseInitializer();
            }
        }

        ParseTypeMemberSemicolon();

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private bool IsTypeMemberStart()
    {
        // Return true if we have the start of a signature member
        if (
            Token() == SyntaxKind.OpenParenToken ||
            Token() == SyntaxKind.LessThanToken ||
            Token() == SyntaxKind.GetKeyword ||
            Token() == SyntaxKind.SetKeyword
        )
        {
            return true;
        }

        var idToken = false;
        // Eat up all modifiers, but hold on to the last one in case it is actually an identifier
        while (IsModifierKind(Token()))
        {
            idToken = true;
            NextToken();
        }

        // Index signatures and computed property names are type members
        if (Token() == SyntaxKind.OpenBracketToken)
        {
            return true;
        }

        // Try to get the first property-like token following all modifiers
        if (IsLiteralPropertyName())
        {
            idToken = true;
            NextToken();
        }

        // If we were able to get any potential identifier, check that it is
        // the start of a member declaration
        if (idToken)
        {
            return Token() == SyntaxKind.OpenParenToken ||
                Token() == SyntaxKind.LessThanToken ||
                Token() == SyntaxKind.QuestionToken ||
                Token() == SyntaxKind.ColonToken ||
                Token() == SyntaxKind.CommaToken ||
                CanParseSemicolon();
        }

        return false;
    }

    private ITypeElement ParseTypeMember()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseSignatureMember(SyntaxKind.CallSignature);
        }

        if (Token() == SyntaxKind.NewKeyword && LookAhead(NextTokenIsOpenParenOrLessThan))
        {
            return ParseSignatureMember(SyntaxKind.ConstructSignature);
        }

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(allowDecorators: false);

        if (ParseContextualModifier(SyntaxKind.GetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.GetAccessor, SignatureFlags.Type);
        }

        if (ParseContextualModifier(SyntaxKind.SetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.SetAccessor, SignatureFlags.Type);
        }

        if (IsIndexSignature())
        {
            return ParseIndexSignatureDeclaration(pos, hasJSDoc, modifiers);
        }

        return ParsePropertyOrMethodSignature(pos, hasJSDoc, modifiers);
    }

    private bool NextTokenIsOpenParenOrLessThan()
    {
        NextToken();

        return Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken;
    }

    private bool NextTokenIsDot()
    {
        return NextToken() == SyntaxKind.DotToken;
    }

    private bool NextTokenIsOpenParenOrLessThanOrDot()
    {
        return NextToken() switch
        {
            SyntaxKind.OpenParenToken or SyntaxKind.LessThanToken or SyntaxKind.DotToken => true,
            _ => false,
        };
    }

    private TypeLiteralNode ParseTypeLiteral()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(new TypeLiteralNode(ParseObjectTypeMembers()), pos);
    }

    private NodeArray<ITypeElement> ParseObjectTypeMembers()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        NodeArray<ITypeElement> members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            members = ParseList(ParsingContext.TypeMembers, ParseTypeMember);
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList<ITypeElement>();
        }

        return members;
    }

    private bool IsStartOfMappedType()
    {
        NextToken();
        if (Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            return NextToken() == SyntaxKind.ReadonlyKeyword;
        }

        if (Token() == SyntaxKind.ReadonlyKeyword)
        {
            NextToken();
        }

        return Token() == SyntaxKind.OpenBracketToken && NextTokenIsIdentifier() && NextToken() == SyntaxKind.InKeyword;
    }

    private TypeParameterDeclaration ParseMappedTypeParameter()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var name = ParseIdentifierName();
        ParseExpected(SyntaxKind.InKeyword);
        var type = ParseType();

        return FinishNode(new TypeParameterDeclaration(modifiers: null, name, type, defaultType: null), pos);
    }

    private MappedTypeNode ParseMappedType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        Token? readonlyToken = null;
        if (Token() == SyntaxKind.ReadonlyKeyword || Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            readonlyToken = ParseTokenNode<ReadonlyKeyword>();
            if (readonlyToken.Kind != SyntaxKind.ReadonlyKeyword)
            {
                ParseExpected(SyntaxKind.ReadonlyKeyword);
            }
        }

        ParseExpected(SyntaxKind.OpenBracketToken);
        var typeParameter = ParseMappedTypeParameter();
        var nameType = ParseOptional(SyntaxKind.AsKeyword) ? ParseType() : null;
        ParseExpected(SyntaxKind.CloseBracketToken);
        Token? questionToken = null;
        if (Token() == SyntaxKind.QuestionToken || Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            questionToken = ParseTokenNode<QuestionToken>();
            if (questionToken.Kind != SyntaxKind.QuestionToken)
            {
                ParseExpected(SyntaxKind.QuestionToken);
            }
        }

        var type = ParseTypeAnnotation();
        ParseSemicolon();
        var members = ParseList(ParsingContext.TypeMembers, ParseTypeMember);
        ParseExpected(SyntaxKind.CloseBraceToken);

        return FinishNode(new MappedTypeNode(readonlyToken!, typeParameter, nameType!, questionToken!, type, members), pos);
    }

    private ITypeNode ParseTupleElementType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        if (ParseOptional(SyntaxKind.DotDotDotToken))
        {
            return FinishNode(new RestTypeNode(ParseType()), pos);
        }

        var type = ParseType();
        if (type is JSDocNullableType jsnt && jsnt.Pos == jsnt.Type?.Pos)
        {
            var node = new OptionalTypeNode(jsnt.Type!);
            SetTextRange(node, jsnt);
            node.Flags = type.Flags;

            return node;
        }

        return type;
    }

    private bool IsNextTokenColonOrQuestionColon()
    {
        return NextToken() == SyntaxKind.ColonToken || (Token() == SyntaxKind.QuestionToken && NextToken() == SyntaxKind.ColonToken);
    }

    private bool IsTupleElementName()
    {
        if (Token() == SyntaxKind.DotDotDotToken)
        {
            return TokenIsIdentifierOrKeyword(NextToken()) && IsNextTokenColonOrQuestionColon();
        }

        return TokenIsIdentifierOrKeyword(Token()) && IsNextTokenColonOrQuestionColon();
    }


    private ITypeNode ParseTupleElementNameOrTupleElementType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (LookAhead(IsTupleElementName))
        {
            var pos = GetNodePos();
            var hasJSDoc = HasPrecedingJSDocComment();
            var dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);
            var name = ParseIdentifierName();
            var questionToken = ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken);
            ParseExpected(SyntaxKind.ColonToken);
            var type = ParseTupleElementType();
            var node = new NamedTupleMember(dotDotDotToken!, name, questionToken!, type);

            return WithJSDoc(FinishNode(node, pos), hasJSDoc);
        }

        return ParseTupleElementType();
    }

    private TupleTypeNode ParseTupleType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(
            new TupleTypeNode(ParseBracketedList(
                ParsingContext.TupleElementTypes,
                ParseTupleElementNameOrTupleElementType,
                SyntaxKind.OpenBracketToken,
                SyntaxKind.CloseBracketToken)),
            pos);
    }

    private ParenthesizedTypeNode ParseParenthesizedType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenParenToken);
        var type = ParseType();
        ParseExpected(SyntaxKind.CloseParenToken);

        return FinishNode(new ParenthesizedTypeNode(type), pos);
    }

    private NodeArray<IModifierLike> ParseModifiersForConstructorType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        NodeArray<IModifierLike>? modifiers = null;
        if (Token() == SyntaxKind.AbstractKeyword)
        {
            var pos = GetNodePos();
            NextToken();
            var modifier = FinishNode(new AbstractKeyword(), pos);
            modifiers = CreateNodeArray(new IModifierLike[] { modifier }, pos);
        }

        return modifiers!;
    }

    private IFunctionOrConstructorTypeNode ParseFunctionOrConstructorType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiersForConstructorType();
        var isConstructorType = ParseOptional(SyntaxKind.NewKeyword);
        Debug.Assert(
            modifiers == null || isConstructorType,
            "Per isStartOfFunctionOrConstructorType, a function type cannot have modifiers.");
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(SignatureFlags.Type);
        var type = ParseReturnType(SyntaxKind.EqualsGreaterThanToken, isType: false);
        IFunctionOrConstructorTypeNode node = isConstructorType
            ? new ConstructorTypeNode(modifiers!, typeParameters!, parameters, type)
            : new FunctionTypeNode(typeParameters!, parameters, type);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ITypeNode ParseKeywordAndNoDot()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var node = ParseTokenNode<KeywordTypeNode>();

        return Token() == SyntaxKind.DotToken ? null! : node;
    }

    private LiteralTypeNode ParseLiteralTypeNode(bool negative = false)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        if (negative)
        {
            NextToken();
        }

        IExpression expression = Token() == SyntaxKind.TrueKeyword || Token() == SyntaxKind.FalseKeyword ?
            ParseTokenNode<BooleanLiteral>() : Token() == SyntaxKind.NullKeyword ? ParseTokenNode<NullLiteral>()
            : ParseLiteralLikeNode<ILiteralExpression>(Token());
        if (negative)
        {
            expression = FinishNode(new PrefixUnaryExpression(SyntaxKind.MinusToken, expression), pos);
        }

        return FinishNode(new LiteralTypeNode(expression), pos);
    }

    private bool IsStartOfTypeOfImportType()
    {
        NextToken();

        return Token() == SyntaxKind.ImportKeyword;
    }

    private ImportTypeNode ParseImportType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        _sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
        var pos = GetNodePos();
        var isTypeOf = ParseOptional(SyntaxKind.TypeOfKeyword);
        ParseExpected(SyntaxKind.ImportKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var type = ParseType();
        ImportAttributes? attributes = null;
        if (ParseOptional(SyntaxKind.CommaToken))
        {
            var _ = _scanner.GetTokenStart();
            ParseExpected(SyntaxKind.OpenBraceToken);
            var currentToken = Token();
            if (currentToken == SyntaxKind.WithKeyword || currentToken == SyntaxKind.AssertKeyword)
            {
                NextToken();
            }
            else
            {
                ParseErrorAtCurrentToken(Diagnostics._argument0Expected, TokenToString(SyntaxKind.WithKeyword));
            }

            ParseExpected(SyntaxKind.ColonToken);
            attributes = ParseImportAttributes(currentToken, skipKeyword: true);
            ParseOptional(SyntaxKind.CommaToken);
            ParseExpected(SyntaxKind.CloseBraceToken);
        }

        ParseExpected(SyntaxKind.CloseParenToken);
        var qualifier = ParseOptional(SyntaxKind.DotToken) ? ParseEntityNameOfTypeReference() : null;
        var typeArguments = ParseTypeArgumentsOfTypeReference();

        return FinishNode(new ImportTypeNode(type, attributes, qualifier, typeArguments, isTypeOf), pos);
    }

    private bool NextTokenIsNumericOrBigIntLiteral()
    {
        NextToken();

        return Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral;
    }

    private ITypeNode ParseNonArrayType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        switch (Token())
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.ObjectKeyword:
                // If these are followed by a dot, then parse these out as a dotted type reference instead.
                return TryParse(ParseKeywordAndNoDot) ?? ParseTypeReference();
            case SyntaxKind.AsteriskEqualsToken:
                // If there is '*=', treat it as * followed by postfix =
                _scanner.ReScanAsteriskEqualsToken();
                return ParseJSDocAllType();
            // falls through
            case SyntaxKind.AsteriskToken:
                return ParseJSDocAllType();
            case SyntaxKind.QuestionQuestionToken:
                // If there is '??', treat it as prefix-'?' in JSDoc type.
                _scanner.ReScanQuestionToken();
                return ParseJSDocUnknownOrNullableType();
            // falls through
            case SyntaxKind.QuestionToken:
                return ParseJSDocUnknownOrNullableType();
            case SyntaxKind.FunctionKeyword:
                return ParseJSDocFunctionType();
            case SyntaxKind.ExclamationToken:
                return ParseJSDocNonNullableType();
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NullKeyword:
                return ParseLiteralTypeNode();
            case SyntaxKind.MinusToken:
                return LookAhead(NextTokenIsNumericOrBigIntLiteral) ? ParseLiteralTypeNode(negative: true) : ParseTypeReference();
            case SyntaxKind.VoidKeyword:
                return ParseTokenNode<KeywordTypeNode>();
            case SyntaxKind.ThisKeyword:
                {
                    var thIsKeyword = ParseThisTypeNode();
                    if (Token() == SyntaxKind.IsKeyword && !_scanner.HasPrecedingLineBreak())
                    {
                        return ParseThisTypePredicate(thIsKeyword);
                    }
                    else
                    {
                        return thIsKeyword;
                    }
                }
            case SyntaxKind.TypeOfKeyword:
                return LookAhead(IsStartOfTypeOfImportType) ? ParseImportType() : ParseTypeQuery();
            case SyntaxKind.OpenBraceToken:
                return LookAhead(IsStartOfMappedType) ? ParseMappedType() : ParseTypeLiteral();
            case SyntaxKind.OpenBracketToken:
                return ParseTupleType();
            case SyntaxKind.OpenParenToken:
                return ParseParenthesizedType();
            case SyntaxKind.ImportKeyword:
                return ParseImportType();
            case SyntaxKind.AssertsKeyword:
                return LookAhead(NextTokenIsIdentifierOrKeywordOnSameLine) ? ParseAssertsTypePredicate() : ParseTypeReference();
            case SyntaxKind.TemplateHead:
                return ParseTemplateType();
            default:
                return ParseTypeReference();
        }
    }

    private bool IsStartOfType(bool inStartOfParameter = false)
    {
        return Token() switch
        {
            SyntaxKind.AnyKeyword or SyntaxKind.UnknownKeyword or SyntaxKind.StringKeyword
                or SyntaxKind.NumberKeyword or SyntaxKind.BigIntKeyword or SyntaxKind.BooleanKeyword
                or SyntaxKind.ReadonlyKeyword or SyntaxKind.SymbolKeyword or SyntaxKind.UniqueKeyword
                or SyntaxKind.VoidKeyword or SyntaxKind.UndefinedKeyword or SyntaxKind.NullKeyword
                or SyntaxKind.ThisKeyword or SyntaxKind.TypeOfKeyword or SyntaxKind.NeverKeyword
                or SyntaxKind.OpenBraceToken or SyntaxKind.OpenBracketToken or SyntaxKind.LessThanToken
                or SyntaxKind.BarToken or SyntaxKind.AmpersandToken or SyntaxKind.NewKeyword
                or SyntaxKind.StringLiteral or SyntaxKind.NumericLiteral or SyntaxKind.BigIntLiteral
                or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.ObjectKeyword
                or SyntaxKind.AsteriskToken or SyntaxKind.QuestionToken or SyntaxKind.ExclamationToken
                or SyntaxKind.DotDotDotToken or SyntaxKind.InferKeyword or SyntaxKind.ImportKeyword
                or SyntaxKind.AssertsKeyword or SyntaxKind.NoSubstitutionTemplateLiteral
                or SyntaxKind.TemplateHead => true,
            SyntaxKind.FunctionKeyword => !inStartOfParameter,
            SyntaxKind.MinusToken => !inStartOfParameter && LookAhead(NextTokenIsNumericOrBigIntLiteral),
            SyntaxKind.OpenParenToken => !inStartOfParameter && LookAhead(IsStartOfParenthesizedOrFunctionType),// Only consider '(' the start of a type if followed by ')', '...', an identifier, a modifier,
                                                                                                                // or something that starts a type. We don't want to consider things like '(1)' a type.
            _ => IsIdentifier(),
        };
    }

    private bool IsStartOfParenthesizedOrFunctionType()
    {
        NextToken();

        return Token() == SyntaxKind.CloseParenToken || IsStartOfParameter(isJSDocParameter: false) || IsStartOfType();
    }

    private ITypeNode ParsePostfixTypeOrHigher()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var type = ParseNonArrayType();
        while (!_scanner.HasPrecedingLineBreak())
        {
            switch (Token())
            {
                case SyntaxKind.ExclamationToken:
                    NextToken();
                    type = FinishNode(new JSDocNonNullableType(type, postfix: true), pos);
                    break;
                case SyntaxKind.QuestionToken:
                    // If next token is start of a type we have a conditional type
                    if (LookAhead(NextTokenIsStartOfType))
                    {
                        return type;
                    }
                    NextToken();
                    type = FinishNode(new JSDocNullableType(type, postfix: true), pos);
                    break;
                case SyntaxKind.OpenBracketToken:
                    ParseExpected(SyntaxKind.OpenBracketToken);
                    if (IsStartOfType())
                    {
                        var indexType = ParseType();
                        ParseExpected(SyntaxKind.CloseBracketToken);
                        type = FinishNode(new IndexedAccessTypeNode(type, indexType), pos);
                    }
                    else
                    {
                        ParseExpected(SyntaxKind.CloseBracketToken);
                        type = FinishNode(new ArrayTypeNode(type), pos);
                    }
                    break;
                default:
                    return type;
            }
        }

        return type;
    }

    private TypeOperatorNode ParseTypeOperator(SyntaxKind @operator)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(@operator);

        return FinishNode(new TypeOperatorNode(@operator, ParseTypeOperatorOrHigher()), pos);
    }

    private ITypeNode TryParseConstraintOfInferType()
    {
        if (ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            var constraint = DisallowConditionalTypesAnd(ParseType);
            if (InDisallowConditionalTypesContext() || Token() != SyntaxKind.QuestionToken)
            {
                return constraint;
            }
        }

        return null!;
    }

    private TypeParameterDeclaration ParseTypeParameterOfInferType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var name = ParseIdentifier();
        var constraint = TryParse(TryParseConstraintOfInferType);
        var node = new TypeParameterDeclaration(modifiers: null, name, constraint);

        return FinishNode(node, pos);
    }

    private InferTypeNode ParseInferType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.InferKeyword);

        return FinishNode(new InferTypeNode(ParseTypeParameterOfInferType()), pos);
    }

    private ITypeNode ParseTypeOperatorOrHigher()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var @operator = Token();

        return @operator switch
        {
            SyntaxKind.KeyOfKeyword or SyntaxKind.UniqueKeyword or SyntaxKind.ReadonlyKeyword => ParseTypeOperator(@operator),
            SyntaxKind.InferKeyword => ParseInferType(),
            _ => AllowConditionalTypesAnd(ParsePostfixTypeOrHigher),
        };
    }

    private ITypeNode ParseFunctionOrConstructorTypeToError(bool IsInUnionType)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // the function type and constructor type shorthand notation
        // are not allowed directly in unions and intersections, but we'll
        // try to parse them gracefully and issue a helpful message.
        if (IsStartOfFunctionTypeOrConstructorType())
        {
            var type = ParseFunctionOrConstructorType();
            DiagnosticMessage diagnostic;
            if (type is FunctionTypeNode)
            {
                diagnostic = IsInUnionType
                    ? Diagnostics._functionTypeNotationMustBeParenthesizedWhenUsedInAUnionType
                    : Diagnostics._functionTypeNotationMustBeParenthesizedWhenUsedInAnIntersectionType;
            }
            else
            {
                diagnostic = IsInUnionType
                    ? Diagnostics._constructorTypeNotationMustBeParenthesizedWhenUsedInAUnionType
                    : Diagnostics._constructorTypeNotationMustBeParenthesizedWhenUsedInAnIntersectionType;
            }

            ParseErrorAtRange(type, diagnostic);

            return type;
        }

        return null!;
    }

    private ITypeNode ParseUnionOrIntersectionType(
        SyntaxKind @operator,
        Func<ITypeNode> parseConstituentType,
        Func<NodeArray<ITypeNode>, IUnionOrIntersectionTypeNode> createTypeNode)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var isUnionType = @operator == SyntaxKind.BarToken;
        var hasLeadingOperator = ParseOptional(@operator);
        var type = (hasLeadingOperator ? ParseFunctionOrConstructorTypeToError(isUnionType) : null)
            ?? parseConstituentType();
        if (Token() == @operator || hasLeadingOperator)
        {
            List<ITypeNode> types = [type];
            while (ParseOptional(@operator))
            {
                types.Add(ParseFunctionOrConstructorTypeToError(isUnionType) ?? parseConstituentType());
            }

            type = FinishNode(createTypeNode(CreateNodeArray(types, pos)), pos);
        }

        return type;
    }

    private ITypeNode ParseIntersectionTypeOrHigher()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseUnionOrIntersectionType(
            SyntaxKind.AmpersandToken,
            ParseTypeOperatorOrHigher,
            types => new IntersectionTypeNode(types));
    }

    private ITypeNode ParseUnionTypeOrHigher()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseUnionOrIntersectionType(
            SyntaxKind.BarToken,
            ParseIntersectionTypeOrHigher,
            types => new UnionTypeNode(types));
    }

    private bool NextTokenIsNewKeyword()
    {
        NextToken();

        return Token() == SyntaxKind.NewKeyword;
    }

    private bool IsStartOfFunctionTypeOrConstructorType()
    {
        if (Token() == SyntaxKind.LessThanToken)
        {
            return true;
        }

        if (Token() == SyntaxKind.OpenParenToken && LookAhead(IsUnambiguouslyStartOfFunctionType))
        {
            return true;
        }

        return Token() == SyntaxKind.NewKeyword ||
            Token() == SyntaxKind.AbstractKeyword && LookAhead(NextTokenIsNewKeyword);
    }

    private bool SkipParameterStart()
    {
        if (IsModifierKind(Token()))
        {
            // Skip modifiers
            ParseModifiers(allowDecorators: false);
        }

        if (IsIdentifier() || Token() == SyntaxKind.ThisKeyword)
        {
            NextToken();

            return true;
        }

        if (Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.OpenBraceToken)
        {
            // Return true if we can parse an array or object binding pattern with no errors
            var previousErrorCount = _parseDiagnostics.Count;
            ParseIdentifierOrPattern();

            return previousErrorCount == _parseDiagnostics.Count;
        }

        return false;
    }

    private bool IsUnambiguouslyStartOfFunctionType()
    {
        NextToken();
        if (Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.DotDotDotToken)
        {
            // ( )
            // ( ...
            return true;
        }

        if (SkipParameterStart())
        {
            // We successfully skipped modifiers (if any) and an identifier or binding pattern,
            // now see if we have something that indicates a parameter declaration
            if (
                Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken ||
                Token() == SyntaxKind.QuestionToken || Token() == SyntaxKind.EqualsToken
            )
            {
                // ( xxx :
                // ( xxx ,
                // ( xxx ?
                // ( xxx =
                return true;
            }

            if (Token() == SyntaxKind.CloseParenToken)
            {
                NextToken();
                if (Token() == SyntaxKind.EqualsGreaterThanToken)
                {
                    // ( xxx ) =>
                    return true;
                }
            }
        }

        return false;
    }

    private ITypeNode ParseTypeOrTypePredicate()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var typePredicateVariable = IsIdentifier() ? TryParse(ParseTypePredicatePrefix) : null;
        var type = ParseType();
        if (typePredicateVariable != null)
        {
            return FinishNode(new TypePredicateNode(assertsModifier: null!, typePredicateVariable, type), pos);
        }
        else
        {
            return type;
        }
    }

    private Identifier ParseTypePredicatePrefix()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var id = ParseIdentifier();
        if (Token() == SyntaxKind.IsKeyword && !_scanner.HasPrecedingLineBreak())
        {
            NextToken();

            return id;
        }

        return null!;
    }

    private TypePredicateNode ParseAssertsTypePredicate()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var assertsModifier = ParseExpectedToken<AssertsKeyword>(SyntaxKind.AssertsKeyword);
        INode parameterName = Token() == SyntaxKind.ThisKeyword ? ParseThisTypeNode() : ParseIdentifier();
        var type = ParseOptional(SyntaxKind.IsKeyword) ? ParseType() : null;

        return FinishNode(new TypePredicateNode(assertsModifier!, parameterName, type!), pos);
    }

    private ITypeNode ParseType()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if ((_contextFlags & NodeFlags.TypeExcludesFlags) != 0)
        {
            return DoOutsideOfContext(NodeFlags.TypeExcludesFlags, ParseType);
        }

        if (IsStartOfFunctionTypeOrConstructorType())
        {
            return ParseFunctionOrConstructorType();
        }

        var pos = GetNodePos();
        var type = ParseUnionTypeOrHigher();
        if (!InDisallowConditionalTypesContext() && !_scanner.HasPrecedingLineBreak() &&
            ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            // The type following 'extends' is not permitted to be another conditional type
            var extendsType = DisallowConditionalTypesAnd(ParseType);
            ParseExpected(SyntaxKind.QuestionToken);
            var trueType = AllowConditionalTypesAnd(ParseType);
            ParseExpected(SyntaxKind.ColonToken);
            var falseType = AllowConditionalTypesAnd(ParseType);

            return FinishNode(new ConditionalTypeNode(type, extendsType, trueType, falseType), pos);
        }

        return type;
    }

    private ITypeNode ParseTypeAnnotation()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseOptional(SyntaxKind.ColonToken) ? ParseType() : null!;
    }

    // EXPRESSIONS
    private bool IsStartOfLeftHandSideExpression()
    {
        return Token() switch
        {
            SyntaxKind.ThisKeyword or SyntaxKind.SuperKeyword or SyntaxKind.NullKeyword
                or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NumericLiteral
                or SyntaxKind.BigIntLiteral or SyntaxKind.StringLiteral or SyntaxKind.NoSubstitutionTemplateLiteral
                or SyntaxKind.TemplateHead or SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken
                or SyntaxKind.OpenBraceToken or SyntaxKind.FunctionKeyword or SyntaxKind.ClassKeyword
                or SyntaxKind.NewKeyword or SyntaxKind.SlashToken or SyntaxKind.SlashEqualsToken
                or SyntaxKind.Identifier => true,
            SyntaxKind.ImportKeyword => LookAhead(NextTokenIsOpenParenOrLessThanOrDot),
            _ => IsIdentifier(),
        };
    }

    private bool IsStartOfExpression()
    {
        if (IsStartOfLeftHandSideExpression())
        {
            return true;
        }

        switch (Token())
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
            case SyntaxKind.ExclamationToken:
            case SyntaxKind.DeleteKeyword:
            case SyntaxKind.TypeOfKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.PlusPlusToken:
            case SyntaxKind.MinusMinusToken:
            case SyntaxKind.LessThanToken:
            case SyntaxKind.AwaitKeyword:
            case SyntaxKind.YieldKeyword:
            case SyntaxKind.PrivateIdentifier:
            case SyntaxKind.AtToken:
                // Yield/await always starts an expression.  Either it is an identifier (in which case
                // it is definitely an expression).  Or it's a keyword (either because we're in
                // a generator or async function, or in strict mode (or both)) and it started a yield or await expression.
                return true;

            default:
                // Error tolerance.  If we see the start of some binary operator, we consider
                // that the start of an expression.  That way we'll parse out a missing identifier,
                // give a good message about an identifier being missing, and then consume the
                // rest of the binary expression.
                if (IsBinaryOperator())
                {
                    return true;
                }

                return IsIdentifier();
        }
    }

    private bool IsStartOfExpressionStatement()
    {
        // As per the grammar, none of '{' or 'function' or 'class' can start an expression statement.
        return Token() != SyntaxKind.OpenBraceToken &&
            Token() != SyntaxKind.FunctionKeyword &&
            Token() != SyntaxKind.ClassKeyword &&
            Token() != SyntaxKind.AtToken &&
            IsStartOfExpression();
    }

    private IExpression ParseExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // Expression[in]:
        //      AssignmentExpression[in]
        //      Expression[in] , AssignmentExpression[in]

        // clear the decorator context when parsing Expression, as it should be unambiguous when parsing a decorator
        var saveDecoratorContext = InDecoratorContext();
        if (saveDecoratorContext)
        {
            SetDecoratorContext(val: false);
        }

        var pos = GetNodePos();
        var expr = ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true);
        Token? operatorToken;
        while ((operatorToken = ParseOptionalToken<CommaToken>(SyntaxKind.CommaToken)) != null)
        {
            expr = MakeBinaryExpression(
                expr,
                operatorToken,
                ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true),
                pos);
        }

        if (saveDecoratorContext)
        {
            SetDecoratorContext(val: true);
        }

        return expr;
    }

    private IExpression ParseInitializer()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseOptional(SyntaxKind.EqualsToken) ? ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true) : null!;
    }

    private IExpression ParseAssignmentExpressionOrHigher(bool allowReturnTypeInArrowFunction)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        //  AssignmentExpression[in,yield]:
        //      1) ConditionalExpression[?in,?yield]
        //      2) LeftHandSideExpression = AssignmentExpression[?in,?yield]
        //      3) LeftHandSideExpression AssignmentOperator AssignmentExpression[?in,?yield]
        //      4) ArrowFunctionExpression[?in,?yield]
        //      5) AsyncArrowFunctionExpression[in,yield,await]
        //      6) [+Yield] YieldExpression[?In]
        //
        // Note: for ease of implementation we treat productions '2' and '3' as the same thing.
        // (i.e. they're both BinaryExpressions with an assignment operator in it).

        // First, do the simple check if we have a YieldExpression (production '6').
        if (IsYieldExpression())
        {
            return ParseYieldExpression();
        }

        // Then, check if we have an arrow function (production '4' and '5') that starts with a parenthesized
        // parameter list or is an async arrow function.
        // AsyncArrowFunctionExpression:
        //      1) async[no LineTerminator here]AsyncArrowBindingIdentifier[?Yield][no LineTerminator here]=>AsyncConciseBody[?In]
        //      2) CoverCallExpressionAndAsyncArrowHead[?Yield, ?Await][no LineTerminator here]=>AsyncConciseBody[?In]
        // Production (1) of AsyncArrowFunctionExpression is parsed in "TryParseAsyncSimpleArrowFunctionExpression".
        // And production (2) is parsed in "TryParseParenthesizedArrowFunctionExpression".
        //
        // If we do successfully parse arrow-function, we must *not* recurse for productions 1, 2 or 3. An ArrowFunction is
        // not a LeftHandSideExpression, nor does it start a ConditionalExpression.  So we are done
        // with AssignmentExpression if we see one.
        IExpression arrowExpression = TryParseParenthesizedArrowFunctionExpression(allowReturnTypeInArrowFunction)
            ?? TryParseAsyncSimpleArrowFunctionExpression(allowReturnTypeInArrowFunction);
        if (arrowExpression != null)
        {
            return arrowExpression;
        }

        // Now try to see if we're in production '1', '2' or '3'.  A conditional expression can
        // start with a LogicalOrExpression, while the assignment productions can only start with
        // LeftHandSideExpressions.
        //
        // So, first, we try to just parse out a BinaryExpression.  If we get something that is a
        // LeftHandSide or higher, then we can try to parse out the assignment expression part.
        // Otherwise, we try to parse out the conditional expression bit.  We want to allow any
        // binary expression here, so we pass in the 'lowest' precedence here so that it matches
        // and consumes anything.
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var expr = ParseBinaryExpressionOrHigher(OperatorPrecedence.Lowest);

        // To avoid a look-ahead, we did not handle the case of an arrow function with a single un-parenthesized
        // parameter ('x => ...') above. We handle it here by checking if the parsed expression was a single
        // identifier and the current token is an arrow.
        if (expr.Kind == SyntaxKind.Identifier && Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            return ParseSimpleArrowFunctionExpression(
                pos,
                (expr as Identifier)!,
                allowReturnTypeInArrowFunction,
                hasJSDoc,
                asyncModifier: null!);
        }

        // Now see if we might be in cases '2' or '3'.
        // If the expression was a LHS expression, and we have an assignment operator, then
        // we're in '2' or '3'. Consume the assignment and return.
        //
        // Note: we call ReScanGreaterToken so that we get an appropriately merged token
        // for cases like `> > =` becoming `>>=`
        if (IsLeftHandSideExpression(expr) && IsAssignmentOperator(ReScanGreaterToken()))
        {
            return MakeBinaryExpression(
                expr,
                ParseTokenNode<OperatorToken>(),
                ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction),
                pos);
        }

        // It wasn't an assignment or a lambda.  This is a conditional expression:
        return ParseConditionalExpressionRest(expr, pos, allowReturnTypeInArrowFunction);
    }

    private bool IsYieldExpression()
    {
        if (Token() == SyntaxKind.YieldKeyword)
        {
            // If we have a 'yield' keyword, and this is a context where yield expressions are
            // allowed, then definitely parse out a yield expression.
            if (InYieldContext())
            {
                return true;
            }

            // We're in a context where 'yield expr' is not allowed.  However, if we can
            // definitely tell that the user was trying to parse a 'yield expr' and not
            // just a normal expr that start with a 'yield' identifier, then parse out
            // a 'yield expr'.  We can then report an error later that they are only
            // allowed in generator expressions.
            //
            // for example, if we see 'yield(foo)', then we'll have to treat that as an
            // invocation expression of something called 'yield'.  However, if we have
            // 'yield foo' then that is not legal as a normal expression, so we can
            // definitely recognize this as a yield expression.
            //
            // for now we just check if the next token is an identifier.  More heuristics
            // can be added here later as necessary.  We just need to make sure that we
            // don't accidentally consume something legal.
            return LookAhead(NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine);
        }

        return false;
    }

    private bool NextTokenIsIdentifierOnSameLine()
    {
        NextToken();

        return !_scanner.HasPrecedingLineBreak() && IsIdentifier();
    }

    private YieldExpression ParseYieldExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        // YieldExpression[In] :
        //      yield
        //      yield [no LineTerminator here] [Lexical goal InputElementRegExp]AssignmentExpression[?In, Yield]
        //      yield [no LineTerminator here] * [Lexical goal InputElementRegExp]AssignmentExpression[?In, Yield]
        NextToken();

        if (
            !_scanner.HasPrecedingLineBreak() &&
            (Token() == SyntaxKind.AsteriskToken || IsStartOfExpression())
        )
        {
            return FinishNode(
                new YieldExpression(
                    ParseOptionalToken<AsteriskToken>(SyntaxKind.AsteriskToken)!,
                    ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true)),
                pos);
        }
        else
        {
            // if the next token is not on the same line as yield.  or we don't have an '*' or
            // the start of an expression, then this is just a simple "yield" expression.
            return FinishNode(new YieldExpression(asteriskToken: null!, expression: null!), pos);
        }
    }

    private ArrowFunction ParseSimpleArrowFunctionExpression(
        int pos,
        Identifier identifier,
        bool allowReturnTypeInArrowFunction,
        bool hasJSDoc,
        NodeArray<IModifierLike> asyncModifier)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        Debug.Assert(
            Token() == SyntaxKind.EqualsGreaterThanToken,
            "parseSimpleArrowFunctionExpression should only have been called if we had a =>");

        var parameter = new ParameterDeclaration(
            modifiers: null,
            dotDotDotToken: null,
            identifier,
            questionToken: null,
            type: null,
            initializer: null
        );
        FinishNode(parameter, identifier.Pos ?? 0);

        var parameters = CreateNodeArray<ParameterDeclaration>([parameter], parameter.Pos ?? 0, parameter.End);
        var equalsGreaterThanToken = ParseExpectedToken<EqualsGreaterThanToken>(SyntaxKind.EqualsGreaterThanToken);
        var body = ParseArrowFunctionExpressionBody(isAsync: asyncModifier != null, allowReturnTypeInArrowFunction);
        var node = new ArrowFunction(asyncModifier, typeParameters: null, parameters, type: null, equalsGreaterThanToken, body);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ArrowFunction TryParseParenthesizedArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        var triState = IsParenthesizedArrowFunctionExpression();
        if (triState == Tristate.False)
        {
            // It's definitely not a parenthesized arrow function expression.
            return null!;
        }

        // If we definitely have an arrow function, then we can just parse one, not requiring a
        // following => or { token. Otherwise, we *might* have an arrow function.  Try to parse
        // it out, but don't allow any ambiguity, and return 'undefined' if this could be an
        // expression instead.
        return triState == Tristate.True ?
            ParseParenthesizedArrowFunctionExpression(allowAmbiguity: true, allowReturnTypeInArrowFunction: true) :
            TryParse(() => ParsePossibleParenthesizedArrowFunctionExpression(allowReturnTypeInArrowFunction));
    }

    //  True        -> We definitely expect a parenthesized arrow function here.
    //  False       -> There *cannot* be a parenthesized arrow function here.
    //  Unknown     -> There *might* be a parenthesized arrow function here.
    //                 Speculatively look ahead to be sure, and rollback if not.
    private Tristate IsParenthesizedArrowFunctionExpression()
    {
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken || Token() == SyntaxKind.AsyncKeyword)
        {
            return LookAhead(IsParenthesizedArrowFunctionExpressionWorker);
        }

        if (Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            // ERROR RECOVERY TWEAK:
            // If we see a standalone => try to parse it as an arrow function expression as that's
            // likely what the user intended to write.
            return Tristate.True;
        }

        // Definitely not a parenthesized arrow function.
        return Tristate.False;
    }

    private Tristate IsParenthesizedArrowFunctionExpressionWorker()
    {
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            NextToken();
            if (_scanner.HasPrecedingLineBreak())
            {
                return Tristate.False;
            }

            if (Token() != SyntaxKind.OpenParenToken && Token() != SyntaxKind.LessThanToken)
            {
                return Tristate.False;
            }
        }

        var first = Token();
        var second = NextToken();

        if (first == SyntaxKind.OpenParenToken)
        {
            if (second == SyntaxKind.CloseParenToken)
            {
                // Simple cases: "() =>", "(): ", and "() {".
                // This is an arrow function with no parameters.
                // The last one is not actually an arrow function,
                // but this is probably what the user intended.
                var third = NextToken();

                return third switch
                {
                    SyntaxKind.EqualsGreaterThanToken or SyntaxKind.ColonToken
                        or SyntaxKind.OpenBraceToken => Tristate.True,
                    _ => Tristate.False,
                };
            }

            // If encounter "([" or "({", this could be the start of a binding pattern.
            // Examples:
            //      ([ x ]) => { }
            //      ({ x }) => { }
            //      ([ x ])
            //      ({ x })
            if (second == SyntaxKind.OpenBracketToken || second == SyntaxKind.OpenBraceToken)
            {
                return Tristate.Unknown;
            }

            // Simple case: "(..."
            // This is an arrow function with a rest parameter.
            if (second == SyntaxKind.DotDotDotToken)
            {
                return Tristate.True;
            }

            // Check for "(xxx yyy", where xxx is a modifier and yyy is an identifier. This
            // isn't actually allowed, but we want to treat it as a lambda so we can provide
            // a good error message.
            if (IsModifierKind(second) && second != SyntaxKind.AsyncKeyword && LookAhead(NextTokenIsIdentifier))
            {
                if (NextToken() == SyntaxKind.AsKeyword)
                {
                    // https://github.com/microsoft/TypeScript/issues/44466
                    return Tristate.False;
                }

                return Tristate.True;
            }

            // If we had "(" followed by something that's not an identifier,
            // then this definitely doesn't look like a lambda.  "this" is not
            // valid, but we want to parse it and then give a semantic error.
            if (!IsIdentifier() && second != SyntaxKind.ThisKeyword)
            {
                return Tristate.False;
            }

            switch (NextToken())
            {
                case SyntaxKind.ColonToken:
                    // If we have something like "(a:", then we must have a
                    // type-annotated parameter in an arrow function expression.
                    return Tristate.True;
                case SyntaxKind.QuestionToken:
                    NextToken();
                    // If we have "(a?:" or "(a?," or "(a?=" or "(a?)" then it is definitely a lambda.
                    if (Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.EqualsToken || Token() == SyntaxKind.CloseParenToken)
                    {
                        return Tristate.True;
                    }
                    // Otherwise it is definitely not a lambda.
                    return Tristate.False;
                case SyntaxKind.CommaToken:
                case SyntaxKind.EqualsToken:
                case SyntaxKind.CloseParenToken:
                    // If we have "(a," or "(a=" or "(a)" this *could* be an arrow function
                    return Tristate.Unknown;
            }

            // It is definitely not an arrow function
            return Tristate.False;
        }
        else
        {
            Debug.Assert(first == SyntaxKind.LessThanToken);

            // If we have "<" not followed by an identifier,
            // then this definitely is not an arrow function.
            if (!IsIdentifier() && Token() != SyntaxKind.ConstKeyword)
            {
                return Tristate.False;
            }

            // JSX overrides
            if (_languageVariant == LanguageVariant.JSX)
            {
                var isArrowFunctionInJsx = LookAhead(() =>
                {
                    ParseOptional(SyntaxKind.ConstKeyword);
                    var third = NextToken();
                    if (third == SyntaxKind.ExtendsKeyword)
                    {
                        var fourth = NextToken();

                        return fourth switch
                        {
                            SyntaxKind.EqualsToken or SyntaxKind.GreaterThanToken or SyntaxKind.SlashToken => false,
                            _ => true,
                        };
                    }
                    else if (third == SyntaxKind.CommaToken || third == SyntaxKind.EqualsToken)
                    {
                        return true;
                    }

                    return false;
                });

                if (isArrowFunctionInJsx)
                {
                    return Tristate.True;
                }

                return Tristate.False;
            }

            // This *could* be a parenthesized arrow function.
            return Tristate.Unknown;
        }
    }

    private ArrowFunction ParsePossibleParenthesizedArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var tokenPos = _scanner.GetTokenStart();
        if (_notParenthesizedArrow?.Contains(tokenPos) == true)
        {
            return null!;
        }

        var result = ParseParenthesizedArrowFunctionExpression(allowAmbiguity: false, allowReturnTypeInArrowFunction);
        if (result == null)
        {
            (_notParenthesizedArrow ??= []).Add(tokenPos);
        }

        return result!;
    }

    private ArrowFunction TryParseAsyncSimpleArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        // We do a check here so that we won't be doing unnecessarily call to "LookAhead"
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            if (LookAhead(IsUnParenthesizedAsyncArrowFunctionWorker) == Tristate.True)
            {
                var pos = GetNodePos();
                var hasJSDoc = HasPrecedingJSDocComment();
                var asyncModifier = ParseModifiersForArrowFunction();
                var expr = ParseBinaryExpressionOrHigher(OperatorPrecedence.Lowest);

                return ParseSimpleArrowFunctionExpression(
                    pos,
                    (expr as Identifier)!,
                    allowReturnTypeInArrowFunction,
                    hasJSDoc,
                    asyncModifier);
            }
        }

        return null!;
    }

    private Tristate IsUnParenthesizedAsyncArrowFunctionWorker()
    {
        // AsyncArrowFunctionExpression:
        //      1) async[no LineTerminator here]AsyncArrowBindingIdentifier[?Yield][no LineTerminator here]=>AsyncConciseBody[?In]
        //      2) CoverCallExpressionAndAsyncArrowHead[?Yield, ?Await][no LineTerminator here]=>AsyncConciseBody[?In]
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            NextToken();
            // If the "async" is followed by "=>" token then it is not a beginning of an async arrow-function
            // but instead a simple arrow-function which will be parsed inside "ParseAssignmentExpressionOrHigher"
            if (_scanner.HasPrecedingLineBreak() || Token() == SyntaxKind.EqualsGreaterThanToken)
            {
                return Tristate.False;
            }

            // Check for un-parenthesized AsyncArrowFunction
            var expr = ParseBinaryExpressionOrHigher(OperatorPrecedence.Lowest);
            if (!_scanner.HasPrecedingLineBreak() && expr.Kind == SyntaxKind.Identifier && Token() == SyntaxKind.EqualsGreaterThanToken)
            {
                return Tristate.True;
            }
        }

        return Tristate.False;
    }

    private ArrowFunction ParseParenthesizedArrowFunctionExpression(
        bool allowAmbiguity,
        bool allowReturnTypeInArrowFunction)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiersForArrowFunction();
        var isAsync = modifiers?.Any(x => x.Kind == SyntaxKind.AsyncKeyword) == true ? SignatureFlags.Await : SignatureFlags.None;
        // Arrow functions are never generators.
        //
        // If we're speculatively parsing a signature for a parenthesized arrow function, then
        // we have to have a complete parameter list.  Otherwise we might see something like
        // a => (b => c)
        // And think that "(b =>" was actually a parenthesized arrow function with a missing
        // close paren.
        var typeParameters = ParseTypeParameters();

        NodeArray<ParameterDeclaration>? parameters = null;
        if (!ParseExpected(SyntaxKind.OpenParenToken))
        {
            if (!allowAmbiguity)
            {
                return null!;
            }

            parameters = CreateMissingList<ParameterDeclaration>();
        }
        else
        {
            if (!allowAmbiguity)
            {
                var maybeParameters = ParseParametersWorker(isAsync, allowAmbiguity);
                if (maybeParameters == null)
                {
                    return null!;
                }

                parameters = maybeParameters;
            }
            else
            {
                parameters = ParseParametersWorker(isAsync, allowAmbiguity);
            }

            if (!ParseExpected(SyntaxKind.CloseParenToken) && !allowAmbiguity)
            {
                return null!;
            }
        }

        var hasReturnColon = Token() == SyntaxKind.ColonToken;
        var type = ParseReturnType(SyntaxKind.ColonToken, isType: false);
        if (type != null && !allowAmbiguity && TypeHasArrowFunctionBlockingParseError(type))
        {
            return null!;
        }

        // Parsing a signature isn't enough.
        // Parenthesized arrow signatures often look like other valid expressions.
        // For instance:
        //  - "(x = 10)" is an assignment expression parsed as a signature with a default parameter value.
        //  - "(x,y)" is a comma expression parsed as a signature with two parameters.
        //  - "a ? (b): c" will have "(b):" parsed as a signature with a return type annotation.
        //  - "a ? (b): function() {}" will too, since function() is a valid JSDoc function type.
        //  - "a ? (b): (function() {})" as well, but inside of a parenthesized type with an arbitrary amount of nesting.
        //
        // So we need just a bit of LookAhead to ensure that it can only be a signature.

        var unwrappedType = type;
        while (unwrappedType?.Kind == SyntaxKind.ParenthesizedType)
        {
            unwrappedType = (unwrappedType as ParenthesizedTypeNode)?.Type; // Skip parens if need be
        }

        var hasJSDocFunctionType = unwrappedType is JSDocFunctionType;
        if (!allowAmbiguity && Token() != SyntaxKind.EqualsGreaterThanToken && (hasJSDocFunctionType || Token() != SyntaxKind.OpenBraceToken))
        {
            // Returning undefined here will cause our caller to rewind to where we started from.
            return null!;
        }

        // If we have an arrow, then try to parse the body. Even if not, try to parse if we
        // have an opening brace, just in case we're in an error state.
        var lastToken = Token();
        var equalsGreaterThanToken = ParseExpectedToken<EqualsGreaterThanToken>(SyntaxKind.EqualsGreaterThanToken);
        var body = (lastToken == SyntaxKind.EqualsGreaterThanToken || lastToken == SyntaxKind.OpenBraceToken)
            ? ParseArrowFunctionExpressionBody(
                modifiers?.Any(x => x.Kind == SyntaxKind.AsyncKeyword) == true,
                allowReturnTypeInArrowFunction)
            : ParseIdentifier();

        // Given:
        //     x ? y => ({ y }) : z => ({ z })
        // We try to parse the body of the first arrow function by looking at:
        //     ({ y }) : z => ({ z })
        // This is a valid arrow function with "z" as the return type.
        //
        // But, if we're in the true side of a conditional expression, this colon
        // terminates the expression, so we cannot allow a return type if we aren't
        // certain whether or not the preceding text was parsed as a parameter list.
        //
        // For example,
        //     a() ? (b: number, c?: string): void => d() : e
        // is determined by isParenthesizedArrowFunctionExpression to unambiguously
        // be an arrow expression, so we allow a return type.
        if (!allowReturnTypeInArrowFunction && hasReturnColon)
        {
            // However, if the arrow function we were able to parse is followed by another colon
            // as in:
            //     a ? (x): string => x : null
            // Then allow the arrow function, and treat the second colon as terminating
            // the conditional expression. It's okay to do this because this code would
            // be a syntax error in JavaScript (as the second colon shouldn't be there).
            if (Token() != SyntaxKind.ColonToken)
            {
                return null!;
            }
        }

        var node = new ArrowFunction(modifiers, typeParameters, parameters, type, equalsGreaterThanToken, body);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private IBlockOrExpression ParseArrowFunctionExpressionBody(bool isAsync, bool allowReturnTypeInArrowFunction)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.OpenBraceToken)
        {
            return ParseFunctionBlock(isAsync ? SignatureFlags.Await : SignatureFlags.None);
        }

        if (
            Token() != SyntaxKind.SemicolonToken &&
            Token() != SyntaxKind.FunctionKeyword &&
            Token() != SyntaxKind.ClassKeyword &&
            IsStartOfStatement() &&
            !IsStartOfExpressionStatement()
        )
        {
            // Check if we got a plain statement (i.e. no expression-statements, no function/class expressions/declarations)
            //
            // Here we try to recover from a potential error situation in the case where the
            // user meant to supply a block. For example, if the user wrote:
            //
            //  a =>
            //      let v = 0;
            //  }
            //
            // they may be missing an open brace.  Check to see if that's the case so we can
            // try to recover better.  If we don't do this, then the next close curly we see may end
            // up preemptively closing the containing construct.
            //
            // Note: even when 'IgnoreMissingOpenBrace' is passed, parseBody will still error.
            return ParseFunctionBlock(SignatureFlags.IgnoreMissingOpenBrace | (isAsync ? SignatureFlags.Await : SignatureFlags.None));
        }

        var savedTopLevel = _topLevel;
        _topLevel = false;
        var node = isAsync
            ? DoInAwaitContext(() => ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction))
            : DoOutsideOfAwaitContext(() => ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction));
        _topLevel = savedTopLevel;

        return node;
    }

    private IExpression ParseConditionalExpressionRest(
        IExpression leftOperand,
        int pos,
        bool allowReturnTypeInArrowFunction)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // Note: we are passed in an expression which was produced from ParseBinaryExpressionOrHigher.
        var questionToken = ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken);
        if (questionToken == null)
        {
            return leftOperand;
        }

        // Note: we explicitly 'allowIn' in the whenTrue part of the condition expression, and
        // we do not that for the 'whenFalse' part.
        ColonToken? colonToken = null;

        return FinishNode(
            new ConditionalExpression(
                leftOperand,
                questionToken,
                DoOutsideOfContext(
                    _disAllowInAndDecoratorContext,
                    () => ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: false)),
                colonToken = ParseExpectedToken<ColonToken>(SyntaxKind.ColonToken)!,
                NodeIsPresent(colonToken)
                    ? ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction)
                    : CreateMissingNode<Identifier>(
                        SyntaxKind.Identifier,
                        reportAtCurrentPosition: false,
                        Diagnostics._argument0Expected,
                        TokenToString(SyntaxKind.ColonToken))),
            pos);
    }

    private IExpression ParseBinaryExpressionOrHigher(OperatorPrecedence precedence)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var leftOperand = ParseUnaryExpressionOrHigher();

        return ParseBinaryExpressionRest(precedence, leftOperand, pos);
    }

    private IExpression ParseBinaryExpressionRest(OperatorPrecedence precedence, IExpression leftOperand, int pos)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        while (true)
        {
            // We either have a binary operator here, or we're finished.  We call
            // ReScanGreaterToken so that we merge token sequences like > and = into >=

            ReScanGreaterToken();
            var newPrecedence = GetBinaryOperatorPrecedence(Token());

            // Check the precedence to see if we should "take" this operator
            // - For left associative operator (all operator but **), consume the operator,
            //   recursively call the function below, and parse binaryExpression as a rightOperand
            //   of the caller if the new precedence of the operator is greater then or equal to the current precedence.
            //   For example:
            //      a - b - c;
            //            ^token; leftOperand = b. Return b to the caller as a rightOperand
            //      a * b - c
            //            ^token; leftOperand = b. Return b to the caller as a rightOperand
            //      a - b * c;
            //            ^token; leftOperand = b. Return b * c to the caller as a rightOperand
            // - For right associative operator (**), consume the operator, recursively call the function
            //   and parse binaryExpression as a rightOperand of the caller if the new precedence of
            //   the operator is strictly grater than the current precedence
            //   For example:
            //      a ** b ** c;
            //             ^^token; leftOperand = b. Return b ** c to the caller as a rightOperand
            //      a - b ** c;
            //            ^^token; leftOperand = b. Return b ** c to the caller as a rightOperand
            //      a ** b - c
            //             ^token; leftOperand = b. Return b to the caller as a rightOperand
            var consumeCurrentOperator = Token() == SyntaxKind.AsteriskAsteriskToken ?
                newPrecedence >= precedence :
                newPrecedence > precedence;

            if (!consumeCurrentOperator)
            {
                break;
            }

            if (Token() == SyntaxKind.InKeyword && InDisallowInContext())
            {
                break;
            }

            if (Token() == SyntaxKind.AsKeyword || Token() == SyntaxKind.SatisfiesKeyword)
            {
                // Make sure we *do* perform ASI for constructs like this:
                //    var x = foo
                //    as (Bar)
                // This should be parsed as an initialized variable, followed
                // by a function call to 'as' with the argument 'Bar'
                if (_scanner.HasPrecedingLineBreak())
                {
                    break;
                }
                else
                {
                    var keywordKind = Token();
                    NextToken();
                    leftOperand = keywordKind == SyntaxKind.SatisfiesKeyword
                        ? MakeSatisfiesExpression(leftOperand, ParseType())
                        : MakeAsExpression(leftOperand, ParseType());
                }
            }
            else
            {
                leftOperand = MakeBinaryExpression(
                    leftOperand,
                    ParseTokenNode<OperatorToken>(),
                    ParseBinaryExpressionOrHigher(newPrecedence),
                    pos);
            }
        }

        return leftOperand;
    }

    private bool IsBinaryOperator()
    {
        if (InDisallowInContext() && Token() == SyntaxKind.InKeyword)
        {
            return false;
        }

        return GetBinaryOperatorPrecedence(Token()) > 0;
    }

    private SatisfiesExpression MakeSatisfiesExpression(IExpression left, ITypeNode right)
    {
        return FinishNode(new SatisfiesExpression(left, right), left.Pos ?? 0);
    }

    private BinaryExpression MakeBinaryExpression(IExpression left, Token operatorToken, IExpression right, int pos)
    {
        return FinishNode(new BinaryExpression(left, operatorToken, right), pos);
    }

    private AsExpression MakeAsExpression(IExpression left, ITypeNode right)
    {
        return FinishNode(new AsExpression(left, right), left.Pos ?? 0);
    }

    private PrefixUnaryExpression ParsePrefixUnaryExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(new PrefixUnaryExpression(Token(), NextTokenAnd(ParseSimpleUnaryExpression)), pos);
    }

    private DeleteExpression ParseDeleteExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(new DeleteExpression(NextTokenAnd(ParseSimpleUnaryExpression)), pos);
    }

    private TypeOfExpression ParseTypeOfExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(new TypeOfExpression(NextTokenAnd(ParseSimpleUnaryExpression)), pos);
    }

    private VoidExpression ParseVoidExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(new VoidExpression(NextTokenAnd(ParseSimpleUnaryExpression)), pos);
    }

    private bool IsAwaitExpression()
    {
        if (Token() == SyntaxKind.AwaitKeyword)
        {
            if (InAwaitContext())
            {
                return true;
            }

            // here we are using similar heuristics as 'isYieldExpression'
            return LookAhead(NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine);
        }

        return false;
    }

    private AwaitExpression ParseAwaitExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(new AwaitExpression(NextTokenAnd(ParseSimpleUnaryExpression)), pos);
    }

    // Parse ES7 exponential expression and await expression
    //
    // ES7 ExponentiationExpression:
    //      1) UnaryExpression[?Yield]
    //      2) UpdateExpression[?Yield] ** ExponentiationExpression[?Yield]
    private IExpression ParseUnaryExpressionOrHigher()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        /*
         * ES7 UpdateExpression:
         *      1) LeftHandSideExpression[?Yield]
         *      2) LeftHandSideExpression[?Yield][no LineTerminator here]++
         *      3) LeftHandSideExpression[?Yield][no LineTerminator here]--
         *      4) ++UnaryExpression[?Yield]
         *      5) --UnaryExpression[?Yield]
         */
        if (IsUpdateExpression())
        {
            var pos = GetNodePos();
            var updateExpression = ParseUpdateExpression();

            return Token() == SyntaxKind.AsteriskAsteriskToken ?
                (ParseBinaryExpressionRest(GetBinaryOperatorPrecedence(Token()), updateExpression, pos) as BinaryExpression)! :
                updateExpression;
        }

        /*
         * ES7 UnaryExpression:
         *      1) UpdateExpression[?yield]
         *      2) delete UpdateExpression[?yield]
         *      3) void UpdateExpression[?yield]
         *      4) typeof UpdateExpression[?yield]
         *      5) + UpdateExpression[?yield]
         *      6) - UpdateExpression[?yield]
         *      7) ~ UpdateExpression[?yield]
         *      8) ! UpdateExpression[?yield]
         */
        var unaryOperator = Token();
        var simpleUnaryExpression = ParseSimpleUnaryExpression();
        if (Token() == SyntaxKind.AsteriskAsteriskToken)
        {
            var pos = SkipTrivia(_sourceText!, simpleUnaryExpression.Pos) ?? 0;
            var end = simpleUnaryExpression.End ?? 0;
            if (simpleUnaryExpression.Kind == SyntaxKind.TypeAssertionExpression)
            {
                ParseErrorAt(
                    pos,
                    end,
                    Diagnostics._aTypeAssertionExpressionIsNotAllowedInTheLeftHandSideOfAnExponentiationExpressionConsiderEnclosingTheExpressionInParentheses);
            }
            else
            {
                Debug.Assert(IsKeywordOrPunctuation(unaryOperator));
                ParseErrorAt(
                    pos,
                    end,
                    Diagnostics._anUnaryExpressionWithThe0OperatorIsNotAllowedInTheLeftHandSideOfAnExponentiationExpressionConsiderEnclosingTheExpressionInParentheses,
                    TokenToString(unaryOperator));
            }
        }

        return simpleUnaryExpression;
    }

    // Parse ES7 simple-unary expression or higher:
    //
    // ES7 UnaryExpression:
    //      1) UpdateExpression[?yield]
    //      2) delete UnaryExpression[?yield]
    //      3) void UnaryExpression[?yield]
    //      4) typeof UnaryExpression[?yield]
    //      5) + UnaryExpression[?yield]
    //      6) - UnaryExpression[?yield]
    //      7) ~ UnaryExpression[?yield]
    //      8) ! UnaryExpression[?yield]
    //      9) [+Await] await UnaryExpression[?yield]
    private IUnaryExpression ParseSimpleUnaryExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        switch (Token())
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
            case SyntaxKind.ExclamationToken:
                return ParsePrefixUnaryExpression();

            case SyntaxKind.DeleteKeyword:
                return ParseDeleteExpression();

            case SyntaxKind.TypeOfKeyword:
                return ParseTypeOfExpression();

            case SyntaxKind.VoidKeyword:
                return ParseVoidExpression();

            case SyntaxKind.LessThanToken:
                // Just like in parseUpdateExpression, we need to avoid parsing type assertions when
                // in JSX and we see an expression like "+ <foo> bar".
                if (_languageVariant == LanguageVariant.JSX)
                {
                    return ParseJsxElementOrSelfClosingElementOrFragment(
                        inExpressionContext: true,
                        topInvalidNodePosition: null,
                        openingTag: null,
                        mustBeUnary: true);
                }
                // This is modified UnaryExpression grammar in TypeScript
                //  UnaryExpression (modified):
                //      < type > UnaryExpression
                return ParseTypeAssertion();

            case SyntaxKind.AwaitKeyword:
                if (IsAwaitExpression())
                {
                    return ParseAwaitExpression();
                }
                return ParseUpdateExpression();

            // falls through
            default:
                return ParseUpdateExpression();
        }
    }


    // Check if the current token can possibly be an ES7 increment expression.
    //
    // ES7 UpdateExpression:
    //      ILeftHandSideExpression[?Yield]
    // LeftHandSideExpression[?Yield][no LineTerminator here]++
    //      ILeftHandSideExpression[?Yield][no LineTerminator here]--
    //      ++ILeftHandSideExpression[?Yield]
    //      --ILeftHandSideExpression[?Yield]
    private bool IsUpdateExpression()
    {
        // This function is called inside parseUnaryExpression to decide
        // whether to call parseSimpleUnaryExpression or call parseUpdateExpression directly
        switch (Token())
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
            case SyntaxKind.ExclamationToken:
            case SyntaxKind.DeleteKeyword:
            case SyntaxKind.TypeOfKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.AwaitKeyword:
                return false;

            case SyntaxKind.LessThanToken:
                // If we are not in JSX context, we are parsing TypeAssertion which is an UnaryExpression
                if (_languageVariant != LanguageVariant.JSX)
                {
                    return false;
                }
                return true;

            // We are in JSX context and the token is part of JSXElement.
            // falls through
            default:
                return true;
        }
    }

    // Parse ES7 UpdateExpression. UpdateExpression is used instead of ES6's PostFixExpression.
    //
    // ES7 UpdateExpression[yield]:
    //      1) LeftHandSideExpression[?yield]
    //      2) LeftHandSideExpression[?yield] [[no LineTerminator here]]++
    //      3) LeftHandSideExpression[?yield] [[no LineTerminator here]]--
    //      4) ++LeftHandSideExpression[?yield]
    //      5) --LeftHandSideExpression[?yield]
    // In TypeScript (2), (3) are parsed as PostfixUnaryExpression. (4), (5) are parsed as PrefixUnaryExpression
    private IUpdateExpression ParseUpdateExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.PlusPlusToken || Token() == SyntaxKind.MinusMinusToken)
        {
            var pos = GetNodePos();

            return FinishNode(new PrefixUnaryExpression(Token(), NextTokenAnd(ParseLeftHandSideExpressionOrHigher)), pos);
        }
        else if (_languageVariant == LanguageVariant.JSX && Token() == SyntaxKind.LessThanToken && LookAhead(NextTokenIsIdentifierOrKeywordOrGreaterThan))
        {
            // JSXElement is part of primaryExpression
            return ParseJsxElementOrSelfClosingElementOrFragment(inExpressionContext: true);
        }

        var expression = ParseLeftHandSideExpressionOrHigher();

        Debug.Assert(IsLeftHandSideExpression(expression));
        if ((Token() == SyntaxKind.PlusPlusToken || Token() == SyntaxKind.MinusMinusToken) && !_scanner.HasPrecedingLineBreak())
        {
            var @operator = Token();
            NextToken();

            return FinishNode(new PostfixUnaryExpression(expression, @operator), expression.Pos ?? 0);
        }

        return expression;
    }

    private ILeftHandSideExpression ParseLeftHandSideExpressionOrHigher()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // Original Ecma:
        // LeftHandSideExpression: See 11.2
        //      NewExpression
        //      CallExpression
        //
        // Our simplification:
        //
        // LeftHandSideExpression: See 11.2
        //      MemberExpression
        //      CallExpression
        //
        // See comment in parseMemberExpressionOrHigher on how we replaced NewExpression with
        // MemberExpression to make our lives easier.
        //
        // to best understand the below code, it's important to see how CallExpression expands
        // out into its own productions:
        //
        // CallExpression:
        //      MemberExpression Arguments
        //      CallExpression Arguments
        //      CallExpression[Expression]
        //      CallExpression.IdentifierName
        //      import (AssignmentExpression)
        //      super Arguments
        //      super.IdentifierName
        //
        // Because of the recursion in these calls, we need to bottom out first. There are three
        // bottom out states we can run into: 1) We see 'super' which must start either of
        // the last two CallExpression productions. 2) We see 'import' which must start import call.
        // 3)we have a MemberExpression which either completes the LeftHandSideExpression,
        // or starts the beginning of the first four CallExpression productions.
        var pos = GetNodePos();
        ILeftHandSideExpression expression;
        if (Token() == SyntaxKind.ImportKeyword)
        {
            if (LookAhead(NextTokenIsOpenParenOrLessThan))
            {
                // We don't want to eagerly consume all import keyword as import call expression so we look ahead to find "("
                // For example:
                //      var foo3 = require("subfolder
                //      import * as foo1 from "module-from-node
                // We want this import to be a statement rather than import call expression
                _sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
                expression = ParseTokenNode<ImportExpression>();
            }
            else if (LookAhead(NextTokenIsDot))
            {
                // This is an 'import.*' metaproperty (i.e. 'import.meta')
                NextToken(); // advance past the 'import'
                NextToken(); // advance past the dot
                expression = FinishNode(new MetaProperty(SyntaxKind.ImportKeyword, ParseIdentifierName()), pos);
                _sourceFlags |= NodeFlags.PossiblyContainsImportMeta;
            }
            else
            {
                expression = ParseMemberExpressionOrHigher();
            }
        }
        else
        {
            expression = Token() == SyntaxKind.SuperKeyword ? ParseSuperExpression() : ParseMemberExpressionOrHigher();
        }

        // Now, we *may* be complete.  However, we might have consumed the start of a
        // CallExpression or OptionalExpression.  As such, we need to consume the rest
        // of it here to be complete.
        return ParseCallExpressionRest(pos, expression);
    }

    private ILeftHandSideExpression ParseMemberExpressionOrHigher()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // Note: to make our lives simpler, we decompose the NewExpression productions and
        // place ObjectCreationExpression and FunctionExpression into PrimaryExpression.
        // like so:
        //
        //   PrimaryExpression : See 11.1
        //      this
        //      Identifier
        //      Literal
        //      ArrayLiteral
        //      ObjectLiteral
        //      (Expression)
        //      FunctionExpression
        //      new MemberExpression Arguments?
        //
        //   MemberExpression : See 11.2
        //      PrimaryExpression
        //      MemberExpression[Expression]
        //      MemberExpression.IdentifierName
        //
        //   CallExpression : See 11.2
        //      MemberExpression
        //      CallExpression Arguments
        //      CallExpression[Expression]
        //      CallExpression.IdentifierName
        //
        // Technically this is ambiguous.  i.e. CallExpression defines:
        //
        //   CallExpression:
        //      CallExpression Arguments
        //
        // If you see: "new Foo()"
        //
        // Then that could be treated as a single ObjectCreationExpression, or it could be
        // treated as the invocation of "new Foo".  We disambiguate that in code (to match
        // the original grammar) by making sure that if we see an ObjectCreationExpression
        // we always consume arguments if they are there. So we treat "new Foo()" as an
        // object creation only, and not at all as an invocation.  Another way to think
        // about this is that for every "new" that we see, we will consume an argument list if
        // it is there as part of the *associated* object creation node.  Any additional
        // argument lists we see, will become invocation expressions.
        //
        // Because there are no other places in the grammar now that refer to FunctionExpression
        // or ObjectCreationExpression, it is safe to push down into the PrimaryExpression
        // production.
        //
        // Because CallExpression and MemberExpression are left recursive, we need to bottom out
        // of the recursion immediately.  So we parse out a primary expression to start with.
        var pos = GetNodePos();
        var expression = ParsePrimaryExpression();

        return ParseMemberExpressionRest(pos, expression, allowOptionalChain: true);
    }

    private IMemberExpression ParseSuperExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        IMemberExpression expression = ParseTokenNode<SuperExpression>();
        if (Token() == SyntaxKind.LessThanToken)
        {
            var startPos = GetNodePos();
            var typeArguments = TryParse(ParseTypeArgumentsInExpression);
            if (typeArguments != null)
            {
                ParseErrorAt(startPos, GetNodePos(), Diagnostics._superMayNotUseTypeArguments);
                if (!IsTemplateStartOfTaggedTemplate())
                {
                    expression = new ExpressionWithTypeArguments(expression, typeArguments);
                }
            }
        }

        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.DotToken || Token() == SyntaxKind.OpenBracketToken)
        {
            return expression;
        }

        // If we have seen "super" it must be followed by '(' or '.'.
        // If it wasn't then just try to parse out a '.' and report an error.
        ParseExpectedToken<DotToken>(
            SyntaxKind.DotToken,
            Diagnostics._superMustBeFollowedByAnArgumentListOrMemberAccess);

        // private names will never work with `super` (`super.#foo`), but that's a semantic error, not syntactic
        return FinishNode(
            new PropertyAccessExpression(
                expression,
                ParseRightSideOfDot(
                    allowIdentifierNames: true,
                    allowPrivateIdentifiers: true,
                    allowUnicodeEscapeSequenceInIdentifierName: true)),
            pos);
    }

    private IJsxElementOrSelfClosingOrFragment ParseJsxElementOrSelfClosingElementOrFragment(
        bool inExpressionContext,
        int? topInvalidNodePosition = null,
        INode? openingTag = null,
        bool mustBeUnary = false)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var opening = ParseJsxOpeningOrSelfClosingElementOrOpeningFragment(inExpressionContext);
        IJsxElementOrSelfClosingOrFragment result;
        if (opening.Kind == SyntaxKind.JsxOpeningElement)
        {
            var children = ParseJsxChildren(opening);
            INode closingElement;

            IJsxChild? lastChild = children.Count > 0 ? children[^1] : null;
            if (
                lastChild?.Kind == SyntaxKind.JsxElement
                && !TagNamesAreEquivalent(
                    (lastChild as JsxElement)?.OpeningElement?.TagName,
                    (lastChild as JsxElement)?.ClosingElement?.TagName)
                && TagNamesAreEquivalent(
                    (opening as IJsxHasTagName)?.TagName,
                    (lastChild as JsxElement)?.ClosingElement?.TagName)
            )
            {
                // when an unclosed JsxOpeningElement incorrectly parses its parent's JsxClosingElement,
                // restructure (<div>(...<span>...</div>)) --> (<div>(...<span>...</>)</div>)
                // (no need to error; the parent will error)
                var end = (lastChild as JsxElement)?.Children?.End;
                IJsxChild newLast = FinishNode(
                    new JsxElement(
                        (lastChild as JsxElement)!.OpeningElement,
                        (lastChild as JsxElement)!.Children,
                        FinishNode(new JsxClosingElement(FinishNode(new Identifier(""), end ?? 0, end)), end ?? 0, end)
                    ),
                    (lastChild as JsxElement)!.OpeningElement?.Pos ?? 0,
                    end);

                children = CreateNodeArray(
                    children
                    .Take(children.Count - 1)
                    .Concat([newLast]),
                    children.Pos ?? 0,
                    end);
                closingElement = (lastChild as JsxElement)!.ClosingElement;
            }
            else
            {
                closingElement = ParseJsxClosingElement((opening as JsxOpeningElement)!, inExpressionContext);
                if (!TagNamesAreEquivalent((opening as IJsxHasTagName)?.TagName, (closingElement as IJsxHasTagName)?.TagName))
                {
                    if (openingTag is JsxOpeningElement openingElement &&
                        TagNamesAreEquivalent((closingElement as IJsxHasTagName)?.TagName, openingElement.TagName))
                    {
                        // opening incorrectly matched with its parent's closing -- put error on opening
                        ParseErrorAtRange(
                            (opening as IJsxHasTagName)!.TagName!,
                            Diagnostics._jSXElement0HasNoCorrespondingClosingTag,
                            GetTextOfNodeFromSourceText(_sourceText!, (opening as IJsxHasTagName)!.TagName!));
                    }
                    else
                    {
                        // other opening/closing mismatches -- put error on closing
                        ParseErrorAtRange(
                            (closingElement as IJsxHasTagName)!.TagName!,
                            Diagnostics._expectedCorrespondingJSXClosingTagFor0,
                            GetTextOfNodeFromSourceText(_sourceText!, (opening as IJsxHasTagName)!.TagName!));
                    }
                }
            }

            result = FinishNode(
                new JsxElement((opening as IJsxHasTagName)!, children, (closingElement as JsxClosingElement)!),
                pos);
        }
        else if (opening.Kind == SyntaxKind.JsxOpeningFragment)
        {
            result = FinishNode(
                new JsxFragment(
                    (opening as JsxOpeningFragment)!,
                    ParseJsxChildren(opening),
                    ParseJsxClosingFragment(inExpressionContext)),
                pos);
        }
        else
        {
            Debug.Assert(opening.Kind == SyntaxKind.JsxSelfClosingElement);
            // Nothing else to do for self-closing elements
            result = (opening as JsxSelfClosingElement)!;
        }

        // If the user writes the invalid code '<div></div><div></div>' in an expression context (i.e. not wrapped in
        // an enclosing tag), we'll naively try to parse   ^ this as a 'less than' operator and the remainder of the tag
        // as garbage, which will cause the formatter to badly mangle the JSX. Perform a speculative parse of a JSX
        // element if we see a < token so that we can wrap it in a synthetic binary expression so the formatter
        // does less damage and we can report a better error.
        // Since JSX elements are invalid < operands anyway, this LookAhead parse will only occur in error scenarios
        // of one sort or another.
        // If we are in a unary context, we can't do this recovery; the binary expression we return here is not
        // a valid UnaryExpression and will cause problems later.
        if (!mustBeUnary && inExpressionContext && Token() == SyntaxKind.LessThanToken)
        {
            var topBadPos = topInvalidNodePosition == null ? result.Pos : topInvalidNodePosition;
            var invalidElement = TryParse(() => ParseJsxElementOrSelfClosingElementOrFragment(inExpressionContext: true, topBadPos));
            if (invalidElement != null)
            {
                var operatorToken = CreateMissingNode<CommaToken>(SyntaxKind.CommaToken, reportAtCurrentPosition: false);
                SetTextRangePosWidth(operatorToken, invalidElement.Pos ?? 0, 0);
                ParseErrorAt(
                    SkipTrivia(_sourceText!, topBadPos) ?? 0,
                    invalidElement.End ?? 0,
                    Diagnostics._jSXExpressionsMustHaveOneParentElement);

                return FinishNode(invalidElement, pos);
            }
        }

        return result;
    }

    private JsxText ParseJsxText()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var node = new JsxText(_scanner.GetTokenValue(), _currentToken == SyntaxKind.JsxTextAllWhiteSpaces);
        _currentToken = _scanner.ScanJsxToken();

        return FinishNode(node, pos);
    }

    private IJsxChild? ParseJsxChild(IJsxOpeningLikeElementOrOpeningFragment openingTag, SyntaxKind token)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        switch (token)
        {
            case SyntaxKind.EndOfFileToken:
                // If we hit EOF, issue the error at the tag that lacks the closing element
                // rather than at the end of the file (which is useless)
                if (openingTag is JsxOpeningFragment)
                {
                    ParseErrorAtRange(openingTag, Diagnostics._jSXFragmentHasNoCorrespondingClosingTag);
                }
                else
                {
                    // We want the error span to cover only 'Foo.Bar' in < Foo.Bar >
                    // or to cover only 'Foo' in < Foo >
                    var tag = (openingTag as JsxOpeningElement)!.TagName;
                    var start = Math.Min(SkipTrivia(_sourceText!, tag.Pos) ?? 0, tag.End ?? tag.Pos ?? 0);
                    ParseErrorAt(
                        start,
                        tag.End ?? 0,
                        Diagnostics._jSXElement0HasNoCorrespondingClosingTag,
                        GetTextOfNodeFromSourceText(_sourceText!, (openingTag as JsxOpeningElement)!.TagName));
                }
                return null;
            case SyntaxKind.LessThanSlashToken:
            case SyntaxKind.ConflictMarkerTrivia:
                return null;
            case SyntaxKind.JsxText:
            case SyntaxKind.JsxTextAllWhiteSpaces:
                return ParseJsxText();
            case SyntaxKind.OpenBraceToken:
                return ParseJsxExpression(inExpressionContext: false);
            case SyntaxKind.LessThanToken:
                return ParseJsxElementOrSelfClosingElementOrFragment(
                    inExpressionContext: false,
                    topInvalidNodePosition: null,
                    openingTag);
            default:
                throw Debug.Fail($"{token} is not a valid JSX child to parse");
        }
    }

    private NodeArray<IJsxChild> ParseJsxChildren(IJsxOpeningLikeElementOrOpeningFragment openingTag)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        List<IJsxChild> list = [];
        var listPos = GetNodePos();
        var saveParsingContext = _parsingContext;
        _parsingContext |= (ParsingContext)(1 << (int)ParsingContext.JsxChildren);

        while (true)
        {
            var child = ParseJsxChild(openingTag, _currentToken = _scanner.ReScanJsxToken());
            if (child == null)
            {
                break;
            }

            list.Add(child);
            if (openingTag is JsxOpeningElement openingElement
                && child is JsxElement jsxChild
                && !TagNamesAreEquivalent(jsxChild.OpeningElement?.TagName, jsxChild.ClosingElement?.TagName)
                && TagNamesAreEquivalent(openingElement.TagName, jsxChild.ClosingElement?.TagName))
            {
                // stop after parsing a mismatched child like <div>...(<span></div>) in order to reattach the </div> higher
                break;
            }
        }

        _parsingContext = saveParsingContext;

        return CreateNodeArray(list, listPos);
    }

    private JsxAttributes ParseJsxAttributes()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        return FinishNode(new JsxAttributes(ParseList(ParsingContext.JsxAttributes, ParseJsxAttribute)), pos);
    }

    private IJsxOpeningLikeElementOrOpeningFragment ParseJsxOpeningOrSelfClosingElementOrOpeningFragment(bool inExpressionContext)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        ParseExpected(SyntaxKind.LessThanToken);

        if (Token() == SyntaxKind.GreaterThanToken)
        {
            // See below for explanation of scanJsxText
            ScanJsxText();

            return FinishNode(new JsxOpeningFragment(), pos);
        }

        var tagName = ParseJsxElementName();
        var typeArguments = (_contextFlags & NodeFlags.JavaScriptFile) == 0 ? TryParseTypeArguments() : null;
        var attributes = ParseJsxAttributes();

        IJsxOpeningLikeElement node;

        if (Token() == SyntaxKind.GreaterThanToken)
        {
            // Closing tag, so scan the immediately-following text with the JSX scanning instead
            // of regular scanning to avoid treating illegal characters (e.g. '#') as immediate
            // scanning errors
            ScanJsxText();
            node = new JsxOpeningElement(tagName, typeArguments!, attributes);
        }
        else
        {
            ParseExpected(SyntaxKind.SlashToken);
            if (ParseExpected(SyntaxKind.GreaterThanToken, diagnosticMessage: null, shouldAdvance: false))
            {
                // manually advance the scanner in order to look for jsx text inside jsx
                if (inExpressionContext)
                {
                    NextToken();
                }
                else
                {
                    ScanJsxText();
                }
            }

            node = new JsxSelfClosingElement(tagName, typeArguments!, attributes);
        }

        return FinishNode(node, pos);
    }

    private IJsxTagNameExpression ParseJsxElementName()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        // JsxElement can have name in the form of
        //      propertyAccessExpression
        //      primaryExpression in the form of an identifier and "this" keyword
        // We can't just simply use ParseLeftHandSideExpressionOrHigher because then we will start consider class,function etc as a keyword
        // We only want to consider "this" as a primaryExpression
        var initialExpression = ParseJsxTagName();
        if (initialExpression is JsxNamespacedName)
        {
            return initialExpression; // `a:b.c` is invalid syntax, don't even look for the `.` if we parse `a:b`, and let `parseAttribute` report "unexpected :" instead.
        }

        var expression = initialExpression;
        while (ParseOptional(SyntaxKind.DotToken))
        {
            expression = FinishNode(
                new PropertyAccessExpression(
                    expression,
                    ParseRightSideOfDot(
                        allowIdentifierNames: true,
                        allowPrivateIdentifiers: false,
                        allowUnicodeEscapeSequenceInIdentifierName: false)),
                pos);
        }

        return expression;
    }

    private IJsxTagNameExpression ParseJsxTagName()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ScanJsxIdentifier();

        var isThis = Token() == SyntaxKind.ThisKeyword;
        var tagName = ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        if (ParseOptional(SyntaxKind.ColonToken))
        {
            ScanJsxIdentifier();

            return FinishNode(new JsxNamespacedName(tagName, ParseIdentifierNameErrorOnUnicodeEscapeSequence()), pos);
        }

        return isThis ? FinishNode(new ThisExpression(), pos) : tagName;
    }

    private JsxExpression ParseJsxExpression(bool inExpressionContext)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        if (!ParseExpected(SyntaxKind.OpenBraceToken))
        {
            return null!;
        }

        DotDotDotToken? dotDotDotToken = null;
        IExpression? expression = null;
        if (Token() != SyntaxKind.CloseBraceToken)
        {
            if (!inExpressionContext)
            {
                dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);
            }

            // Only an AssignmentExpression is valid here per the JSX spec,
            // but we can unambiguously parse a comma sequence and provide
            // a better error message in grammar checking.
            expression = ParseExpression();
        }

        if (inExpressionContext)
        {
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            if (ParseExpected(SyntaxKind.CloseBraceToken, diagnosticMessage: null, shouldAdvance: false))
            {
                ScanJsxText();
            }
        }

        return FinishNode(new JsxExpression(dotDotDotToken!, expression!), pos);
    }

    private IObjectLiteralElement ParseJsxAttribute()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.OpenBraceToken)
        {
            return ParseJsxSpreadAttribute();
        }

        var pos = GetNodePos();

        return FinishNode(new JsxAttribute(ParseJsxAttributeName(), ParseJsxAttributeValue()), pos);
    }

    private IJsxAttributeValue ParseJsxAttributeValue()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.EqualsToken)
        {
            if (ScanJsxAttributeValue() == SyntaxKind.StringLiteral)
            {
                return ParseLiteralNode<StringLiteral>();
            }

            if (Token() == SyntaxKind.OpenBraceToken)
            {
                return ParseJsxExpression(inExpressionContext: true);
            }

            if (Token() == SyntaxKind.LessThanToken)
            {
                return ParseJsxElementOrSelfClosingElementOrFragment(inExpressionContext: true);
            }

            ParseErrorAtCurrentToken(Diagnostics._orJSXElementExpected);
        }

        return null!;
    }

    private IJsxAttributeName ParseJsxAttributeName()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ScanJsxIdentifier();

        var attrName = ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        if (ParseOptional(SyntaxKind.ColonToken))
        {
            ScanJsxIdentifier();

            return FinishNode(new JsxNamespacedName(attrName, ParseIdentifierNameErrorOnUnicodeEscapeSequence()), pos);
        }

        return attrName;
    }

    private JsxSpreadAttribute ParseJsxSpreadAttribute()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        ParseExpected(SyntaxKind.DotDotDotToken);
        var expression = ParseExpression();
        ParseExpected(SyntaxKind.CloseBraceToken);

        return FinishNode(new JsxSpreadAttribute(expression), pos);
    }

    private JsxClosingElement ParseJsxClosingElement(JsxOpeningElement open, bool inExpressionContext)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.LessThanSlashToken);
        var tagName = ParseJsxElementName();
        if (ParseExpected(SyntaxKind.GreaterThanToken, diagnosticMessage: null, shouldAdvance: false))
        {
            // manually advance the scanner in order to look for jsx text inside jsx
            if (inExpressionContext || !TagNamesAreEquivalent((open as IJsxHasTagName).TagName, tagName))
            {
                NextToken();
            }
            else
            {
                ScanJsxText();
            }
        }

        return FinishNode(new JsxClosingElement(tagName), pos);
    }

    private JsxClosingFragment ParseJsxClosingFragment(bool inExpressionContext)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.LessThanSlashToken);
        if (ParseExpected(
            SyntaxKind.GreaterThanToken,
            Diagnostics._expectedCorrespondingClosingTagForJSXFragment,
            shouldAdvance: false))
        {
            // manually advance the scanner in order to look for jsx text inside jsx
            if (inExpressionContext)
            {
                NextToken();
            }
            else
            {
                ScanJsxText();
            }
        }

        return FinishNode(new JsxClosingFragment(), pos);
    }

    private TypeAssertion ParseTypeAssertion()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        Debug.Assert(
            _languageVariant != LanguageVariant.JSX,
            "Type assertions should never be parsed in JSX; they should be parsed as comparisons or JSX elements/fragments.");
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.LessThanToken);
        var type = ParseType();
        ParseExpected(SyntaxKind.GreaterThanToken);
        var expression = ParseSimpleUnaryExpression();

        return FinishNode(new TypeAssertion(type, expression), pos);
    }

    private bool NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate()
    {
        NextToken();

        return TokenIsIdentifierOrKeyword(Token())
            || Token() == SyntaxKind.OpenBracketToken
            || IsTemplateStartOfTaggedTemplate();
    }

    private bool IsStartOfOptionalPropertyOrElementAccessChain()
    {
        return Token() == SyntaxKind.QuestionDotToken
            && LookAhead(NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate);
    }

    private static bool TryReParseOptionalChain(IExpression node)
    {
        if ((node.Flags & NodeFlags.OptionalChain) != 0)
        {
            return true;
        }

        // check for an optional chain in a non-null expression
        if (node is NonNullExpression)
        {
            var expr = (node as NonNullExpression)!.Expression;
            while (expr is NonNullExpression nonNullExpr && (expr.Flags & NodeFlags.OptionalChain) == 0)
            {
                expr = nonNullExpr.Expression;
            }

            if ((expr.Flags & NodeFlags.OptionalChain) != 0)
            {
                // this is part of an optional chain. Walk down from `node` to `expression` and set the flag.
                while (node is NonNullExpression nonNullExpr)
                {
                    ((Node)(INode)node).Flags |= NodeFlags.OptionalChain;
                    node = nonNullExpr.Expression;
                }

                return true;
            }
        }

        return false;
    }

    private PropertyAccessExpression ParsePropertyAccessExpressionRest(
        int pos,
        ILeftHandSideExpression expression,
        QuestionDotToken questionDotToken)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var name = ParseRightSideOfDot(
            allowIdentifierNames: true,
            allowPrivateIdentifiers: true,
            allowUnicodeEscapeSequenceInIdentifierName: true);
        var isOptionalChain = questionDotToken != null || TryReParseOptionalChain(expression);
        var propertyAccess = isOptionalChain ?
            new PropertyAccessChain(expression, questionDotToken!, name) :
            new PropertyAccessExpression(expression, name);
        if (isOptionalChain && propertyAccess.Name is PrivateIdentifier)
        {
            ParseErrorAtRange(propertyAccess.Name, Diagnostics._anOptionalChainCannotContainPrivateIdentifiers);
        }

        if (expression is ExpressionWithTypeArguments eta && eta.TypeArguments != null)
        {
            pos = eta.TypeArguments.Pos ?? 0 - 1;
            var end = SkipTrivia(_sourceText!, eta.TypeArguments.End) ?? 0 + 1;
            ParseErrorAt(pos, end, Diagnostics._anInstantiationExpressionCannotBeFollowedByAPropertyAccess);
        }

        return FinishNode(propertyAccess, pos);
    }

    private ElementAccessExpression ParseElementAccessExpressionRest(
        int pos,
        ILeftHandSideExpression expression,
        QuestionDotToken questionDotToken)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        IExpression argumentExpression;
        if (Token() == SyntaxKind.CloseBracketToken)
        {
            argumentExpression = CreateMissingNode<Identifier>(
                SyntaxKind.Identifier,
                reportAtCurrentPosition: true,
                Diagnostics._anElementAccessExpressionShouldTakeAnArgument);
        }
        else
        {
            var argument = AllowInAnd(ParseExpression);
            if (argument is IStringLiteralLike or NumericLiteral &&
                argument is IHasLiteralText hasLiteralText)
            {
                ((LiteralLikeNode)argument).Text = InternIdentifier(hasLiteralText.Text!);
            }

            argumentExpression = argument;
        }

        ParseExpected(SyntaxKind.CloseBracketToken);

        var indexedAccess = questionDotToken != null || TryReParseOptionalChain(expression) ?
            new ElementAccessChain(expression, questionDotToken!, argumentExpression) :
            new ElementAccessExpression(expression, argumentExpression);

        return FinishNode(indexedAccess, pos);
    }

    private ILeftHandSideExpression ParseMemberExpressionRest(
        int pos,
        ILeftHandSideExpression expression,
        bool allowOptionalChain)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        while (true)
        {
            QuestionDotToken? questionDotToken = null;
            var isPropertyAccess = false;
            if (allowOptionalChain && IsStartOfOptionalPropertyOrElementAccessChain())
            {
                questionDotToken = ParseExpectedToken<QuestionDotToken>(SyntaxKind.QuestionDotToken);
                isPropertyAccess = TokenIsIdentifierOrKeyword(Token());
            }
            else
            {
                isPropertyAccess = ParseOptional(SyntaxKind.DotToken);
            }

            if (isPropertyAccess)
            {
                expression = ParsePropertyAccessExpressionRest(pos, expression, questionDotToken!);
                continue;
            }

            // when in the [Decorator] context, we do not parse ElementAccess as it could be part of a ComputedPropertyName
            if ((questionDotToken != null || !InDecoratorContext()) && ParseOptional(SyntaxKind.OpenBracketToken))
            {
                expression = ParseElementAccessExpressionRest(pos, expression, questionDotToken!);
                continue;
            }

            if (IsTemplateStartOfTaggedTemplate())
            {
                // Absorb type arguments into TemplateExpression when preceding expression is ExpressionWithTypeArguments
                expression = (questionDotToken == null) && expression is ExpressionWithTypeArguments ewta ?
                    ParseTaggedTemplateRest(pos, ewta.Expression, questionDotToken!, ewta.TypeArguments) :
                    ParseTaggedTemplateRest(pos, expression, questionDotToken!, typeArguments: null!);
                continue;
            }

            if (questionDotToken == null)
            {
                if (Token() == SyntaxKind.ExclamationToken && !_scanner.HasPrecedingLineBreak())
                {
                    NextToken();
                    expression = FinishNode(new NonNullExpression(expression), pos);
                    continue;
                }

                var typeArguments = TryParse(ParseTypeArgumentsInExpression);
                if (typeArguments != null)
                {
                    expression = FinishNode(new ExpressionWithTypeArguments(expression, typeArguments), pos);
                    continue;
                }
            }

            return expression;
        }
    }

    private bool IsTemplateStartOfTaggedTemplate()
    {
        return Token() == SyntaxKind.NoSubstitutionTemplateLiteral || Token() == SyntaxKind.TemplateHead;
    }

    private TaggedTemplateExpression ParseTaggedTemplateRest(
        int pos,
        ILeftHandSideExpression tag,
        QuestionDotToken questionDotToken,
        NodeArray<ITypeNode>? typeArguments)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        INode template;
        if (Token() == SyntaxKind.NoSubstitutionTemplateLiteral)
        {
            ReScanTemplateToken(isTaggedTemplate: true);
            template = ParseLiteralNode<NoSubstitutionTemplateLiteral>();
        }
        else
        {
            template = ParseTemplateExpression(isTaggedTemplate: true);
        }

        var tagExpression = new TaggedTemplateExpression(tag, typeArguments, template);

        if (questionDotToken != null || (tag.Flags & NodeFlags.OptionalChain) != 0)
        {
            tagExpression.Flags |= NodeFlags.OptionalChain;
        }

        tagExpression.QuestionDotToken = questionDotToken;

        return FinishNode(tagExpression, pos);
    }

    private ILeftHandSideExpression ParseCallExpressionRest(int pos, ILeftHandSideExpression expression)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        while (true)
        {
            expression = ParseMemberExpressionRest(pos, expression, allowOptionalChain: true);
            NodeArray<ITypeNode>? typeArguments = null;

            var questionDotToken = ParseOptionalToken<QuestionDotToken>(SyntaxKind.QuestionDotToken);
            if (questionDotToken != null)
            {
                typeArguments = TryParse(ParseTypeArgumentsInExpression);
                if (IsTemplateStartOfTaggedTemplate())
                {
                    expression = ParseTaggedTemplateRest(pos, expression, questionDotToken, typeArguments);
                    continue;
                }
            }

            if (typeArguments != null || Token() == SyntaxKind.OpenParenToken)
            {
                // Absorb type arguments into CallExpression when preceding expression is ExpressionWithTypeArguments
                if (questionDotToken == null && expression is ExpressionWithTypeArguments ewta)
                {
                    typeArguments = ewta.TypeArguments;
                    expression = ewta.Expression;
                }

                var argumentList = ParseArgumentList();
                var callExpr = questionDotToken != null || TryReParseOptionalChain(expression) ?
                    new CallChain(expression, questionDotToken!, typeArguments!, argumentList) :
                    new CallExpression(expression, typeArguments, argumentList);
                expression = FinishNode(callExpr, pos);
                continue;
            }

            if (questionDotToken != null)
            {
                // We parsed `?.` but then failed to parse anything, so report a missing identifier here.
                var name = CreateMissingNode<Identifier>(
                    SyntaxKind.Identifier,
                    reportAtCurrentPosition: false,
                    Diagnostics._identifierExpected);
                expression = FinishNode(new PropertyAccessChain(expression, questionDotToken, name), pos);
            }

            break;
        }

        return expression;
    }

    private NodeArray<IExpression> ParseArgumentList()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        ParseExpected(SyntaxKind.OpenParenToken);
        var result = ParseDelimitedList(ParsingContext.ArgumentExpressions, ParseArgumentExpression);
        ParseExpected(SyntaxKind.CloseParenToken);

        return result!;
    }

    private NodeArray<ITypeNode> ParseTypeArgumentsInExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if ((_contextFlags & NodeFlags.JavaScriptFile) != 0)
        {
            // TypeArguments must not be parsed in JavaScript files to avoid ambiguity with binary operators.
            return null!;
        }

        if (ReScanLessThanToken() != SyntaxKind.LessThanToken)
        {
            return null!;
        }

        NextToken();

        var typeArguments = ParseDelimitedList(ParsingContext.TypeArguments, ParseType);
        if (ReScanGreaterToken() != SyntaxKind.GreaterThanToken)
        {
            // If it doesn't have the closing `>` then it's definitely not an type argument list.
            return null!;
        }

        NextToken();

        // We successfully parsed a type argument list. The next token determines whether we want to
        // treat it as such. If the type argument list is followed by `(` or a template literal, as in
        // `f<number>(42)`, we favor the type argument interpretation even though JavaScript would view
        // it as a relational expression.
        return typeArguments != null && CanFollowTypeArgumentsInExpression() ? typeArguments : null!;
    }

    private bool CanFollowTypeArgumentsInExpression()
    {
        return Token() switch
        {
            // These tokens can follow a type argument list in a call expression.
            // foo<x>(
            SyntaxKind.OpenParenToken or SyntaxKind.NoSubstitutionTemplateLiteral or SyntaxKind.TemplateHead => true,
            // A type argument list followed by `<` never makes sense, and a type argument list followed
            // by `>` is ambiguous with a (re-scanned) `>>` operator, so we disqualify both. Also, in
            // this context, `+` and `-` are unary operators, not binary operators.
            SyntaxKind.LessThanToken or SyntaxKind.GreaterThanToken or SyntaxKind.PlusToken or SyntaxKind.MinusToken => false,
            // We favor the type argument list interpretation when it is immediately followed by
            // a line break, a binary operator, or something that can't start an expression.
            _ => _scanner.HasPrecedingLineBreak() || IsBinaryOperator() || !IsStartOfExpression(),
        };
    }

    private IPrimaryExpression ParsePrimaryExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        switch (Token())
        {
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                if ((_scanner.GetTokenFlags() & TokenFlags.IsInvalid) != 0)
                {
                    ReScanTemplateToken(isTaggedTemplate: false);
                }
                goto literalsLabel;
            // falls through
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
            literalsLabel:
                return ParseLiteralNode<ILiteralExpression>();
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
                return ParseTokenNode<PrimaryExpressionToken>();
            case SyntaxKind.OpenParenToken:
                return ParseParenthesizedExpression();
            case SyntaxKind.OpenBracketToken:
                return ParseArrayLiteralExpression();
            case SyntaxKind.OpenBraceToken:
                return ParseObjectLiteralExpression();
            case SyntaxKind.AsyncKeyword:
                // Async arrow functions are parsed earlier in ParseAssignmentExpressionOrHigher.
                // If we encounter `async [no LineTerminator here] function` then this is an async
                // function; otherwise, its an identifier.
                if (!LookAhead(NextTokenIsFunctionKeywordOnSameLine))
                {
                    break;
                }

                return ParseFunctionExpression();
            case SyntaxKind.AtToken:
                return ParseDecoratedExpression();
            case SyntaxKind.ClassKeyword:
                return ParseClassExpression();
            case SyntaxKind.FunctionKeyword:
                return ParseFunctionExpression();
            case SyntaxKind.NewKeyword:
                return ParseNewExpressionOrNewDotTarget();
            case SyntaxKind.SlashToken:
            case SyntaxKind.SlashEqualsToken:
                if (ReScanSlashToken() == SyntaxKind.RegularExpressionLiteral)
                {
                    return ParseLiteralNode<ILiteralExpression>();
                }
                break;
            case SyntaxKind.TemplateHead:
                return ParseTemplateExpression(isTaggedTemplate: false);
            case SyntaxKind.PrivateIdentifier:
                return ParsePrivateIdentifier();
        }

        return ParseIdentifier(Diagnostics._expressionExpected);
    }

    private ParenthesizedExpression ParseParenthesizedExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.CloseParenToken);

        return WithJSDoc(FinishNode(new ParenthesizedExpression(expression), pos), hasJSDoc);
    }

    private SpreadElement ParseSpreadElement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.DotDotDotToken);
        var expression = ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true);

        return FinishNode(new SpreadElement(expression), pos);
    }

    private IExpression ParseArgumentOrArrayLiteralElement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return Token() == SyntaxKind.DotDotDotToken ? ParseSpreadElement() :
            Token() == SyntaxKind.CommaToken ? FinishNode(new OmittedExpression(), GetNodePos()) :
            ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true);
    }

    private IExpression ParseArgumentExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return DoOutsideOfContext(_disAllowInAndDecoratorContext, ParseArgumentOrArrayLiteralElement);
    }

    private ArrayLiteralExpression ParseArrayLiteralExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var openBracketPosition = _scanner.GetTokenStart();
        var openBracketParsed = ParseExpected(SyntaxKind.OpenBracketToken);
        var multiLine = _scanner.HasPrecedingLineBreak();
        var elements = ParseDelimitedList(ParsingContext.ArrayLiteralMembers, ParseArgumentOrArrayLiteralElement);
        ParseExpectedMatchingBrackets(
            SyntaxKind.OpenBracketToken,
            SyntaxKind.CloseBracketToken,
            openBracketParsed,
            openBracketPosition);

        return FinishNode(new ArrayLiteralExpression(elements!, multiLine), pos);
    }

    private IObjectLiteralElementLike ParseObjectLiteralElement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        if (ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken) != null)
        {
            var expression = ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true);

            return WithJSDoc(FinishNode(new SpreadAssignment(expression), pos), hasJSDoc);
        }

        var modifiers = ParseModifiers(allowDecorators: true);
        if (ParseContextualModifier(SyntaxKind.GetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.GetAccessor, SignatureFlags.None);
        }

        if (ParseContextualModifier(SyntaxKind.SetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.SetAccessor, SignatureFlags.None);
        }

        var asteriskToken = ParseOptionalToken<AsteriskToken>(SyntaxKind.AsteriskToken);
        var TokenIsIdentifier = IsIdentifier();
        var name = ParsePropertyName();

        // Disallowing of optional property assignments and definite assignment assertion happens in the grammar checker.
        var questionToken = ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken);
        var exclamationToken = ParseOptionalToken<ExclamationToken>(SyntaxKind.ExclamationToken);

        if (asteriskToken != null || Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseMethodDeclaration(pos, hasJSDoc, modifiers, asteriskToken!, name, questionToken!, exclamationToken!);
        }

        // check if it is short-hand property assignment or normal property assignment
        // NOTE: if token is EqualsToken it is interpreted as CoverInitializedName production
        // CoverInitializedName[Yield] :
        //     IdentifierReference[?Yield] Initializer[In, ?Yield]
        // this is necessary because ObjectLiteral productions are also used to cover grammar for ObjectAssignmentPattern
        IObjectLiteralElementLike node;
        var isShorthandPropertyAssignment = TokenIsIdentifier && (Token() != SyntaxKind.ColonToken);
        if (isShorthandPropertyAssignment)
        {
            var equalsToken = ParseOptionalToken<EqualsToken>(SyntaxKind.EqualsToken);
            var objectAssignmentInitializer = equalsToken != null ? AllowInAnd(() => ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true)) : null;
            node = new ShorthandPropertyAssignment((name as Identifier)!, objectAssignmentInitializer)
            {
                // Save equals token for error reporting.
                // TODO(rbuckton): Consider manufacturing this when we need to report an error as it is otherwise not useful.
                EqualsToken = equalsToken,
                QuestionToken = questionToken,
                ExclamationToken = exclamationToken
            };
        }
        else
        {
            ParseExpected(SyntaxKind.ColonToken);
            var initializer = AllowInAnd(() => ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true));
            node = new PropertyAssignment(name, initializer)
            {
                // Decorators, Modifiers, questionToken, and exclamationToken are not supported
                // by property assignments and are reported in the grammar checker
                Modifiers = modifiers,
                QuestionToken = questionToken,
                ExclamationToken = exclamationToken
            };
        }

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ObjectLiteralExpression ParseObjectLiteralExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var openBracePosition = _scanner.GetTokenStart();
        var openBraceParsed = ParseExpected(SyntaxKind.OpenBraceToken);
        var multiLine = _scanner.HasPrecedingLineBreak();
        var properties = ParseDelimitedList(
            ParsingContext.ObjectLiteralMembers,
            ParseObjectLiteralElement,
            considerSemicolonAsDelimiter: true);
        ParseExpectedMatchingBrackets(
            SyntaxKind.OpenBraceToken,
            SyntaxKind.CloseBraceToken,
            openBraceParsed,
            openBracePosition);

        return FinishNode(new ObjectLiteralExpression(properties!, multiLine), pos);
    }

    private FunctionExpression ParseFunctionExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // GeneratorExpression:
        //      function* BindingIdentifier [Yield][opt](FormalParameters[Yield]){ GeneratorBody }
        //
        // FunctionExpression:
        //      function BindingIdentifier[opt](FormalParameters){ FunctionBody }
        var savedDecoratorContext = InDecoratorContext();
        SetDecoratorContext(val: false);

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(allowDecorators: false);
        ParseExpected(SyntaxKind.FunctionKeyword);
        var asteriskToken = ParseOptionalToken<AsteriskToken>(SyntaxKind.AsteriskToken);
        var isGenerator = asteriskToken != null ? SignatureFlags.Yield : SignatureFlags.None;
        var isAsync = modifiers?.Any(x => x.Kind == SyntaxKind.AsyncKeyword) == true ? SignatureFlags.Await : SignatureFlags.None;
        var name = isGenerator != 0 && isAsync != 0 ? DoInYieldAndAwaitContext(ParseOptionalBindingIdentifier) :
            isGenerator != 0 ? DoInYieldContext(ParseOptionalBindingIdentifier) :
            isAsync != 0 ? DoInAwaitContext(ParseOptionalBindingIdentifier) :
            ParseOptionalBindingIdentifier();

        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, isType: false);
        var body = ParseFunctionBlock(isGenerator | isAsync);

        SetDecoratorContext(savedDecoratorContext);

        var node = new FunctionExpression(modifiers!, asteriskToken!, name!, typeParameters!, parameters, type!, body);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Identifier ParseOptionalBindingIdentifier()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return IsBindingIdentifier() ? ParseBindingIdentifier() : null!;
    }

    private IPrimaryExpression ParseNewExpressionOrNewDotTarget()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.NewKeyword);
        if (ParseOptional(SyntaxKind.DotToken))
        {
            var name = ParseIdentifierName();

            return FinishNode(new MetaProperty(SyntaxKind.NewKeyword, name), pos);
        }

        var expressionPos = GetNodePos();
        ILeftHandSideExpression expression = ParseMemberExpressionRest(
            expressionPos,
            ParsePrimaryExpression(),
            allowOptionalChain: false);

        NodeArray<ITypeNode>? typeArguments = null;

        // Absorb type arguments into NewExpression when preceding expression is ExpressionWithTypeArguments
        if (expression is ExpressionWithTypeArguments ewta)
        {
            typeArguments = ewta.TypeArguments;
            expression = ewta.Expression;
        }

        if (Token() == SyntaxKind.QuestionDotToken)
        {
            ParseErrorAtCurrentToken(
                Diagnostics._invalidOptionalChainFromNewExpressionDidYouMeanToCall0,
                GetTextOfNodeFromSourceText(_sourceText!, expression));
        }

        var argumentList = Token() == SyntaxKind.OpenParenToken ? ParseArgumentList() : null;

        return FinishNode(new NewExpression(expression, typeArguments!, argumentList!), pos);
    }

    // STATEMENTS
    private Block ParseBlock(bool ignoreMissingOpenBrace, DiagnosticMessage? diagnosticMessage = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var openBracePosition = _scanner.GetTokenStart();
        var openBraceParsed = ParseExpected(SyntaxKind.OpenBraceToken, diagnosticMessage);
        if (openBraceParsed || ignoreMissingOpenBrace)
        {
            var multiLine = _scanner.HasPrecedingLineBreak();
            var statements = ParseList(ParsingContext.BlockStatements, ParseStatement);
            ParseExpectedMatchingBrackets(
                SyntaxKind.OpenBraceToken,
                SyntaxKind.CloseBraceToken,
                openBraceParsed,
                openBracePosition);
            var result = WithJSDoc(FinishNode(new Block(statements, multiLine), pos), hasJSDoc);
            if (Token() == SyntaxKind.EqualsToken)
            {
                ParseErrorAtCurrentToken(Diagnostics._declarationOrStatementExpectedThisFollowsABlockOfStatementsSoIfYouIntendedToWriteADestructuringAssignmentYouMightNeedToWrapTheWholeAssignmentInParentheses);
                NextToken();
            }

            return result;
        }
        else
        {
            var statements = CreateMissingList<IStatement>();

            return WithJSDoc(FinishNode(new Block(statements, multiLine: null), pos), hasJSDoc);
        }
    }

    private Block ParseFunctionBlock(SignatureFlags flags, DiagnosticMessage? diagnosticMessage = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var savedYieldContext = InYieldContext();
        SetYieldContext((flags & SignatureFlags.Yield) != 0);

        var savedAwaitContext = InAwaitContext();
        SetAwaitContext((flags & SignatureFlags.Await) != 0);

        var savedTopLevel = _topLevel;
        _topLevel = false;

        // We may be in a [Decorator] context when parsing a function expression or
        // arrow function. The body of the function is not in [Decorator] context.
        var saveDecoratorContext = InDecoratorContext();
        if (saveDecoratorContext)
        {
            SetDecoratorContext(val: false);
        }

        var block = ParseBlock((flags & SignatureFlags.IgnoreMissingOpenBrace) != 0, diagnosticMessage);

        if (saveDecoratorContext)
        {
            SetDecoratorContext(val: true);
        }

        _topLevel = savedTopLevel;
        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return block;
    }

    private EmptyStatement ParseEmptyStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.SemicolonToken);

        return WithJSDoc(FinishNode(new EmptyStatement(), pos), hasJSDoc);
    }

    private IfStatement ParseIfStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.IfKeyword);
        var openParenPosition = _scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(
            SyntaxKind.OpenParenToken,
            SyntaxKind.CloseParenToken,
            openParenParsed,
            openParenPosition);
        var thenStatement = ParseStatement();
        var elseStatement = ParseOptional(SyntaxKind.ElseKeyword) ? ParseStatement() : null;

        return WithJSDoc(FinishNode(new IfStatement(expression, thenStatement, elseStatement), pos), hasJSDoc);
    }

    private DoStatement ParseDoStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.DoKeyword);
        var statement = ParseStatement();
        ParseExpected(SyntaxKind.WhileKeyword);
        var openParenPosition = _scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(
            SyntaxKind.OpenParenToken,
            SyntaxKind.CloseParenToken,
            openParenParsed,
            openParenPosition);

        // From: https://mail.mozilla.org/pipermail/es-discuss/2011-August/016188.html
        // 157 min --- All allen at wirfs-brock.com CONF --- "do{;}while(false)false" prohibited in
        // spec but allowed in consensus reality. Approved -- this is the de-facto standard whereby
        //  do;while(0)x will have a semicolon inserted before x.
        ParseOptional(SyntaxKind.SemicolonToken);

        return WithJSDoc(FinishNode(new DoStatement(statement, expression), pos), hasJSDoc);
    }





    private WhileStatement ParseWhileStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.WhileKeyword);
        var openParenPosition = _scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(
            SyntaxKind.OpenParenToken,
            SyntaxKind.CloseParenToken,
            openParenParsed,
            openParenPosition);
        var statement = ParseStatement();

        return WithJSDoc(FinishNode(new WhileStatement(expression, statement), pos), hasJSDoc);
    }





    private IterationStatement ParseForOrForInOrForOfStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ForKeyword);
        var awaitToken = ParseOptionalToken<AwaitKeyword>(SyntaxKind.AwaitKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);

        IForInitializer? initializer = null;
        if (Token() != SyntaxKind.SemicolonToken)
        {
            if (Token() == SyntaxKind.VarKeyword || Token() == SyntaxKind.LetKeyword || Token() == SyntaxKind.ConstKeyword ||
                Token() == SyntaxKind.UsingKeyword && LookAhead(NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOfTrue) ||
                // this one is meant to allow of
                Token() == SyntaxKind.AwaitKeyword && LookAhead(NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLineDisallowOfFalse))
            {
                initializer = ParseVariableDeclarationList(inForStatementInitializer: true);
            }
            else
            {
                initializer = DisAllowInAnd(ParseExpression);
            }
        }

        IterationStatement node;
        if (awaitToken != null ? ParseExpected(SyntaxKind.OfKeyword) : ParseOptional(SyntaxKind.OfKeyword))
        {
            var expression = AllowInAnd(() => ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true));
            ParseExpected(SyntaxKind.CloseParenToken);
            node = new ForOfStatement(awaitToken!, initializer!, expression, ParseStatement());
        }
        else if (ParseOptional(SyntaxKind.InKeyword))
        {
            var expression = AllowInAnd(ParseExpression);
            ParseExpected(SyntaxKind.CloseParenToken);
            node = new ForInStatement(initializer!, expression, ParseStatement());
        }
        else
        {
            ParseExpected(SyntaxKind.SemicolonToken);
            var condition = Token() != SyntaxKind.SemicolonToken && Token() != SyntaxKind.CloseParenToken
                ? AllowInAnd(ParseExpression)
                : null;
            ParseExpected(SyntaxKind.SemicolonToken);
            var incrementor = Token() != SyntaxKind.CloseParenToken
                ? AllowInAnd(ParseExpression)
                : null;
            ParseExpected(SyntaxKind.CloseParenToken);
            node = new ForStatement(initializer!, condition!, incrementor!, ParseStatement());
        }

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private IBreakOrContinueStatement ParseBreakOrContinueStatement(SyntaxKind kind)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        ParseExpected(kind == SyntaxKind.BreakStatement ? SyntaxKind.BreakKeyword : SyntaxKind.ContinueKeyword);
        var label = CanParseSemicolon() ? null : ParseIdentifier();

        ParseSemicolon();
        IBreakOrContinueStatement node = kind == SyntaxKind.BreakStatement
            ? new BreakStatement(label)
            : new ContinueStatement(label);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ReturnStatement ParseReturnStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ReturnKeyword);
        var expression = CanParseSemicolon() ? null : AllowInAnd(ParseExpression);
        ParseSemicolon();

        return WithJSDoc(FinishNode(new ReturnStatement(expression!), pos), hasJSDoc);
    }

    private WithStatement ParseWithStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.WithKeyword);
        var openParenPosition = _scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(
            SyntaxKind.OpenParenToken,
            SyntaxKind.CloseParenToken,
            openParenParsed,
            openParenPosition);
        var statement = DoInsideOfContext(NodeFlags.InWithStatement, ParseStatement);

        return WithJSDoc(FinishNode(new WithStatement(expression, statement), pos), hasJSDoc);
    }

    private CaseClause ParseCaseClause()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.CaseKeyword);
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.ColonToken);
        var statements = ParseList(ParsingContext.SwitchClauseStatements, ParseStatement);

        return WithJSDoc(FinishNode(new CaseClause(expression, statements), pos), hasJSDoc);
    }

    private DefaultClause ParseDefaultClause()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.DefaultKeyword);
        ParseExpected(SyntaxKind.ColonToken);
        var statements = ParseList(ParsingContext.SwitchClauseStatements, ParseStatement);

        return FinishNode(new DefaultClause(statements), pos);
    }

    private ICaseOrDefaultClause ParseCaseOrDefaultClause()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return Token() == SyntaxKind.CaseKeyword ? ParseCaseClause() : ParseDefaultClause();
    }

    private CaseBlock ParseCaseBlock()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        var clauses = ParseList(ParsingContext.SwitchClauses, ParseCaseOrDefaultClause);
        ParseExpected(SyntaxKind.CloseBraceToken);

        return FinishNode(new CaseBlock(clauses), pos);
    }

    private SwitchStatement ParseSwitchStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.SwitchKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.CloseParenToken);
        var caseBlock = ParseCaseBlock();

        return WithJSDoc(FinishNode(new SwitchStatement(expression, caseBlock), pos), hasJSDoc);
    }

    private ThrowStatement ParseThrowStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // ThrowStatement[Yield] :
        //      throw [no LineTerminator here]Expression[In, ?Yield];

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ThrowKeyword);

        // Because of automatic semicolon insertion, we need to report error if this
        // throw could be terminated with a semicolon.  Note: we can't call 'parseExpression'
        // directly as that might consume an expression on the following line.
        // Instead, we create a "missing" identifier, but don't report an error. The actual error
        // will be reported in the grammar walker.
        var expression = _scanner.HasPrecedingLineBreak() ? null : AllowInAnd(ParseExpression);
        if (expression == null)
        {
            expression = FinishNode(new Identifier(""), GetNodePos());
        }

        if (!TryParseSemicolon())
        {
            ParseErrorForMissingSemicolonAfter(expression);
        }

        return WithJSDoc(FinishNode(new ThrowStatement(expression), pos), hasJSDoc);
    }


    // TODO: Review for error recovery
    private TryStatement ParseTryStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        ParseExpected(SyntaxKind.TryKeyword);
        var tryBlock = ParseBlock(ignoreMissingOpenBrace: false);
        var catchClause = Token() == SyntaxKind.CatchKeyword ? ParseCatchClause() : null;

        // If we don't have a catch clause, then we must have a finally clause.  Try to parse
        // one out no matter what.
        Block? finallyBlock = null;
        if (catchClause == null || Token() == SyntaxKind.FinallyKeyword)
        {
            ParseExpected(SyntaxKind.FinallyKeyword, Diagnostics._catchOrFinallyExpected);
            finallyBlock = ParseBlock(ignoreMissingOpenBrace: false);
        }

        return WithJSDoc(FinishNode(new TryStatement(tryBlock, catchClause!, finallyBlock!), pos), hasJSDoc);
    }

    private CatchClause ParseCatchClause()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.CatchKeyword);

        VariableDeclaration? variableDeclaration;
        if (ParseOptional(SyntaxKind.OpenParenToken))
        {
            variableDeclaration = ParseVariableDeclaration();
            ParseExpected(SyntaxKind.CloseParenToken);
        }
        else
        {
            // Keep shape of node to avoid degrading performance.
            variableDeclaration = null;
        }

        var block = ParseBlock(ignoreMissingOpenBrace: false);

        return FinishNode(new CatchClause(variableDeclaration!, block), pos);
    }

    private DebuggerStatement ParseDebuggerStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.DebuggerKeyword);
        ParseSemicolon();

        return WithJSDoc(FinishNode(new DebuggerStatement(), pos), hasJSDoc);
    }

    private Statement ParseExpressionOrLabeledStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // Avoiding having to do the LookAhead for a labeled statement by just trying to parse
        // out an expression, seeing if it is identifier and then seeing if it is followed by
        // a colon.
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        Statement node;
        var hasParen = Token() == SyntaxKind.OpenParenToken;
        var expression = AllowInAnd(ParseExpression);
        if (expression is Identifier exprIdentifier && ParseOptional(SyntaxKind.ColonToken))
        {
            node = new LabeledStatement(exprIdentifier, ParseStatement());
        }
        else
        {
            if (!TryParseSemicolon())
            {
                ParseErrorForMissingSemicolonAfter(expression);
            }

            node = new ExpressionStatement(expression);
            if (hasParen)
            {
                // do not parse the same jsdoc twice
                hasJSDoc = false;
            }
        }

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private bool NextTokenIsIdentifierOrKeywordOnSameLine()
    {
        NextToken();

        return TokenIsIdentifierOrKeyword(Token()) && !_scanner.HasPrecedingLineBreak();
    }

    private bool NextTokenIsClassKeywordOnSameLine()
    {
        NextToken();

        return Token() == SyntaxKind.ClassKeyword && !_scanner.HasPrecedingLineBreak();
    }

    private bool NextTokenIsFunctionKeywordOnSameLine()
    {
        NextToken();

        return Token() == SyntaxKind.FunctionKeyword && !_scanner.HasPrecedingLineBreak();
    }

    private bool NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine()
    {
        NextToken();

        return (TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.NumericLiteral
            || Token() == SyntaxKind.BigIntLiteral || Token() == SyntaxKind.StringLiteral) &&
            !_scanner.HasPrecedingLineBreak();
    }

    private bool IsDeclaration()
    {
        while (true)
        {
            switch (Token())
            {
                case SyntaxKind.VarKeyword:
                case SyntaxKind.LetKeyword:
                case SyntaxKind.ConstKeyword:
                case SyntaxKind.FunctionKeyword:
                case SyntaxKind.ClassKeyword:
                case SyntaxKind.EnumKeyword:
                    return true;
                case SyntaxKind.UsingKeyword:
                    return IsUsingDeclaration();
                case SyntaxKind.AwaitKeyword:
                    return IsAwaitUsingDeclaration();

                // 'declare', 'module', 'namespace', 'interface'* and 'type' are all legal JavaScript identifiers;
                // however, an identifier cannot be followed by another identifier on the same line. This is what we
                // count on to parse out the respective declarations. For instance, we exploit this to say that
                //
                //    namespace n
                //
                // can be none other than the beginning of a namespace declaration, but need to respect that JavaScript sees
                //
                //    namespace
                //    n
                //
                // as the identifier 'namespace' on one line followed by the identifier 'n' on another.
                // We need to look one token ahead to see if it permissible to try parsing a declaration.
                //
                // *Note*: 'interface' is actually a strict mode reserved word. So while
                //
                //   "use strict"
                //   interface
                //   I {}
                //
                // could be legal, it would add complexity for very little gain.
                case SyntaxKind.InterfaceKeyword:
                case SyntaxKind.TypeKeyword:
                    return NextTokenIsIdentifierOnSameLine();
                case SyntaxKind.ModuleKeyword:
                case SyntaxKind.NamespaceKeyword:
                    return NextTokenIsIdentifierOrStringLiteralOnSameLine();
                case SyntaxKind.AbstractKeyword:
                case SyntaxKind.AccessorKeyword:
                case SyntaxKind.AsyncKeyword:
                case SyntaxKind.DeclareKeyword:
                case SyntaxKind.PrivateKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.PublicKeyword:
                case SyntaxKind.ReadonlyKeyword:
                    var previousToken = Token();
                    NextToken();
                    // ASI takes effect for this modifier.
                    if (_scanner.HasPrecedingLineBreak())
                    {
                        return false;
                    }
                    if (previousToken == SyntaxKind.DeclareKeyword && Token() == SyntaxKind.TypeKeyword)
                    {
                        // If we see 'declare type', then commit to parsing a type alias. ParseTypeAliasDeclaration will
                        // report Line_break_not_permitted_here if needed.
                        return true;
                    }
                    continue;

                case SyntaxKind.GlobalKeyword:
                    NextToken();
                    return Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.Identifier || Token() == SyntaxKind.ExportKeyword;

                case SyntaxKind.ImportKeyword:
                    NextToken();
                    return Token() == SyntaxKind.StringLiteral || Token() == SyntaxKind.AsteriskToken ||
                        Token() == SyntaxKind.OpenBraceToken || TokenIsIdentifierOrKeyword(Token());
                case SyntaxKind.ExportKeyword:
                    var currentToken = NextToken();
                    if (currentToken == SyntaxKind.TypeKeyword)
                    {
                        currentToken = LookAhead(NextToken);
                    }
                    if (
                        currentToken == SyntaxKind.EqualsToken || currentToken == SyntaxKind.AsteriskToken ||
                        currentToken == SyntaxKind.OpenBraceToken || currentToken == SyntaxKind.DefaultKeyword ||
                        currentToken == SyntaxKind.AsKeyword || currentToken == SyntaxKind.AtToken
                    )
                    {
                        return true;
                    }
                    continue;

                case SyntaxKind.StaticKeyword:
                    NextToken();
                    continue;

                default:
                    return false;
            }
        }
    }

    private bool IsStartOfDeclaration()
    {
        return LookAhead(IsDeclaration);
    }

    private bool IsStartOfStatement()
    {
        return Token() switch
        {
            SyntaxKind.AtToken or SyntaxKind.SemicolonToken or SyntaxKind.OpenBraceToken
                or SyntaxKind.VarKeyword or SyntaxKind.LetKeyword or SyntaxKind.UsingKeyword
                or SyntaxKind.FunctionKeyword or SyntaxKind.ClassKeyword
                or SyntaxKind.EnumKeyword or SyntaxKind.IfKeyword or SyntaxKind.DoKeyword
                or SyntaxKind.WhileKeyword or SyntaxKind.ForKeyword or SyntaxKind.ContinueKeyword
                or SyntaxKind.BreakKeyword or SyntaxKind.ReturnKeyword
                or SyntaxKind.WithKeyword or SyntaxKind.SwitchKeyword or SyntaxKind.ThrowKeyword
                or SyntaxKind.TryKeyword or SyntaxKind.DebuggerKeyword or SyntaxKind.CatchKeyword
                or SyntaxKind.FinallyKeyword => true,
            // When these don't start a declaration, they're an identifier in an expression statement
            SyntaxKind.ImportKeyword => IsStartOfDeclaration() || LookAhead(NextTokenIsOpenParenOrLessThanOrDot),
            SyntaxKind.ConstKeyword or SyntaxKind.ExportKeyword => IsStartOfDeclaration(),
            SyntaxKind.AsyncKeyword or SyntaxKind.DeclareKeyword or SyntaxKind.InterfaceKeyword
                or SyntaxKind.ModuleKeyword or SyntaxKind.NamespaceKeyword or SyntaxKind.TypeKeyword
                or SyntaxKind.GlobalKeyword => true,
            // When these don't start a declaration, they may be the start of a class member if an identifier
            SyntaxKind.AccessorKeyword or SyntaxKind.PublicKeyword
                or SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword
                or SyntaxKind.StaticKeyword or SyntaxKind.ReadonlyKeyword =>
                IsStartOfDeclaration() || !LookAhead(NextTokenIsIdentifierOrKeywordOnSameLine),
            // immediately follows. Otherwise they're an identifier in an expression statement.
            _ => IsStartOfExpression(),
        };
    }

    private bool NextTokenIsBindingIdentifierOrStartOfDestructuring()
    {
        NextToken();

        return IsBindingIdentifier() || Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.OpenBracketToken;
    }

    private bool IsLetDeclaration()
    {
        // In ES6 'let' always starts a lexical declaration if followed by an identifier or {
        // or [.
        return LookAhead(NextTokenIsBindingIdentifierOrStartOfDestructuring);
    }

    private bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOfTrue()
    {
        return NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(disallowOf: true);
    }

    private bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOfFalse()
    {
        return NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(disallowOf: false);
    }

    private bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(bool disallowOf)
    {
        NextToken();
        if (disallowOf && Token() == SyntaxKind.OfKeyword)
        {
            return false;
        }

        return (IsBindingIdentifier() || Token() == SyntaxKind.OpenBraceToken) && !_scanner.HasPrecedingLineBreak();
    }

    private bool IsUsingDeclaration()
    {
        // 'using' always starts a lexical declaration if followed by an identifier. We also eagerly parse
        // |ObjectBindingPattern| so that we can report a grammar error during check. We don't parse out
        // |ArrayBindingPattern| since it potentially conflicts with element access (i.e., `using[x]`).
        return LookAhead(NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOfFalse);
    }

    private bool NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLineDisallowOfFalse()
    {
        return NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine(disallowOf: false);
    }

    private bool NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine(bool disallowOf)
    {
        if (NextToken() == SyntaxKind.UsingKeyword)
        {
            return NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(disallowOf);
        }

        return false;
    }

    private bool IsAwaitUsingDeclaration()
    {
        // 'await using' always starts a lexical declaration if followed by an identifier. We also eagerly parse
        // |ObjectBindingPattern| so that we can report a grammar error during check. We don't parse out
        // |ArrayBindingPattern| since it potentially conflicts with element access (i.e., `await using[x]`).
        return LookAhead(NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLineDisallowOfFalse);
    }

    private IStatement ParseStatement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        switch (Token())
        {
            case SyntaxKind.SemicolonToken:
                return ParseEmptyStatement();
            case SyntaxKind.OpenBraceToken:
                return ParseBlock(ignoreMissingOpenBrace: false);
            case SyntaxKind.VarKeyword:
                return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), modifiers: null!);
            case SyntaxKind.LetKeyword:
                if (IsLetDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), modifiers: null!);
                }
                break;
            case SyntaxKind.AwaitKeyword:
                if (IsAwaitUsingDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), modifiers: null!);
                }
                break;
            case SyntaxKind.UsingKeyword:
                if (IsUsingDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), modifiers: null!);
                }
                break;
            case SyntaxKind.FunctionKeyword:
                return ParseFunctionDeclaration(GetNodePos(), HasPrecedingJSDocComment(), modifiers: null!);
            case SyntaxKind.ClassKeyword:
                return ParseClassDeclaration(GetNodePos(), HasPrecedingJSDocComment(), modifiers: null!);
            case SyntaxKind.IfKeyword:
                return ParseIfStatement();
            case SyntaxKind.DoKeyword:
                return ParseDoStatement();
            case SyntaxKind.WhileKeyword:
                return ParseWhileStatement();
            case SyntaxKind.ForKeyword:
                return ParseForOrForInOrForOfStatement();
            case SyntaxKind.ContinueKeyword:
                return ParseBreakOrContinueStatement(SyntaxKind.ContinueStatement);
            case SyntaxKind.BreakKeyword:
                return ParseBreakOrContinueStatement(SyntaxKind.BreakStatement);
            case SyntaxKind.ReturnKeyword:
                return ParseReturnStatement();
            case SyntaxKind.WithKeyword:
                return ParseWithStatement();
            case SyntaxKind.SwitchKeyword:
                return ParseSwitchStatement();
            case SyntaxKind.ThrowKeyword:
                return ParseThrowStatement();
            case SyntaxKind.TryKeyword:
            // Include 'catch' and 'finally' for error recovery.
            // falls through
            case SyntaxKind.CatchKeyword:
            case SyntaxKind.FinallyKeyword:
                return ParseTryStatement();
            case SyntaxKind.DebuggerKeyword:
                return ParseDebuggerStatement();
            case SyntaxKind.AtToken:
                return ParseDeclaration();
            case SyntaxKind.AsyncKeyword:
            case SyntaxKind.InterfaceKeyword:
            case SyntaxKind.TypeKeyword:
            case SyntaxKind.ModuleKeyword:
            case SyntaxKind.NamespaceKeyword:
            case SyntaxKind.DeclareKeyword:
            case SyntaxKind.ConstKeyword:
            case SyntaxKind.EnumKeyword:
            case SyntaxKind.ExportKeyword:
            case SyntaxKind.ImportKeyword:
            case SyntaxKind.PrivateKeyword:
            case SyntaxKind.ProtectedKeyword:
            case SyntaxKind.PublicKeyword:
            case SyntaxKind.AbstractKeyword:
            case SyntaxKind.AccessorKeyword:
            case SyntaxKind.StaticKeyword:
            case SyntaxKind.ReadonlyKeyword:
            case SyntaxKind.GlobalKeyword:
                if (IsStartOfDeclaration())
                {
                    return ParseDeclaration();
                }
                break;
        }

        return ParseExpressionOrLabeledStatement();
    }

    private bool IsDeclareModifier(IModifierLike modifier)
    {
        return modifier.Kind == SyntaxKind.DeclareKeyword;
    }

    private IStatement ParseDeclaration()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // `parseListElement` attempted to get the reused node at this position,
        // but the ambient context flag was not yet set, so the node appeared
        // not reusable in that context.
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(allowDecorators: true);
        var isAmbient = modifiers?.Any(IsDeclareModifier) == true;
        if (isAmbient)
        {
            var node = TryReuseAmbientDeclaration(pos);
            if (node != null)
            {
                return node;
            }

            foreach (var m in modifiers!)
            {
                ((Node)m).Flags |= NodeFlags.Ambient;
            }

            return DoInsideOfContext(NodeFlags.Ambient, () => ParseDeclarationWorker(pos, hasJSDoc, modifiers!));
        }
        else
        {
            return ParseDeclarationWorker(pos, hasJSDoc, modifiers!);
        }
    }

    private Statement TryReuseAmbientDeclaration(int pos)
    {
        return DoInsideOfContext(
            NodeFlags.Ambient,
            () =>
            {
                // TODO(jakebailey): this is totally wrong; `parsingContext` is the result of ORing a bunch of `1 << ParsingContext.XYZ`.
                // The enum should really be a bunch of flags.
                var node = CurrentNode(_parsingContext, pos);
                if (node != null)
                {
                    return (ConsumeNode(node) as Statement)!;
                }

                return null!;
            });
    }

    private IStatement ParseDeclarationWorker(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiersIn)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        switch (Token())
        {
            case SyntaxKind.VarKeyword:
            case SyntaxKind.LetKeyword:
            case SyntaxKind.ConstKeyword:
            case SyntaxKind.UsingKeyword:
            case SyntaxKind.AwaitKeyword:
                return ParseVariableStatement(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.FunctionKeyword:
                return ParseFunctionDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.ClassKeyword:
                return ParseClassDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.InterfaceKeyword:
                return ParseInterfaceDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.TypeKeyword:
                return ParseTypeAliasDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.EnumKeyword:
                return ParseEnumDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.GlobalKeyword:
            case SyntaxKind.ModuleKeyword:
            case SyntaxKind.NamespaceKeyword:
                return ParseModuleDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.ImportKeyword:
                return ParseImportDeclarationOrImportEqualsDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.ExportKeyword:
                NextToken();
                return Token() switch
                {
                    SyntaxKind.DefaultKeyword or SyntaxKind.EqualsToken => ParseExportAssignment(pos, hasJSDoc, modifiersIn),
                    SyntaxKind.AsKeyword => ParseNamespaceExportDeclaration(pos, hasJSDoc, modifiersIn),
                    _ => ParseExportDeclaration(pos, hasJSDoc, modifiersIn),
                };
            default:
                if (modifiersIn != null)
                {
                    // We reached this point because we encountered decorators and/or modifiers and assumed a declaration
                    // would follow. For recovery and error reporting purposes, return an incomplete declaration.
                    var missing = CreateMissingNode<MissingDeclaration>(
                        SyntaxKind.MissingDeclaration,
                        reportAtCurrentPosition: true,
                        Diagnostics._declarationExpected);
                    SetTextRangePos(missing, pos);
                    missing.Modifiers = modifiersIn;

                    return missing;
                }
                return null!; // TODO: GH#18217
        }
    }

    private bool NextTokenIsStringLiteral()
    {
        return NextToken() == SyntaxKind.StringLiteral;
    }

    private bool NextTokenIsFromKeywordOrEqualsToken()
    {
        NextToken();

        return Token() == SyntaxKind.FromKeyword || Token() == SyntaxKind.EqualsToken;
    }

    private bool NextTokenIsIdentifierOrStringLiteralOnSameLine()
    {
        NextToken();

        return !_scanner.HasPrecedingLineBreak() && (IsIdentifier() || Token() == SyntaxKind.StringLiteral);
    }

    private Block ParseFunctionBlockOrSemicolon(SignatureFlags flags, DiagnosticMessage? diagnosticMessage = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() != SyntaxKind.OpenBraceToken)
        {
            if ((flags & SignatureFlags.Type) != 0)
            {
                ParseTypeMemberSemicolon();

                return null!;
            }

            if (CanParseSemicolon())
            {
                ParseSemicolon();

                return null!;
            }
        }

        return ParseFunctionBlock(flags, diagnosticMessage);
    }

    // DECLARATIONS
    private IArrayBindingElement ParseArrayBindingElement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        if (Token() == SyntaxKind.CommaToken)
        {
            return FinishNode(new OmittedExpression(), pos);
        }

        var dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);
        var name = ParseIdentifierOrPattern();
        var initializer = ParseInitializer();

        return FinishNode(new BindingElement(dotDotDotToken, propertyName: null, name, initializer), pos);
    }

    private BindingElement ParseObjectBindingElement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);
        var TokenIsIdentifier = IsBindingIdentifier();
        IPropertyName? propertyName = ParsePropertyName();
        IBindingName name;
        if (TokenIsIdentifier && Token() != SyntaxKind.ColonToken)
        {
            name = (propertyName as Identifier)!;
            propertyName = null;
        }
        else
        {
            ParseExpected(SyntaxKind.ColonToken);
            name = ParseIdentifierOrPattern();
        }

        var initializer = ParseInitializer();

        return FinishNode(new BindingElement(dotDotDotToken, propertyName, name, initializer), pos);
    }

    private ObjectBindingPattern ParseObjectBindingPattern()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        var elements = AllowInAnd(() => ParseDelimitedList<IArrayBindingElement>(
            ParsingContext.ObjectBindingElements,
            ParseObjectBindingElement));
        ParseExpected(SyntaxKind.CloseBraceToken);

        return FinishNode(new ObjectBindingPattern(elements!), pos);
    }

    private ArrayBindingPattern ParseArrayBindingPattern()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBracketToken);
        var elements = AllowInAnd(() => ParseDelimitedList(ParsingContext.ArrayBindingElements, ParseArrayBindingElement));
        ParseExpected(SyntaxKind.CloseBracketToken);

        return FinishNode(new ArrayBindingPattern(elements!), pos);
    }

    private bool IsBindingIdentifierOrPrivateIdentifierOrPattern()
    {
        return Token() == SyntaxKind.OpenBraceToken
            || Token() == SyntaxKind.OpenBracketToken
            || Token() == SyntaxKind.PrivateIdentifier
            || IsBindingIdentifier();
    }

    private IBindingName ParseIdentifierOrPattern(DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.OpenBracketToken)
        {
            return ParseArrayBindingPattern();
        }

        if (Token() == SyntaxKind.OpenBraceToken)
        {
            return ParseObjectBindingPattern();
        }

        return ParseBindingIdentifier(privateIdentifierDiagnosticMessage);
    }

    private VariableDeclaration ParseVariableDeclarationAllowExclamation()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseVariableDeclaration(allowExclamation: true);
    }

    private VariableDeclaration ParseVariableDeclarationDisallowExclamation()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseVariableDeclaration(allowExclamation: false);
    }

    private VariableDeclaration ParseVariableDeclaration(bool allowExclamation = false)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var name = ParseIdentifierOrPattern(Diagnostics._privateIdentifiersAreNotAllowedInVariableDeclarations);
        ExclamationToken? exclamationToken = null;
        if (
            allowExclamation && name.Kind == SyntaxKind.Identifier &&
            Token() == SyntaxKind.ExclamationToken && !_scanner.HasPrecedingLineBreak()
        )
        {
            exclamationToken = ParseTokenNode<ExclamationToken>();
        }

        var type = ParseTypeAnnotation();
        var initializer = IsInOrOfKeyword(Token()) ? null : ParseInitializer();
        var node = new VariableDeclaration(name, exclamationToken, type, initializer);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private VariableDeclarationList ParseVariableDeclarationList(bool inForStatementInitializer)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        NodeFlags flags = 0;
        switch (Token())
        {
            case SyntaxKind.VarKeyword:
                break;
            case SyntaxKind.LetKeyword:
                flags |= NodeFlags.Let;
                break;
            case SyntaxKind.ConstKeyword:
                flags |= NodeFlags.Const;
                break;
            case SyntaxKind.UsingKeyword:
                flags |= NodeFlags.Using;
                break;
            case SyntaxKind.AwaitKeyword:
                Debug.Assert(IsAwaitUsingDeclaration());
                flags |= NodeFlags.AwaitUsing;
                NextToken();
                break;
            default:
                throw Debug.Fail($"Unknown syntax kind in ParseVariableDeclarationList: {Token()}");
        }

        NextToken();

        // The user may have written the following:
        //
        //    for (var of X) { }
        //
        // In this case, we want to parse an empty declaration list, and then parse 'of'
        // as a keyword. The reason this is not automatic is that 'of' is a valid identifier.
        // So we need to look ahead to determine if 'of' should be treated as a keyword in
        // this context.
        // The checker will then give an error that there is an empty declaration list.
        NodeArray<VariableDeclaration>? declarations;
        if (Token() == SyntaxKind.OfKeyword && LookAhead(CanFollowContextualOfKeyword))
        {
            declarations = CreateMissingList<VariableDeclaration>();
        }
        else
        {
            var savedDisallowIn = InDisallowInContext();
            SetDisallowInContext(inForStatementInitializer);

            declarations = ParseDelimitedList<VariableDeclaration>(
                ParsingContext.VariableDeclarations,
                (inForStatementInitializer ? ParseVariableDeclarationDisallowExclamation
                    : ParseVariableDeclarationAllowExclamation)
            );

            SetDisallowInContext(savedDisallowIn);
        }

        return FinishNode(new VariableDeclarationList(declarations!, flags), pos);
    }

    private bool CanFollowContextualOfKeyword()
    {
        return NextTokenIsIdentifier() && NextToken() == SyntaxKind.CloseParenToken;
    }


    private VariableStatement ParseVariableStatement(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var declarationList = ParseVariableDeclarationList(inForStatementInitializer: false);
        ParseSemicolon();
        var node = new VariableStatement(modifiers, declarationList);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private FunctionDeclaration ParseFunctionDeclaration(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var savedAwaitContext = InAwaitContext();
        var modifierFlags = ModifiersToFlags(modifiers);
        ParseExpected(SyntaxKind.FunctionKeyword);
        var asteriskToken = ParseOptionalToken<AsteriskToken>(SyntaxKind.AsteriskToken);
        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        var name = (modifierFlags & ModifierFlags.Default) != 0 ? ParseOptionalBindingIdentifier() : ParseBindingIdentifier();
        var isGenerator = asteriskToken != null ? SignatureFlags.Yield : SignatureFlags.None;
        var isAsync = (modifierFlags & ModifierFlags.Async) != 0 ? SignatureFlags.Await : SignatureFlags.None;
        var typeParameters = ParseTypeParameters();
        if ((modifierFlags & ModifierFlags.Export) != 0)
        {
            SetAwaitContext(val: true);
        }

        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, isType: false);
        var body = ParseFunctionBlockOrSemicolon(isGenerator | isAsync, Diagnostics._orExpected);
        SetAwaitContext(savedAwaitContext);
        var node = new FunctionDeclaration(modifiers, asteriskToken!, name, typeParameters!, parameters, type, body);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private TypeOption<bool, ILiteralExpression> ParseConstructorName()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.ConstructorKeyword)
        {
            return new(ParseExpected(SyntaxKind.ConstructorKeyword));
        }

        if (Token() == SyntaxKind.StringLiteral && LookAhead(NextToken) == SyntaxKind.OpenParenToken)
        {
            return new(TryParse(() =>
            {
                var literalNode = ParseLiteralNode<ILiteralExpression>();

                return literalNode.Text == "constructor" ? literalNode : null;
            })!);
        }

        return default;
    }

    private ConstructorDeclaration TryParseConstructorDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers)
    {
        return TryParse(() =>
        {
            var constructorName = ParseConstructorName();
            if (constructorName is { Value1: true } or { Value2: not null })
            {
                var typeParameters = ParseTypeParameters();
                var parameters = ParseParameters(SignatureFlags.None);
                var type = ParseReturnType(SyntaxKind.ColonToken, isType: false);
                var body = ParseFunctionBlockOrSemicolon(SignatureFlags.None, Diagnostics._orExpected);
                var node = new ConstructorDeclaration(modifiers, parameters, body)
                {
                    // Attach invalid nodes if they exist so that we can report them in the grammar checker.
                    TypeParameters = typeParameters,
                    Type = type
                };

                return WithJSDoc(FinishNode(node, pos), hasJSDoc);
            }

            return null!;
        });
    }

    private MethodDeclaration ParseMethodDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers,
        AsteriskToken asteriskToken,
        IPropertyName name,
        QuestionToken questionToken,
        ExclamationToken exclamationToken,
        DiagnosticMessage? diagnosticMessage = null)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var isGenerator = asteriskToken != null ? SignatureFlags.Yield : SignatureFlags.None;
        var isAsync = modifiers?.Any(x => x.Kind == SyntaxKind.AsyncKeyword) == true ? SignatureFlags.Await : SignatureFlags.None;
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, isType: false);
        var body = ParseFunctionBlockOrSemicolon(isGenerator | isAsync, diagnosticMessage);
        var node = new MethodDeclaration(
            modifiers!,
            asteriskToken!,
            name,
            questionToken,
            typeParameters!,
            parameters,
            type,
            body
        )
        {
            // An exclamation token on a method is invalid syntax and will be handled by the grammar checker
            ExclamationToken = exclamationToken
        };

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private PropertyDeclaration ParsePropertyDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers,
        IPropertyName name,
        QuestionToken questionToken)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var exclamationToken = questionToken == null && !_scanner.HasPrecedingLineBreak()
            ? ParseOptionalToken<ExclamationToken>(SyntaxKind.ExclamationToken) : null;
        var type = ParseTypeAnnotation();
        var initializer = DoOutsideOfContext(
            NodeFlags.YieldContext | NodeFlags.AwaitContext | NodeFlags.DisallowInContext,
            ParseInitializer);
        ParseSemicolonAfterPropertyName(name, type, initializer);
        var node = new PropertyDeclaration(
            modifiers,
            name,
            (INode?)questionToken ?? exclamationToken,
            type,
            initializer
        );

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private IClassElement ParsePropertyOrMethodDeclaration(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var asteriskToken = ParseOptionalToken<AsteriskToken>(SyntaxKind.AsteriskToken);
        var name = ParsePropertyName();
        // Note: this is not legal as per the grammar.  But we allow it in the parser and
        // report an error in the grammar checker.
        var questionToken = ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken);
        if (asteriskToken != null || Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseMethodDeclaration(
                pos,
                hasJSDoc,
                modifiers,
                asteriskToken!,
                name,
                questionToken!,
                exclamationToken: null!,
                Diagnostics._orExpected);
        }

        return ParsePropertyDeclaration(pos, hasJSDoc, modifiers, name, questionToken!);
    }

    private IAccessorDeclaration ParseAccessorDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers,
        SyntaxKind kind,
        SignatureFlags flags)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var name = ParsePropertyName();
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(SignatureFlags.None);
        var type = ParseReturnType(SyntaxKind.ColonToken, isType: false);
        var body = ParseFunctionBlockOrSemicolon(flags);
        var node = kind == SyntaxKind.GetAccessor
            ? (IAccessorDeclaration)new GetAccessorDeclaration(modifiers, name, parameters, type, body)
            : new SetAccessorDeclaration(modifiers, name, parameters, body);
        // Keep track of `typeParameters` (for both) and `type` (for setters) if they were parsed those indicate grammar errors
        ((SignatureDeclarationBase)node).TypeParameters = typeParameters;
        if (node is SetAccessorDeclaration setAccessorDeclaration)
        {
            setAccessorDeclaration.Type = type;
        }

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private bool IsClassMemberStart()
    {
        SyntaxKind? idToken = null;

        if (Token() == SyntaxKind.AtToken)
        {
            return true;
        }

        // Eat up all modifiers, but hold on to the last one in case it is actually an identifier.
        while (IsModifierKind(Token()))
        {
            idToken = Token();
            // If the idToken is a class modifier (protected, private, public, and static), it is
            // certain that we are starting to parse class member. This allows better error recovery
            // Example:
            //      public foo() ...     // true
            //      public @dec blah ... // true; we will then report an error later
            //      export public ...    // true; we will then report an error later
            if (IsClassMemberModifier(idToken.Value))
            {
                return true;
            }

            NextToken();
        }

        if (Token() == SyntaxKind.AsteriskToken)
        {
            return true;
        }

        // Try to get the first property-like token following all modifiers.
        // This can either be an identifier or the 'get' or 'set' keywords.
        if (IsLiteralPropertyName())
        {
            idToken = Token();
            NextToken();
        }

        // Index signatures and computed properties are class members; we can parse.
        if (Token() == SyntaxKind.OpenBracketToken)
        {
            return true;
        }

        // If we were able to get any potential identifier...
        if (idToken != null)
        {
            // If we have a non-keyword identifier, or if we have an accessor, then it's safe to parse.
            if (!IsKeyword(idToken.Value) || idToken == SyntaxKind.SetKeyword || idToken == SyntaxKind.GetKeyword)
            {
                return true;
            }

            // If it *is* a keyword, but not an accessor, check a little farther along
            // to see if it should actually be parsed as a class member.
            return Token() switch
            {
                SyntaxKind.OpenParenToken // Method declaration
                    or SyntaxKind.LessThanToken // Generic Method declaration
                    or SyntaxKind.ExclamationToken // Non-null assertion on property name
                    or SyntaxKind.ColonToken // Type Annotation for declaration
                    or SyntaxKind.EqualsToken // Initializer for declaration
                    or SyntaxKind.QuestionToken => true, // Not valid, but permitted so that it gets caught later on.
                _ =>
                    // Covers
                    //  - Semicolons     (declaration termination)
                    //  - Closing braces (end-of-class, must be declaration)
                    //  - End-of-files   (not valid, but permitted so that it gets caught later on)
                    //  - Line-breaks    (enabling *automatic semicolon insertion*)
                    CanParseSemicolon()
            };
        }

        return false;
    }

    private ClassStaticBlockDeclaration ParseClassStaticBlockDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        ParseExpectedToken<StaticKeyword>(SyntaxKind.StaticKeyword);
        var body = ParseClassStaticBlockBody();
        var node = WithJSDoc(FinishNode(new ClassStaticBlockDeclaration(body), pos), hasJSDoc);
        node.Modifiers = modifiers;

        return node;
    }

    private Block ParseClassStaticBlockBody()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var savedYieldContext = InYieldContext();
        var savedAwaitContext = InAwaitContext();

        SetYieldContext(false);
        SetAwaitContext(true);

        var body = ParseBlock(ignoreMissingOpenBrace: false);

        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return body;
    }

    private ILeftHandSideExpression ParseDecoratorExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (InAwaitContext() && Token() == SyntaxKind.AwaitKeyword)
        {
            // `@await` is is disallowed in an [Await] context, but can cause parsing to go off the rails
            // This simply parses the missing identifier and moves on.
            var pos = GetNodePos();
            var awaitExpression = ParseIdentifier(Diagnostics._expressionExpected);
            NextToken();
            var memberExpression = ParseMemberExpressionRest(pos, awaitExpression, allowOptionalChain: true);

            return ParseCallExpressionRest(pos, memberExpression);
        }

        return ParseLeftHandSideExpressionOrHigher();
    }

    private Decorator TryParseDecorator()
    {
        var pos = GetNodePos();
        if (!ParseOptional(SyntaxKind.AtToken))
        {
            return null!;
        }

        var expression = DoInDecoratorContext(ParseDecoratorExpression);

        return FinishNode(new Decorator(expression), pos);
    }

    private ModifierToken TryParseModifier(
        bool hasSeenStaticModifier,
        bool permitConstAsModifier,
        bool stopOnStartOfClassStaticBlock)
    {
        var pos = GetNodePos();
        var kind = Token();

        if (Token() == SyntaxKind.ConstKeyword && permitConstAsModifier)
        {
            // We need to ensure that any subsequent modifiers appear on the same line
            // so that when 'const' is a standalone declaration, we don't issue an error.
            if (!TryParse(NextTokenIsOnSameLineAndCanFollowModifier))
            {
                return null!;
            }
        }
        else if (stopOnStartOfClassStaticBlock && Token() == SyntaxKind.StaticKeyword && LookAhead(NextTokenIsOpenBrace))
        {
            return null!;
        }
        else if (hasSeenStaticModifier && Token() == SyntaxKind.StaticKeyword)
        {
            return null!;
        }
        else
        {
            if (!ParseAnyContextualModifier())
            {
                return null!;
            }
        }

        return FinishNode(new ModifierToken(kind), pos);
    }

    /*
         * There are situations in which a modifier like 'const' will appear unexpectedly, such as on a class member.
         * In those situations, if we are entirely sure that 'const' is not valid on its own (such as when ASI takes effect
         * and turns it into a standalone declaration), then it is better to parse it and report an error later.
         *
         * In such situations, 'permitConstAsModifier' should be set to true.
         */
    private NodeArray<IModifierLike> ParseModifiers(
        bool allowDecorators,
        bool permitConstAsModifier = false,
        bool stopOnStartOfClassStaticBlock = false)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        List<IModifierLike>? list = null;
        Decorator decorator;
        IModifier modifier;
        var hasSeenStaticModifier = false;
        var hasLeadingModifier = false;
        var hasTrailingDecorator = false;

        // Decorators should be contiguous in a list of modifiers but can potentially appear in two places (i.e., `[...leadingDecorators, ...leadingModifiers, ...trailingDecorators, ...trailingModifiers]`).
        // The leading modifiers *should* only contain `export` and `default` when trailingDecorators are present, but we'll handle errors for any other leading modifiers in the checker.
        // It is illegal to have both leadingDecorators and trailingDecorators, but we will report that as a grammar check in the checker.

        // parse leading decorators
        if (allowDecorators && Token() == SyntaxKind.AtToken)
        {
            while ((decorator = TryParseDecorator()) != null)
            {
                list ??= [];
                list.Add(decorator);
            }
        }

        // parse leading modifiers
        while ((modifier = TryParseModifier(
            hasSeenStaticModifier,
            permitConstAsModifier,
            stopOnStartOfClassStaticBlock)) != null)
        {
            if (modifier.Kind == SyntaxKind.StaticKeyword)
            {
                hasSeenStaticModifier = true;
            }

            list ??= [];
            list.Add(modifier);
            hasLeadingModifier = true;
        }

        // parse trailing decorators, but only if we parsed any leading modifiers
        if (hasLeadingModifier && allowDecorators && Token() == SyntaxKind.AtToken)
        {
            while ((decorator = TryParseDecorator()) != null)
            {
                list ??= [];
                list.Add(decorator);
                hasTrailingDecorator = true;
            }
        }

        // parse trailing modifiers, but only if we parsed any trailing decorators
        if (hasTrailingDecorator)
        {
            while ((modifier = TryParseModifier(
                hasSeenStaticModifier,
                permitConstAsModifier,
                stopOnStartOfClassStaticBlock)) != null)
            {
                if (modifier.Kind == SyntaxKind.StaticKeyword)
                {
                    hasSeenStaticModifier = true;
                }

                list ??= [];
                list.Add(modifier);
            }
        }

        return list != null ? CreateNodeArray(list, pos) : null!;
    }

    private NodeArray<IModifierLike> ParseModifiersForArrowFunction()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        NodeArray<IModifierLike>? modifiers = null;
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            var pos = GetNodePos();
            NextToken();
            var modifier = FinishNode(new AsyncKeyword(), pos);
            modifiers = CreateNodeArray(new IModifierLike[] { modifier }, pos);
        }

        return modifiers!;
    }

    private IClassElement ParseClassElement()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        if (Token() == SyntaxKind.SemicolonToken)
        {
            NextToken();

            return WithJSDoc(FinishNode(new SemicolonClassElement(), pos), hasJSDoc);
        }

        var modifiers = ParseModifiers(
            allowDecorators: true,
            permitConstAsModifier: true,
            stopOnStartOfClassStaticBlock: true);

        if (Token() == SyntaxKind.StaticKeyword && LookAhead(NextTokenIsOpenBrace))
        {
            return ParseClassStaticBlockDeclaration(pos, hasJSDoc, modifiers);
        }

        if (ParseContextualModifier(SyntaxKind.GetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.GetAccessor, SignatureFlags.None);
        }

        if (ParseContextualModifier(SyntaxKind.SetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.SetAccessor, SignatureFlags.None);
        }

        if (Token() == SyntaxKind.ConstructorKeyword || Token() == SyntaxKind.StringLiteral)
        {
            var constructorDeclaration = TryParseConstructorDeclaration(pos, hasJSDoc, modifiers);
            if (constructorDeclaration != null)
            {
                return constructorDeclaration;
            }
        }

        if (IsIndexSignature())
        {
            return ParseIndexSignatureDeclaration(pos, hasJSDoc, modifiers);
        }

        // It is very important that we check this *after* checking indexers because
        // the [ token can start an index signature or a computed property name
        if (
            TokenIsIdentifierOrKeyword(Token()) ||
            Token() == SyntaxKind.StringLiteral ||
            Token() == SyntaxKind.NumericLiteral ||
            Token() == SyntaxKind.BigIntLiteral ||
            Token() == SyntaxKind.AsteriskToken ||
            Token() == SyntaxKind.OpenBracketToken
        )
        {
            var isAmbient = modifiers?.Any(x => x != null && IsDeclareModifier(x)) == true;
            if (isAmbient)
            {
                foreach (var m in modifiers!)
                {
                    ((Node)m).Flags |= NodeFlags.Ambient;
                }

                return DoInsideOfContext(
                    NodeFlags.Ambient,
                    () => ParsePropertyOrMethodDeclaration(pos, hasJSDoc, modifiers!));
            }
            else
            {
                return ParsePropertyOrMethodDeclaration(pos, hasJSDoc, modifiers!);
            }
        }

        if (modifiers != null)
        {
            // treat this as a property declaration with a missing name.
            var name = CreateMissingNode<Identifier>(
                SyntaxKind.Identifier,
                reportAtCurrentPosition: true,
                Diagnostics._declarationExpected);

            return ParsePropertyDeclaration(pos, hasJSDoc, modifiers, name, questionToken: null!);
        }

        // 'isClassMemberStart' should have hinted not to attempt parsing.
        throw Debug.Fail("Should not have attempted to parse class member declaration.");
    }

    private IPrimaryExpression ParseDecoratedExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(allowDecorators: true);
        if (Token() == SyntaxKind.ClassKeyword)
        {
            return (ParseClassDeclarationOrExpression(pos, hasJSDoc, modifiers, SyntaxKind.ClassExpression) as ClassExpression)!;
        }

        var missing = CreateMissingNode<MissingDeclaration>(
            SyntaxKind.MissingDeclaration,
            reportAtCurrentPosition: true,
            Diagnostics._expressionExpected);
        SetTextRangePos(missing, pos);
        missing.Modifiers = modifiers;

        return missing;
    }

    private ClassExpression ParseClassExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return (ParseClassDeclarationOrExpression(
            GetNodePos(),
            HasPrecedingJSDocComment(),
            modifiers: null!,
            SyntaxKind.ClassExpression) as ClassExpression)!;
    }

    private ClassDeclaration ParseClassDeclaration(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return (ParseClassDeclarationOrExpression(pos, hasJSDoc, modifiers, SyntaxKind.ClassDeclaration) as ClassDeclaration)!;
    }

    private IClassLikeDeclaration ParseClassDeclarationOrExpression(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers,
        SyntaxKind kind)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var savedAwaitContext = InAwaitContext();
        ParseExpected(SyntaxKind.ClassKeyword);

        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        var name = ParseNameOfClassDeclarationOrExpression();
        var typeParameters = ParseTypeParameters();
        if (modifiers?.Any(x => x.Kind == SyntaxKind.ExportKeyword) == true)
        {
            SetAwaitContext(val: true);
        }

        var heritageClauses = ParseHeritageClauses();

        NodeArray<IClassElement> members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            // ClassTail[Yield,Await] : (Modified) See 14.5
            //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }
            members = ParseClassMembers();
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList<IClassElement>();
        }

        SetAwaitContext(savedAwaitContext);
        IClassLikeDeclaration node = kind == SyntaxKind.ClassDeclaration
            ? new ClassDeclaration(modifiers!, name, typeParameters!, heritageClauses, members)
            : new ClassExpression(modifiers!, name, typeParameters!, heritageClauses, members);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Identifier ParseNameOfClassDeclarationOrExpression()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        // implements is a future reserved word so
        // 'class implements' might mean either
        // - class expression with omitted name, 'implements' starts heritage clause
        // - class with name 'implements'
        // 'isImplementsClause' helps to disambiguate between these two cases
        return IsBindingIdentifier() && !IsImplementsClause()
            ? CreateIdentifier(IsBindingIdentifier())
            : null!;
    }

    private bool IsImplementsClause()
    {
        return Token() == SyntaxKind.ImplementsKeyword && LookAhead(NextTokenIsIdentifierOrKeyword);
    }

    private NodeArray<HeritageClause> ParseHeritageClauses()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // ClassTail[Yield,Await] : (Modified) See 14.5
        //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }

        if (IsHeritageClause())
        {
            return ParseList(ParsingContext.HeritageClauses, ParseHeritageClause);
        }

        return null!;
    }

    private HeritageClause ParseHeritageClause()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var tok = Token();
        Debug.Assert(tok == SyntaxKind.ExtendsKeyword || tok == SyntaxKind.ImplementsKeyword); // isListElement() should ensure this.
        NextToken();
        var types = ParseDelimitedList(ParsingContext.HeritageClauseElement, ParseExpressionWithTypeArguments);

        return FinishNode(new HeritageClause(tok, types!), pos);
    }

    private ExpressionWithTypeArguments ParseExpressionWithTypeArguments()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var expression = ParseLeftHandSideExpressionOrHigher();
        if (expression.Kind == SyntaxKind.ExpressionWithTypeArguments)
        {
            return (expression as ExpressionWithTypeArguments)!;
        }

        var typeArguments = TryParseTypeArguments();

        return FinishNode(new ExpressionWithTypeArguments(expression, typeArguments), pos);
    }

    private NodeArray<ITypeNode> TryParseTypeArguments()
    {
        return Token() == SyntaxKind.LessThanToken ?
            ParseBracketedList(
                ParsingContext.TypeArguments,
                ParseType,
                SyntaxKind.LessThanToken,
                SyntaxKind.GreaterThanToken) : null!;
    }

    private bool IsHeritageClause()
    {
        return Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;
    }

    private NodeArray<IClassElement> ParseClassMembers()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return ParseList(ParsingContext.ClassMembers, ParseClassElement);
    }

    private InterfaceDeclaration ParseInterfaceDeclaration(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        ParseExpected(SyntaxKind.InterfaceKeyword);
        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        var heritageClauses = ParseHeritageClauses();
        var members = ParseObjectTypeMembers();
        var node = new InterfaceDeclaration(modifiers, name, typeParameters, heritageClauses, members);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private TypeAliasDeclaration ParseTypeAliasDeclaration(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        ParseExpected(SyntaxKind.TypeKeyword);
        if (_scanner.HasPrecedingLineBreak())
        {
            ParseErrorAtCurrentToken(Diagnostics._lineBreakNotPermittedHere);
        }

        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        ParseExpected(SyntaxKind.EqualsToken);
        var type = (Token() == SyntaxKind.IntrinsicKeyword ? TryParse(ParseKeywordAndNoDot) : null) ?? ParseType();
        ParseSemicolon();
        var node = new TypeAliasDeclaration(modifiers, name, typeParameters, type);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    // In an ambient declaration, the grammar only allows integer literals as initializers.
    // In a non-ambient declaration, the grammar allows uninitialized members only in a
    // ConstantEnumMemberSection, which starts at the beginning of an enum declaration
    // or any time an integer literal initializer is encountered.
    private EnumMember ParseEnumMember()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var name = ParsePropertyName();
        var initializer = AllowInAnd(ParseInitializer);

        return WithJSDoc(FinishNode(new EnumMember(name, initializer), pos), hasJSDoc);
    }

    private EnumDeclaration ParseEnumDeclaration(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        ParseExpected(SyntaxKind.EnumKeyword);
        var name = ParseIdentifier();
        NodeArray<EnumMember>? members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            members = DoOutsideOfYieldAndAwaitContext(() => ParseDelimitedList(ParsingContext.EnumMembers, ParseEnumMember));
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList<EnumMember>();
        }

        var node = new EnumDeclaration(modifiers, name, members);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ModuleBlock ParseModuleBlock()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        NodeArray<IStatement> statements;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            statements = ParseList(ParsingContext.BlockStatements, ParseStatement);
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            statements = CreateMissingList<IStatement>();
        }

        return FinishNode(new ModuleBlock(statements), pos);
    }

    private NamespaceDeclaration ParseModuleOrNamespaceDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers,
        NodeFlags flags)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // If we are parsing a dotted namespace name, we want to
        // propagate the 'Namespace' flag across the names if set.
        var namespaceFlag = flags & NodeFlags.Namespace;
        var name = (flags & NodeFlags.NestedNamespace) != 0 ? ParseIdentifierName() : ParseIdentifier();
        INamespaceBody body = ParseOptional(SyntaxKind.DotToken)
            ? ParseModuleOrNamespaceDeclaration(
                GetNodePos(),
                hasJSDoc: false,
                modifiers: null!,
                NodeFlags.NestedNamespace | namespaceFlag)
            : ParseModuleBlock();
        var node = new NamespaceDeclaration(modifiers, name, body, flags);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ModuleDeclaration ParseAmbientExternalModuleDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiersIn)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        NodeFlags flags = 0;
        IModuleName name;
        if (Token() == SyntaxKind.GlobalKeyword)
        {
            // parse 'global' as name of global scope augmentation
            name = ParseIdentifier();
            flags |= NodeFlags.GlobalAugmentation;
        }
        else
        {
            name = ParseLiteralNode<StringLiteral>();
            ((StringLiteral)name).Text = InternIdentifier(((StringLiteral)name).Text!);
        }

        ModuleBlock? body = null;
        if (Token() == SyntaxKind.OpenBraceToken)
        {
            body = ParseModuleBlock();
        }
        else
        {
            ParseSemicolon();
        }

        var node = new ModuleDeclaration(modifiersIn, name, body, flags);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ModuleDeclaration ParseModuleDeclaration(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiersIn)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        NodeFlags flags = 0;
        if (Token() == SyntaxKind.GlobalKeyword)
        {
            // global augmentation
            return ParseAmbientExternalModuleDeclaration(pos, hasJSDoc, modifiersIn);
        }
        else if (ParseOptional(SyntaxKind.NamespaceKeyword))
        {
            flags |= NodeFlags.Namespace;
        }
        else
        {
            ParseExpected(SyntaxKind.ModuleKeyword);
            if (Token() == SyntaxKind.StringLiteral)
            {
                return ParseAmbientExternalModuleDeclaration(pos, hasJSDoc, modifiersIn);
            }
        }

        return ParseModuleOrNamespaceDeclaration(pos, hasJSDoc, modifiersIn, flags);
    }

    private bool IsExternalModuleReference()
    {
        return Token() == SyntaxKind.RequireKeyword &&
            LookAhead(NextTokenIsOpenParen);
    }

    private bool NextTokenIsOpenParen()
    {
        return NextToken() == SyntaxKind.OpenParenToken;
    }

    private bool NextTokenIsOpenBrace()
    {
        return NextToken() == SyntaxKind.OpenBraceToken;
    }

    private bool NextTokenIsSlash()
    {
        return NextToken() == SyntaxKind.SlashToken;
    }

    private NamespaceExportDeclaration ParseNamespaceExportDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        ParseExpected(SyntaxKind.AsKeyword);
        ParseExpected(SyntaxKind.NamespaceKeyword);
        var name = ParseIdentifier();
        ParseSemicolon();
        var node = new NamespaceExportDeclaration(name)
        {
            // NamespaceExportDeclaration nodes cannot have decorators or modifiers, so we attach them here so we can report them in the grammar checker
            Modifiers = modifiers
        };

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private IStatement ParseImportDeclarationOrImportEqualsDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        ParseExpected(SyntaxKind.ImportKeyword);

        var afterImportPos = _scanner.GetTokenFullStart();

        // We don't parse the identifier here in await context, instead we will report a grammar error in the checker.
        Identifier? identifier = null;
        if (IsIdentifier())
        {
            identifier = ParseIdentifier();
        }

        var isTypeOnly = false;
        if (
            identifier?.EscapedText == "type" &&
            (Token() != SyntaxKind.FromKeyword || IsIdentifier() && LookAhead(NextTokenIsFromKeywordOrEqualsToken)) &&
            (IsIdentifier() || TokenAfterImportDefinitelyProducesImportDeclaration())
        )
        {
            isTypeOnly = true;
            identifier = IsIdentifier() ? ParseIdentifier() : null;
        }

        if (identifier != null && !TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration())
        {
            return ParseImportEqualsDeclaration(pos, hasJSDoc, modifiers, identifier, isTypeOnly);
        }

        var importClause = TryParseImportClause(identifier!, afterImportPos, isTypeOnly);
        var moduleSpecifier = ParseModuleSpecifier();
        var attributes = TryParseImportAttributes();

        ParseSemicolon();
        var node = new ImportDeclaration(modifiers, importClause, moduleSpecifier, attributes);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ImportClause TryParseImportClause(
        Identifier identifier,
        int pos,
        bool isTypeOnly,
        bool skipJsDocLeadingAsterisks = false)
    {
        // ImportDeclaration:
        //  import ImportClause from ModuleSpecifier ;
        //  import ModuleSpecifier;
        ImportClause? importClause = null;
        if (identifier != null || // import id
            Token() == SyntaxKind.AsteriskToken || // import *
            Token() == SyntaxKind.OpenBraceToken // import {
        )
        {
            importClause = ParseImportClause(identifier!, pos, isTypeOnly, skipJsDocLeadingAsterisks);
            ParseExpected(SyntaxKind.FromKeyword);
        }

        return importClause!;
    }

    private ImportAttributes TryParseImportAttributes()
    {
        var currentToken = Token();
        if ((currentToken == SyntaxKind.WithKeyword || currentToken == SyntaxKind.AssertKeyword)
            && !_scanner.HasPrecedingLineBreak())
        {
            return ParseImportAttributes(currentToken);
        }

        return null!;
    }

    private ImportAttribute ParseImportAttribute()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        IDeclarationName name = TokenIsIdentifierOrKeyword(Token()) ? ParseIdentifierName()
            : ParseLiteralLikeNode<StringLiteral>(SyntaxKind.StringLiteral);
        ParseExpected(SyntaxKind.ColonToken);
        var value = ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true);

        return FinishNode(new ImportAttribute(name, value), pos);
    }

    private ImportAttributes ParseImportAttributes(SyntaxKind token, bool skipKeyword = false)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        if (!skipKeyword)
        {
            ParseExpected(token);
        }

        var openBracePosition = _scanner.GetTokenStart();
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            var multiLine = _scanner.HasPrecedingLineBreak();
            var elements = ParseDelimitedList(
                ParsingContext.ImportAttributes,
                ParseImportAttribute,
                considerSemicolonAsDelimiter: true);
            ParseExpected(SyntaxKind.CloseBraceToken);

            return FinishNode(new ImportAttributes(elements!, multiLine, token), pos);
        }
        else
        {
            var elements = CreateNodeArray<ImportAttribute>([], GetNodePos(), end: null, hasTrailingComma: false);

            return FinishNode(new ImportAttributes(elements, multiLine: false, token), pos);
        }
    }

    private bool TokenAfterImportDefinitelyProducesImportDeclaration()
    {
        return Token() == SyntaxKind.AsteriskToken || Token() == SyntaxKind.OpenBraceToken;
    }

    private bool TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration()
    {
        // In `import id ___`, the current token decides whether to produce
        // an ImportDeclaration or ImportEqualsDeclaration.
        return Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.FromKeyword;
    }

    private ImportEqualsDeclaration ParseImportEqualsDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<IModifierLike> modifiers,
        Identifier identifier,
        bool isTypeOnly)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        ParseExpected(SyntaxKind.EqualsToken);
        var moduleReference = ParseModuleReference();
        ParseSemicolon();
        var node = new ImportEqualsDeclaration(modifiers, isTypeOnly, identifier, moduleReference);
        var finished = WithJSDoc(FinishNode(node, pos), hasJSDoc);

        return finished;
    }

    private ImportClause ParseImportClause(
        Identifier identifier,
        int pos,
        bool isTypeOnly,
        bool skipJsDocLeadingAsterisks = false)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // ImportClause:
        //  ImportedDefaultBinding
        //  NameSpaceImport
        //  NamedImports
        //  ImportedDefaultBinding, NameSpaceImport
        //  ImportedDefaultBinding, NamedImports

        // If there was no default import or if there is comma token after default import
        // parse namespace or named imports
        INamedImportBindings? namedBindings = null;
        if (identifier == null ||
            ParseOptional(SyntaxKind.CommaToken))
        {
            if (skipJsDocLeadingAsterisks)
            {
                _scanner.SetSkipJsDocLeadingAsterisks(true);
            }

            namedBindings = Token() == SyntaxKind.AsteriskToken ? ParseNamespaceImport()
                : (INamedImportBindings)ParseNamedImportsOrExports(SyntaxKind.NamedImports);
            if (skipJsDocLeadingAsterisks)
            {
                _scanner.SetSkipJsDocLeadingAsterisks(false);
            }
        }

        return FinishNode(new ImportClause(isTypeOnly, identifier, namedBindings), pos);
    }

    private IModuleReference ParseModuleReference()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return IsExternalModuleReference()
            ? ParseExternalModuleReference()
            : ParseEntityName(allowReservedWords: false);
    }

    private ExternalModuleReference ParseExternalModuleReference()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        ParseExpected(SyntaxKind.RequireKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = ParseModuleSpecifier();
        ParseExpected(SyntaxKind.CloseParenToken);

        return FinishNode(new ExternalModuleReference(expression), pos);
    }

    private IExpression ParseModuleSpecifier()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        if (Token() == SyntaxKind.StringLiteral)
        {
            var result = ParseLiteralNode<StringLiteral>();
            result.Text = InternIdentifier(result.Text!);

            return result;
        }
        else
        {
            // We allow arbitrary expressions here, even though the grammar only allows string
            // literals.  We check to ensure that it is only a string literal later in the grammar
            // check pass.
            return ParseExpression();
        }
    }

    private NamespaceImport ParseNamespaceImport()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());
        // NameSpaceImport:
        //  * as ImportedBinding
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.AsteriskToken);
        ParseExpected(SyntaxKind.AsKeyword);
        var name = ParseIdentifier();

        return FinishNode(new NamespaceImport(name), pos);
    }

    private bool CanParseModuleExportName()
    {
        return TokenIsIdentifierOrKeyword(Token()) ||
            Token() == SyntaxKind.StringLiteral;
    }

    private IModuleExportName ParseModuleExportName(Func<Identifier> parseName)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return Token() == SyntaxKind.StringLiteral ? ParseLiteralNode<StringLiteral>() : parseName();
    }

    private INamedImportsOrExports ParseNamedImportsOrExports(SyntaxKind kind)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();

        // NamedImports:
        //  { }
        //  { ImportsList }
        //  { ImportsList, }

        // ImportsList:
        //  ImportSpecifier
        //  ImportsList, ImportSpecifier
        INamedImportsOrExports node = kind == SyntaxKind.NamedImports
            ? new NamedImports(ParseBracketedList(
                ParsingContext.ImportOrExportSpecifiers,
                ParseImportSpecifier,
                SyntaxKind.OpenBraceToken,
                SyntaxKind.CloseBraceToken))
            : new NamedExports(ParseBracketedList(
                ParsingContext.ImportOrExportSpecifiers,
                ParseExportSpecifier,
                SyntaxKind.OpenBraceToken,
                SyntaxKind.CloseBraceToken));

        return FinishNode(node, pos);
    }

    private ExportSpecifier ParseExportSpecifier()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var hasJSDoc = HasPrecedingJSDocComment();

        return WithJSDoc((ParseImportOrExportSpecifier(SyntaxKind.ExportSpecifier) as ExportSpecifier)!, hasJSDoc);
    }

    private ImportSpecifier ParseImportSpecifier()
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return (ParseImportOrExportSpecifier(SyntaxKind.ImportSpecifier) as ImportSpecifier)!;
    }

    private IImportOrExportSpecifier ParseImportOrExportSpecifier(SyntaxKind kind)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var pos = GetNodePos();
        // ImportSpecifier:
        //   BindingIdentifier
        //   ModuleExportName as BindingIdentifier
        // ExportSpecifier:
        //   ModuleExportName
        //   ModuleExportName as ModuleExportName
        var checkIdentifierIsKeyword = IsKeyword(Token()) && !IsIdentifier();
        var checkIdentifierStart = _scanner.GetTokenStart();
        var checkIdentifierEnd = _scanner.GetTokenEnd();
        var isTypeOnly = false;
        IModuleExportName? propertyName = null;
        var CanParseAsKeyword = true;
        var name = ParseModuleExportName(() => ParseIdentifierName());
        if (name is Identifier identifier && identifier.EscapedText == "type")
        {
            // If the first token of an import specifier is 'type', there are a lot of possibilities,
            // especially if we see 'as' afterwards:
            //
            // import { type } from "mod";          - isTypeOnly: false,   name: type
            // import { type as } from "mod";       - isTypeOnly: true,    name: as
            // import { type as as } from "mod";    - isTypeOnly: false,   name: as,    propertyName: type
            // import { type as as as } from "mod"; - isTypeOnly: true,    name: as,    propertyName: as
            if (Token() == SyntaxKind.AsKeyword)
            {
                // { type as ...? }
                var firstAs = ParseIdentifierName();
                if (Token() == SyntaxKind.AsKeyword)
                {
                    // { type as as ...? }
                    var secondAs = ParseIdentifierName();
                    if (CanParseModuleExportName())
                    {
                        // { type as as something }
                        isTypeOnly = true;
                        propertyName = firstAs;
                        name = ParseModuleExportName(() => ParseNameWithKeywordCheck());
                        CanParseAsKeyword = false;
                    }
                    else
                    {
                        // { type as as }
                        propertyName = name;
                        name = secondAs;
                        CanParseAsKeyword = false;
                    }
                }
                else if (CanParseModuleExportName())
                {
                    // { type as "something" }
                    propertyName = name;
                    CanParseAsKeyword = false;
                    name = ParseModuleExportName(() => ParseNameWithKeywordCheck());
                }
                else
                {
                    // { type as }
                    isTypeOnly = true;
                    name = firstAs;
                }
            }
            else if (CanParseModuleExportName())
            {
                // { type "something" ...? }
                isTypeOnly = true;
                name = ParseModuleExportName(() => ParseNameWithKeywordCheck());
            }
        }

        if (CanParseAsKeyword && Token() == SyntaxKind.AsKeyword)
        {
            propertyName = name;
            ParseExpected(SyntaxKind.AsKeyword);
            name = ParseModuleExportName(() => ParseNameWithKeywordCheck());
        }

        if (kind == SyntaxKind.ImportSpecifier)
        {
            if (name.Kind != SyntaxKind.Identifier)
            {
                // ImportSpecifier casts "name" to Identifier below, so make sure it's an identifier
                ParseErrorAt(SkipTrivia(_sourceText!, name.Pos ?? 0) ?? 0, name.End ?? 0, Diagnostics._identifierExpected);
                var missingNode = CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false);
                SetTextRangePosEnd(missingNode, name.Pos ?? 0, name.Pos ?? 0);
                name = missingNode;
            }
            else if (checkIdentifierIsKeyword)
            {
                ParseErrorAt(checkIdentifierStart, checkIdentifierEnd, Diagnostics._identifierExpected);
            }
        }

        IImportOrExportSpecifier node = kind == SyntaxKind.ImportSpecifier
            ? new ImportSpecifier(isTypeOnly, propertyName, (name as Identifier)!)
            : new ExportSpecifier(isTypeOnly, propertyName, name);

        return FinishNode(node, pos);

        Identifier ParseNameWithKeywordCheck()
        {
            checkIdentifierIsKeyword = IsKeyword(Token()) && !IsIdentifier();
            checkIdentifierStart = _scanner.GetTokenStart();
            checkIdentifierEnd = _scanner.GetTokenEnd();

            return ParseIdentifierName();
        }
    }

    private NamespaceExport ParseNamespaceExport(int pos)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        return FinishNode(new NamespaceExport(ParseModuleExportName(() => ParseIdentifierName())), pos);
    }

    private ExportDeclaration ParseExportDeclaration(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var savedAwaitContext = InAwaitContext();
        SetAwaitContext(val: true);
        INamedExportBindings? exportClause = null;
        IExpression? moduleSpecifier = null;
        ImportAttributes? attributes = null;
        var isTypeOnly = ParseOptional(SyntaxKind.TypeKeyword);
        var namespaceExportPos = GetNodePos();
        if (ParseOptional(SyntaxKind.AsteriskToken))
        {
            if (ParseOptional(SyntaxKind.AsKeyword))
            {
                exportClause = ParseNamespaceExport(namespaceExportPos);
            }

            ParseExpected(SyntaxKind.FromKeyword);
            moduleSpecifier = ParseModuleSpecifier();
        }
        else
        {
            exportClause = (INamedExportBindings)ParseNamedImportsOrExports(SyntaxKind.NamedExports);
            // It is not uncommon to accidentally omit the 'from' keyword. Additionally, in editing scenarios,
            // the 'from' keyword can be parsed as a named export when the export clause is unterminated (i.e. `export { from "moduleName";`)
            // If we don't have a 'from' keyword, see if we have a string literal such that ASI won't take effect.
            if (Token() == SyntaxKind.FromKeyword || (Token() == SyntaxKind.StringLiteral &&
                !_scanner.HasPrecedingLineBreak()))
            {
                ParseExpected(SyntaxKind.FromKeyword);
                moduleSpecifier = ParseModuleSpecifier();
            }
        }

        var currentToken = Token();
        if (moduleSpecifier != null && (currentToken == SyntaxKind.WithKeyword ||
            currentToken == SyntaxKind.AssertKeyword) && !_scanner.HasPrecedingLineBreak())
        {
            attributes = ParseImportAttributes(currentToken);
        }

        ParseSemicolon();
        SetAwaitContext(savedAwaitContext);
        var node = new ExportDeclaration(modifiers, isTypeOnly, exportClause, moduleSpecifier, attributes);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ExportAssignment ParseExportAssignment(int pos, bool hasJSDoc, NodeArray<IModifierLike> modifiers)
    {
        using var recursion = _budget.Enter(_scanner.GetTokenStart());

        var savedAwaitContext = InAwaitContext();
        SetAwaitContext(val: true);
        bool isExportEquals = false;
        if (ParseOptional(SyntaxKind.EqualsToken))
        {
            isExportEquals = true;
        }
        else
        {
            ParseExpected(SyntaxKind.DefaultKeyword);
        }

        var expression = ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction: true);
        ParseSemicolon();
        SetAwaitContext(savedAwaitContext);
        var node = new ExportAssignment(modifiers, isExportEquals, expression);

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }
}
