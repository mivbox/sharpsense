namespace SharpSense.TypeScript.Scanning;

internal sealed partial class Scanner
{
    private string _text = null!;

    // Current position (end position of text of current token)
    private int _pos;

    // end of text
    private int _end;

    // Start position of whitespace before current token
    private int _fullStartPos;

    // Start position of text of current token
    private int _tokenStart;

    private SyntaxKind _token;
    private string _tokenValue = null!;
    private TokenFlags _tokenFlags;

    private List<CommentDirective>? _commentDirectives;
    private int _skipJsDocLeadingAsterisks;

    private ScriptKind _scriptKind = ScriptKind.Unknown;
    private JSDocParsingMode _jsDocParsingMode = JSDocParsingMode.ParseAll;

    private ScriptTarget _languageVersion = ScriptTarget.Latest;
    private LanguageVariant _languageVariant;
    private readonly bool _skipTrivia;

    private ErrorCallback? _onError;
    private readonly ParseBudget _budget;
    private readonly CancellationToken _cancellationToken;

    public Scanner(
        ScriptTarget languageVersion,
        bool skipTrivia,
        LanguageVariant languageVariant = LanguageVariant.Standard,
        string? textInitial = null,
        ErrorCallback? onError = null,
        int? start = null,
        int? length = null,
        ParseBudget? budget = null)
    {
        _budget = budget ?? new ParseBudget(new TypeScriptParseOptions(), CancellationToken.None);
        _cancellationToken = _budget.CancellationToken;
        _languageVersion = languageVersion;
        _skipTrivia = skipTrivia;
        _languageVariant = languageVariant;
        SetText(textInitial, start, length);
        _onError = onError;
    }

    public SyntaxKind GetToken() => _token;

    public int GetTokenFullStart() => _fullStartPos;

    public int GetTokenStart() => _tokenStart;

    public int GetTokenEnd() => _pos;

    public string GetTokenText() => _text[_tokenStart.._pos];

    public string GetTokenValue() => _tokenValue;

    public bool HasUnicodeEscape() => (_tokenFlags & TokenFlags.UnicodeEscape) != 0;

    public bool HasExtendedUnicodeEscape() => (_tokenFlags & TokenFlags.ExtendedUnicodeEscape) != 0;

    public bool HasPrecedingLineBreak() => (_tokenFlags & TokenFlags.PrecedingLineBreak) != 0;

    internal bool HasPrecedingJSDocComment() => (_tokenFlags & TokenFlags.PrecedingJSDocComment) != 0;

    internal bool HasPrecedingJSDocLeadingAsterisks() => (_tokenFlags & TokenFlags.PrecedingJSDocLeadingAsterisks) != 0;

    public bool IsIdentifier() => _token == SyntaxKind.Identifier || _token > SyntaxKindMarker.LastReservedWord;

    public bool IsReservedWord() => _token >= SyntaxKindMarker.FirstReservedWord && _token <= SyntaxKindMarker.LastReservedWord;

    public bool IsUnterminated() => (_tokenFlags & TokenFlags.Unterminated) != 0;

    internal TokenFlags GetNumericLiteralFlags() => _tokenFlags & TokenFlags.NumericLiteralFlags;

    internal IEnumerable<CommentDirective>? GetCommentDirectives() => _commentDirectives;

    internal TokenFlags GetTokenFlags() => _tokenFlags;

    private void Error(DiagnosticMessage message, int? errPos = null, int length = 0, object? arg0 = null)
    {
        if (_onError != null)
        {
            var oldPos = _pos;
            _pos = errPos ?? _pos;
            _onError?.Invoke(message, length, arg0);
            _pos = oldPos;
        }
    }

    private string ScanNumberFragment()
    {
        var start = _pos;
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        var result = "";
        while (_pos < _end)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var ch = _text[_pos];
            if (ch == CharacterCodes.Underscore)
            {
                _tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                    result += _text[start.._pos];
                }
                else
                {
                    _tokenFlags |= TokenFlags.ContainsInvalidSeparator;
                    if (isPreviousTokenSeparator)
                    {
                        Error(Diagnostics._multipleConsecutiveNumericSeparatorsAreNotPermitted, _pos, 1);
                    }
                    else
                    {
                        Error(Diagnostics._numericSeparatorsAreNotAllowedHere, _pos, 1);
                    }
                }

                _pos++;
                start = _pos;
                continue;
            }

            if (IsDigit(ch))
            {
                allowSeparator = true;
                isPreviousTokenSeparator = false;
                _pos++;
                continue;
            }

