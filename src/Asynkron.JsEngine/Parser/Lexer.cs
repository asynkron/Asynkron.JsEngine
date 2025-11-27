using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Parser;

internal sealed record TemplateExpression(string ExpressionText);

internal sealed record TemplateStringPart(string RawText, DecodedString Cooked);

internal sealed record RegexLiteralValue(string Pattern, string Flags);

public sealed class Lexer(string source)
{
    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
    {
        ["let"] = TokenType.Let,
        ["var"] = TokenType.Var,
        ["const"] = TokenType.Const,
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
        ["undefined"] = TokenType.Undefined,
        ["typeof"] = TokenType.Typeof,
        ["instanceof"] = TokenType.Instanceof,
        ["void"] = TokenType.Void,
        ["delete"] = TokenType.Delete,
        ["import"] = TokenType.Import,
        ["export"] = TokenType.Export
    };

    private readonly string _source = source ?? string.Empty;
    private readonly List<Token> _tokens = [];
    private int _column = 1;
    private int _current;
    private int _line = 1;
    private int _start;
    private int _startColumn = 1;
    private int _startLine = 1;

    private bool IsAtEnd => _current >= _source.Length;

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
                AddToken(TokenType.LeftBrace);
                break;
            case '}':
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
                else if (Match('.'))
                {
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
        var builder = new StringBuilder();
        if (firstChar == '\\')
        {
            builder.Append(ReadIdentifierEscape(backslashConsumed: true));
        }
        else
        {
            builder.Append(firstChar);
        }

        while (true)
        {
            if (Peek() == '\\')
            {
                builder.Append(ReadIdentifierEscape());
                continue;
            }

            var current = Peek();
            if (!IsIdentifierPart(current))
            {
                break;
            }

            builder.Append(Advance());
        }

        var text = builder.ToString();
        if (Keywords.TryGetValue(text, out var keyword))
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

        var hex = _source.Substring(_current, 4);
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
        var builder = new StringBuilder();
        builder.Append('#');

        if (Peek() == '\\')
        {
            builder.Append(ReadIdentifierEscape());
        }
        else
        {
            var first = Peek();
            if (!IsIdentifierStart(first))
            {
                throw new ParseException($"Expected identifier after '#' on line {_line} column {_column}.");
            }

            builder.Append(Advance());
        }

        while (true)
        {
            if (Peek() == '\\')
            {
                builder.Append(ReadIdentifierEscape());
                continue;
            }

            var current = Peek();
            if (!IsIdentifierPart(current))
            {
                break;
            }

            builder.Append(Advance());
        }

        var text = builder.ToString();
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
                // Hexadecimal literal
                var prefixStart = _start; // Remember where '0' started
                Advance(); // consume 'x' or 'X'
                var hexDigits = ReadDigitsWithSeparators(IsHexDigit, "hexadecimal");

                // Check for BigInt suffix 'n'
                if (Peek() == 'n')
                {
                    var nextChar = PeekNext();
                    var isEndOrNonAlphaNum = nextChar == '\0' || (!IsAlpha(nextChar) && !IsDigit(nextChar));
                    if (isEndOrNonAlphaNum)
                    {
                        Advance(); // consume 'n'
                        var bigIntValue = BigInteger.Parse("0" + hexDigits, NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture);
                        var value = new JsBigInt(bigIntValue);
                        AddToken(TokenType.BigInt, value);
                        return;
                    }
                }

                var hexBigInt = BigInteger.Parse("0" + hexDigits, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture);
                var hexValue = (double)hexBigInt;
                AddToken(TokenType.Number, hexValue);
                return;
            }

            if (next is 'o' or 'O')
            {
                // Octal literal
                var prefixStart = _start; // Remember where '0' started
                Advance(); // consume 'o' or 'O'
                var octalDigits = ReadDigitsWithSeparators(IsOctalDigit, "octal");

                // Check for BigInt suffix 'n'
                if (Peek() == 'n')
                {
                    var nextChar = PeekNext();
                    var isEndOrNonAlphaNum = nextChar == '\0' || (!IsAlpha(nextChar) && !IsDigit(nextChar));
                    if (isEndOrNonAlphaNum)
                    {
                        Advance(); // consume 'n'
                        // Convert octal string to BigInteger by parsing each digit
                        var bigIntValue = BigInteger.Zero;
                        foreach (var c in octalDigits)
                        {
                            bigIntValue = bigIntValue * 8 + (c - '0');
                        }

                        var value = new JsBigInt(bigIntValue);
                        AddToken(TokenType.BigInt, value);
                        return;
                    }
                }
                var octalBigInt = BigInteger.Zero;
                foreach (var c in octalDigits)
                {
                    octalBigInt = octalBigInt * 8 + (c - '0');
                }

                var octalValue = (double)octalBigInt;
                AddToken(TokenType.Number, octalValue);
                return;
            }

