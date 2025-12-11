using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Parser;

internal sealed record TemplateExpression(string ExpressionText);

internal sealed record TemplateStringPart(string RawText, DecodedString Cooked);

internal sealed record RegexLiteralValue(string Pattern, string Flags);

/// <summary>
/// Distinguishes different brace contexts for regex vs division disambiguation.
/// </summary>
internal enum BraceKind
{
    /// <summary>Object literal - division follows</summary>
    ObjectLiteral,
    /// <summary>Function/method/class body - division follows (produces a value)</summary>
    FunctionBody,
    /// <summary>Statement block (if/for/while/etc.) - regex follows (no value)</summary>
    StatementBlock
}

public sealed class Lexer(string source, bool allowHtmlComments = true)
{
    private readonly record struct DigitRun(int Start, int Length, bool HasSeparator)
    {
        public ReadOnlySpan<char> Slice(string source)
        {
            return source.AsSpan(Start, Length);
        }
    }

    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
    {
        ["let"] = TokenType.Let,
        ["var"] = TokenType.Var,
        ["const"] = TokenType.Const,
        ["using"] = TokenType.Using,
        ["class"] = TokenType.Class,
        ["extends"] = TokenType.Extends,
        ["function"] = TokenType.Function,
        ["switch"] = TokenType.Switch,
        ["case"] = TokenType.Case,
        ["default"] = TokenType.Default,
        ["try"] = TokenType.Try,
        ["catch"] = TokenType.Catch,
        ["finally"] = TokenType.Finally,
        ["with"] = TokenType.With,
        ["throw"] = TokenType.Throw,
        ["if"] = TokenType.If,
        ["else"] = TokenType.Else,
        ["for"] = TokenType.For,
        ["in"] = TokenType.In,
        ["of"] = TokenType.Of,
        ["while"] = TokenType.While,
        ["do"] = TokenType.Do,
        ["break"] = TokenType.Break,
        ["continue"] = TokenType.Continue,
        ["return"] = TokenType.Return,
        ["this"] = TokenType.This,
        ["super"] = TokenType.Super,
        ["new"] = TokenType.New,
        ["get"] = TokenType.Get,
        ["set"] = TokenType.Set,
        ["static"] = TokenType.Static,
        ["yield"] = TokenType.Yield,
        ["async"] = TokenType.Async,
        ["await"] = TokenType.Await,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["null"] = TokenType.Null,
        ["typeof"] = TokenType.Typeof,
        ["instanceof"] = TokenType.Instanceof,
        ["void"] = TokenType.Void,
        ["delete"] = TokenType.Delete,
        ["import"] = TokenType.Import,
        ["export"] = TokenType.Export
    };

    private readonly bool _allowHtmlComments = allowHtmlComments;

    private readonly string _source = source ?? string.Empty;
    private readonly List<Token> _tokens = [];
    private int _column = 1;
    private int _current;
    private int _line = 1;
    private int _start;
    private int _startColumn = 1;
    private int _startLine = 1;

    // Track brace context: ObjectLiteral/FunctionBody → division; StatementBlock → regex
    private readonly Stack<BraceKind> _braceKindStack = new();

    private bool IsAtEnd => _current >= _source.Length;

    private string SliceText(int start, int length) => new(_source.AsSpan(start, length));