            break;
        }

        if (_pos > 0 && _text[_pos - 1] == CharacterCodes.Underscore)
        {
            _tokenFlags |= TokenFlags.ContainsInvalidSeparator;
            Error(Diagnostics._numericSeparatorsAreNotAllowedHere, _pos - 1, 1);
        }

        return result + _text[start.._pos];
    }

    // Extract from Section 12.9.3
    // NumericLiteral ::=
    //     | DecimalLiteral
    //     | DecimalBigIntegerLiteral
    //     | NonDecimalIntegerLiteral 'n'?
    //     | LegacyOctalIntegerLiteral
    // DecimalBigIntegerLiteral ::=
    //     | '0n'
    //     | [1-9] DecimalDigits? 'n'
    //     | [1-9] '_' DecimalDigits 'n'
    // DecimalLiteral ::=
    //     | DecimalIntegerLiteral? '.' DecimalDigits? ExponentPart?
    //     | '.' DecimalDigits ExponentPart?
    //     | DecimalIntegerLiteral ExponentPart?
    // DecimalIntegerLiteral ::=
    //     | '0'
    //     | [1-9] '_'? DecimalDigits
    //     | NonOctalDecimalIntegerLiteral
    // LegacyOctalIntegerLiteral ::= '0' [0-7]+
    // NonOctalDecimalIntegerLiteral ::= '0' [0-7]* [89] [0-9]*
    private SyntaxKind ScanNumber()
    {
        var start = _pos;
        string mainFragment;
        if (_text[_pos] == CharacterCodes.Digit0)
        {
            _pos++;
            if (_text[_pos] == CharacterCodes.Underscore)
            {
                _tokenFlags |= TokenFlags.ContainsSeparator | TokenFlags.ContainsInvalidSeparator;
                Error(Diagnostics._numericSeparatorsAreNotAllowedHere, _pos, 1);
                // treat it as a normal number literal
                _pos--;
                mainFragment = ScanNumberFragment();
            }
            // Separators are not allowed in the below cases
            else if (!ScanDigits())
            {
                // NonOctalDecimalIntegerLiteral, emit error later
                // Separators in decimal and exponent parts are still allowed according to the spec
                _tokenFlags |= TokenFlags.ContainsLeadingZero;
                mainFragment = "" + (long.TryParse(_tokenValue, out var v) ? v.ToString(CultureInfo.InvariantCulture) : _tokenValue);
            }
            else if (string.IsNullOrEmpty(_tokenValue))
            {
                // a single zero
                mainFragment = "0";
            }
            else
            {
                // LegacyOctalIntegerLiteral
                var value = Convert.ToInt64(_tokenValue, 8);
                _tokenValue = "" + value.ToString(CultureInfo.InvariantCulture);
                _tokenFlags |= TokenFlags.Octal;
                var withMinus = _token == SyntaxKind.MinusToken;
                var literal = (withMinus ? "-" : "") + "0o" + Convert.ToString(value, 8);
                if (withMinus)
                {
                    start--;
                }

                Error(Diagnostics._octalLiteralsAreNotAllowedUseTheSyntax0, start, _pos - start, literal);

                return SyntaxKind.NumericLiteral;
            }
        }
        else
        {
            mainFragment = ScanNumberFragment();
        }

        string? decimalFragment = null;
        string? scientificFragment = null;
        if (_pos < _end && _text[_pos] == CharacterCodes.Dot)
        {
            _pos++;
            decimalFragment = ScanNumberFragment();
        }

        var endPos = _pos;
        if (_pos < _end && (_text[_pos] == CharacterCodes.UppercaseE || _text[_pos] == CharacterCodes.LowercaseE))
        {
            _pos++;
            _tokenFlags |= TokenFlags.Scientific;
            if (_text[_pos] == CharacterCodes.Plus || _text[_pos] == CharacterCodes.Minus)
            {
                _pos++;
            }

            var preNumericPart = _pos;
            var finalFragment = ScanNumberFragment();
            if (string.IsNullOrEmpty(finalFragment))
            {
                Error(Diagnostics._digitExpected);
            }
            else
            {
                scientificFragment = _text[endPos..preNumericPart] + finalFragment;
                endPos = _pos;
            }
        }

        string result;
        if ((_tokenFlags & TokenFlags.ContainsSeparator) != 0)
        {
            result = mainFragment;
            if (!string.IsNullOrEmpty(decimalFragment))
            {
                result += "." + decimalFragment;
            }

            if (!string.IsNullOrEmpty(scientificFragment))
            {
                result += scientificFragment;
            }
        }
        else
        {
            result = _text[start..endPos]; // No need to use all the fragments; no _ removal needed
        }

        if ((_tokenFlags & TokenFlags.ContainsLeadingZero) != 0)
        {
            Error(Diagnostics._decimalsWithLeadingZerosAreNotAllowed, start, endPos - start);
            // if a literal has a leading zero, it must not be bigint
            _tokenValue = "" + result;

            return SyntaxKind.NumericLiteral;
        }

        if (decimalFragment != null || (_tokenFlags & TokenFlags.Scientific) != 0)
        {
            CheckForIdentifierStartAfterNumericLiteral(
                start,
                decimalFragment == null && (_tokenFlags & TokenFlags.Scientific) != 0);
            // if value is not an integer, it can be safely coerced to a number
            _tokenValue = double.Parse(result, CultureInfo.InvariantCulture).ToString().ToLowerInvariant();

            return SyntaxKind.NumericLiteral;
        }
        else
        {
            _tokenValue = result;
            var type = CheckBigIntSuffix(); // if value is an integer, check whether it is a bigint
            CheckForIdentifierStartAfterNumericLiteral(start);

            return type;
        }
    }

    private void CheckForIdentifierStartAfterNumericLiteral(int numericStart, bool isScientific = false)
    {
        if (_pos >= _end || !IsIdentifierStart(_text[_pos], _languageVersion))
        {
            return;
        }

        var identifierStart = _pos;
        var length = ScanIdentifierParts().Length;

        if (length == 1 && _text[identifierStart] == CharacterCodes.LineFeed)
        {
            if (isScientific)
            {
                Error(
                    Diagnostics._aBigintLiteralCannotUseExponentialNotation,
                    numericStart,
                    identifierStart - numericStart + 1);
            }
            else
            {
                Error(Diagnostics._aBigintLiteralMustBeAnInteger, numericStart, identifierStart - numericStart + 1);
            }
        }
        else
        {
            Error(Diagnostics._anIdentifierOrKeywordCannotImmediatelyFollowANumericLiteral, identifierStart, length);
            _pos = identifierStart;
        }
    }

    private bool ScanDigits()
    {
        var start = _pos;
        var isOctal = true;
        while (_pos < _end && IsDigit(_text[_pos]))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!IsOctalDigit(_text[_pos]))
            {
                isOctal = false;
            }

            _pos++;
        }

        _tokenValue = _text[start.._pos];

        return isOctal;
    }

    // Scans the given number of hexadecimal digits in the text,
    // returning -1 if the given number is unavailable.
    private long ScanExactNumberOfHexDigits(int count, bool canHaveSeparators)
    {
        var valueString = ScanHexDigits(minCount: count, scanAsManyAsPossible: false, canHaveSeparators);

        return !string.IsNullOrEmpty(valueString) ? Convert.ToInt64(valueString, 16) : -1;
    }

    // Scans as many hexadecimal digits as are available in the text,
    // returning "" if the given number of digits was unavailable.
    private string ScanMinimumNumberOfHexDigits(int count, bool canHaveSeparators)
    {
        return ScanHexDigits(minCount: count, scanAsManyAsPossible: true, canHaveSeparators);
    }

    private string ScanHexDigits(int minCount, bool scanAsManyAsPossible, bool canHaveSeparators)
    {
        var valueChars = new StringBuilder();
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        while ((valueChars.Length < minCount || scanAsManyAsPossible) && _pos < _end)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var ch = _text[_pos];
            if (canHaveSeparators && ch == CharacterCodes.Underscore)
            {
                _tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                }
                else if (isPreviousTokenSeparator)
                {
                    Error(Diagnostics._multipleConsecutiveNumericSeparatorsAreNotPermitted, _pos, 1);
                }
                else
                {
                    Error(Diagnostics._numericSeparatorsAreNotAllowedHere, _pos, 1);
                }

                _pos++;
                continue;
            }

            allowSeparator = canHaveSeparators;
            if (ch >= CharacterCodes.UppercaseA && ch <= CharacterCodes.UppercaseF)
            {
                ch = (char)(ch + CharacterCodes.LowercaseA - CharacterCodes.UppercaseA); // standardize hex literals to lowercase
            }
            else if (
                !((ch >= CharacterCodes.Digit0 && ch <= CharacterCodes.Digit9) ||
                    (ch >= CharacterCodes.LowercaseA && ch <= CharacterCodes.LowercaseF))
            )
            {
                break;
            }

            valueChars.Append(ch);
            _pos++;
            isPreviousTokenSeparator = false;
        }

        if (valueChars.Length < minCount)
        {
            valueChars.Clear();
        }

        if (_pos > 0 && _text[_pos - 1] == CharacterCodes.Underscore)
        {
            Error(Diagnostics._numericSeparatorsAreNotAllowedHere, _pos - 1, 1);
        }

        return valueChars.ToString();
    }

    private string ScanString(bool jsxAttributeString = false)
    {
        var quote = _text[_pos];
        _pos++;
        StringBuilder? result = null;
        var start = _pos;
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_pos >= _end)
            {
                _tokenFlags |= TokenFlags.Unterminated;
                Error(Diagnostics._unterminatedStringLiteral);
                if (result is null)
                {
                    return _text[start.._pos];
                }

                result.Append(_text, start, _pos - start);
                break;
            }

            var ch = _text[_pos];
            if (ch == quote)
            {
                if (result is null)
                {
                    return _text[start.._pos++];
                }

                result.Append(_text, start, _pos - start);
                _pos++;
                break;
            }

            if (ch == CharacterCodes.Backslash && !jsxAttributeString)
            {
                result ??= new();
                result.Append(_text, start, _pos - start);
                ScanEscapeSequence(result, EscapeSequenceScanningFlags.String | EscapeSequenceScanningFlags.ReportErrors);
                start = _pos;
                continue;
            }

            if ((ch == CharacterCodes.LineFeed || ch == CharacterCodes.CarriageReturn) &&
                !jsxAttributeString)
            {
                _tokenFlags |= TokenFlags.Unterminated;
                Error(Diagnostics._unterminatedStringLiteral);
                if (result is null)
                {
                    return _text[start.._pos];
                }

                result.Append(_text, start, _pos - start);
                break;
            }

            _pos++;
        }

        return result.ToString();
    }

    // Sets the current 'tokenValue' and returns a NoSubstitutionTemplateLiteral or
    // a literal component of a TemplateExpression.
    private SyntaxKind ScanTemplateAndSetTokenValue(bool shouldEmitInvalidEscapeError)
    {
        var startedWithBacktick = _text[_pos] == CharacterCodes.Backtick;

        _pos++;
        var start = _pos;
        var contents = new StringBuilder();
        SyntaxKind resultingToken;

        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_pos >= _end)
            {
                contents.Append(_text, start, _pos - start);
                _tokenFlags |= TokenFlags.Unterminated;
                Error(Diagnostics._unterminatedTemplateLiteral);
                resultingToken = startedWithBacktick ? SyntaxKind.NoSubstitutionTemplateLiteral : SyntaxKind.TemplateTail;
                break;
            }

            var currChar = _text[_pos];

            // '`'
            if (currChar == CharacterCodes.Backtick)
            {
                contents.Append(_text, start, _pos - start);
                _pos++;
                resultingToken = startedWithBacktick ? SyntaxKind.NoSubstitutionTemplateLiteral : SyntaxKind.TemplateTail;
                break;
            }

            // '${'
            if (currChar == CharacterCodes.Dollar && _pos + 1 < _end && _text[_pos + 1] == CharacterCodes.OpenBrace)
            {
                contents.Append(_text, start, _pos - start);
                _pos += 2;
                resultingToken = startedWithBacktick ? SyntaxKind.TemplateHead : SyntaxKind.TemplateMiddle;
                break;
            }

            // Escape character
            if (currChar == CharacterCodes.Backslash)
            {
                contents.Append(_text, start, _pos - start);
                ScanEscapeSequence(
                    contents,
                    EscapeSequenceScanningFlags.String | (shouldEmitInvalidEscapeError ? EscapeSequenceScanningFlags.ReportErrors : 0));
                start = _pos;
                continue;
            }

            // Speculated ECMAScript 6 Spec 11.8.6.1:
            // <CR><LF> and <CR> LineTerminatorSequences are normalized to <LF> for Template Values
            if (currChar == CharacterCodes.CarriageReturn)
            {
                contents.Append(_text, start, _pos - start);
                _pos++;

                if (_pos < _end && _text[_pos] == CharacterCodes.LineFeed)
                {
                    _pos++;
                }

                contents.Append('\n');
                start = _pos;
                continue;
            }

            _pos++;
        }

        Debug.Assert(resultingToken != SyntaxKind.Unknown);

        _tokenValue = contents.ToString();

        return resultingToken;
    }

    // Extract from Section A.1
    // EscapeSequence ::
    //     | CharacterEscapeSequence
    //     | 0 (?![0-9])
    //     | LegacyOctalEscapeSequence
    //     | NonOctalDecimalEscapeSequence
    //     | HexEscapeSequence
    //     | UnicodeEscapeSequence
    // LegacyOctalEscapeSequence ::=
    //     | '0' (?=[89])
    //     | [1-7] (?![0-7])
    //     | [0-3] [0-7] (?![0-7])
    //     | [4-7] [0-7]
    //     | [0-3] [0-7] [0-7]
    // NonOctalDecimalEscapeSequence ::= [89]
    private void ScanEscapeSequence(StringBuilder sb, EscapeSequenceScanningFlags flags)
    {
        var start = _pos;
        _pos++;
        if (_pos >= _end)
        {
            Error(Diagnostics._unexpectedEndOfText);

            return;
        }

        var ch = _text[_pos];
        _pos++;
        switch ((int)ch)
        {
            case CharacterCodes.Digit0:
                // Although '0' preceding any digit is treated as LegacyOctalEscapeSequence,
                // '\08' should separately be interpreted as '\0' + '8'.
                if (_pos >= _end || !IsDigit(_text[_pos]))
                {
                    sb.Append('\0');

                    return;
                }
                goto label_characterCodes_8_9;

            // '\01', '\011'
            // falls through
            case CharacterCodes.Digit1:
            case CharacterCodes.Digit2:
            case CharacterCodes.Digit3:
                // '\1', '\17', '\177'
                if (_pos < _end && IsOctalDigit(_text[_pos]))
                {
                    _pos++;
                }
                goto label_characterCodes_8_9;

            // '\17', '\177'
            // falls through
            case CharacterCodes.Digit4:
            case CharacterCodes.Digit5:
            case CharacterCodes.Digit6:
            case CharacterCodes.Digit7:
                // '\4', '\47' but not '\477'
                if (_pos < _end && IsOctalDigit(_text[_pos]))
                {
                    _pos++;
                }
                // '\47'
                _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                {
                    var code = Convert.ToUInt32(_text[(start + 1).._pos], 8);
                    if (flags.HasFlag(EscapeSequenceScanningFlags.RegularExpression) &&
                        !flags.HasFlag(EscapeSequenceScanningFlags.AtomEscape) &&
                        ch != CharacterCodes.Digit0)
                    {
                        Error(
                            Diagnostics._octalEscapeSequencesAndBackreferencesAreNotAllowedInACharacterClassIfThisWasIntendedAsAnEscapeSequenceUseTheSyntax0Instead,
                            start,
                            _pos - start,
                            "\\x" + Convert.ToString(code, 16).PadLeft(2, '0'));
                    }
                    else
                    {
                        Error(
                            Diagnostics._octalEscapeSequencesAreNotAllowedUseTheSyntax0,
                            start,
                            _pos - start,
                            "\\x" + Convert.ToString(code, 16).PadLeft(2, '0'));
                    }

                    sb.Append((char)code);

                    return;
                }
                sb.Append(_text[start.._pos]);
                return;

            case CharacterCodes.Digit8:
            case CharacterCodes.Digit9:
            label_characterCodes_8_9:
                // the invalid '\8' and '\9'
                _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                {
                    if (flags.HasFlag(EscapeSequenceScanningFlags.RegularExpression) &&
                        !flags.HasFlag(EscapeSequenceScanningFlags.AtomEscape))
                    {
                        Error(Diagnostics._decimalEscapeSequencesAndBackreferencesAreNotAllowedInACharacterClass, start, _pos - start);
                    }
                    else
                    {
                        Error(Diagnostics._escapeSequence0IsNotAllowed, start, _pos - start, _text[start.._pos]);
                    }

                    sb.Append(ch);

                    return;
                }
                sb.Append(_text[start.._pos]);
                return;

            case CharacterCodes.LowercaseB:
                sb.Append('\b');
                return;

            case CharacterCodes.LowercaseT:
                sb.Append('\t');
                return;

            case CharacterCodes.LowercaseN:
                sb.Append('\n');
                return;

            case CharacterCodes.LowercaseV:
                sb.Append('\v');
                return;

            case CharacterCodes.LowercaseF:
                sb.Append('\f');
                return;

            case CharacterCodes.LowercaseR:
                sb.Append('\r');
                return;

            case CharacterCodes.SingleQuote:
                sb.Append('\'');
                return;

            case CharacterCodes.DoubleQuote:
                sb.Append('"');
                return;

            case CharacterCodes.LowercaseU:
                if (_pos < _end && _text[_pos] == CharacterCodes.OpenBrace)
                {
                    // '\u{DDDDDD}'
                    _pos -= 2;
                    var result = ScanExtendedUnicodeEscape((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0);
                    if ((flags & EscapeSequenceScanningFlags.AllowExtendedUnicodeEscape) == 0)
                    {
                        _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(
                                Diagnostics._unicodeEscapeSequencesAreOnlyAvailableWhenTheUnicodeUFlagOrTheUnicodeSetsVFlagIsSet,
                                start,
                                _pos - start);
                        }
                    }

                    sb.Append(result);

                    return;
                }
                // '\uDDDD'
                for (; _pos < start + 6; _pos++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (!(_pos < _end && IsHexDigit(_text[_pos])))
                    {
                        _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(Diagnostics._hexadecimalDigitExpected);
                        }

                        sb.Append(_text[start.._pos]);

                        return;
                    }
                }
                _tokenFlags |= TokenFlags.UnicodeEscape;
                var escapedValue = Convert.ToUInt32(_text[(start + 2).._pos], 16);
                var escapedValueString = (char)escapedValue;
                if (flags.HasFlag(EscapeSequenceScanningFlags.AnyUnicodeMode) &&
                    escapedValue >= 0xD800 && escapedValue <= 0xDBFF &&
                    _pos + 6 < _end && _text[_pos..(_pos + 2)] == "\\u" &&
                    _text[_pos + 2] != CharacterCodes.OpenBrace)
                {
                    // For regular expressions in any Unicode mode, \u HexLeadSurrogate \u HexTrailSurrogate is treated as a single character
                    // for the purpose of determining whether a character class range is out of order
                    // https://tc39.es/ecma262/#prod-RegExpUnicodeEscapeSequence
                    var nextStart = _pos;
                    var nextPos = _pos + 2;
                    for (; nextPos < nextStart + 6; nextPos++)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        if (!IsHexDigit(_text[nextPos]))
                        {
                            // leave the error to the next call
                            sb.Append(escapedValueString);

                            return;
                        }
                    }

                    var nextEscapedValue = Convert.ToUInt32(_text[(nextStart + 2)..nextPos], 16);
                    if (nextEscapedValue >= 0xDC00 && nextEscapedValue <= 0xDFFF)
                    {
                        _pos = nextPos;
                        sb.Append(escapedValueString + (char)nextEscapedValue);

                        return;
                    }
                }
                sb.Append(escapedValueString);
                return;

            case CharacterCodes.LowercaseX:
                // '\xDD'
                for (; _pos < start + 4; _pos++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (!(_pos < _end && IsHexDigit(_text[_pos])))
                    {
                        _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(Diagnostics._hexadecimalDigitExpected);
                        }

                        sb.Append(_text[start.._pos]);

                        return;
                    }
                }
                _tokenFlags |= TokenFlags.HexEscape;
                sb.Append((char)Convert.ToUInt32(_text[(start + 2).._pos], 16));
                return;

            // when encountering a LineContinuation (i.e. a backslash and a line terminator sequence),
            // the line terminator is interpreted to be "the empty code unit sequence".
            case CharacterCodes.CarriageReturn:
                if (_pos < _end && _text[_pos] == CharacterCodes.LineFeed)
                {
                    _pos++;
                }
                return;
            // falls through
            case CharacterCodes.LineFeed:
            case CharacterCodes.LineSeparator:
            case CharacterCodes.ParagraphSeparator:
                return;
            default:
                if (flags.HasFlag(EscapeSequenceScanningFlags.AnyUnicodeMode)
                    || flags.HasFlag(EscapeSequenceScanningFlags.RegularExpression)
                        && !(flags.HasFlag(EscapeSequenceScanningFlags.AnnexB))
                        && IsIdentifierPart(ch, _languageVersion))
                {
                    Error(Diagnostics._thisCharacterCannotBeEscapedInARegularExpression, _pos - 2, 2);
                }
                sb.Append(ch);
                return;
        }
    }

    private string ScanExtendedUnicodeEscape(bool shouldEmitInvalidEscapeError)
    {
        var start = _pos;
        _pos += 3;
        var escapedStart = _pos;
        var escapedValueString = ScanMinimumNumberOfHexDigits(1, /*canHaveSeparators*/ false);
        var escapedValue = !string.IsNullOrEmpty(escapedValueString) ? Convert.ToInt32(escapedValueString, 16) : -1;
        var isInvalidExtendedEscape = false;

        // Validate the value of the digit
        if (escapedValue < 0)
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(Diagnostics._hexadecimalDigitExpected);
            }

            isInvalidExtendedEscape = true;
        }
        else if (escapedValue > 0x10FFFF)
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(
                    Diagnostics._anExtendedUnicodeEscapeValueMustBeBetween0x0And0x10FFFFInclusive,
                    escapedStart,
                    _pos - escapedStart);
            }

            isInvalidExtendedEscape = true;
        }

        if (_pos >= _end)
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(Diagnostics._unexpectedEndOfText);
            }

            isInvalidExtendedEscape = true;
        }
        else if (_text[_pos] == CharacterCodes.CloseBrace)
        {
            // Only swallow the following character up if it's a '}'.
            _pos++;
        }
        else
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(Diagnostics._unterminatedUnicodeEscapeSequence);
            }

            isInvalidExtendedEscape = true;
        }

        if (isInvalidExtendedEscape)
        {
            _tokenFlags |= TokenFlags.ContainsInvalidEscape;

            return _text[start.._pos];
        }

        _tokenFlags |= TokenFlags.ExtendedUnicodeEscape;

        return char.ConvertFromUtf32(escapedValue);
    }

    // Current character is known to be a backslash. Check for Unicode escape of the form '\uXXXX'
    // and return code point value if valid Unicode escape is found. Otherwise return -1.
    private int PeekUnicodeEscape()
    {
        if (_pos + 5 < _end && _text[_pos + 1] == CharacterCodes.LowercaseU)
        {
            var start = _pos;
            _pos += 2;
            var value = ScanExactNumberOfHexDigits(4, canHaveSeparators: false);
            _pos = start;

            return (int)value;
        }

        return -1;
    }

    private int PeekExtendedUnicodeEscape()
    {
        if (CodePointUnchecked(_pos + 1) == CharacterCodes.LowercaseU &&
            CodePointUnchecked(_pos + 2) == CharacterCodes.OpenBrace)
        {
            var start = _pos;
            _pos += 3;
            var escapedValueString = ScanMinimumNumberOfHexDigits(1, canHaveSeparators: false);
            var escapedValue = !string.IsNullOrEmpty(escapedValueString) ? Convert.ToInt32(escapedValueString, 16) : -1;
            _pos = start;

            return escapedValue;
        }

        return -1;
    }

    private string ScanIdentifierParts()
    {
        var result = new StringBuilder();
        var start = _pos;
        while (_pos < _end)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var ch = CodePointUnchecked(_pos);
            if (IsIdentifierPart(ch, _languageVersion))
            {
                _pos++;
            }
            else if (ch == CharacterCodes.Backslash)
            {
                ch = PeekExtendedUnicodeEscape();
                if (ch >= 0 && IsIdentifierPart(ch, _languageVersion))
                {
                    result.Append(ScanExtendedUnicodeEscape(shouldEmitInvalidEscapeError: true));
                    start = _pos;
                    continue;
                }

                ch = PeekUnicodeEscape();
                if (!(ch >= 0 && IsIdentifierPart(ch, _languageVersion)))
                {
                    break;
                }

                _tokenFlags |= TokenFlags.UnicodeEscape;
                result.Append(_text, start, _pos - start);
                result.Append(char.ConvertFromUtf32(ch));
                // Valid Unicode escape is always six characters
                _pos += 6;
                start = _pos;
            }
            else
            {
                break;
            }
        }

        result.Append(_text, start, _pos - start);

        return result.ToString();
    }

    private SyntaxKind GetIdentifierToken()
    {
        var len = _tokenValue.Length;
        if (len >= 2 && len <= 11)
        {
            var ch = _tokenValue[0];
            if (ch >= CharacterCodes.LowercaseA && ch <= CharacterCodes.LowercaseZ)
            {
                if (_textToToken.TryGetValue(_tokenValue, out SyntaxKind keyword))
                {
                    return _token = keyword;
                }
            }
        }

        return _token = SyntaxKind.Identifier;
    }

    private string ScanBinaryOrOctalDigits(int @base)
    {
        var value = new StringBuilder();
        // For counting number of digits; Valid binaryIntegerLiteral must have at least one binary digit following B or b.
        // Similarly valid octalIntegerLiteral must have at least one octal digit following o or O.
        var separatorAllowed = false;
        var isPreviousTokenSeparator = false;
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var ch = _text[_pos];
            // Numeric separators are allowed anywhere within a numeric literal, except not at the beginning, or following another separator
            if (ch == CharacterCodes.Underscore)
            {
                _tokenFlags |= TokenFlags.ContainsSeparator;
                if (separatorAllowed)
                {
                    separatorAllowed = false;
                    isPreviousTokenSeparator = true;
                }
                else if (isPreviousTokenSeparator)
                {
                    Error(Diagnostics._multipleConsecutiveNumericSeparatorsAreNotPermitted, _pos, 1);
                }
                else
                {
                    Error(Diagnostics._numericSeparatorsAreNotAllowedHere, _pos, 1);
                }

                _pos++;
                continue;
            }

            separatorAllowed = true;
            if (!IsDigit(ch) || ch - CharacterCodes.Digit0 >= @base)
            {
                break;
            }

            value.Append(_text[_pos]);
            _pos++;
            isPreviousTokenSeparator = false;
        }

        if (_pos > 0 && _text[_pos - 1] == CharacterCodes.Underscore)
        {
            // Literal ends with underscore - not allowed
            Error(Diagnostics._numericSeparatorsAreNotAllowedHere, _pos - 1, 1);
        }

        return value.ToString();
    }

    private SyntaxKind CheckBigIntSuffix()
    {
        if (_pos < _end && _text[_pos] == CharacterCodes.LowercaseN)
        {
            _tokenValue += "n";
            // Use base 10 instead of base 2 or base 8 for shorter literals
            if ((_tokenFlags & TokenFlags.BinaryOrOctalSpecifier) != 0)
            {
                _tokenValue = ParsePseudoBigInt(_tokenValue) + "n";
            }

            _pos++;

            return SyntaxKind.BigIntLiteral;
        }
        else
        {
            // not a bigint, so can convert to number in simplified form
            // Number() may not support 0b or 0o, so use parseInt() instead
            var numericValue = (_tokenFlags & TokenFlags.BinarySpecifier) != 0
                ? Convert.ToInt64(_tokenValue[2..], 2) // skip "0b"
                : (_tokenFlags & TokenFlags.OctalSpecifier) != 0
                ? Convert.ToInt64(_tokenValue[2..], 8) // skip "0o"
                : (_tokenValue.Length > 1 && _tokenValue[1] == 'x')
                ? Convert.ToInt64(_tokenValue[2..], 16)
                : double.Parse(_tokenValue);
            _tokenValue = "" + numericValue.ToString(CultureInfo.InvariantCulture);

            return SyntaxKind.NumericLiteral;
        }
    }

    public SyntaxKind Scan()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        _fullStartPos = _pos;
        _tokenFlags = TokenFlags.None;
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _tokenStart = _pos;
            if (_pos >= _end)
            {
                return _token = SyntaxKind.EndOfFileToken;
            }

            var ch = CodePointUnchecked(_pos);
            if (_pos == 0)
            {
                // Special handling for shebang
                if (ch == CharacterCodes.Hash && IsShebangTrivia(_text, _pos))
                {
                    _pos = ScanShebangTrivia(_text, _pos);
                    if (_skipTrivia)
                    {
                        continue;
                    }
                    else
                    {
                        return _token = SyntaxKind.ShebangTrivia;
                    }
                }
            }

            switch (ch)
            {
                case CharacterCodes.LineFeed:
                case CharacterCodes.CarriageReturn:
                    _tokenFlags |= TokenFlags.PrecedingLineBreak;
                    if (_skipTrivia)
                    {
                        _pos++;
                        continue;
                    }
                    else
                    {
                        if (ch == CharacterCodes.CarriageReturn && _pos + 1 < _end && _text[_pos + 1] == CharacterCodes.LineFeed)
                        {
                            // consume both CR and LF
                            _pos += 2;
                        }
                        else
                        {
                            _pos++;
                        }

                        return _token = SyntaxKind.NewLineTrivia;
                    }

                case CharacterCodes.Tab:
                case CharacterCodes.VerticalTab:
                case CharacterCodes.FormFeed:
                case CharacterCodes.Space:
                case CharacterCodes.NonBreakingSpace:
                case CharacterCodes.Ogham:
                case CharacterCodes.EnQuad:
                case CharacterCodes.EmQuad:
                case CharacterCodes.EnSpace:
                case CharacterCodes.EmSpace:
                case CharacterCodes.ThreePerEmSpace:
                case CharacterCodes.FourPerEmSpace:
                case CharacterCodes.SixPerEmSpace:
                case CharacterCodes.FigureSpace:
                case CharacterCodes.PunctuationSpace:
                case CharacterCodes.ThinSpace:
                case CharacterCodes.HairSpace:
                case CharacterCodes.ZeroWidthSpace:
                case CharacterCodes.NarrowNoBreakSpace:
                case CharacterCodes.MathematicalSpace:
                case CharacterCodes.IdeographicSpace:
                case CharacterCodes.ByteOrderMark:
                    if (_skipTrivia)
                    {
                        _pos++;
                        continue;
                    }
                    else
                    {
                        while (_pos < _end && IsWhiteSpaceSingleLine(_text[_pos]))
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            _pos++;
                        }

                        return _token = SyntaxKind.WhitespaceTrivia;
                    }

                case CharacterCodes.Exclamation:
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        if (_pos + 1 < _end && _text[_pos + 2] == CharacterCodes.Equals)
                        {
                            _pos += 3;

                            return _token = SyntaxKind.ExclamationEqualsEqualsToken;
                        }

                        _pos += 2;

                        return _token = SyntaxKind.ExclamationEqualsToken;
                    }
                    _pos++;
                    return _token = SyntaxKind.ExclamationToken;

                case CharacterCodes.DoubleQuote:
                case CharacterCodes.SingleQuote:
                    _tokenValue = ScanString();
                    return _token = SyntaxKind.StringLiteral;

                case CharacterCodes.Backtick:
                    return _token = ScanTemplateAndSetTokenValue(/*shouldEmitInvalidEscapeError*/ false);

                case CharacterCodes.Percent:
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.PercentEqualsToken;
                    }
                    _pos++;
                    return _token = SyntaxKind.PercentToken;

                case CharacterCodes.Ampersand:
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Ampersand)
                    {
                        if (_pos + 2 < _end && _text[_pos + 2] == CharacterCodes.Equals)
                        {
                            _pos += 3;

                            return _token = SyntaxKind.AmpersandAmpersandEqualsToken;
                        }

                        _pos += 2;

                        return _token = SyntaxKind.AmpersandAmpersandToken;
                    }
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.AmpersandEqualsToken;
                    }
                    _pos++;
                    return _token = SyntaxKind.AmpersandToken;

                case CharacterCodes.OpenParen:
                    _pos++;
                    return _token = SyntaxKind.OpenParenToken;

                case CharacterCodes.CloseParen:
                    _pos++;
                    return _token = SyntaxKind.CloseParenToken;

                case CharacterCodes.Asterisk:
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.AsteriskEqualsToken;
                    }
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Asterisk)
                    {
                        if (_pos + 2 < _end && _text[_pos + 2] == CharacterCodes.Equals)
                        {
                            _pos += 3;

                            return _token = SyntaxKind.AsteriskAsteriskEqualsToken;
                        }

                        _pos += 2;

                        return _token = SyntaxKind.AsteriskAsteriskToken;
                    }
                    _pos++;
                    if (_skipJsDocLeadingAsterisks > 0 &&
                        !_tokenFlags.HasFlag(TokenFlags.PrecedingJSDocLeadingAsterisks) &&
                        _tokenFlags.HasFlag(TokenFlags.PrecedingLineBreak))
                    {
                        // decoration at the start of a JSDoc comment line
                        _tokenFlags |= TokenFlags.PrecedingJSDocLeadingAsterisks;
                        continue;
                    }
                    return _token = SyntaxKind.AsteriskToken;

                case CharacterCodes.Plus:
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Plus)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.PlusPlusToken;
                    }
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.PlusEqualsToken;
                    }
                    _pos++;
                    return _token = SyntaxKind.PlusToken;

                case CharacterCodes.Comma:
                    _pos++;
                    return _token = SyntaxKind.CommaToken;

                case CharacterCodes.Minus:
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Minus)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.MinusMinusToken;
                    }
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.MinusEqualsToken;
                    }
                    _pos++;
                    return _token = SyntaxKind.MinusToken;

                case CharacterCodes.Dot:
                    if (_pos + 1 < _end && IsDigit(_text[_pos + 1]))
                    {
                        ScanNumber();

                        return _token = SyntaxKind.NumericLiteral;
                    }
                    if (_pos + 2 < _end && _text[_pos + 1] == CharacterCodes.Dot && _text[_pos + 2] == CharacterCodes.Dot)
                    {
                        _pos += 3;

                        return _token = SyntaxKind.DotDotDotToken;
                    }
                    _pos++;
                    return _token = SyntaxKind.DotToken;

                case CharacterCodes.Slash:
                    // Single-line comment
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Slash)
                    {
                        _pos += 2;

                        while (_pos < _end)
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            if (IsLineBreak(_text[_pos]))
                            {
                                break;
                            }

                            _pos++;
                        }

                        _commentDirectives = AppendIfCommentDirective(
                            _commentDirectives,
                            _text[_tokenStart.._pos],
                            _commentDirectiveRegExSingleLine,
                            _tokenStart
                        );

                        if (_skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return _token = SyntaxKind.SingleLineCommentTrivia;
                        }
                    }
                    // Multi-line comment
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Asterisk)
                    {
                        _pos += 2;
                        var isJSDoc = _pos < _end && _text[_pos] == CharacterCodes.Asterisk && (_pos + 1 >= _end || _text[_pos + 1] != CharacterCodes.Slash);

                        var commentClosed = false;
                        var lastLineStart = _tokenStart;
                        while (_pos < _end)
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            ch = _text[_pos];

                            if (ch == CharacterCodes.Asterisk && _pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Slash)
                            {
                                _pos += 2;
                                commentClosed = true;
                                break;
                            }

                            _pos++;

                            if (IsLineBreak(ch))
                            {
                                lastLineStart = _pos;
                                _tokenFlags |= TokenFlags.PrecedingLineBreak;
                            }
                        }

                        if (isJSDoc && ShouldParseJSDoc())
                        {
                            _tokenFlags |= TokenFlags.PrecedingJSDocComment;
                        }

                        _commentDirectives = AppendIfCommentDirective(
                            _commentDirectives,
                            _text[lastLineStart.._pos],
                            _commentDirectiveRegExMultiLine,
                            lastLineStart);

                        if (!commentClosed)
                        {
                            Error(Diagnostics._asteriskSlashExpected);
                        }

                        if (_skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            if (!commentClosed)
                            {
                                _tokenFlags |= TokenFlags.Unterminated;
                            }

                            return _token = SyntaxKind.MultiLineCommentTrivia;
                        }
                    }

                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.SlashEqualsToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.SlashToken;

                case CharacterCodes.Digit0:
                    if (_pos + 2 < _end && (_text[_pos + 1] == CharacterCodes.UppercaseX || _text[_pos + 1] == CharacterCodes.LowercaseX))
                    {
                        _pos += 2;
                        _tokenValue = ScanMinimumNumberOfHexDigits(1, canHaveSeparators: true);
                        if (string.IsNullOrEmpty(_tokenValue))
                        {
                            Error(Diagnostics._hexadecimalDigitExpected);
                            _tokenValue = "0";
                        }

                        _tokenValue = "0x" + _tokenValue;
                        _tokenFlags |= TokenFlags.HexSpecifier;

                        return _token = CheckBigIntSuffix();
                    }
                    else if (_pos + 2 < _end && (_text[_pos + 1] == CharacterCodes.UppercaseB || _text[_pos + 1] == CharacterCodes.LowercaseB))
                    {
                        _pos += 2;
                        _tokenValue = ScanBinaryOrOctalDigits(@base: 2);
                        if (string.IsNullOrEmpty(_tokenValue))
                        {
                            Error(Diagnostics._binaryDigitExpected);
                            _tokenValue = "0";
                        }

                        _tokenValue = "0b" + _tokenValue;
                        _tokenFlags |= TokenFlags.BinarySpecifier;

                        return _token = CheckBigIntSuffix();
                    }
                    else if (_pos + 2 < _end && (_text[_pos + 1] == CharacterCodes.UppercaseO || _text[_pos + 1] == CharacterCodes.LowercaseO))
                    {
                        _pos += 2;
                        _tokenValue = ScanBinaryOrOctalDigits(@base: 8);
                        if (string.IsNullOrEmpty(_tokenValue))
                        {
                            Error(Diagnostics._octalDigitExpected);
                            _tokenValue = "0";
                        }

                        _tokenValue = "0o" + _tokenValue;
                        _tokenFlags |= TokenFlags.OctalSpecifier;

                        return _token = CheckBigIntSuffix();
                    }
                    goto characterCodes1;

                // falls through
                case CharacterCodes.Digit1:
                case CharacterCodes.Digit2:
                case CharacterCodes.Digit3:
                case CharacterCodes.Digit4:
                case CharacterCodes.Digit5:
                case CharacterCodes.Digit6:
                case CharacterCodes.Digit7:
                case CharacterCodes.Digit8:
                case CharacterCodes.Digit9:
                characterCodes1:
                    return _token = ScanNumber();

                case CharacterCodes.Colon:
                    _pos++;
                    return _token = SyntaxKind.ColonToken;

                case CharacterCodes.Semicolon:
                    _pos++;
                    return _token = SyntaxKind.SemicolonToken;

                case CharacterCodes.LessThan:
                    if (IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScanConflictMarkerTrivia(_text, _pos, Error);
                        if (_skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return _token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.LessThan)
                    {
                        if (_pos + 2 < _end && _text[_pos + 2] == CharacterCodes.Equals)
                        {
                            _pos += 3;

                            return _token = SyntaxKind.LessThanLessThanEqualsToken;
                        }

                        _pos += 2;

                        return _token = SyntaxKind.LessThanLessThanToken;
                    }

                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.LessThanEqualsToken;
                    }

                    if (_languageVariant == LanguageVariant.JSX &&
                        _pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Slash &&
                        (_pos + 2 >= _end || _text[_pos + 2] != CharacterCodes.Asterisk))
                    {
                        _pos += 2;

                        return _token = SyntaxKind.LessThanSlashToken;
                    }
                    _pos++;
                    return _token = SyntaxKind.LessThanToken;

                case CharacterCodes.Equals:
                    if (IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScanConflictMarkerTrivia(_text, _pos, Error);
                        if (_skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return _token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        if (_pos + 2 < _end && _text[_pos + 2] == CharacterCodes.Equals)
                        {
                            _pos += 3;

                            return _token = SyntaxKind.EqualsEqualsEqualsToken;
                        }

                        _pos += 2;

                        return _token = SyntaxKind.EqualsEqualsToken;
                    }
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.GreaterThan)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.EqualsGreaterThanToken;
                    }
                    _pos++;
                    return _token = SyntaxKind.EqualsToken;

                case CharacterCodes.GreaterThan:
                    if (IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScanConflictMarkerTrivia(_text, _pos, Error);
                        if (_skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return _token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    _pos++;
                    return _token = SyntaxKind.GreaterThanToken;

                case CharacterCodes.Question:
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Dot && (_pos + 2 >= _end || !IsDigit(_text[_pos + 2])))
                    {
                        _pos += 2;

                        return _token = SyntaxKind.QuestionDotToken;
                    }

                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Question)
                    {
                        if (_pos + 2 < _end && _text[_pos + 2] == CharacterCodes.Equals)
                        {
                            _pos += 3;

                            return _token = SyntaxKind.QuestionQuestionEqualsToken;
                        }

                        _pos += 2;

                        return _token = SyntaxKind.QuestionQuestionToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.QuestionToken;

                case CharacterCodes.OpenBracket:
                    _pos++;
                    return _token = SyntaxKind.OpenBracketToken;

                case CharacterCodes.CloseBracket:
                    _pos++;
                    return _token = SyntaxKind.CloseBracketToken;

                case CharacterCodes.Caret:
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.CaretEqualsToken;
                    }
                    _pos++;
                    return _token = SyntaxKind.CaretToken;

                case CharacterCodes.OpenBrace:
                    _pos++;
                    return _token = SyntaxKind.OpenBraceToken;

                case CharacterCodes.Bar:
                    if (IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScanConflictMarkerTrivia(_text, _pos, Error);
                        if (_skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return _token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Bar)
                    {
                        if (_pos + 2 < _end && _text[_pos + 2] == CharacterCodes.Equals)
                        {
                            _pos += 3;

                            return _token = SyntaxKind.BarBarEqualsToken;
                        }

                        _pos += 2;

                        return _token = SyntaxKind.BarBarToken;
                    }
                    if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                    {
                        _pos += 2;

                        return _token = SyntaxKind.BarEqualsToken;
                    }
                    _pos++;
                    return _token = SyntaxKind.BarToken;

                case CharacterCodes.CloseBrace:
                    _pos++;
                    return _token = SyntaxKind.CloseBraceToken;

                case CharacterCodes.Tilde:
                    _pos++;
                    return _token = SyntaxKind.TildeToken;

                case CharacterCodes.At:
                    _pos++;
                    return _token = SyntaxKind.AtToken;

                case CharacterCodes.Backslash:
                    var extendedCookedChar = PeekExtendedUnicodeEscape();
                    if (extendedCookedChar >= 0 && IsIdentifierStart(extendedCookedChar, _languageVersion))
                    {
                        _tokenValue = ScanExtendedUnicodeEscape(shouldEmitInvalidEscapeError: true) + ScanIdentifierParts();

                        return _token = GetIdentifierToken();
                    }

                    var cookedChar = PeekUnicodeEscape();
                    if (cookedChar >= 0 && IsIdentifierStart(cookedChar, _languageVersion))
                    {
                        _pos += 6;
                        _tokenFlags |= TokenFlags.UnicodeEscape;
                        _tokenValue = (char)cookedChar + ScanIdentifierParts();

                        return _token = GetIdentifierToken();
                    }

                    Error(Diagnostics._invalidCharacter);
                    _pos++;
                    return _token = SyntaxKind.Unknown;

                case CharacterCodes.Hash:
                    if (_pos != 0 && _pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Exclamation)
                    {
                        Error(Diagnostics._canOnlyBeUsedAtTheStartOfAFile, _pos, 2);
                        _pos++;

                        return _token = SyntaxKind.Unknown;
                    }

                    var charAfterHash = CodePointUnchecked(_pos + 1);
                    if (charAfterHash == CharacterCodes.Backslash)
                    {
                        _pos++;
                        extendedCookedChar = PeekExtendedUnicodeEscape();
                        if (extendedCookedChar >= 0 && IsIdentifierStart((char)extendedCookedChar, _languageVersion))
                        {
                            _tokenValue = "#" + ScanExtendedUnicodeEscape(shouldEmitInvalidEscapeError: true) + ScanIdentifierParts();

                            return _token = SyntaxKind.PrivateIdentifier;
                        }

                        cookedChar = PeekUnicodeEscape();
                        if (cookedChar >= 0 && IsIdentifierStart(cookedChar, _languageVersion))
                        {
                            _pos += 6;
                            _tokenFlags |= TokenFlags.UnicodeEscape;
                            _tokenValue = "#" + (char)cookedChar + ScanIdentifierParts();

                            return _token = SyntaxKind.PrivateIdentifier;
                        }

                        _pos--;
                    }

                    if (IsIdentifierStart(charAfterHash, _languageVersion))
                    {
                        _pos++;
                        // We're relying on ScanIdentifier's behavior and adjusting the token kind after the fact.
                        // Notably absent from this block is the fact that calling a function named "scanIdentifier",
                        // but identifiers don't include '#', and that function doesn't deal with it at all.
                        // This works because 'scanIdentifier' tries to reuse source characters and builds up substrings;
                        // however, it starts at the 'tokenPos' which includes the '#', and will "accidentally" prepend the '#' for us.
                        ScanIdentifier(charAfterHash, _languageVersion);
                    }
                    else
                    {
                        _tokenValue = "#";
                        Error(Diagnostics._invalidCharacter, _pos++, CharSize(ch));
                    }
                    return _token = SyntaxKind.PrivateIdentifier;

                case CharacterCodes.ReplacementCharacter:
                    Error(Diagnostics._fileAppearsToBeBinary, 0, 0);
                    _pos = _end;
                    return _token = SyntaxKind.NonTextFileMarkerTrivia;

                default:
                    var identifierKind = ScanIdentifier(ch, _languageVersion);
                    if (identifierKind != SyntaxKind.Unknown)
                    {
                        return _token = identifierKind;
                    }
                    else if (IsWhiteSpaceSingleLine(ch))
                    {
                        _pos += CharSize(ch);
                        continue;
                    }
                    else if (IsLineBreak(ch))
                    {
                        _tokenFlags |= TokenFlags.PrecedingLineBreak;
                        _pos += CharSize(ch);
                        continue;
                    }
                    var size = CharSize(ch);
                    Error(Diagnostics._invalidCharacter, _pos, size);
                    _pos += size;
                    return _token = SyntaxKind.Unknown;
            }
        }
    }

    private bool ShouldParseJSDoc()
    {
        switch (_jsDocParsingMode)
        {
            case JSDocParsingMode.ParseAll:
                return true;
            case JSDocParsingMode.ParseNone:
                return false;
        }

        if (_scriptKind != ScriptKind.TS && _scriptKind != ScriptKind.TSX)
        {
            // If outside of TS, we need JSDoc to get any type info.
            return true;
        }

        if (_jsDocParsingMode == JSDocParsingMode.ParseForTypeInfo)
        {
            // If we're in TS, but we don't need to produce reliable errors,
            // we don't need to parse to find @see or @link.
            return false;
        }

        return _jsDocSeeOrLink.IsMatch(_text[_fullStartPos.._pos]);
    }

    public SyntaxKind ReScanInvalidIdentifier()
    {
        Debug.Assert(
            _token == SyntaxKind.Unknown,
            "'reScanInvalidIdentifier' should only be called when the current token is 'SyntaxKind.Unknown'.");
        _pos = _tokenStart = _fullStartPos;
        _tokenFlags = 0;
        var ch = CodePointUnchecked(_pos);
        var identifierKind = ScanIdentifier(ch, ScriptTarget.ESNext);
        if (identifierKind != SyntaxKind.Unknown)
        {
            return _token = identifierKind;
        }

        _pos += CharSize(ch);

        return _token; // Still `SyntaxKind.Unknown`
    }

    private SyntaxKind ScanIdentifier(int startCharacter, ScriptTarget languageVersion)
    {
        var ch = startCharacter;
        if (IsIdentifierStart(ch, languageVersion))
        {
            _pos += CharSize(ch);
            while (_pos < _end && IsIdentifierPart(ch = CodePointUnchecked(_pos), languageVersion))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _pos += CharSize(ch);
            }

            _tokenValue = _text[_tokenStart.._pos];
            if (ch == CharacterCodes.Backslash)
            {
                _tokenValue += ScanIdentifierParts();
            }

            return GetIdentifierToken();
        }

        return SyntaxKind.Unknown;
    }

    public SyntaxKind ReScanGreaterToken()
    {
        if (_token == SyntaxKind.GreaterThanToken)
        {
            if (_pos < _end && _text[_pos] == CharacterCodes.GreaterThan)
            {
                if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.GreaterThan)
                {
                    if (_text[_pos + 2] == CharacterCodes.Equals)
                    {
                        _pos += 3;

                        return _token = SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken;
                    }

                    _pos += 2;

                    return _token = SyntaxKind.GreaterThanGreaterThanGreaterThanToken;
                }

                if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Equals)
                {
                    _pos += 2;

                    return _token = SyntaxKind.GreaterThanGreaterThanEqualsToken;
                }

                _pos++;

                return _token = SyntaxKind.GreaterThanGreaterThanToken;
            }

            if (_pos < _end && _text[_pos] == CharacterCodes.Equals)
            {
                _pos++;

                return _token = SyntaxKind.GreaterThanEqualsToken;
            }
        }

        return _token;
    }

    public SyntaxKind ReScanAsteriskEqualsToken()
    {
        Debug.Assert(
            _token == SyntaxKind.AsteriskEqualsToken,
            "'reScanAsteriskEqualsToken' should only be called on a '*='");
        _pos = _tokenStart + 1;

        return _token = SyntaxKind.EqualsToken;
    }

    /*
    * Returns the code point for the character at the given position within `text`. This
    * should only be used when pos is guaranteed to be within the bounds of `text` as this
    * function does not perform bounds checks.
    */
    private int CodePointUnchecked(int pos)
    {
        return CodePointAt(_text, pos);
    }

    /*
     * Returns the code point for the character at the given position within `text`. If
     * `pos` is outside the bounds set for `text`, `CharacterCodes.EOF` is returned instead.
     */
    private int CodePointChecked(int pos)
    {
        return pos >= 0 && pos < _end ? CodePointAt(_text, pos) : CharacterCodes.EndOfFile;
    }

    /*
     * Returns the char code for the character at the given position within `text`. This
     * should only be used when pos is guaranteed to be within the bounds of `text` as this
     * function does not perform bounds checks.
     */
    private char CharCodeUnchecked(int pos)
    {
        return _text[pos];
    }

    /*
     * Returns the char code for the character at the given position within `text`. If
     * `pos` is outside the bounds set for `text`, `CharacterCodes.EOF` is returned instead.
     */
    private char CharCodeChecked(int pos)
    {
        if (pos >= 0 && pos < _end)
        {
            return _text[pos];
        }

        unchecked
        {
            return (char)(short)-1;
        }
    }

    public SyntaxKind ReScanSlashToken(bool reportErrors)
    {
        if (_token == SyntaxKind.SlashToken || _token == SyntaxKind.SlashEqualsToken)
        {
            // Quickly get to the end of regex such that we know the flags
            var startOfRegExpBody = _tokenStart + 1;
            _pos = startOfRegExpBody;
            var inEscape = false;
            var namedCaptureGroups = false;
            // Although nested character classes are allowed in Unicode Sets mode,
            // an unescaped slash is nevertheless invalid even in a character class in any Unicode mode.
            // This is indicated by Section 12.9.5 Regular Expression Literals of the specification,
            // where nested character classes are not considered at all. (A `[` RegularExpressionClassChar
            // does nothing in a RegularExpressionClass, and a `]` always closes the class.)
            // Additionally, parsing nested character classes will misinterpret regexes like `/[[]/`
            // as unterminated, consuming characters beyond the slash. (This even applies to `/[[]/v`,
            // which should be parsed as a well-terminated regex with an incomplete character class.)
            // Thus we must not handle nested character classes in the first pass.
            var inCharacterClass = false;
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                // If we reach the end of a file, or hit a newline, then this is an unterminated
                // regex.  Report error and return what we have so far.
                char ch;
                if (_pos >= _text.Length || IsLineBreak(ch = _text[_pos]))
                {
                    _tokenFlags |= TokenFlags.Unterminated;
                    break;
                }

                if (inEscape)
                {
                    // Parsing an escape character;
                    // reset the flag and just advance to the next char.
                    inEscape = false;
                }
                else if (ch == CharacterCodes.Slash && !inCharacterClass)
                {
                    // A slash within a character class is permissible,
                    // but in general it signals the end of the regexp literal.
                    break;
                }
                else if (ch == CharacterCodes.OpenBracket)
                {
                    inCharacterClass = true;
                }
                else if (ch == CharacterCodes.Backslash)
                {
                    inEscape = true;
                }
                else if (ch == CharacterCodes.CloseBracket)
                {
                    inCharacterClass = false;
                }
                else if (!inCharacterClass
                   && ch == CharacterCodes.OpenParen
                   && _pos < _text.Length - 2
                   && _text[_pos + 1] == CharacterCodes.Question
                   && _text[_pos + 2] == CharacterCodes.LessThan
                   && (_pos >= _text.Length - 3 ||
                       (_text[_pos + 3] != CharacterCodes.Equals
                        && _text[_pos + 3] != CharacterCodes.Exclamation))
                )
                {
                    namedCaptureGroups = true;
                }

                _pos++;
            }

            var endOfRegExpBody = _pos;
            if (_tokenFlags.HasFlag(TokenFlags.Unterminated))
            {
                // Search for the nearest unbalanced bracket for better recovery. Since the expression is
                // invalid anyways, we take nested square brackets into consideration for the best guess.
                _pos = startOfRegExpBody;
                inEscape = false;
                var characterClassDepth = 0;
                var inDecimalQuantifier = false;
                var groupDepth = 0;
                while (_pos < endOfRegExpBody)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var ch = _text[_pos];
                    if (inEscape)
                    {
                        inEscape = false;
                    }
                    else if (ch == CharacterCodes.Backslash)
                    {
                        inEscape = true;
                    }
                    else if (ch == CharacterCodes.OpenBracket)
                    {
                        characterClassDepth++;
                    }
                    else if (ch == CharacterCodes.CloseBracket && characterClassDepth != 0)
                    {
                        characterClassDepth--;
                    }
                    else if (characterClassDepth == 0)
                    {
                        if (ch == CharacterCodes.OpenBrace)
                        {
                            inDecimalQuantifier = true;
                        }
                        else if (ch == CharacterCodes.CloseBrace && inDecimalQuantifier)
                        {
                            inDecimalQuantifier = false;
                        }
                        else if (!inDecimalQuantifier)
                        {
                            if (ch == CharacterCodes.OpenParen)
                            {
                                groupDepth++;
                            }
                            else if (ch == CharacterCodes.CloseParen && groupDepth != 0)
                            {
                                groupDepth--;
                            }
                            else if (ch == CharacterCodes.CloseParen || ch == CharacterCodes.CloseBracket || ch == CharacterCodes.CloseBrace)
                            {
                                // We encountered an unbalanced bracket outside a character class. Treat this position as the end of regex.
                                break;
                            }
                        }
                    }

                    _pos++;
                }

                // Whitespaces and semicolons at the end are not likely to be part of the regex
                while (IsWhiteSpaceLike(CharCodeChecked(_pos - 1)) || CharCodeChecked(_pos - 1) == CharacterCodes.Semicolon)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    _pos--;
                }

                Error(Diagnostics._unterminatedRegularExpressionLiteral, _tokenStart, _pos - _tokenStart);
            }
            else
            {
                // Consume the slash character
                _pos++;
                var regExpFlags = RegularExpressionFlags.None;
                while (true)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var ch = CodePointChecked(_pos);
                    if (ch == CharacterCodes.EndOfFile || !IsIdentifierPart(ch, _languageVersion))
                    {
                        break;
                    }

                    var size = CharSize(ch);
                    if (reportErrors)
                    {
                        var flag = CharacterCodeToRegularExpressionFlag(ch);
                        if (flag == null)
                        {
                            Error(Diagnostics._unknownRegularExpressionFlag, _pos, size);
                        }
                        else if (regExpFlags.HasFlag(flag.Value))
                        {
                            Error(Diagnostics._duplicateRegularExpressionFlag, _pos, size);
                        }
                        else if (((regExpFlags | flag) & RegularExpressionFlags.AnyUnicodeMode) == RegularExpressionFlags.AnyUnicodeMode)
                        {
                            Error(Diagnostics._theUnicodeUFlagAndTheUnicodeSetsVFlagCannotBeSetSimultaneously, _pos, size);
                        }
                        else
                        {
                            regExpFlags |= flag.Value;
                            CheckRegularExpressionFlagAvailability(flag.Value, size);
                        }
                    }

                    _pos += size;
                }

                if (reportErrors)
                {
                    ScanRange(
                        startOfRegExpBody,
                        endOfRegExpBody - startOfRegExpBody,
                        () =>
                        {
                            ScanRegularExpressionWorker(regExpFlags, annexB: true, namedCaptureGroups);

                            return 0;
                        });
                }
            }

            _tokenValue = _text[_tokenStart.._pos];
            _token = SyntaxKind.RegularExpressionLiteral;
        }

        return _token;
    }


    private void ScanRegularExpressionWorker(RegularExpressionFlags regExpFlags, bool annexB, bool namedCaptureGroups)
    {
        /* Grammar parameter */
        var unicodeSetsMode = regExpFlags.HasFlag(RegularExpressionFlags.UnicodeSets);
        /* Grammar parameter */
        var anyUnicodeMode = (regExpFlags & RegularExpressionFlags.AnyUnicodeMode) != 0;

        // Regular expressions are checked more strictly when either in 'u' or 'v' mode, or
        // when not using the looser interpretation of the syntax from ECMA-262 Annex B.
        var anyUnicodeModeOrNonAnnexB = anyUnicodeMode || !annexB;

        /* @see {scanClassSetExpression} */
        var mayContainStrings = false;

        /* The number of all (named and unnamed) capturing groups defined in the regex. */
        var numberOfCapturingGroups = 0;
        /* All named capturing groups defined in the regex. */
        HashSet<string>? groupSpecifiers = null;
        /* All references to named capturing groups in the regex. */
        List<RegexGroupNameRef>? groupNameReferences = null;
        /* All numeric backreferences within the regex. */
        List<RegexDecimalEscape>? decimalEscapes = null;
        /* A stack of scopes for named capturing groups. @see {scanGroupName} */
        Stack<HashSet<string>?> namedCapturingGroupsScopeStack = [];
        HashSet<string>? topNamedCapturingGroupsScope = null;

        // Disjunction ::= Alternative ('|' Alternative)*
        void ScanDisjunction(bool isInGroup)
        {
            using var recursion = _budget.Enter(_pos);

            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                namedCapturingGroupsScopeStack.Push(topNamedCapturingGroupsScope);
                topNamedCapturingGroupsScope = null;
                ScanAlternative(isInGroup);
                topNamedCapturingGroupsScope = namedCapturingGroupsScopeStack.Pop();
                if (CharCodeChecked(_pos) != CharacterCodes.Bar)
                {
                    return;
                }

                _pos++;
            }
        }

        // Alternative ::= Term*
        // Term ::=
        //     | Assertion
        //     | Atom Quantifier?
        // Assertion ::=
        //     | '^'
        //     | '$'
        //     | '\b'
        //     | '\B'
        //     | '(?=' Disjunction ')'
        //     | '(?!' Disjunction ')'
        //     | '(?<=' Disjunction ')'
        //     | '(?<!' Disjunction ')'
        // Quantifier ::= QuantifierPrefix '?'?
        // QuantifierPrefix ::=
        //     | '*'
        //     | '+'
        //     | '?'
        //     | '{' DecimalDigits (',' DecimalDigits?)? '}'
        // Atom ::=
        //     | PatternCharacter
        //     | '.'
        //     | '\' AtomEscape
        //     | CharacterClass
        //     | '(?<' RegExpIdentifierName '>' Disjunction ')'
        //     | '(?' RegularExpressionFlags ('-' RegularExpressionFlags)? ':' Disjunction ')'
        // CharacterClass ::= unicodeMode
        //     ? '[' ClassRanges ']'
        //     : '[' ClassSetExpression ']'
        void ScanAlternative(bool isInGroup)
        {
            using var recursion = _budget.Enter(_pos);

            var isPreviousTermQuantifiable = false;
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var start = _pos;
                var ch = CharCodeChecked(_pos);
                switch ((int)ch)
                {
                    case CharacterCodes.EndOfFile:
                        return;
                    case CharacterCodes.Caret:
                    case CharacterCodes.Dollar:
                        _pos++;
                        isPreviousTermQuantifiable = false;
                        break;
                    case CharacterCodes.Backslash:
                        _pos++;
                        switch ((int)CharCodeChecked(_pos))
                        {
                            case CharacterCodes.LowercaseB:
                            case CharacterCodes.UppercaseB:
                                _pos++;
                                isPreviousTermQuantifiable = false;
                                break;
                            default:
                                ScanAtomEscape();
                                isPreviousTermQuantifiable = true;
                                break;
                        }
                        break;
                    case CharacterCodes.OpenParen:
                        _pos++;
                        if (CharCodeChecked(_pos) == CharacterCodes.Question)
                        {
                            _pos++;
                            switch ((int)CharCodeChecked(_pos))
                            {
                                case CharacterCodes.Equals:
                                case CharacterCodes.Exclamation:
                                    _pos++;
                                    // In Annex B, `(?=Disjunction)` and `(?!Disjunction)` are quantifiable
                                    isPreviousTermQuantifiable = !anyUnicodeModeOrNonAnnexB;
                                    break;
                                case CharacterCodes.LessThan:
                                    var groupNameStart = _pos;
                                    _pos++;
                                    switch ((int)CharCodeChecked(_pos))
                                    {
                                        case CharacterCodes.Equals:
                                        case CharacterCodes.Exclamation:
                                            _pos++;
                                            isPreviousTermQuantifiable = false;
                                            break;
                                        default:
                                            ScanGroupName(isReference: false);
                                            ScanExpectedChar(CharacterCodes.GreaterThan);
                                            if (_languageVersion < ScriptTarget.ES2018)
                                            {
                                                Error(
                                                    Diagnostics._namedCapturingGroupsAreOnlyAvailableWhenTargetingES2018OrLater,
                                                    groupNameStart,
                                                    _pos - groupNameStart);
                                            }
                                            numberOfCapturingGroups++;
                                            isPreviousTermQuantifiable = true;
                                            break;
                                    }
                                    break;
                                default:
                                    start = _pos;
                                    var setFlags = ScanPatternModifiers(RegularExpressionFlags.None);
                                    if (CharCodeChecked(_pos) == CharacterCodes.Minus)
                                    {
                                        _pos++;
                                        ScanPatternModifiers(setFlags);
                                        if (_pos == start + 1)
                                        {
                                            Error(Diagnostics._subpatternFlagsMustBePresentWhenThereIsAMinusSign, start, _pos - start);
                                        }
                                    }
                                    ScanExpectedChar(CharacterCodes.Colon);
                                    isPreviousTermQuantifiable = true;
                                    break;
                            }
                        }
                        else
                        {
                            numberOfCapturingGroups++;
                            isPreviousTermQuantifiable = true;
                        }
                        ScanDisjunction(/*isInGroup*/ true);
                        ScanExpectedChar(CharacterCodes.CloseParen);
                        break;
                    case CharacterCodes.OpenBrace:
                        _pos++;
                        var digitsStart = _pos;
                        ScanDigits();
                        var min = _tokenValue;
                        if (!anyUnicodeModeOrNonAnnexB && string.IsNullOrEmpty(min))
                        {
                            isPreviousTermQuantifiable = true;
                            break;
                        }
                        if (CharCodeChecked(_pos) == CharacterCodes.Comma)
                        {
                            _pos++;
                            ScanDigits();
                            var max = _tokenValue;
                            if (string.IsNullOrEmpty(min))
                            {
                                if (!string.IsNullOrEmpty(max) || CharCodeChecked(_pos) == CharacterCodes.CloseBrace)
                                {
                                    Error(Diagnostics._incompleteQuantifierDigitExpected, digitsStart, 0);
                                }
                                else
                                {
                                    Error(Diagnostics._unexpected0DidYouMeanToEscapeItWithBackslash, start, 1, ch);
                                    isPreviousTermQuantifiable = true;
                                    break;
                                }
                            }
                            else if (!string.IsNullOrEmpty(max) && int.Parse(min, CultureInfo.InvariantCulture) >
                                int.Parse(max, CultureInfo.InvariantCulture) && (anyUnicodeModeOrNonAnnexB || CharCodeChecked(_pos) == CharacterCodes.CloseBrace))
                            {
                                Error(Diagnostics._numbersOutOfOrderInQuantifier, digitsStart, _pos - digitsStart);
                            }
                        }
                        else if (string.IsNullOrEmpty(min))
                        {
                            if (anyUnicodeModeOrNonAnnexB)
                            {
                                Error(Diagnostics._unexpected0DidYouMeanToEscapeItWithBackslash, start, 1, ch);
                            }

                            isPreviousTermQuantifiable = true;
                            break;
                        }
                        if (CharCodeChecked(_pos) != CharacterCodes.CloseBrace)
                        {
                            if (anyUnicodeModeOrNonAnnexB)
                            {
                                Error(Diagnostics._argument0Expected, _pos, 0, (char)CharacterCodes.CloseBrace);
                                _pos--;
                            }
                            else
                            {
                                isPreviousTermQuantifiable = true;
                                break;
                            }
                        }
                        goto asteriskPlusQuestion;
                    // falls through
                    case CharacterCodes.Asterisk:
                    case CharacterCodes.Plus:
                    case CharacterCodes.Question:
                    asteriskPlusQuestion:
                        _pos++;
                        if (CharCodeChecked(_pos) == CharacterCodes.Question)
                        {
                            // Non-greedy
                            _pos++;
                        }
                        if (!isPreviousTermQuantifiable)
                        {
                            Error(Diagnostics._thereIsNothingAvailableForRepetition, start, _pos - start);
                        }
                        isPreviousTermQuantifiable = false;
                        break;
                    case CharacterCodes.Dot:
                        _pos++;
                        isPreviousTermQuantifiable = true;
                        break;
                    case CharacterCodes.OpenBracket:
                        _pos++;
                        if (unicodeSetsMode)
                        {
                            ScanClassSetExpression();
                        }
                        else
                        {
                            ScanClassRanges();
                        }
                        ScanExpectedChar(CharacterCodes.CloseBracket);
                        isPreviousTermQuantifiable = true;
                        break;
                    case CharacterCodes.CloseParen:
                        if (isInGroup)
                        {
                            return;
                        }
                        goto closeBracketBrace;
                    // falls through
                    case CharacterCodes.CloseBracket:
                    case CharacterCodes.CloseBrace:
                    closeBracketBrace:
                        if (anyUnicodeModeOrNonAnnexB || ch == CharacterCodes.CloseParen)
                        {
                            Error(Diagnostics._unexpected0DidYouMeanToEscapeItWithBackslash, _pos, 1, ch);
                        }
                        _pos++;
                        isPreviousTermQuantifiable = true;
                        break;
                    case CharacterCodes.Slash:
                    case CharacterCodes.Bar:
                        return;
                    default:
                        ScanSourceCharacter();
                        isPreviousTermQuantifiable = true;
                        break;
                }
            }
        }

        RegularExpressionFlags ScanPatternModifiers(RegularExpressionFlags currFlags)
        {
            using var recursion = _budget.Enter(_pos);

            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ch = CodePointChecked(_pos);
                if (ch == CharacterCodes.EndOfFile ||
                    !IsIdentifierPart(ch, _languageVersion))
                {
                    break;
                }

                var size = CharSize(ch);
                var flag = CharacterCodeToRegularExpressionFlag(ch);
                if (flag == null)
                {
                    Error(Diagnostics._unknownRegularExpressionFlag, _pos, size);
                }
                else if (currFlags.HasFlag(flag.Value))
                {
                    Error(Diagnostics._duplicateRegularExpressionFlag, _pos, size);
                }
                else if ((flag.Value & RegularExpressionFlags.Modifiers) != 0)
                {
                    Error(Diagnostics._thisRegularExpressionFlagCannotBeToggledWithinASubpattern, _pos, size);
                }
                else
                {
                    currFlags |= flag.Value;
                    CheckRegularExpressionFlagAvailability(flag.Value, size);
                }

                _pos += size;
            }

            return currFlags;
        }

        // AtomEscape ::=
        //     | DecimalEscape
        //     | CharacterClassEscape
        //     | CharacterEscape
        //     | 'k<' RegExpIdentifierName '>'
        void ScanAtomEscape()
        {
            using var recursion = _budget.Enter(_pos);

            Debug.Assert(CharCodeUnchecked(_pos - 1) == CharacterCodes.Backslash);
            switch ((int)CharCodeChecked(_pos))
            {
                case CharacterCodes.LowercaseK:
                    _pos++;
                    if (CharCodeChecked(_pos) == CharacterCodes.LessThan)
                    {
                        _pos++;
                        ScanGroupName(isReference: true);
                        ScanExpectedChar(CharacterCodes.GreaterThan);
                    }
                    else if (anyUnicodeModeOrNonAnnexB || namedCaptureGroups)
                    {
                        Error(Diagnostics._kMustBeFollowedByACapturingGroupNameEnclosedInAngleBrackets, _pos - 2, 2);
                    }
                    break;
                case CharacterCodes.LowercaseQ:
                    if (unicodeSetsMode)
                    {
                        _pos++;
                        Error(Diagnostics._qIsOnlyAvailableInsideCharacterClass, _pos - 2, 2);
                        break;
                    }
                    goto default;
                // falls through
                default:
                    // The scanEscapeSequence call in scanCharacterEscape must return non-empty strings
                    // since there must not be line breaks in a regex literal
                    Debug.Assert(ScanCharacterClassEscape() || ScanDecimalEscape() || !string.IsNullOrEmpty(ScanCharacterEscape(atomEscape: true)));
                    break;
            }
        }

        // DecimalEscape ::= [1-9] [0-9]*
        bool ScanDecimalEscape()
        {
            using var recursion = _budget.Enter(_pos);

            Debug.Assert(CharCodeUnchecked(_pos - 1) == CharacterCodes.Backslash);
            var ch = CharCodeChecked(_pos);
            if (ch >= CharacterCodes.Digit1 && ch <= CharacterCodes.Digit9)
            {
                var start = _pos;
                ScanDigits();
                (decimalEscapes ??= []).Append(new()
                {
                    Pos = start,
                    End = _pos,
                    Value = decimal.Parse(_tokenValue, CultureInfo.InvariantCulture)
                });

                return true;
            }

            return false;
        }

        // CharacterEscape ::=
        //     | `c` ControlLetter
        //     | IdentityEscape
        //     | (Other sequences handled by `scanEscapeSequence`)
        // IdentityEscape ::=
        //     | '^' | '$' | '/' | '\' | '.' | '*' | '+' | '?' | '(' | ')' | '[' | ']' | '{' | '}' | '|'
        //     | [~UnicodeMode] (any other non-identifier characters)
        string ScanCharacterEscape(bool atomEscape)
        {
            using var recursion = _budget.Enter(_pos);

            Debug.Assert(CharCodeUnchecked(_pos - 1) == CharacterCodes.Backslash);
            var ch = (int)CharCodeChecked(_pos);
            switch (ch)
            {
                case CharacterCodes.EndOfFile:
                    Error(Diagnostics._undeterminedCharacterEscape, _pos - 1, 1);
                    return "\\";
                case CharacterCodes.LowercaseC:
                    _pos++;
                    ch = CharCodeChecked(_pos);
                    if (IsASCIILetter(ch))
                    {
                        _pos++;

                        return ((char)(ch & 0x1f)).ToString();
                    }
                    if (anyUnicodeModeOrNonAnnexB)
                    {
                        Error(Diagnostics._cMustBeFollowedByAnASCIILetter, _pos - 2, 2);
                    }
                    else if (atomEscape)
                    {
                        // Annex B treats
                        //
                        //  ExtendedAtom : `\` [lookahead = `c`]
                        //
                        // as the single character `\` when `c` isn't followed by a valid control character
                        _pos--;

                        return "\\";
                    }
                    return ((char)ch).ToString();
                case CharacterCodes.Caret:
                case CharacterCodes.Dollar:
                case CharacterCodes.Slash:
                case CharacterCodes.Backslash:
                case CharacterCodes.Dot:
                case CharacterCodes.Asterisk:
                case CharacterCodes.Plus:
                case CharacterCodes.Question:
                case CharacterCodes.OpenParen:
                case CharacterCodes.CloseParen:
                case CharacterCodes.OpenBracket:
                case CharacterCodes.CloseBracket:
                case CharacterCodes.OpenBrace:
                case CharacterCodes.CloseBrace:
                case CharacterCodes.Bar:
                    _pos++;
                    return ((char)ch).ToString();
                default:
                    _pos--;
                    var sb = new StringBuilder();
                    ScanEscapeSequence(
                        sb,
                        EscapeSequenceScanningFlags.RegularExpression
                            | (annexB ? EscapeSequenceScanningFlags.AnnexB : 0)
                            | (anyUnicodeMode ? EscapeSequenceScanningFlags.AnyUnicodeMode : 0)
                            | (atomEscape ? EscapeSequenceScanningFlags.AtomEscape : 0)
                    );
                    return sb.ToString();
            }
        }

        void ScanGroupName(bool isReference)
        {
            using var recursion = _budget.Enter(_pos);

            Debug.Assert(CharCodeUnchecked(_pos - 1) == CharacterCodes.LessThan);
            _tokenStart = _pos;
            ScanIdentifier(CodePointChecked(_pos), _languageVersion);
            if (_pos == _tokenStart)
            {
                Error(Diagnostics._expectedACapturingGroupName);
            }
            else if (isReference)
            {
                (groupNameReferences ??= []).Add(new()
                {
                    Pos = _tokenStart,
                    End = _pos,
                    Name = _tokenValue
                });
            }
            else if (topNamedCapturingGroupsScope?.Contains(_tokenValue) == true ||
                namedCapturingGroupsScopeStack.Any(group => group?.Contains(_tokenValue) == true))
            {
                Error(
                    Diagnostics._namedCapturingGroupsWithTheSameNameMustBeMutuallyExclusiveToEachOther,
                    _tokenStart,
                    _pos - _tokenStart);
            }
            else
            {
                (topNamedCapturingGroupsScope ??= []).Add(_tokenValue);
                (groupSpecifiers ??= []).Add(_tokenValue);
            }
        }

        bool IsClassContentExit(int ch)
        {
            return ch == CharacterCodes.CloseBracket || ch == CharacterCodes.EndOfFile || _pos >= _end;
        }

        // ClassRanges ::= '^'? (ClassAtom ('-' ClassAtom)?)*
        void ScanClassRanges()
        {
            using var recursion = _budget.Enter(_pos);

            Debug.Assert(CharCodeUnchecked(_pos - 1) == CharacterCodes.OpenBracket);
            if (CharCodeChecked(_pos) == CharacterCodes.Caret)
            {
                // character complement
                _pos++;
            }

            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ch = CharCodeChecked(_pos);
                if (IsClassContentExit(ch))
                {
                    return;
                }

                var minStart = _pos;
                var minCharacter = ScanClassAtom();
                if (CharCodeChecked(_pos) == CharacterCodes.Minus)
                {
                    _pos++;
                    ch = CharCodeChecked(_pos);
                    if (IsClassContentExit(ch))
                    {
                        return;
                    }

                    if (string.IsNullOrEmpty(minCharacter) && anyUnicodeModeOrNonAnnexB)
                    {
                        Error(Diagnostics._aCharacterClassRangeMustNotBeBoundedByAnotherCharacterClass, minStart, _pos - 1 - minStart);
                    }

                    var maxStart = _pos;
                    var maxCharacter = ScanClassAtom();
                    if (string.IsNullOrEmpty(maxCharacter) && anyUnicodeModeOrNonAnnexB)
                    {
                        Error(Diagnostics._aCharacterClassRangeMustNotBeBoundedByAnotherCharacterClass, maxStart, _pos - maxStart);
                        continue;
                    }

                    if (string.IsNullOrEmpty(minCharacter))
                    {
                        continue;
                    }

                    var minCharacterValue = CodePointAt(minCharacter, 0);
                    var maxCharacterValue = CodePointAt(maxCharacter, 0);
                    if (
                        minCharacter.Length == CharSize(minCharacterValue) &&
                        maxCharacter.Length == CharSize(maxCharacterValue) &&
                        minCharacterValue > maxCharacterValue)
                    {
                        Error(Diagnostics._rangeOutOfOrderInCharacterClass, minStart, _pos - minStart);
                    }
                }
            }
        }

        // Static Semantics: MayContainStrings
        //     ClassUnion: ClassSetOperands.some(ClassSetOperand => ClassSetOperand.MayContainStrings)
        //     ClassIntersection: ClassSetOperands.every(ClassSetOperand => ClassSetOperand.MayContainStrings)
        //     ClassSubtraction: ClassSetOperands[0].MayContainStrings
        //     ClassSetOperand:
        //         || ClassStringDisjunctionContents.MayContainStrings
        //         || CharacterClassEscape.UnicodePropertyValueExpression.LoneUnicodePropertyNameOrValue.MayContainStrings
        //     ClassStringDisjunctionContents: ClassStrings.some(ClassString => ClassString.ClassSetCharacters.length !== 1)
        //     LoneUnicodePropertyNameOrValue: isBinaryUnicodePropertyOfStrings(LoneUnicodePropertyNameOrValue)

        // ClassSetExpression ::= '^'? (ClassUnion | ClassIntersection | ClassSubtraction)
        // ClassUnion ::= (ClassSetRange | ClassSetOperand)*
        // ClassIntersection ::= ClassSetOperand ('&&' ClassSetOperand)+
        // ClassSubtraction ::= ClassSetOperand ('--' ClassSetOperand)+
        // ClassSetRange ::= ClassSetCharacter '-' ClassSetCharacter
        void ScanClassSetExpression()
        {
            using var recursion = _budget.Enter(_pos);

            Debug.Assert(CharCodeUnchecked(_pos - 1) == CharacterCodes.OpenBracket);
            var isCharacterComplement = false;
            if (CharCodeChecked(_pos) == CharacterCodes.Caret)
            {
                _pos++;
                isCharacterComplement = true;
            }

            var expressionMayContainStrings = false;
            var ch = (int)CharCodeChecked(_pos);
            if (IsClassContentExit(ch))
            {
                return;
            }

            var start = _pos;
            string? operand = null;
            switch (_text[_pos..(_pos + 2)])
            {
                case "--":
                case "&&":
                    Error(Diagnostics._expectedAClassSetOperand);
                    mayContainStrings = false;
                    break;
                default:
                    operand = ScanClassSetOperand();
                    break;
            }

            switch ((int)CharCodeChecked(_pos))
            {
                case CharacterCodes.Minus:
                    if (CharCodeChecked(_pos + 1) == CharacterCodes.Minus)
                    {
                        if (isCharacterComplement && mayContainStrings)
                        {
                            Error(
                                Diagnostics._anythingThatWouldPossiblyMatchMoreThanASingleCharacterIsInvalidInsideANegatedCharacterClass,
                                start,
                                _pos - start);
                        }

                        expressionMayContainStrings = mayContainStrings;
                        ScanClassSetSubExpression(ClassSetExpressionType.ClassSubtraction);
                        mayContainStrings = !isCharacterComplement && expressionMayContainStrings;

                        return;
                    }
                    break;
                case CharacterCodes.Ampersand:
                    if (CharCodeChecked(_pos + 1) == CharacterCodes.Ampersand)
                    {
                        ScanClassSetSubExpression(ClassSetExpressionType.ClassIntersection);
                        if (isCharacterComplement && mayContainStrings)
                        {
                            Error(
                                Diagnostics._anythingThatWouldPossiblyMatchMoreThanASingleCharacterIsInvalidInsideANegatedCharacterClass,
                                start,
                                _pos - start);
                        }

                        expressionMayContainStrings = mayContainStrings;
                        mayContainStrings = !isCharacterComplement && expressionMayContainStrings;

                        return;
                    }
                    else
                    {
                        Error(Diagnostics._unexpected0DidYouMeanToEscapeItWithBackslash, _pos, 1, (char)ch);
                    }
                    break;
                default:
                    if (isCharacterComplement && mayContainStrings)
                    {
                        Error(
                            Diagnostics._anythingThatWouldPossiblyMatchMoreThanASingleCharacterIsInvalidInsideANegatedCharacterClass,
                            start,
                            _pos - start);
                    }
                    expressionMayContainStrings = mayContainStrings;
                    break;
            }

            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                ch = CharCodeChecked(_pos);
                if (ch == CharacterCodes.EndOfFile)
                {
                    break;
                }

                switch (ch)
                {
                    case CharacterCodes.Minus:
                        _pos++;
                        ch = CharCodeChecked(_pos);
                        if (IsClassContentExit(ch))
                        {
                            mayContainStrings = !isCharacterComplement && expressionMayContainStrings;

                            return;
                        }
                        if (ch == CharacterCodes.Minus)
                        {
                            _pos++;
                            Error(Diagnostics._operatorsMustNotBeMixedWithinACharacterClassWrapItInANestedClassInstead, _pos - 2, 2);
                            start = _pos - 2;
                            operand = _text[start.._pos];
                            continue;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(operand))
                            {
                                Error(Diagnostics._aCharacterClassRangeMustNotBeBoundedByAnotherCharacterClass, start, _pos - 1 - start);
                            }

                            var secondStart = _pos;
                            var secondOperand = ScanClassSetOperand();
                            if (isCharacterComplement && mayContainStrings)
                            {
                                Error(
                                    Diagnostics._anythingThatWouldPossiblyMatchMoreThanASingleCharacterIsInvalidInsideANegatedCharacterClass,
                                    secondStart,
                                    _pos - secondStart);
                            }

                            expressionMayContainStrings |= mayContainStrings;
                            if (string.IsNullOrEmpty(secondOperand))
                            {
                                Error(
                                    Diagnostics._aCharacterClassRangeMustNotBeBoundedByAnotherCharacterClass,
                                    secondStart,
                                    _pos - secondStart);
                                break;
                            }

                            if (string.IsNullOrEmpty(operand))
                            {
                                break;
                            }

                            var minCharacterValue = CodePointAt(operand!, 0);
                            var maxCharacterValue = CodePointAt(secondOperand, 0);
                            if (
                                 operand!.Length == CharSize(minCharacterValue) &&
                                 secondOperand.Length == CharSize(maxCharacterValue) &&
                                minCharacterValue > maxCharacterValue)
                            {
                                Error(Diagnostics._rangeOutOfOrderInCharacterClass, start, _pos - start);
                            }
                        }
                        break;
                    case CharacterCodes.Ampersand:
                        start = _pos;
                        _pos++;
                        if (CharCodeChecked(_pos) == CharacterCodes.Ampersand)
                        {
                            _pos++;
                            Error(Diagnostics._operatorsMustNotBeMixedWithinACharacterClassWrapItInANestedClassInstead, _pos - 2, 2);
                            if (CharCodeChecked(_pos) == CharacterCodes.Ampersand)
                            {
                                Error(Diagnostics._unexpected0DidYouMeanToEscapeItWithBackslash, _pos, 1, (char)ch);
                                _pos++;
                            }
                        }
                        else
                        {
                            Error(Diagnostics._unexpected0DidYouMeanToEscapeItWithBackslash, _pos - 1, 1, (char)ch);
                        }
                        operand = _text[start.._pos];
                        continue;
                }

                if (IsClassContentExit(CharCodeChecked(_pos)))
                {
                    break;
                }

                start = _pos;
                switch (_text[_pos..(_pos + 2)])
                { // TODO: don't use slice
                    case "--":
                    case "&&":
                        Error(Diagnostics._operatorsMustNotBeMixedWithinACharacterClassWrapItInANestedClassInstead, _pos, 2);
                        _pos += 2;
                        operand = _text[start.._pos];
                        break;
                    default:
                        operand = ScanClassSetOperand();
                        break;
                }
            }

            mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
        }

        void ScanClassSetSubExpression(ClassSetExpressionType expressionType)
        {
            using var recursion = _budget.Enter(_pos);

            var expressionMayContainStrings = mayContainStrings;
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ch = CharCodeChecked(_pos);
                if (IsClassContentExit(ch))
                {
                    break;
                }

                // Provide user-friendly diagnostic messages
                switch ((int)ch)
                {
                    case CharacterCodes.Minus:
                        _pos++;
                        if (CharCodeChecked(_pos) == CharacterCodes.Minus)
                        {
                            _pos++;
                            if (expressionType != ClassSetExpressionType.ClassSubtraction)
                            {
                                Error(Diagnostics._operatorsMustNotBeMixedWithinACharacterClassWrapItInANestedClassInstead, _pos - 2, 2);
                            }
                        }
                        else
                        {
                            Error(Diagnostics._operatorsMustNotBeMixedWithinACharacterClassWrapItInANestedClassInstead, _pos - 1, 1);
                        }
                        break;
                    case CharacterCodes.Ampersand:
                        _pos++;
                        if (CharCodeChecked(_pos) == CharacterCodes.Ampersand)
                        {
                            _pos++;
                            if (expressionType != ClassSetExpressionType.ClassIntersection)
                            {
                                Error(Diagnostics._operatorsMustNotBeMixedWithinACharacterClassWrapItInANestedClassInstead, _pos - 2, 2);
                            }

                            if (CharCodeChecked(_pos) == CharacterCodes.Ampersand)
                            {
                                Error(Diagnostics._unexpected0DidYouMeanToEscapeItWithBackslash, _pos, 1, ch);
                                _pos++;
                            }
                        }
                        else
                        {
                            Error(Diagnostics._unexpected0DidYouMeanToEscapeItWithBackslash, _pos - 1, 1, ch);
                        }
                        break;
                    default:
                        switch (expressionType)
                        {
                            case ClassSetExpressionType.ClassSubtraction:
                                Error(Diagnostics._argument0Expected, _pos, 0, "--");
                                break;
                            case ClassSetExpressionType.ClassIntersection:
                                Error(Diagnostics._argument0Expected, _pos, 0, "&&");
                                break;
                            default:
                                break;
                        }
                        break;
                }

                ch = CharCodeChecked(_pos);
                if (IsClassContentExit(ch))
                {
                    Error(Diagnostics._expectedAClassSetOperand);
                    break;
                }

                ScanClassSetOperand();
                // Used only if expressionType is Intersection
                expressionMayContainStrings &= mayContainStrings;
            }

            mayContainStrings = expressionMayContainStrings;
        }

        // ClassSetOperand ::=
        //     | '[' ClassSetExpression ']'
        //     | '\' CharacterClassEscape
        //     | '\q{' ClassStringDisjunctionContents '}'
        //     | ClassSetCharacter
        string ScanClassSetOperand()
        {
            using var recursion = _budget.Enter(_pos);

            mayContainStrings = false;
            switch ((int)CharCodeChecked(_pos))
            {
                case CharacterCodes.EndOfFile:
                    return "";
                case CharacterCodes.OpenBracket:
                    _pos++;
                    ScanClassSetExpression();
                    ScanExpectedChar(CharacterCodes.CloseBracket);
                    return "";
                case CharacterCodes.Backslash:
                    _pos++;
                    if (ScanCharacterClassEscape())
                    {
                        return "";
                    }
                    else if (CharCodeChecked(_pos) == CharacterCodes.UppercaseQ)
                    {
                        _pos++;
                        if (CharCodeChecked(_pos) == CharacterCodes.OpenBrace)
                        {
                            _pos++;
                            ScanClassStringDisjunctionContents();
                            ScanExpectedChar(CharacterCodes.CloseBrace);

                            return "";
                        }
                        else
                        {
                            Error(Diagnostics._qMustBeFollowedByStringAlternativesEnclosedInBraces, _pos - 2, 2);

                            return "q";
                        }
                    }
                    _pos--;
                    goto default;
                // falls through
                default:
                    return ScanClassSetCharacter();
            }
        }

        // ClassStringDisjunctionContents ::= ClassSetCharacter* ('|' ClassSetCharacter*)*
        void ScanClassStringDisjunctionContents()
        {
            using var recursion = _budget.Enter(_pos);

            Debug.Assert(CharCodeUnchecked(_pos - 1) == CharacterCodes.OpenBrace);
            var characterCount = 0;
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ch = CharCodeChecked(_pos);
                switch ((int)ch)
                {
                    case CharacterCodes.EndOfFile:
                        return;
                    case CharacterCodes.CloseBrace:
                        if (characterCount != 1)
                        {
                            mayContainStrings = true;
                        }
                        return;
                    case CharacterCodes.Bar:
                        if (characterCount != 1)
                        {
                            mayContainStrings = true;
                        }
                        _pos++;
                        characterCount = 0;
                        break;
                    default:
                        ScanClassSetCharacter();
                        characterCount++;
                        break;
                }
            }
        }

        // ClassSetCharacter ::=
        //     | SourceCharacter -- ClassSetSyntaxCharacter -- ClassSetReservedDoublePunctuator
        //     | '\' (CharacterEscape | ClassSetReservedPunctuator | 'b')
        string ScanClassSetCharacter()
        {
            using var recursion = _budget.Enter(_pos);

            var ch = CharCodeChecked(_pos);
            if (ch == CharacterCodes.EndOfFile)
            {
                // no need to report an error, the initial scan will already have reported that the RegExp is unterminated.
                return "";
            }

            if (ch == CharacterCodes.Backslash)
            {
                _pos++;
                ch = CharCodeChecked(_pos);
                switch ((int)ch)
                {
                    case CharacterCodes.LowercaseB:
                        _pos++;
                        return "\b";
                    case CharacterCodes.Ampersand:
                    case CharacterCodes.Minus:
                    case CharacterCodes.Exclamation:
                    case CharacterCodes.Hash:
                    case CharacterCodes.Percent:
                    case CharacterCodes.Comma:
                    case CharacterCodes.Colon:
                    case CharacterCodes.Semicolon:
                    case CharacterCodes.LessThan:
                    case CharacterCodes.Equals:
                    case CharacterCodes.GreaterThan:
                    case CharacterCodes.At:
                    case CharacterCodes.Backtick:
                    case CharacterCodes.Tilde:
                        _pos++;
                        return ch.ToString();
                    default:
                        return ScanCharacterEscape(atomEscape: false).ToString();
                }
            }
            else if (ch == CharCodeChecked(_pos + 1))
            {
                switch ((int)ch)
                {
                    case CharacterCodes.Ampersand:
                    case CharacterCodes.Exclamation:
                    case CharacterCodes.Hash:
                    case CharacterCodes.Percent:
                    case CharacterCodes.Asterisk:
                    case CharacterCodes.Plus:
                    case CharacterCodes.Comma:
                    case CharacterCodes.Dot:
                    case CharacterCodes.Colon:
                    case CharacterCodes.Semicolon:
                    case CharacterCodes.LessThan:
                    case CharacterCodes.Equals:
                    case CharacterCodes.GreaterThan:
                    case CharacterCodes.Question:
                    case CharacterCodes.At:
                    case CharacterCodes.Backtick:
                    case CharacterCodes.Tilde:
                        Error(
                            Diagnostics._aCharacterClassMustNotContainAReservedDoublePunctuatorDidYouMeanToEscapeItWithBackslash,
                            _pos,
                            2);
                        _pos += 2;
                        return _text[(_pos - 2).._pos];
                }
            }

            switch ((int)ch)
            {
                case CharacterCodes.Slash:
                case CharacterCodes.OpenParen:
                case CharacterCodes.CloseParen:
                case CharacterCodes.OpenBracket:
                case CharacterCodes.CloseBracket:
                case CharacterCodes.OpenBrace:
                case CharacterCodes.CloseBrace:
                case CharacterCodes.Minus:
                case CharacterCodes.Bar:
                    Error(Diagnostics._unexpected0DidYouMeanToEscapeItWithBackslash, _pos, 1, ch);
                    _pos++;
                    return ch.ToString();
            }

            return ScanSourceCharacter();
        }

        // ClassAtom ::=
        //     | SourceCharacter but not one of '\' or ']'
        //     | '\' ClassEscape
        // ClassEscape ::=
        //     | 'b'
        //     | '-'
        //     | CharacterClassEscape
        //     | CharacterEscape
        string ScanClassAtom()
        {
            using var recursion = _budget.Enter(_pos);

            if (CharCodeChecked(_pos) == CharacterCodes.Backslash)
            {
                _pos++;
                var ch = CharCodeChecked(_pos);
                switch ((int)ch)
                {
                    case CharacterCodes.LowercaseB:
                        _pos++;
                        return "\b";
                    case CharacterCodes.Minus:
                        _pos++;
                        return ch.ToString();
                    default:
                        if (ScanCharacterClassEscape())
                        {
                            return "";
                        }
                        return ScanCharacterEscape(atomEscape: false).ToString();
                }
            }
            else
            {
                return ScanSourceCharacter();
            }
        }

        // CharacterClassEscape ::=
        //     | 'd' | 'D' | 's' | 'S' | 'w' | 'W'
        //     | [+UnicodeMode] ('P' | 'p') '{' UnicodePropertyValueExpression '}'
        bool ScanCharacterClassEscape()
        {
            using var recursion = _budget.Enter(_pos);

            Debug.Assert(CharCodeUnchecked(_pos - 1) == CharacterCodes.Backslash);
            var isCharacterComplement = false;
            var start = _pos - 1;
            var ch = CharCodeChecked(_pos);
            switch ((int)ch)
            {
                case CharacterCodes.LowercaseD:
                case CharacterCodes.UppercaseD:
                case CharacterCodes.LowercaseS:
                case CharacterCodes.UppercaseS:
                case CharacterCodes.LowercaseW:
                case CharacterCodes.UppercaseW:
                    _pos++;
                    return true;
                case CharacterCodes.UppercaseP:
                    isCharacterComplement = true;
                    goto charactercodesP;
                // falls through
                case CharacterCodes.LowercaseP:
                charactercodesP:
                    _pos++;
                    if (CharCodeChecked(_pos) == CharacterCodes.OpenBrace)
                    {
                        _pos++;
                        var propertyNameOrValueStart = _pos;
                        var propertyNameOrValue = ScanWordCharacters();
                        if (CharCodeChecked(_pos) == CharacterCodes.Equals)
                        {
                            var propertyName = _nonBinaryUnicodeProperties.TryGetValue(propertyNameOrValue, out string? value) ? value : null;
                            if (_pos == propertyNameOrValueStart)
                            {
                                Error(Diagnostics._expectedAUnicodePropertyName);
                            }
                            else if (propertyName == null)
                            {
                                Error(Diagnostics._unknownUnicodePropertyName, propertyNameOrValueStart, _pos - propertyNameOrValueStart);
                            }

                            _pos++;
                            var propertyValueStart = _pos;
                            var propertyValue = ScanWordCharacters();
                            if (_pos == propertyValueStart)
                            {
                                Error(Diagnostics._expectedAUnicodePropertyValue);
                            }
                            else if (propertyName != null &&
                                (!_valuesOfNonBinaryUnicodeProperties.TryGetValue(propertyName, out var x) ||
                                 x?.Contains(propertyValue) != true))
                            {
                                Error(Diagnostics._unknownUnicodePropertyValue, propertyValueStart, _pos - propertyValueStart);
                            }
                        }
                        else
                        {
                            if (_pos == propertyNameOrValueStart)
                            {
                                Error(Diagnostics._expectedAUnicodePropertyNameOrValue);
                            }
                            else if (_binaryUnicodePropertiesOfStrings.Contains(propertyNameOrValue))
                            {
                                if (!unicodeSetsMode)
                                {
                                    Error(
                                        Diagnostics._anyUnicodePropertyThatWouldPossiblyMatchMoreThanASingleCharacterIsOnlyAvailableWhenTheUnicodeSetsVFlagIsSet,
                                        propertyNameOrValueStart,
                                        _pos - propertyNameOrValueStart);
                                }
                                else if (isCharacterComplement)
                                {
                                    Error(
                                        Diagnostics._anythingThatWouldPossiblyMatchMoreThanASingleCharacterIsInvalidInsideANegatedCharacterClass,
                                        propertyNameOrValueStart,
                                        _pos - propertyNameOrValueStart);
                                }
                                else
                                {
                                    mayContainStrings = true;
                                }
                            }
                            else if (!_valuesOfNonBinaryUnicodeProperties["General_Category"].Contains(propertyNameOrValue) &&
                                !_binaryUnicodeProperties.Contains(propertyNameOrValue))
                            {
                                Error(
                                    Diagnostics._unknownUnicodePropertyNameOrValue,
                                    propertyNameOrValueStart,
                                    _pos - propertyNameOrValueStart);
                            }
                        }

                        ScanExpectedChar(CharacterCodes.CloseBrace);
                        if (!anyUnicodeMode)
                        {
                            Error(
                                Diagnostics._unicodePropertyValueExpressionsAreOnlyAvailableWhenTheUnicodeUFlagOrTheUnicodeSetsVFlagIsSet,
                                start,
                                _pos - start);
                        }
                    }
                    else if (anyUnicodeModeOrNonAnnexB)
                    {
                        Error(Diagnostics._argument0MustBeFollowedByAUnicodePropertyValueExpressionEnclosedInBraces, _pos - 2, 2, ch);
                    }
                    else
                    {
                        _pos--;

                        return false;
                    }
                    return true;
            }

            return false;
        }

        string ScanWordCharacters()
        {
            using var recursion = _budget.Enter(_pos);

            var value = "";
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ch = CharCodeChecked(_pos);
                if (ch == CharacterCodes.EndOfFile || !IsWordCharacter(ch))
                {
                    break;
                }

                value += ch;
                _pos++;
            }

            return value;
        }

        string ScanSourceCharacter()
        {
            using var recursion = _budget.Enter(_pos);

            var size = anyUnicodeMode ? CharSize(CodePointChecked(_pos)) : 1;
            _pos += size;

            return size > 0 ? _text[(_pos - size).._pos] : "";
        }

        void ScanExpectedChar(int ch)
        {
            using var recursion = _budget.Enter(_pos);

            if (CharCodeChecked(_pos) == ch)
            {
                _pos++;
            }
            else
            {
                Error(Diagnostics._argument0Expected, _pos, 0, (char)ch);
            }
        }

        ScanDisjunction(isInGroup: false);

        groupNameReferences?.ForEach(reference =>
        {
            if (!groupSpecifiers?.Contains(reference.Name!) != true)
            {
                Error(
                    Diagnostics._thereIsNoCapturingGroupNamed0InThisRegularExpression,
                    reference.Pos,
                    (reference.End ?? 0) - (reference.Pos ?? 0),
                    reference.Name);
            }
        });

        decimalEscapes?.ForEach(escape =>
        {
            // Although a DecimalEscape with a value greater than the number of capturing groups
            // is treated as either a LegacyOctalEscapeSequence or an IdentityEscape in Annex B,
            // an error is nevertheless reported since it's most likely a mistake.
            if (escape.Value > numberOfCapturingGroups)
            {
                if (numberOfCapturingGroups != 0)
                {
                    Error(
                        Diagnostics._thisBackreferenceRefersToAGroupThatDoesNotExistThereAreOnly0CapturingGroupsInThisRegularExpression,
                        escape.Pos,
                        (escape.End ?? 0) - (escape.Pos ?? 0),
                        numberOfCapturingGroups);
                }
                else
                {
                    Error(
                        Diagnostics._thisBackreferenceRefersToAGroupThatDoesNotExistThereAreNoCapturingGroupsInThisRegularExpression,
                        escape.Pos,
                        (escape.End ?? 0) - (escape.Pos ?? 0));
                }
            }
        });
    }

    private void CheckRegularExpressionFlagAvailability(RegularExpressionFlags flag, int size)
    {
        var availableFrom = _regExpFlagToFirstAvailableLanguageVersion.TryGetValue(flag, out var v) ? (ScriptTarget)v : (ScriptTarget?)null;
        if (availableFrom != null && _languageVersion < availableFrom)
        {
            Error(
                Diagnostics._thisRegularExpressionFlagIsOnlyAvailableWhenTargeting0OrLater,
                _pos,
                size,
                Enum.GetName(typeof(ScriptTarget), availableFrom.Value));
        }
    }

    // Unconditionally back up and Scan a template expression portion.
    public SyntaxKind ReScanTemplateToken(bool isTaggedTemplate)
    {
        _pos = _tokenStart;
        _token = ScanTemplateAndSetTokenValue(!!isTaggedTemplate);

        return _token;
    }

    public SyntaxKind ReScanJsxToken(bool allowMultilineJsxText = true)
    {
        _pos = _tokenStart = _fullStartPos;
        _token = ScanJsxToken(allowMultilineJsxText);

        return _token;
    }

    public SyntaxKind ReScanLessThanToken()
    {
        if (_token == SyntaxKind.LessThanLessThanToken)
        {
            _pos = _tokenStart + 1;

            return _token = SyntaxKind.LessThanToken;
        }

        return _token;
    }

    public SyntaxKind ReScanHashToken()
    {
        if (_token == SyntaxKind.PrivateIdentifier)
        {
            _pos = _tokenStart + 1;

            return _token = SyntaxKind.HashToken;
        }

        return _token;
    }

    public SyntaxKind ReScanQuestionToken()
    {
        Debug.Assert(
            _token == SyntaxKind.QuestionQuestionToken,
            "'reScanQuestionToken' should only be called on a '??'");
        _pos = _tokenStart + 1;

        return _token = SyntaxKind.QuestionToken;
    }

    public SyntaxKind ScanJsxToken(bool allowMultilineJsxText = true)
    {
        _fullStartPos = _tokenStart = _pos;

        if (_pos >= _end)
        {
            return _token = SyntaxKind.EndOfFileToken;
        }

        var ch = _text[_pos];
        if (ch == CharacterCodes.LessThan)
        {
            if (_pos + 1 < _end && _text[_pos + 1] == CharacterCodes.Slash)
            {
                _pos += 2;

                return _token = SyntaxKind.LessThanSlashToken;
            }

            _pos++;

            return _token = SyntaxKind.LessThanToken;
        }

        if (ch == CharacterCodes.OpenBrace)
        {
            _pos++;

            return _token = SyntaxKind.OpenBraceToken;
        }

        // First non-whitespace character on this line.
        var firstNonWhitespace = 0;

        // These initial values are special because the first line is:
        // firstNonWhitespace = 0 to indicate that we want leading whitespace,

        while (_pos < _end)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            ch = _text[_pos];
            if (ch == CharacterCodes.OpenBrace)
            {
                break;
            }

            if (ch == CharacterCodes.LessThan)
            {
                if (IsConflictMarkerTrivia(_text, _pos))
                {
                    _pos = ScanConflictMarkerTrivia(_text, _pos, Error);

                    return _token = SyntaxKind.ConflictMarkerTrivia;
                }

                break;
            }

            if (ch == CharacterCodes.GreaterThan)
            {
                Error(Diagnostics._unexpectedTokenDidYouMeanOrGt, _pos, 1);
            }

            if (ch == CharacterCodes.CloseBrace)
            {
                Error(Diagnostics._unexpectedTokenDidYouMeanOrRbrace, _pos, 1);
            }

            // FirstNonWhitespace is 0, then we only see whitespaces so far. If we see a linebreak, we want to ignore that whitespaces.
            // i.e (- : whitespace)
            //      <div>----
            //      </div> becomes <div></div>
            //
            //      <div>----</div> becomes <div>----</div>
            if (IsLineBreak(ch) && firstNonWhitespace == 0)
            {
                firstNonWhitespace = -1;
            }
            else if (!allowMultilineJsxText && IsLineBreak(ch) && firstNonWhitespace > 0)
            {
                // Stop JsxText on each line during formatting. This allows the formatter to
                // indent each line correctly.
                break;
            }
            else if (!IsWhiteSpaceLike(ch))
            {
                firstNonWhitespace = _pos;
            }

            _pos++;
        }

        _tokenValue = _text[_fullStartPos.._pos];

        return firstNonWhitespace == -1 ? SyntaxKind.JsxTextAllWhiteSpaces : SyntaxKind.JsxText;
    }

    // Scans a JSX identifier; these differ from normal identifiers in that
    // they allow dashes
    internal SyntaxKind ScanJsxIdentifier()
    {
        if (TokenIsIdentifierOrKeyword(_token))
        {
            // An identifier or keyword has already been parsed - Check for a `-` or a single instance of `:` and then append it and
            // everything after it to the token
            // Do note that this means that `scanJsxIdentifier` effectively _mutates_ the visible token without advancing to a new token
            // Any caller should be expecting this behavior and should only read the pos or token value after calling it.
            while (_pos < _end)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ch = _text[_pos];
                if (ch == CharacterCodes.Minus)
                {
                    _tokenValue += "-";
                    _pos++;
                    continue;
                }

                var oldPos = _pos;
                _tokenValue += ScanIdentifierParts(); // reuse `scanIdentifierParts` so unicode escapes are handled
                if (_pos == oldPos)
                {
                    break;
                }
            }

            return GetIdentifierToken();
        }

        return _token;
    }

    public SyntaxKind ScanJsxAttributeValue()
    {
        _fullStartPos = _pos;

        switch ((int)_text[_pos])
        {
            case CharacterCodes.DoubleQuote:
            case CharacterCodes.SingleQuote:
                _tokenValue = ScanString(/*jsxAttributeString*/ true);
                return _token = SyntaxKind.StringLiteral;
            default:
                // If this Scans anything other than `{`, it's a parse error.
                return Scan();
        }
    }

    public SyntaxKind ReScanJsxAttributeValue()
    {
        _pos = _tokenStart = _fullStartPos;

        return ScanJsxAttributeValue();
    }

    internal SyntaxKind ScanJSDocCommentTextToken(bool inBackticks)
    {
        _fullStartPos = _tokenStart = _pos;
        _tokenFlags = TokenFlags.None;
        if (_pos >= _end)
        {
            return _token = SyntaxKind.EndOfFileToken;
        }

        for (var ch = _text[_pos]; _pos < _end && (!IsLineBreak(ch) && ch != CharacterCodes.Backtick); ch = CharCodeUnchecked(++_pos))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!inBackticks)
            {
                if (ch == CharacterCodes.OpenBrace)
                {
                    break;
                }
                else if (
                    ch == CharacterCodes.At
                    && _pos - 1 >= 0 && IsWhiteSpaceSingleLine(_text[_pos - 1])
                    && !(_pos + 1 < _end && IsWhiteSpaceLike(_text[_pos + 1]))
                )
                {
                    // @ doesn't start a new tag inside ``, and elsewhere, only after whitespace and before non-whitespace
                    break;
                }
            }
        }

        if (_pos == _tokenStart)
        {
            return ScanJsDocToken();
        }

        _tokenValue = _text[_tokenStart.._pos];

        return _token = SyntaxKind.JSDocCommentTextToken;
    }

    internal SyntaxKind ScanJsDocToken()
    {
        _fullStartPos = _tokenStart = _pos;
        _tokenFlags = TokenFlags.None;
        if (_pos >= _end)
        {
            return _token = SyntaxKind.EndOfFileToken;
        }

        var ch = _text[_pos];
        _pos += CharSize(ch);
        switch ((int)ch)
        {
            case CharacterCodes.Tab:
            case CharacterCodes.VerticalTab:
            case CharacterCodes.FormFeed:
            case CharacterCodes.Space:
                while (_pos < _end && IsWhiteSpaceSingleLine(_text[_pos]))
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    _pos++;
                }
                return _token = SyntaxKind.WhitespaceTrivia;
            case CharacterCodes.At:
                return _token = SyntaxKind.AtToken;
            case CharacterCodes.CarriageReturn:
                if (_text[_pos] == CharacterCodes.LineFeed)
                {
                    _pos++;
                }
                _tokenFlags |= TokenFlags.PrecedingLineBreak;
                return _token = SyntaxKind.NewLineTrivia;

            case CharacterCodes.LineFeed:
                _tokenFlags |= TokenFlags.PrecedingLineBreak;
                return _token = SyntaxKind.NewLineTrivia;

            case CharacterCodes.Asterisk:
                return _token = SyntaxKind.AsteriskToken;

            case CharacterCodes.OpenBrace:
                return _token = SyntaxKind.OpenBraceToken;

            case CharacterCodes.CloseBrace:
                return _token = SyntaxKind.CloseBraceToken;

            case CharacterCodes.OpenBracket:
                return _token = SyntaxKind.OpenBracketToken;

            case CharacterCodes.CloseBracket:
                return _token = SyntaxKind.CloseBracketToken;

            case CharacterCodes.OpenParen:
                return _token = SyntaxKind.OpenParenToken;

            case CharacterCodes.CloseParen:
                return _token = SyntaxKind.CloseParenToken;

            case CharacterCodes.LessThan:
                return _token = SyntaxKind.LessThanToken;

            case CharacterCodes.GreaterThan:
                return _token = SyntaxKind.GreaterThanToken;

            case CharacterCodes.Equals:
                return _token = SyntaxKind.EqualsToken;

            case CharacterCodes.Comma:
                return _token = SyntaxKind.CommaToken;

            case CharacterCodes.Dot:
                return _token = SyntaxKind.DotToken;

            case CharacterCodes.Backtick:
                return _token = SyntaxKind.BacktickToken;

            case CharacterCodes.Hash:
                return _token = SyntaxKind.HashToken;

            case CharacterCodes.Backslash:
                _pos--;
                var extendedCookedChar = PeekExtendedUnicodeEscape();
                if (extendedCookedChar >= 0 && IsIdentifierStart(extendedCookedChar, _languageVersion))
                {
                    _tokenValue = ScanExtendedUnicodeEscape(shouldEmitInvalidEscapeError: true) + ScanIdentifierParts();

                    return _token = GetIdentifierToken();
                }

                var cookedChar = PeekUnicodeEscape();
                if (cookedChar >= 0 && IsIdentifierStart(cookedChar, _languageVersion))
                {
                    _pos += 6;
                    _tokenFlags |= TokenFlags.UnicodeEscape;
                    _tokenValue = (char)cookedChar + ScanIdentifierParts();

                    return _token = GetIdentifierToken();
                }
                _pos++;
                return _token = SyntaxKind.Unknown;
        }

        if (IsIdentifierStart(ch, _languageVersion))
        {
            var ch2 = ch;
            while (_pos < _end && IsIdentifierPart(ch2 = _text[_pos], _languageVersion) || _text[_pos] == CharacterCodes.Minus)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _pos += CharSize(ch2);
            }

            _tokenValue = _text[_tokenStart.._pos];
            if (ch2 == CharacterCodes.Backslash)
            {
                _tokenValue += ScanIdentifierParts();
            }

            return _token = GetIdentifierToken();
        }
        else
        {
            return _token = SyntaxKind.Unknown;
        }
    }

    internal static bool IsFalsy(object? obj)
    {
        return obj is null or false or 0 or "" or SyntaxKind.Unknown ||
            (obj is not true and not string && int.TryParse(obj.ToString(), out int i) && i == 0);
    }

    private T SpeculationHelper<T>(Func<T> callback, bool isLookahead)
    {
        var savePos = _pos;
        var saveStartPos = _fullStartPos;
        var saveTokenPos = _tokenStart;
        var saveToken = _token;
        var saveTokenValue = _tokenValue;
        var saveTokenFlags = _tokenFlags;
        var result = callback();

        // If our callback returned something 'falsy' or we're just looking ahead,
        // then unconditionally restore us to where we were.
        if (isLookahead || IsFalsy(result))
        {
            _pos = savePos;
            _fullStartPos = saveStartPos;
            _tokenStart = saveTokenPos;
            _token = saveToken;
            _tokenValue = saveTokenValue;
            _tokenFlags = saveTokenFlags;
        }

        return result;
    }

    public T LookAhead<T>(Func<T> callback)
    {
        return SpeculationHelper<T>(callback, isLookahead: true);
    }

    private T ScanRange<T>(int start, int length, Func<T> callback)
    {
        var saveEnd = _end;
        var savePos = _pos;
        var saveStartPos = _fullStartPos;
        var saveTokenPos = _tokenStart;
        var saveToken = _token;
        var saveTokenValue = _tokenValue;
        var saveTokenFlags = _tokenFlags;
        var saveErrorExpectations = _commentDirectives;

        SetText(_text, start, length);
        var result = callback();

        _end = saveEnd;
        _pos = savePos;
        _fullStartPos = saveStartPos;
        _tokenStart = saveTokenPos;
        _token = saveToken;
        _tokenValue = saveTokenValue;
        _tokenFlags = saveTokenFlags;
        _commentDirectives = saveErrorExpectations;

        return result;
    }

    public T TryScan<T>(Func<T> callback)
    {
        return SpeculationHelper<T>(callback, isLookahead: false);
    }

    public string GetText() => _text;

    internal void ClearCommentDirectives()
    {
        _commentDirectives = null;
    }

    public void SetOnError(ErrorCallback? errorCallback)
    {
        _onError = errorCallback;
    }

    public void SetText(string? newText, int? start = null, int? length = null)
    {
        _text = newText ?? "";
        _end = length == null ? _text.Length : (start ?? 0) + (length ?? 0);
        ResetTokenState(start ?? 0);
    }

    public void SetScriptTarget(ScriptTarget scriptTarget)
    {
        _languageVersion = scriptTarget;
    }

    public void SetLanguageVariant(LanguageVariant variant)
    {
        _languageVariant = variant;
    }

    public void SetScriptKind(ScriptKind kind)
    {
        _scriptKind = kind;
    }

    public void SetJSDocParsingMode(JSDocParsingMode kind)
    {
        _jsDocParsingMode = kind;
    }

    public void ResetTokenState(int position)
    {
        Debug.Assert(position >= 0);
        _pos = position;
        _fullStartPos = position;
        _tokenStart = position;
        _token = SyntaxKind.Unknown;
        _tokenValue = null!;
        _tokenFlags = TokenFlags.None;
    }

    public void SetSkipJsDocLeadingAsterisks(bool skip)
    {
        _skipJsDocLeadingAsterisks += skip ? 1 : -1;
    }
}