            if (next is 'b' or 'B')
            {
                // Binary literal
                var prefixStart = _start; // Remember where '0' started
                Advance(); // consume 'b' or 'B'
                var binaryDigits = ReadDigitsWithSeparators(IsBinaryDigit, "binary");

                // Check for BigInt suffix 'n'
                if (Peek() == 'n')
                {
                    var nextChar = PeekNext();
                    var isEndOrNonAlphaNum = nextChar == '\0' || (!IsAlpha(nextChar) && !IsDigit(nextChar));
                    if (isEndOrNonAlphaNum)
                    {
                        Advance(); // consume 'n'
                        // Convert binary string to BigInteger by parsing each digit
                        var bigIntValue = BigInteger.Zero;
                        foreach (var c in binaryDigits)
                        {
                            bigIntValue = bigIntValue * 2 + (c - '0');
                        }

                        var value = new JsBigInt(bigIntValue);
                        AddToken(TokenType.BigInt, value);
                        return;
                    }
                }
                var binaryBigInt = BigInteger.Zero;
                foreach (var c in binaryDigits)
                {
                    binaryBigInt = binaryBigInt * 2 + (c - '0');
                }

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

                var octalDigits = _source[_start.._current];
                var octalBigInt = BigInteger.Zero;
                foreach (var c in octalDigits)
                {
                    octalBigInt = octalBigInt * 8 + (c - '0');
                }

                var octalValue = (double)octalBigInt;
                AddToken(TokenType.Number, octalValue);
                return;
            }
        }

        // Regular decimal number
        ReadDigitsWithSeparators(IsDigit, "decimal", true);

        // Check for decimal point (makes it a regular number, not BigInt)
        var hasDecimal = false;
        if (Peek() == '.')
        {
            hasDecimal = true;
            Advance();
            if (IsDigit(Peek()))
            {
                ReadDigitsWithSeparators(IsDigit, "fractional");
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

                ReadDigitsWithSeparators(IsDigit, "exponent");

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
                var text = _source[_start..(_current - 1)].Replace("_", string.Empty); // exclude the 'n'
                var value = new JsBigInt(text);
                AddToken(TokenType.BigInt, value);
                return;
            }
        }

        // Regular number
        var text2 = _source[_start.._current].Replace("_", string.Empty);
        var value2 = double.Parse(text2, CultureInfo.InvariantCulture);
        AddToken(TokenType.Number, value2);
    }

    private void ReadLeadingDotNumber()
    {
        // We have already consumed the '.' and confirmed the next char is a digit.
        ReadDigitsWithSeparators(IsDigit, "fractional");

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

                ReadDigitsWithSeparators(IsDigit, "exponent");
            }
            else
            {
                throw new ParseException($"Invalid exponent in decimal literal at line {_line} column {_column}.");
            }
        }

        var text = _source[_start.._current].Replace("_", string.Empty);
        var value = double.Parse(text, CultureInfo.InvariantCulture);
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
        builder.Append(terminator);
        if (terminator == '\r' && Peek() == '\n')
        {
            builder.Append(Advance());
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

    private static bool IsDigit(char c)
    {
        return c is >= '0' and <= '9';
    }

    private static bool IsDigitOrUnderscore(char c)
    {
        return IsDigit(c) || c == '_';
    }

    private string ReadDigitsWithSeparators(Func<char, bool> isDigit, string context, bool hasLeadingDigit = false)
    {
        var builder = new StringBuilder();
        var sawDigit = hasLeadingDigit;
        var lastUnderscore = false;

        while (!IsAtEnd)
        {
            var c = Peek();
            if (isDigit(c))
            {
                builder.Append(c);
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

        return builder.ToString();
    }

    private static bool IsIdentifierStart(char c)
    {
        // Include Other_ID_Start code points (e.g. \u2118, \u212E, \u309B, \u309C) alongside the usual letter set.
        if (c == '$' || c == '_' || char.IsLetter(c) || c is '\u2118' or '\u212E' or '\u309B' or '\u309C')
        {
            return true;
        }

        var category = char.GetUnicodeCategory(c);
        return category is UnicodeCategory.LetterNumber or UnicodeCategory.OtherLetter
            or UnicodeCategory.TitlecaseLetter;
    }

    private static bool IsIdentifierPart(char c)
    {
        if (IsIdentifierStart(c) || IsDigit(c))
        {
            return true;
        }

        var category = char.GetUnicodeCategory(c);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.Format;
    }

    private static bool IsAlpha(char c)
    {
        return IsIdentifierStart(c);
    }

    private static bool IsAlphaNumeric(char c)
    {
        return IsIdentifierPart(c);
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
        // Common contexts: =, (, [, ,, {, :, ;, !, &, |, ?, return, throw, etc.
        if (_tokens.Count == 0)
        {
            return true; // Start of input
        }

        var lastToken = _tokens[^1].Type;
        return lastToken is
            TokenType.Equal or
            TokenType.LeftParen or
            TokenType.LeftBracket or
            TokenType.LeftBrace or
            TokenType.RightBrace or
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
            TokenType.LessEqual;
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
                            var hex = rawString.Substring(i + 2, 2);
                            if (int.TryParse(hex, NumberStyles.HexNumber, null, out var value))
                            {
                                result.Append((char)value);
                                i += 4;
                            }
                            else
                            {
                                // Invalid hex, keep the backslash and x
                                result.Append('\\');
                                result.Append('x');
                                i += 2;
                            }
                        }
                        else
                        {
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
                                var hexDigits = rawString.Substring(i + 3, closingBrace - (i + 3));
                                if (int.TryParse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                        out var codePoint) &&
                                    codePoint >= 0 && codePoint <= 0x10FFFF)
                                {
                                    result.Append(char.ConvertFromUtf32(codePoint));
                                    i = closingBrace + 1;
                                    break;
                                }
                            }

                            // Invalid escape, keep the backslash and u
                            result.Append('\\');
                            result.Append('u');
                            i += 2;
                            break;
                        }

                        // Unicode escape sequence \uHHHH
                        if (i + 5 < rawString.Length)
                        {
                            var hex = rawString.Substring(i + 2, 4);
                            if (int.TryParse(hex, NumberStyles.HexNumber, null, out var value))
                            {
                                result.Append((char)value);
                                i += 6;
                            }
                            else
                            {
                                // Invalid hex, keep the backslash and u
                                result.Append('\\');
                                result.Append('u');
                                i += 2;
                            }
                        }
                        else
                        {
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

        return new DecodedString(result.ToString(), hasLegacyOctal);

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