    public IReadOnlyList<Token> Tokenize()
    {
        while (!IsAtEnd)
        {
            _start = _current;
            _startLine = _line;
            _startColumn = _column;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.Eof, string.Empty, null, _line, _column, _current, _current));
        return _tokens;
    }

    private void ScanToken()
    {
        var c = Advance();
        switch (c)
        {
            case '(':
                AddToken(TokenType.LeftParen);
                break;
            case ')':
                AddToken(TokenType.RightParen);
                break;
            case '{':
                // Determine the kind of brace context for regex/division disambiguation
                var braceKind = GetBraceKind();
                _braceKindStack.Push(braceKind);
                AddToken(TokenType.LeftBrace);
                break;
            case '}':
                // Pop the brace context stack (if not empty) and track the value
                // This must be done BEFORE AddToken so IsRegexContext can use it
                if (_braceKindStack.Count > 0)
                {
                    _lastPoppedBraceKind = _braceKindStack.Pop();
                }
                else
                {
                    _lastPoppedBraceKind = BraceKind.StatementBlock; // Default to statement block for safety
                }
                AddToken(TokenType.RightBrace);
                break;
            case '[':
                AddToken(TokenType.LeftBracket);
                break;
            case ']':
                AddToken(TokenType.RightBracket);
                break;
            case ',':
                AddToken(TokenType.Comma);
                break;
            case ':':
                AddToken(TokenType.Colon);
                break;
            case ';':
                AddToken(TokenType.Semicolon);
                break;
            case '@':
                AddToken(TokenType.At);
                break;
            case '#' when _start == 0 && Match('!'):
                // Hashbang comments (Annex B.1.3) are allowed at the start of
                // Script/Module source texts. Treat them as a single-line comment
                // so the rest of the source parses normally (e.g. directive prologue).
                SkipSingleLineComment();
                break;
            case '+':
                if (Match('+'))
                {
                    AddToken(TokenType.PlusPlus);
                }
                else if (Match('='))
                {
                    AddToken(TokenType.PlusEqual);
                }
                else
                {
                    AddToken(TokenType.Plus);
                }

                break;
            case '.':
                if (Match('.') && Match('.'))
                {
                    AddToken(TokenType.DotDotDot);
                }
                else if (IsDigit(Peek()))
                {
                    ReadLeadingDotNumber();
                }
                else
                {
                    AddToken(TokenType.Dot);
                }

                break;
            case '-':
                if (_allowHtmlComments && Peek() == '-' && PeekNext() == '>')
                {
                    var isLineStart = IsAtStartOfLineIgnoringWhitespace();
                    Advance(); // second '-'
                    Advance(); // '>'
                    if (isLineStart)
                    {
                        SkipSingleLineComment();
                        break;
                    }

                    AddToken(TokenType.MinusMinus);
                    AddToken(TokenType.Greater);
                    break;
                }

                if (Match('-'))
                {
                    AddToken(TokenType.MinusMinus);
                }
                else if (Match('='))
                {
                    AddToken(TokenType.MinusEqual);
                }
                else
                {
                    AddToken(TokenType.Minus);
                }

                break;
            case '*':
                if (Match('*'))
                {
                    AddToken(Match('=') ? TokenType.StarStarEqual : TokenType.StarStar);
                }
                else if (Match('='))
                {
                    AddToken(TokenType.StarEqual);
                }
                else
                {
                    AddToken(TokenType.Star);
                }

                break;
            case '&':
                if (Match('&'))
                {
                    AddToken(Match('=') ? TokenType.AmpAmpEqual : TokenType.AmpAmp);
                }
                else if (Match('='))
                {
                    AddToken(TokenType.AmpEqual);
                }
                else
                {
                    AddToken(TokenType.Amp);
                }

                break;
            case '|':
                if (Match('|'))
                {
                    AddToken(Match('=') ? TokenType.PipePipeEqual : TokenType.PipePipe);
                }
                else if (Match('='))
                {
                    AddToken(TokenType.PipeEqual);
                }
                else
                {
                    AddToken(TokenType.Pipe);
                }

                break;
            case '?':
                if (Match('?'))
                {
                    AddToken(Match('=') ? TokenType.QuestionQuestionEqual : TokenType.QuestionQuestion);
                }
                else if (Peek() == '.' && !char.IsDigit(PeekNext()))
                {
                    // OptionalChainingPunctuator: ?. [lookahead ∉ DecimalDigit]
                    // If the character after '.' is a digit, this is NOT optional chaining
                    // (e.g., `x ?.30 : y` is a ternary with `.30` as a number, not optional chaining)
                    Advance(); // consume the '.'
                    AddToken(TokenType.QuestionDot);
                }
                else
                {
                    AddToken(TokenType.Question);
                }

                break;
            case '/':
                if (Match('/'))
                {
                    SkipSingleLineComment();
                }
                else if (Match('*'))
                {
                    SkipMultiLineComment();
                }
                else if (IsRegexContext())
                {
                    ReadRegexLiteral();
                }
                else if (Match('='))
                {
                    AddToken(TokenType.SlashEqual);
                }
                else
                {
                    AddToken(TokenType.Slash);
                }

                break;
            case '!':
                if (Match('='))
                {
                    AddToken(Match('=') ? TokenType.BangEqualEqual : TokenType.BangEqual);
                }
                else
                {
                    AddToken(TokenType.Bang);
                }

                break;
            case '=':
                if (Match('='))
                {
                    AddToken(Match('=') ? TokenType.EqualEqualEqual : TokenType.EqualEqual);
                }
                else if (Match('>'))
                {
                    AddToken(TokenType.Arrow);
                }
                else
                {
                    AddToken(TokenType.Equal);
                }

                break;
            case '>':
                if (Match('>'))
                {
                    if (Match('>'))
                    {
                        AddToken(Match('=') ? TokenType.GreaterGreaterGreaterEqual : TokenType.GreaterGreaterGreater);
                    }
                    else
                    {
                        AddToken(Match('=') ? TokenType.GreaterGreaterEqual : TokenType.GreaterGreater);
                    }
                }
                else
                {
                    AddToken(Match('=') ? TokenType.GreaterEqual : TokenType.Greater);
                }

                break;
            case '<':
                if (_allowHtmlComments && Peek() == '!' && PeekNext() == '-' && PeekOffset(2) == '-')
                {
                    Advance();
                    Advance();
                    Advance();
                    SkipSingleLineComment();
                    break;
                }

                if (Match('<'))
                {
                    AddToken(Match('=') ? TokenType.LessLessEqual : TokenType.LessLess);
                }
                else
                {
                    AddToken(Match('=') ? TokenType.LessEqual : TokenType.Less);
                }

                break;
            case '%':
                if (Match('='))
                {
                    AddToken(TokenType.PercentEqual);
                }
                else
                {
                    AddToken(TokenType.Percent);
                }

                break;
            case '^':
                if (Match('='))
                {
                    AddToken(TokenType.CaretEqual);
                }
                else
                {
                    AddToken(TokenType.Caret);
                }

                break;
            case '~':
                AddToken(TokenType.Tilde);
                break;
            case '\'':
                ReadSingleQuotedString();
                break;
            case ' ': // ignore insignificant whitespace
            case '\t':
            case '\v':
            case '\f':
            case '\u00A0': // no-break space
            case '\uFEFF': // BOM
                break;
            case '\r':
                HandleLineTerminator('\r');
                break;
            case '\n':
                HandleLineTerminator('\n');
                break;
            case '\u2028': // Line Separator
            case '\u2029': // Paragraph Separator
                HandleLineTerminator(c);
                break;
            case '"':
                ReadString();
                break;
            case '`':
                ReadTemplateLiteral();
                break;
            case '#':
                ReadPrivateIdentifier();
                break;
            default:
                if (IsOtherWhitespace(c))
                {
                    break;
                }

                if (IsDigit(c))
                {
                    ReadNumber();
                }
                else if (IsIdentifierStart(c) || c == '\\')
                {
                    ReadIdentifier(c);
                }
                else
                {
                    throw new ParseException($"Unexpected character '{c}' on line {_line} column {_column}.");
                }

                break;
        }
    }

    private static bool IsOtherWhitespace(char c)
    {
        return c == '\u1680' || c == '\u2000' || c == '\u2001' || c == '\u2002' || c == '\u2003' ||
               c == '\u2004' || c == '\u2005' || c == '\u2006' || c == '\u2007' || c == '\u2008' ||
               c == '\u2009' || c == '\u200A' || c == '\u202F' || c == '\u205F' || c == '\u3000';
    }

    private void SkipSingleLineComment()
    {
        while (!IsAtEnd && Peek() != '\n')
        {
            if (IsLineTerminator(Peek()))
            {
                return;
            }

            Advance();
        }
    }

    private void SkipMultiLineComment()
    {
        while (!IsAtEnd)
        {
            var ch = Peek();
            if (ch == '*' && PeekNext() == '/')
            {
                Advance(); // consume '*'
                Advance(); // consume '/'
                return;
            }

            if (IsLineTerminator(ch))
            {
                ConsumeLineTerminator(ch);
                continue;
            }

            Advance();
        }

        throw new ParseException("Unterminated multi-line comment.");
    }

    private void ReadIdentifier(char firstChar)
    {
        StringBuilder? builder = null;
        var containsEscape = firstChar == '\\';

        if (firstChar == '\\')
        {
            builder = new StringBuilder();
            builder.Append(ReadIdentifierEscape(true));
        }

        while (true)
        {
            if (Peek() == '\\')
            {
                containsEscape = true;
                if (builder is null)
                {
                    builder = new StringBuilder(_current - _start + 16);
                    builder.Append(_source.AsSpan(_start, _current - _start));
                }

                builder.Append(ReadIdentifierEscape());
                continue;
            }

            var current = Peek();
            if (!IsIdentifierPart(current))
            {
                break;
            }

            if (builder is not null)
            {
                builder.Append(Advance());
            }
            else
            {
                Advance();
            }
        }

        var text = builder is null
            ? SliceText(_start, _current - _start)
            : builder.ToString();
        if (!containsEscape && Keywords.TryGetValue(text, out var keyword))
        {
            _tokens.Add(new Token(keyword, text, null, _startLine, _startColumn, _start, _current));
        }
        else
        {
            _tokens.Add(new Token(TokenType.Identifier, text, null, _startLine, _startColumn, _start, _current));
        }
    }

    private string ReadIdentifierEscape(bool backslashConsumed = false)
    {
        if (!backslashConsumed)
        {
            Advance(); // consume '\'
        }

        if (!Match('u'))
        {
            throw new ParseException("Invalid identifier escape sequence.");
        }

        if (Match('{'))
        {
            var start = _current;
            while (!IsAtEnd && Peek() != '}')
            {
                Advance();
            }

            if (IsAtEnd)
            {
                throw new ParseException("Unterminated identifier escape sequence.");
            }

            var hexDigits = _source[start.._current];
            if (!int.TryParse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
            {
                throw new ParseException("Invalid identifier escape sequence.");
            }

            Advance(); // consume }
            return char.ConvertFromUtf32(codePoint);
        }

        if (_current + 4 > _source.Length)
        {
            throw new ParseException("Invalid identifier escape sequence.");
        }

        var hex = SliceText(_current, 4);
        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            throw new ParseException("Invalid identifier escape sequence.");
        }

        _current += 4;
        _column += 4;
        return char.ConvertFromUtf32(value);
    }

    private void ReadPrivateIdentifier()
    {
        // '#' has already been consumed
        StringBuilder? builder = null;
        var containsEscape = false;

        if (Peek() == '\\')
        {
            containsEscape = true;
            builder = new StringBuilder();
            builder.Append('#');
            builder.Append(ReadIdentifierEscape());
        }
        else
        {
            var first = Peek();
            if (!IsIdentifierStart(first))
            {
                throw new ParseException($"Expected identifier after '#' on line {_line} column {_column}.");
            }

            Advance();
        }

        while (true)
        {
            if (Peek() == '\\')
            {
                containsEscape = true;
                if (builder is null)
                {
                    builder = new StringBuilder(_current - _start + 16);
                    builder.Append(_source.AsSpan(_start, _current - _start));
                }

                builder.Append(ReadIdentifierEscape());
                continue;
            }

            var current = Peek();
            if (!IsIdentifierPart(current))
            {
                break;
            }

            if (builder is not null)
            {
                builder.Append(Advance());
            }
            else
            {
                Advance();
            }
        }

        var text = builder is null
            ? SliceText(_start, _current - _start)
            : builder.ToString();
        _tokens.Add(new Token(TokenType.PrivateIdentifier, text, null, _startLine, _startColumn, _start, _current));
    }

    private void ReadNumber()
    {
        // Check for special numeric literals: 0x (hex), 0o (octal), 0b (binary)
        if (_source[_start] == '0' && _current < _source.Length)
        {
            var next = Peek();
            if (next is 'x' or 'X')
            {
                Advance(); // consume 'x' or 'X'
                var hexDigits = ReadDigitsWithSeparators(IsHexDigit, "hexadecimal");
                var hexSpan = hexDigits.Slice(_source);

                // Check for BigInt suffix 'n'
                if (Peek() == 'n')
                {
                    var nextChar = PeekNext();
                    var isEndOrNonAlphaNum = nextChar == '\0' || (!IsAlpha(nextChar) && !IsDigit(nextChar));
                    if (isEndOrNonAlphaNum)
                    {
                        Advance(); // consume 'n'
                        var value = new JsBigInt(ParseIntegerLiteral(hexSpan, 16, hexDigits.HasSeparator));
                        AddToken(TokenType.BigInt, value);
                        return;
                    }
                }

                var hexBigInt = ParseIntegerLiteral(hexSpan, 16, hexDigits.HasSeparator);
                var hexValue = (double)hexBigInt;
                AddToken(TokenType.Number, hexValue);
                return;
            }

            if (next is 'o' or 'O')
            {
                Advance(); // consume 'o' or 'O'
                var octalDigits = ReadDigitsWithSeparators(IsOctalDigit, "octal");
                var octalSpan = octalDigits.Slice(_source);

                // Check for BigInt suffix 'n'
                if (Peek() == 'n')
                {
                    var nextChar = PeekNext();
                    var isEndOrNonAlphaNum = nextChar == '\0' || (!IsAlpha(nextChar) && !IsDigit(nextChar));
                    if (isEndOrNonAlphaNum)
                    {
                        Advance(); // consume 'n'
                        var value = new JsBigInt(ParseIntegerLiteral(octalSpan, 8, octalDigits.HasSeparator));
                        AddToken(TokenType.BigInt, value);
                        return;
                    }
                }

                var octalBigInt = ParseIntegerLiteral(octalSpan, 8, octalDigits.HasSeparator);
                var octalValue = (double)octalBigInt;
                AddToken(TokenType.Number, octalValue);
                return;
            }

            if (next is 'b' or 'B')
            {
                Advance(); // consume 'b' or 'B'
                var binaryDigits = ReadDigitsWithSeparators(IsBinaryDigit, "binary");
                var binarySpan = binaryDigits.Slice(_source);

                // Check for BigInt suffix 'n'
                if (Peek() == 'n')
                {
                    var nextChar = PeekNext();
                    var isEndOrNonAlphaNum = nextChar == '\0' || (!IsAlpha(nextChar) && !IsDigit(nextChar));
                    if (isEndOrNonAlphaNum)
                    {
                        Advance(); // consume 'n'
                        var value = new JsBigInt(ParseIntegerLiteral(binarySpan, 2, binaryDigits.HasSeparator));
                        AddToken(TokenType.BigInt, value);
                        return;
                    }
                }

                var binaryBigInt = ParseIntegerLiteral(binarySpan, 2, binaryDigits.HasSeparator);
                var binaryValue = (double)binaryBigInt;
                AddToken(TokenType.Number, binaryValue);
                return;
            }
        }

        // Legacy octal literals (non-strict mode)
        if (_source[_start] == '0' && IsDigit(Peek()))
        {
            var idx = _current;
            var hasOctalDigits = false;
            var isPureOctal = true;
            var hasSeparators = false;
            while (idx < _source.Length)
            {
                var ch = _source[idx];
                if (!IsDigit(ch))
                {
                    break;
                }

                hasOctalDigits = true;
                if (ch is '8' or '9')
                {
                    isPureOctal = false;
                    break;
                }
                if (ch == '_')
                {
                    hasSeparators = true;
                }

                idx++;
            }

            if (hasOctalDigits && isPureOctal)
            {
                if (idx < _source.Length)
                {
                    var nextChar = _source[idx];
                    if (nextChar is '.' or 'e' or 'E' or 'n' || IsAlpha(nextChar))
                    {
                        isPureOctal = false;
                    }
                }
            }

            if (hasOctalDigits && isPureOctal)
            {
                while (_current < idx)
                {
                    Advance();
                }

                var octalSpan = _source.AsSpan(_start, _current - _start);
                var octalBigInt = ParseIntegerLiteral(octalSpan, 8, hasSeparators);

                var octalValue = (double)octalBigInt;
                AddToken(TokenType.Number, octalValue);
                return;
            }
        }

        // Regular decimal number
        var leadingDigits = ReadDigitsWithSeparators(IsDigit, "decimal", true);
        var hasSeparator = leadingDigits.HasSeparator;

        // Check for decimal point (makes it a regular number, not BigInt)
        var hasDecimal = false;
        if (Peek() == '.')
        {
            hasDecimal = true;
            Advance();
            if (IsDigit(Peek()))
            {
                var fractionalDigits = ReadDigitsWithSeparators(IsDigit, "fractional");
                hasSeparator |= fractionalDigits.HasSeparator;
            }
        }

        // Check for exponential notation (e or E followed by optional +/- and digits)
        if (Peek() == 'e' || Peek() == 'E')
        {
            var next = PeekNext();
            // Check if it's scientific notation: 'e' or 'E' followed by optional sign and digit
            if (IsDigit(next) || next == '+' || next == '-')
            {
                Advance(); // consume 'e' or 'E'

                // Consume optional sign
                if (Peek() == '+' || Peek() == '-')
                {
                    Advance();
                }

                // Must have at least one digit after the exponent
                if (!IsDigit(Peek()))
                {
                    throw new ParseException($"Expected digit after exponent on line {_line} column {_column}.");
                }

                var exponentDigits = ReadDigitsWithSeparators(IsDigit, "exponent");
                hasSeparator |= exponentDigits.HasSeparator;

                hasDecimal = true; // exponential notation makes it a regular number, not BigInt
            }
            else
            {
                throw new ParseException($"Invalid exponent in decimal literal at line {_line} column {_column}.");
            }
        }

        // Check for BigInt suffix 'n'
        if (!hasDecimal && Peek() == 'n')
        {
            // Check that 'n' is not part of a larger identifier
            var next = PeekNext();
            var isEndOrNonAlphaNum = next == '\0' || (!IsAlpha(next) && !IsDigit(next));

            if (isEndOrNonAlphaNum)
            {
                Advance(); // consume 'n'
                var digitsSpan = _source.AsSpan(_start, _current - _start - 1);
                var value = new JsBigInt(ParseIntegerLiteral(digitsSpan, 10, hasSeparator));
                AddToken(TokenType.BigInt, value);
                return;
            }
        }

        // Regular number
        var literalSpan = _source.AsSpan(_start, _current - _start);
        var value2 = ParseDoubleLiteral(literalSpan, hasSeparator);
        AddToken(TokenType.Number, value2);
    }

    private void ReadLeadingDotNumber()
    {
        // We have already consumed the '.' and confirmed the next char is a digit.
        var fractionalDigits = ReadDigitsWithSeparators(IsDigit, "fractional");
        var hasSeparator = fractionalDigits.HasSeparator;

        // Optional exponent
        if (Peek() is 'e' or 'E')
        {
            var next = PeekNext();
            if (IsDigit(next) || next is '+' or '-')
            {
                Advance(); // e/E
                if (Peek() is '+' or '-')
                {
                    Advance();
                }

                if (!IsDigit(Peek()))
                {
                    throw new ParseException($"Expected digit after exponent on line {_line} column {_column}.");
                }

                var exponentDigits = ReadDigitsWithSeparators(IsDigit, "exponent");
                hasSeparator |= exponentDigits.HasSeparator;
            }
            else
            {
                throw new ParseException($"Invalid exponent in decimal literal at line {_line} column {_column}.");
            }
        }

        var text = _source.AsSpan(_start, _current - _start);
        var value = ParseDoubleLiteral(text, hasSeparator);
        AddToken(TokenType.Number, value);
    }

    private void ReadString()
    {
        while (!IsAtEnd && Peek() != '"')
        {
            if (Peek() == '\\')
            {
                // Handle escape sequences: consume the backslash and the next character
                Advance(); // consume '\'
                if (!IsAtEnd)
                {
                    if (IsLineTerminator(Peek()))
                    {
                        ConsumeLineTerminator(Peek());
                        continue;
                    }

                    Advance(); // consume the escaped character
                }
            }
            else if (IsLineTerminator(Peek()))
            {
                ConsumeLineTerminator(Peek());
            }
            else
            {
                Advance();
            }
        }

        if (IsAtEnd)
        {
            throw new ParseException("Unterminated string literal.");
        }

        Advance();
        var rawValue = _source[(_start + 1)..(_current - 1)];
        var value = DecodeEscapeSequences(rawValue);
        AddToken(TokenType.String, value);
    }

    private void ReadSingleQuotedString()
    {
        while (!IsAtEnd && Peek() != '\'')
        {
            if (Peek() == '\\')
            {
                // Handle escape sequences: consume the backslash and the next character
                Advance(); // consume '\'
                if (!IsAtEnd)
                {
                    if (Peek() == '\n')
                    {
                        _line++;
                        _column = 1;
                    }

                    Advance(); // consume the escaped character
                }
            }
            else if (IsLineTerminator(Peek()))
            {
                ConsumeLineTerminator(Peek());
            }
            else
            {
                Advance();
            }
        }

        if (IsAtEnd)
        {
            throw new ParseException("Unterminated string literal.");
        }

        Advance();
        var rawValue = _source[(_start + 1)..(_current - 1)];
        var value = DecodeEscapeSequences(rawValue);
        AddToken(TokenType.String, value);
    }

    private void ReadTemplateLiteral()
    {
        var parts = new List<object>();
        var currentString = new StringBuilder();

        while (!IsAtEnd && Peek() != '`')
        {
            if (Peek() == '$' && PeekNext() == '{')
            {
                // Save the string part so far (include empty segments to preserve positions)
                var rawPart = currentString.ToString();
                parts.Add(new TemplateStringPart(rawPart, DecodeEscapeSequences(rawPart)));
                currentString.Clear();

                // Skip ${
                Advance(); // $
                Advance(); // {

                // Now we need to tokenize the expression inside ${}
                var expressionStart = _current;
                var braceCount = 1;

                while (!IsAtEnd && braceCount > 0)
                {
                    var c = Peek();
                    if (c == '{')
                    {
                        braceCount++;
                    }
                    else if (c == '}')
                    {
                        braceCount--;
                    }

                    if (braceCount <= 0)
                    {
                        break;
                    }

                    if (IsLineTerminator(c))
                    {
                        ConsumeLineTerminator(c);
                        continue;
                    }

                    Advance();
                }

                if (IsAtEnd)
                {
                    throw new ParseException("Unterminated template literal expression.");
                }

                // Extract the expression text
                var expressionText = _source[expressionStart.._current];
                parts.Add(new TemplateExpression(expressionText));

                // Skip the closing }
                Advance();
            }
            else if (Peek() == '\\')
            {
                // Handle escape sequences: append backslash and the next character
                currentString.Append(Advance()); // append and consume '\'
                if (!IsAtEnd)
                {
                    // For template literals, we need to preserve the raw content including
                    // line continuations (backslash followed by line terminator)
                    if (IsLineTerminator(Peek()))
                    {
                        AppendAndConsumeLineTerminator(currentString);
                    }
                    else
                    {
                        // Append the character after the backslash (could be `, $, or any other char)
                        currentString.Append(Advance());
                    }
                }
            }
            else
            {
                if (IsLineTerminator(Peek()))
                {
                    AppendAndConsumeLineTerminator(currentString);
                    continue;
                }

                currentString.Append(Advance());
            }
        }

        if (IsAtEnd)
        {
            throw new ParseException("Unterminated template literal.");
        }

        // Add any remaining string content (including trailing empty part)
        var finalRaw = currentString.ToString();
        parts.Add(new TemplateStringPart(finalRaw, DecodeEscapeSequences(finalRaw)));

        // Skip closing backtick
        Advance();

        // Store the parts as the literal value
        AddToken(TokenType.TemplateLiteral, parts);
    }

    private char Advance()
    {
        var c = _source[_current++];
        _column++;
        return c;
    }

    private bool Match(char expected)
    {
        if (IsAtEnd || _source[_current] != expected)
        {
            return false;
        }

        _current++;
        _column++;
        return true;
    }

    private static bool IsLineTerminator(char c)
    {
        return c is '\n' or '\r' or '\u2028' or '\u2029';
    }

    private void HandleLineTerminator(char terminator)
    {
        // Treat CRLF as a single line terminator
        if (terminator == '\r' && Match('\n'))
        {
            // Already advanced over '\n' via Match; column will be reset below.
        }

        _line++;
        _column = 1;
    }

    private void ConsumeLineTerminator(char terminator)
    {
        Advance(); // consume the terminator
        if (terminator == '\r' && Peek() == '\n')
        {
            Advance(); // consume LF in CRLF
        }

        _line++;
        _column = 1;
    }

    private void AppendAndConsumeLineTerminator(StringBuilder builder)
    {
        var terminator = Advance();
        // According to ECMA-262 11.8.6.1, the TRV (Template Raw Value) of:
        // - <LF> is code unit 0x000A
        // - <CR> is code unit 0x000A (normalized to LF)
        // - <CR><LF> is code unit 0x000A (normalized to single LF)
        // - <LS> (U+2028) is code unit 0x2028 (preserved)
        // - <PS> (U+2029) is code unit 0x2029 (preserved)
        // Only CR and CRLF are normalized to LF; LS and PS are preserved.
        if (terminator is '\u2028' or '\u2029')
        {
            builder.Append(terminator);
        }
        else
        {
            builder.Append('\n');
            if (terminator == '\r' && Peek() == '\n')
            {
                Advance(); // consume the LF in CRLF, but don't append it (already appended LF above)
            }
        }

        _line++;
        _column = 1;
    }

    private char Peek()
    {
        return IsAtEnd ? '\0' : _source[_current];
    }

    private char PeekNext()
    {
        return _current + 1 >= _source.Length ? '\0' : _source[_current + 1];
    }

    private char PeekOffset(int offset)
    {
        var index = _current + offset;
        return index >= _source.Length ? '\0' : _source[index];
    }

    private bool IsAtStartOfLineIgnoringWhitespace()
    {
        for (var i = _start - 1; i >= 0; i--)
        {
            var ch = _source[i];
            if (ch is ' ' or '\t')
            {
                continue;
            }

            if (IsLineTerminator(ch))
            {
                return true;
            }

            return false;
        }

        return true;
    }

    private static bool IsDigit(char c)
    {
        return c is >= '0' and <= '9';
    }

    private DigitRun ReadDigitsWithSeparators(Func<char, bool> isDigit, string context, bool hasLeadingDigit = false)
    {
        // If a leading digit was already consumed by the caller (e.g., initial decimal digit),
        // begin the span at _start so that digit is included in the returned slice.
        var start = hasLeadingDigit ? _start : _current;
        var sawDigit = hasLeadingDigit;
        var lastUnderscore = false;
        var hasSeparator = false;

        while (!IsAtEnd)
        {
            var c = Peek();
            if (isDigit(c))
            {
                sawDigit = true;
                lastUnderscore = false;
                Advance();
                continue;
            }

            if (c == '_')
            {
                if (!sawDigit || lastUnderscore)
                {
                    throw new ParseException(
                        $"Invalid numeric separator in {context} literal at line {_line} column {_column}.");
                }

                hasSeparator = true;
                lastUnderscore = true;
                Advance();
                continue;
            }

            break;
        }

        if (!sawDigit)
        {
            throw new ParseException($"Expected digit in {context} literal at line {_line} column {_column}.");
        }

        if (lastUnderscore)
        {
            throw new ParseException(
                $"Numeric separator may not be trailing in {context} literal at line {_line} column {_column}.");
        }

        return new DigitRun(start, _current - start, hasSeparator);
    }

    private double ParseDoubleLiteral(ReadOnlySpan<char> literalSpan, bool hasSeparator)
    {
        if (!hasSeparator)
        {
            return double.Parse(literalSpan, CultureInfo.InvariantCulture);
        }

        Span<char> stackBuffer = stackalloc char[Math.Min(literalSpan.Length, 256)];
        var cleaned = StripNumericSeparators(literalSpan, stackBuffer, out var heapBuffer);
        return double.Parse(cleaned, CultureInfo.InvariantCulture);
    }

    private static ReadOnlySpan<char> StripNumericSeparators(
        ReadOnlySpan<char> source,
        Span<char> stackBuffer,
        out char[]? heapBuffer)
    {
        heapBuffer = null;
        if (source.Length <= stackBuffer.Length)
        {
            var write = 0;
            foreach (var ch in source)
            {
                if (ch == '_')
                {
                    continue;
                }

                stackBuffer[write++] = ch;
            }

            return stackBuffer[..write];
        }

        var heap = new char[source.Length];
        var writeHeap = 0;
        foreach (var ch in source)
        {
            if (ch == '_')
            {
                continue;
            }

            heap[writeHeap++] = ch;
        }

        heapBuffer = heap;
        return heap.AsSpan(0, writeHeap);
    }

    private static BigInteger ParseIntegerLiteral(ReadOnlySpan<char> digits, int numberBase, bool hasSeparators)
    {
        if (!hasSeparators && numberBase == 10)
        {
            return BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        }

        return ParseIntegerDigits(digits, numberBase);
    }

    private static BigInteger ParseIntegerDigits(ReadOnlySpan<char> digits, int numberBase)
    {
        var value = BigInteger.Zero;
        foreach (var c in digits)
        {
            if (c == '_')
            {
                continue;
            }

            var digitValue = numberBase switch
            {
                2 when c is '0' or '1' => c - '0',
                8 when c is >= '0' and <= '7' => c - '0',
                10 when c is >= '0' and <= '9' => c - '0',
                16 when c is >= '0' and <= '9' => c - '0',
                16 when c is >= 'a' and <= 'f' => 10 + c - 'a',
                16 when c is >= 'A' and <= 'F' => 10 + c - 'A',
                _ => throw new ParseException($"Invalid digit '{c}' in base-{numberBase} literal.")
            };

            value = value * numberBase + digitValue;
        }

        return value;
    }

    private static bool IsIdentifierStart(char c)
    {
        // Disallow ASCII digits as a fast-path guard.
        if (char.IsDigit(c))
        {
            return false;
        }

        // Treat surrogate halves as valid identifier pieces so supplementary plane
        // ID_Start code points encoded as UTF-16 pairs are accepted.
        if (char.IsSurrogate(c))
        {
            return true;
        }

        // Include Other_ID_Start code points (e.g. \u2118, \u212E, \u309B, \u309C, \u1885, \u1886) alongside the usual letter set.
        if (c == '$' || c == '_' || char.IsLetter(c) ||
            c is '\u2118' or '\u212E' or '\u309B' or '\u309C' or '\u1885' or '\u1886')
        {
            return true;
        }

        var category = char.GetUnicodeCategory(c);
        if (category is UnicodeCategory.LetterNumber or UnicodeCategory.OtherLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter)
        {
            return true;
        }

        if (c >= 0x80 &&
            category is not UnicodeCategory.SpaceSeparator
                and not UnicodeCategory.LineSeparator
                and not UnicodeCategory.ParagraphSeparator
                and not UnicodeCategory.Control
                and not UnicodeCategory.Format)
        {
            // Accept remaining non-ASCII code points (including ones not yet in the runtime's
            // Unicode tables) to stay in sync with evolving ID_Start sets.
            // Note: Format category (Cf) characters like ZWNBSP (U+FEFF) are excluded because
            // they are treated as whitespace in ECMAScript, not identifier start characters.
            return true;
        }

        return false;
    }

    private static bool IsIdentifierPart(char c)
    {
        if (IsIdentifierStart(c) || IsDigit(c))
        {
            return true;
        }

        // Other_ID_Continue code points per ECMA-262 (includes ID_Continue and additional middle dots etc).
        if (c is '\u00B7' or '\u0387' or '\u19DA' || (c >= '\u1369' && c <= '\u1371'))
        {
            return true;
        }

        if (c is '\u200C' or '\u200D') // ZWNJ / ZWJ
        {
            return true;
        }

        var category = char.GetUnicodeCategory(c);
        if (category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.ModifierLetter)
        {
            // Note: Format category (Cf) is NOT included here because most Format chars
            // like Mongolian Vowel Separator (U+180E) are not valid in identifiers.
            // ZWNJ (U+200C) and ZWJ (U+200D) are handled explicitly above.
            return true;
        }

        if (c >= 0x80 &&
            category is not UnicodeCategory.SpaceSeparator
                and not UnicodeCategory.LineSeparator
                and not UnicodeCategory.ParagraphSeparator
                and not UnicodeCategory.Control
                and not UnicodeCategory.Format)
        {
            // Permit the broader set of non-ASCII code points for ID_Continue to match
            // latest Unicode revisions (ID_Start plus ID_Continue extras).
            // Format chars (like Mongolian Vowel Separator) are excluded; ZWNJ/ZWJ handled above.
            return true;
        }

        return false;
    }

    private static bool IsAlpha(char c)
    {
        return IsIdentifierStart(c);
    }

    private static bool IsHexDigit(char c)
    {
        return c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static bool IsOctalDigit(char c)
    {
        return c is >= '0' and <= '7';
    }

    private static bool IsBinaryDigit(char c)
    {
        return c is '0' or '1';
    }

    private void AddToken(TokenType type)
    {
        AddToken(type, null);
    }

    private void AddToken(TokenType type, object? literal)
    {
        var text = _source[_start.._current];
        _tokens.Add(new Token(type, text, literal, _startLine, _startColumn, _start, _current));
    }

    private bool IsRegexContext()
    {
        // A regex literal can appear after tokens that cannot be followed by a division operator
        // Common contexts: =, (, [, ,, {, :, ;, !, &, |, ?, return, throw, await, yield, etc.
        if (_tokens.Count == 0)
        {
            return true; // Start of input
        }

        var lastToken = _tokens[^1].Type;

        // Special case: RightBrace - it depends on what kind of brace it was
        // StatementBlock (if/for/while blocks) → regex context
        // ObjectLiteral or FunctionBody → division context
        if (lastToken == TokenType.RightBrace)
        {
            return WasLastBraceAStatementBlock();
        }

        return lastToken is
            TokenType.Equal or
            TokenType.LeftParen or
            TokenType.LeftBracket or
            TokenType.LeftBrace or
            TokenType.Comma or
            TokenType.Colon or
            TokenType.Semicolon or
            TokenType.Bang or
            TokenType.AmpAmp or
            TokenType.PipePipe or
            TokenType.Question or
            TokenType.QuestionQuestion or
            TokenType.Return or
            TokenType.Throw or
            TokenType.New or
            TokenType.EqualEqual or
            TokenType.EqualEqualEqual or
            TokenType.BangEqual or
            TokenType.BangEqualEqual or
            TokenType.Greater or
            TokenType.GreaterEqual or
            TokenType.Less or
            TokenType.LessEqual or
            // Keywords that can be followed by regex
            TokenType.Await or
            TokenType.Yield or
            TokenType.Case or
            TokenType.Typeof or
            TokenType.Void or
            TokenType.Delete or
            TokenType.In or
            TokenType.Instanceof or
            // Note: TokenType.Of is intentionally NOT included here.
            // 'of' is a contextual keyword only used in for-of loops where the
            // expression parsing handles regex. When 'of' appears as an identifier
            // in expressions like `instance/of/g`, division should follow, not regex.
            // Assignment operators
            TokenType.PlusEqual or
            TokenType.MinusEqual or
            TokenType.StarEqual or
            TokenType.SlashEqual or
            TokenType.PercentEqual or
            TokenType.StarStarEqual or
            TokenType.AmpEqual or
            TokenType.PipeEqual or
            TokenType.CaretEqual or
            TokenType.LessLessEqual or
            TokenType.GreaterGreaterEqual or
            TokenType.GreaterGreaterGreaterEqual or
            TokenType.AmpAmpEqual or
            TokenType.PipePipeEqual or
            TokenType.QuestionQuestionEqual or
            // Binary operators
            TokenType.Plus or
            TokenType.Minus or
            TokenType.Star or
            TokenType.Percent or
            TokenType.StarStar or
            TokenType.Amp or
            TokenType.Pipe or
            TokenType.Caret or
            TokenType.LessLess or
            TokenType.GreaterGreater or
            TokenType.GreaterGreaterGreater or
            TokenType.Tilde or
            // Arrow
            TokenType.Arrow;
    }

    /// <summary>
    /// Determines if the last } token closed a statement block (returns true for regex context).
    /// Only StatementBlock returns true; ObjectLiteral and FunctionBody return false (division context).
    /// </summary>
    private bool WasLastBraceAStatementBlock()
    {
        return _lastPoppedBraceKind == BraceKind.StatementBlock;
    }

    private BraceKind _lastPoppedBraceKind;

    /// <summary>
    /// Determines the kind of brace context for the current {.
    /// This is used to decide if / after the corresponding } should be regex or division.
    /// </summary>
    private BraceKind GetBraceKind()
    {
        if (_tokens.Count == 0)
        {
            return BraceKind.StatementBlock; // Start of file - must be a block statement
        }

        var lastToken = _tokens[^1].Type;

        // Check for function body context
        // After ) with function pattern: function() { }, async function() { }
        if (lastToken == TokenType.RightParen)
        {
            // Look back for function/async to identify function bodies
            var funcContext = IsFunctionBodyContext();
            if (funcContext.IsFunctionBody)
            {
                // Function declarations allow regex after closing brace (they're statements)
                // Function expressions produce values, so division follows
                return funcContext.IsDeclaration ? BraceKind.StatementBlock : BraceKind.FunctionBody;
            }
        }

        // Class body after class keyword or extends clause
        // Pattern: class { } or class X { } or class X extends Y { }
        var classContext = IsClassBodyContext();
        if (classContext.IsClassBody)
        {
            // Class declarations allow regex after closing brace (they're statements)
            // Class expressions produce values, so division follows (they're expressions)
            return classContext.IsDeclaration ? BraceKind.StatementBlock : BraceKind.FunctionBody;
        }

        // Arrow function body: () => { }
        if (lastToken == TokenType.Arrow)
        {
            return BraceKind.FunctionBody;
        }

        // Tokens that indicate { is an OBJECT LITERAL (not a block):
        // After these tokens, { starts an object literal expression
        var isObjectLiteralContext = lastToken is
            TokenType.Equal or           // x = { }
            TokenType.Colon or           // case x: { } or { a: { } }
            TokenType.LeftParen or       // f({ }) or ({ })
            TokenType.LeftBracket or     // [{ }]
            TokenType.Comma or           // [a, { }] or f(a, { })
            TokenType.Question or        // x ? { } : y
            TokenType.QuestionQuestion or // x ?? { }
            TokenType.Return or          // return { }
            TokenType.Throw or           // throw { }
            TokenType.New or             // new X({ })
            TokenType.PipePipe or        // x || { }
            TokenType.AmpAmp or          // x && { }
            TokenType.Plus or            // x + { } (weird but valid)
            TokenType.Minus or
            TokenType.Star or
            TokenType.Slash or
            TokenType.Percent or
            TokenType.StarStar or
            TokenType.Amp or
            TokenType.Pipe or
            TokenType.Caret or
            TokenType.LessLess or
            TokenType.GreaterGreater or
            TokenType.GreaterGreaterGreater or
            TokenType.EqualEqual or
            TokenType.EqualEqualEqual or
            TokenType.BangEqual or
            TokenType.BangEqualEqual or
            TokenType.Less or
            TokenType.LessEqual or
            TokenType.Greater or
            TokenType.GreaterEqual or
            TokenType.In or
            TokenType.Instanceof or
            TokenType.Of or
            TokenType.Typeof or          // typeof { } (weird)
            TokenType.Void or
            TokenType.Delete or
            // Assignment operators
            TokenType.PlusEqual or
            TokenType.MinusEqual or
            TokenType.StarEqual or
            TokenType.SlashEqual or
            TokenType.PercentEqual or
            TokenType.StarStarEqual or
            TokenType.AmpEqual or
            TokenType.PipeEqual or
            TokenType.CaretEqual or
            TokenType.LessLessEqual or
            TokenType.GreaterGreaterEqual or
            TokenType.GreaterGreaterGreaterEqual or
            TokenType.AmpAmpEqual or
            TokenType.PipePipeEqual or
            TokenType.QuestionQuestionEqual;

        if (isObjectLiteralContext)
        {
            return BraceKind.ObjectLiteral;
        }

        // Default: statement block (if/for/while/try/catch/etc.)
        return BraceKind.StatementBlock;
    }

    /// <summary>
    /// Checks if the current { is a function body by looking back through tokens.
    /// Called when last token is RightParen.
    /// Returns (isFunctionBody: bool, isDeclaration: bool).
    /// </summary>
    private (bool IsFunctionBody, bool IsDeclaration) IsFunctionBodyContext()
    {
        // Look back for function keyword or method-like patterns
        // We need to skip the parentheses and look for function/async
        var depth = 1; // Start at 1 because we already saw the )
        for (var i = _tokens.Count - 2; i >= 0; i--)
        {
            var token = _tokens[i].Type;
            if (token == TokenType.RightParen)
            {
                depth++;
            }
            else if (token == TokenType.LeftParen)
            {
                depth--;
                if (depth == 0)
                {
                    // Found the matching (, check what's before it
                    if (i > 0)
                    {
                        var beforeParen = _tokens[i - 1].Type;
                        // function(...) or async function(...) or *generator syntax
                        if (beforeParen is TokenType.Function)
                        {
                            // Anonymous function: function() { }
                            // Check if it's a declaration (can't be - anonymous functions are expressions)
                            return (true, false);
                        }
                        // function name(...) pattern
                        if (beforeParen is TokenType.Identifier)
                        {
                            // Check if there's function/async before the identifier
                            if (i > 1)
                            {
                                var beforeIdent = _tokens[i - 2].Type;
                                if (beforeIdent is TokenType.Function)
                                {
                                    // function name() { } - check if declaration
                                    var isDecl = IsDeclarationContext(i - 2);
                                    return (true, isDecl);
                                }
                                if (beforeIdent is TokenType.Star && i > 2)
                                {
                                    // *name() or function *name() { } - generator
                                    var beforeStar = _tokens[i - 3].Type;
                                    if (beforeStar is TokenType.Function)
                                    {
                                        var isDecl = IsDeclarationContext(i - 3);
                                        return (true, isDecl);
                                    }
                                    // Method generator: * name() in class/object
                                    return (true, false);
                                }
                                if (beforeIdent is TokenType.Async)
                                {
                                    // async name() { }
                                    var isDecl = IsDeclarationContext(i - 2);
                                    return (true, isDecl);
                                }
                                // async function name(...)
                                if (beforeIdent is TokenType.Identifier && i > 2 &&
                                    _tokens[i - 3].Type is TokenType.Async)
                                {
                                    // Check if there's 'function' between async and name
                                    if (i > 3 && _tokens[i - 2].Lexeme == "function")
                                    {
                                        var isDecl = IsDeclarationContext(i - 3);
                                        return (true, isDecl);
                                    }
                                    return (true, false);
                                }
                            }
                            // Method syntax: name(...) { } in object/class
                            // Methods are never declarations at the top level
                            return (true, false);
                        }
                        // get/set accessor: get name() or set name()
                        if (beforeParen is TokenType.Get or TokenType.Set)
                        {
                            return (true, false); // Accessors are always in objects/classes
                        }
                        // Static method: static name(...)
                        if (beforeParen is TokenType.Static)
                        {
                            return (true, false); // Static methods are always in classes
                        }
                        // Arrow function with params: (...) => {}
                        // This is handled separately via Arrow token
                    }
                    break;
                }
            }
        }
        return (false, false);
    }

    /// <summary>
    /// Checks if the current { is a class body and determines if it's a declaration or expression.
    /// Returns (isClassBody: bool, isDeclaration: bool).
    /// </summary>
    private (bool IsClassBody, bool IsDeclaration) IsClassBodyContext()
    {
        // Look back for class keyword
        // Patterns: class { }, class X { }, class X extends Y { }
        for (var i = _tokens.Count - 1; i >= 0; i--)
        {
            var token = _tokens[i].Type;
            if (token is TokenType.Class)
            {
                // Found class keyword - now check if it's a declaration or expression
                // Class declaration: class appears at statement position (after ; } { or at start)
                // Class expression: class appears in expression context (after = ( [ , : etc.)
                var isDeclaration = IsDeclarationContext(i);
                return (true, isDeclaration);
            }
            // Stop looking if we hit something that couldn't be part of a class header
            if (token is TokenType.Semicolon or TokenType.LeftBrace or TokenType.RightBrace or
                TokenType.Function or TokenType.Return or TokenType.If or TokenType.For or
                TokenType.While or TokenType.Do or TokenType.Switch or TokenType.Try)
            {
                return (false, false);
            }
        }
        return (false, false);
    }

    /// <summary>
    /// Determines if the token at the given index is at a declaration (statement) position.
    /// A declaration position is after ;, {, }, or at the start of the token stream.
    /// </summary>
    private bool IsDeclarationContext(int classOrFunctionIndex)
    {
        if (classOrFunctionIndex == 0)
        {
            return true; // Start of file is declaration context
        }

        var prevToken = _tokens[classOrFunctionIndex - 1].Type;

        // Declaration context: after statement terminators or block delimiters
        // These indicate the start of a new statement where declarations are allowed
        if (prevToken is TokenType.Semicolon or TokenType.LeftBrace or TokenType.RightBrace)
        {
            return true;
        }

        // After control flow keywords that start statements
        if (prevToken is TokenType.Else or TokenType.Do)
        {
            return true;
        }

        // After colon in case/default statements (statements follow)
        // But NOT after colon in object literals (expressions follow)
        // This is tricky - for now, treat colon as expression context
        // since object literals are more common

        // Everything else is expression context
        // This includes: = ( [ , : ? || && + - * / etc.
        return false;
    }

    private void ReadRegexLiteral()
    {
        var pattern = new StringBuilder();

        // Read pattern until unescaped /
        while (!IsAtEnd && Peek() != '/')
        {
            if (Peek() == '\\')
            {
                // Include escape sequences in the pattern
                pattern.Append(Advance());
                if (!IsAtEnd)
                {
                    var escapedChar = Advance();
                    if (IsLineTerminator(escapedChar))
                    {
                        throw new ParseException("Unterminated regex literal - newline in pattern.");
                    }

                    pattern.Append(escapedChar);
                }
            }
            else if (IsLineTerminator(Peek()))
            {
                throw new ParseException("Unterminated regex literal - newline in pattern.");
            }
            else if (Peek() == '[')
            {
                // Character class - read until ]
                pattern.Append(Advance());
                while (!IsAtEnd && Peek() != ']')
                {
                    if (Peek() == '\\')
                    {
                        pattern.Append(Advance());
                        if (!IsAtEnd)
                        {
                            pattern.Append(Advance());
                        }
                    }
                    else
                    {
                        pattern.Append(Advance());
                    }
                }

                if (!IsAtEnd && Peek() == ']')
                {
                    pattern.Append(Advance());
                }
            }
            else
            {
                pattern.Append(Advance());
            }
        }

        if (IsAtEnd)
        {
            throw new ParseException("Unterminated regex literal.");
        }

        // Skip closing /
        Advance();

        // Read flags (g, i, m, etc.)
        var flags = new StringBuilder();
        while (!IsAtEnd && IsAlpha(Peek()))
        {
            flags.Append(Advance());
        }

        var regexValue = new RegexLiteralValue(pattern.ToString(), flags.ToString());
        AddToken(TokenType.RegexLiteral, regexValue);
    }

    private static DecodedString DecodeEscapeSequences(string rawString)
    {
        var result = new StringBuilder(rawString.Length);
        var hasLegacyOctal = false;
        var hasInvalidEscape = false;
        var hasLegacyNonOctalEscape = false;
        var i = 0;
        while (i < rawString.Length)
        {
            if (rawString[i] == '\\' && i + 1 < rawString.Length)
            {
                var nextChar = rawString[i + 1];
                switch (nextChar)
                {
                    case 'n':
                        result.Append('\n');
                        i += 2;
                        break;
                    case 'r':
                        result.Append('\r');
                        i += 2;
                        break;
                    case 't':
                        result.Append('\t');
                        i += 2;
                        break;
                    case 'b':
                        result.Append('\b');
                        i += 2;
                        break;
                    case 'f':
                        result.Append('\f');
                        i += 2;
                        break;
                    case 'v':
                        result.Append('\v');
                        i += 2;
                        break;
                    case '0':
                    case >= '1' and <= '7':
                    {
                        var firstDigit = rawString[i + 1];
                        var (octalValue, length) = DecodeLegacyOctal(rawString, i + 1);
                        result.Append((char)octalValue);
                        if (!(length == 1 && firstDigit == '0'))
                        {
                            hasLegacyOctal = true;
                        }

                        i += 1 + length;
                        break;
                    }
                    case '8':
                    case '9':
                        // \8 and \9 are "legacy non-octal decimal escape sequences"
                        // They produce "8" and "9" in non-strict mode
                        // They should throw SyntaxError in strict mode
                        // In tagged templates, they make the cooked value undefined
                        hasLegacyNonOctalEscape = true;
                        // Append the digit as-is (sloppy mode behavior)
                        result.Append(nextChar);
                        i += 2;
                        break;
                    case '\\':
                        result.Append('\\');
                        i += 2;
                        break;
                    case '\'':
                        result.Append('\'');
                        i += 2;
                        break;
                    case '"':
                        result.Append('"');
                        i += 2;
                        break;
                    case 'x':
                        // Hexadecimal escape sequence \xHH
                        if (i + 3 < rawString.Length)
                        {
                            var hexSpan = rawString.AsSpan(i + 2, 2);
                            if (int.TryParse(hexSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                            {
                                result.Append((char)value);
                                i += 4;
                            }
                            else
                            {
                                // Invalid hex escape sequence - mark as invalid for tagged templates
                                hasInvalidEscape = true;
                                result.Append('\\');
                                result.Append('x');
                                i += 2;
                            }
                        }
                        else
                        {
                            // Incomplete hex escape sequence - mark as invalid
                            hasInvalidEscape = true;
                            result.Append('\\');
                            result.Append('x');
                            i += 2;
                        }

                        break;
                    case 'u':
                        // Unicode escape sequence \uHHHH or \u{...}
                        if (i + 2 < rawString.Length && rawString[i + 2] == '{')
                        {
                            var closingBrace = rawString.IndexOf('}', i + 3);
                            if (closingBrace > i + 3)
                            {
                                var hexDigits = rawString.AsSpan(i + 3, closingBrace - (i + 3));
                                if (int.TryParse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                        out var codePoint) &&
                                    codePoint is >= 0 and <= 0x10FFFF)
                                {
                                    result.Append(char.ConvertFromUtf32(codePoint));
                                    i = closingBrace + 1;
                                    break;
                                }
                            }

                            // Invalid unicode escape (e.g., \u{10FFFFF}, \u{g}, \u{0 without closing brace)
                            hasInvalidEscape = true;
                            result.Append('\\');
                            result.Append('u');
                            i += 2;
                            break;
                        }

                        // Unicode escape sequence \uHHHH
                        if (i + 5 < rawString.Length)
                        {
                            var hexSpan = rawString.AsSpan(i + 2, 4);
                            if (int.TryParse(hexSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                            {
                                result.Append((char)value);
                                i += 6;
                            }
                            else
                            {
                                // Invalid unicode escape (e.g., \u0g, \uXXXX where X is not hex)
                                hasInvalidEscape = true;
                                result.Append('\\');
                                result.Append('u');
                                i += 2;
                            }
                        }
                        else
                        {
                            // Incomplete unicode escape (e.g., \u0, \u00, \u000)
                            hasInvalidEscape = true;
                            result.Append('\\');
                            result.Append('u');
                            i += 2;
                        }

                        break;
                    default:
                        // Handle line continuations: backslash followed by line terminator
                        // According to ECMAScript spec, this should be removed from the string
                        if (nextChar == '\n')
                        {
                            // Line continuation with LF - skip both backslash and newline
                            i += 2;
                        }
                        else if (nextChar == '\r')
                        {
                            // Line continuation with CR or CRLF - skip backslash and line terminator(s)
                            i += 2;
                            // Check for CRLF
                            if (i < rawString.Length && rawString[i] == '\n')
                            {
                                i++;
                            }
                        }
                        else if (nextChar == '\u2028' || nextChar == '\u2029')
                        {
                            // Line continuation with Unicode LS or PS
                            i += 2;
                        }
                        else
                        {
                            // For any other character after \, just include the character itself
                            result.Append(nextChar);
                            i += 2;
                        }

                        break;
                }
            }
            else
            {
                result.Append(rawString[i]);
                i++;
            }
        }

        // For regular strings: Value contains the decoded string
        // For tagged templates: Value is null if hasInvalidEscape is true (cooked value is undefined)
        // Note: hasLegacyNonOctalEscape (\8, \9) is used for:
        //   - Strict mode validation in parser (should throw)
        //   - Tagged template cooked value calculation (should be undefined)
        var cookedValue = hasInvalidEscape ? null : result.ToString();
        return new DecodedString(cookedValue, hasLegacyOctal, hasInvalidEscape, hasLegacyNonOctalEscape);

        static (int Value, int Length) DecodeLegacyOctal(string raw, int start)
        {
            var first = raw[start];
            if (!IsOctalDigit(first))
            {
                return (first, 1);
            }

            var length = 1;
            var maxLength = first is >= '0' and <= '3' ? 3 : 2;

            var value = first - '0';
            var index = start + 1;
            while (index < raw.Length && length < maxLength && IsOctalDigit(raw[index]))
            {
                value = value * 8 + (raw[index] - '0');
                length++;
                index++;
            }

            return (value, length);
        }
    }
}
