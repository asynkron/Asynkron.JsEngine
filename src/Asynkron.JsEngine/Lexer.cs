using System.Globalization;

namespace Asynkron.JsEngine;

internal sealed record TemplateExpression(string ExpressionText);

internal sealed record RegexLiteralValue(string Pattern, string Flags);

internal sealed class Lexer(string source)
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
        ["yield"] = TokenType.Yield,
        ["async"] = TokenType.Async,
        ["await"] = TokenType.Await,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["null"] = TokenType.Null,
        ["undefined"] = TokenType.Undefined,
        ["typeof"] = TokenType.Typeof,
        ["import"] = TokenType.Import,
        ["export"] = TokenType.Export
    };

    private readonly string _source = source ?? string.Empty;
    private readonly List<Token> _tokens = [];
    private int _start;
    private int _current;
    private int _line = 1;
    private int _column = 1;

    public IReadOnlyList<Token> Tokenize()
    {
        while (!IsAtEnd)
        {
            _start = _current;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.Eof, string.Empty, null, _line, _column));
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
                    AddToken(TokenType.AmpAmp);
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
                    AddToken(TokenType.PipePipe);
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
                    AddToken(TokenType.QuestionQuestion);
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
            case '\r':
            case '\t':
                break;
            case '\n':
                _line++;
                _column = 1;
                break;
            case '"':
                ReadString();
                break;
            case '`':
                ReadTemplateLiteral();
                break;
            default:
                if (IsDigit(c))
                {
                    ReadNumber();
                }
                else if (IsAlpha(c))
                {
                    ReadIdentifier();
                }
                else
                {
                    throw new ParseException($"Unexpected character '{c}' on line {_line} column {_column}.");
                }
                break;
        }
    }

    private void SkipSingleLineComment()
    {
        while (!IsAtEnd && Peek() != '\n')
        {
            Advance();
        }
    }

    private void SkipMultiLineComment()
    {
        while (!IsAtEnd)
        {
            if (Peek() == '*' && PeekNext() == '/')
            {
                Advance(); // consume '*'
                Advance(); // consume '/'
                return;
            }
            if (Peek() == '\n')
            {
                _line++;
                _column = 1;
            }
            Advance();
        }
        throw new ParseException("Unterminated multi-line comment.");
    }

    private void ReadIdentifier()
    {
        while (IsAlphaNumeric(Peek()))
        {
            Advance();
        }

        var text = _source[_start.._current];
        if (Keywords.TryGetValue(text, out var keyword))
        {
            AddToken(keyword);
        }
        else
        {
            AddToken(TokenType.Identifier);
        }
    }

    private void ReadNumber()
    {
        while (IsDigit(Peek()))
        {
            Advance();
        }

        if (Peek() == '.' && IsDigit(PeekNext()))
        {
            Advance();
            while (IsDigit(Peek()))
            {
                Advance();
            }
        }

        var text = _source[_start.._current];
        var value = double.Parse(text, CultureInfo.InvariantCulture);
        AddToken(TokenType.Number, value);
    }

    private void ReadString()
    {
        while (!IsAtEnd && Peek() != '"')
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 1;
            }

            Advance();
        }

        if (IsAtEnd)
        {
            throw new ParseException("Unterminated string literal.");
        }

        Advance();
        var value = _source[(_start + 1)..(_current - 1)];
        AddToken(TokenType.String, value);
    }

    private void ReadSingleQuotedString()
    {
        while (!IsAtEnd && Peek() != '\'')
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 1;
            }

            Advance();
        }

        if (IsAtEnd)
        {
            throw new ParseException("Unterminated string literal.");
        }

        Advance();
        var value = _source[(_start + 1)..(_current - 1)];
        AddToken(TokenType.String, value);
    }

    private void ReadTemplateLiteral()
    {
        var parts = new List<object>();
        var currentString = new System.Text.StringBuilder();

        while (!IsAtEnd && Peek() != '`')
        {
            if (Peek() == '$' && PeekNext() == '{')
            {
                // Save the string part so far
                if (currentString.Length > 0)
                {
                    parts.Add(currentString.ToString());
                    currentString.Clear();
                }

                // Skip ${
                Advance(); // $
                Advance(); // {

                // Now we need to tokenize the expression inside ${}
                var expressionStart = _current;
                var braceCount = 1;

                while (!IsAtEnd && braceCount > 0)
                {
                    var c = Peek();
                    if (c == '{') braceCount++;
                    else if (c == '}') braceCount--;

                    if (braceCount > 0)
                    {
                        if (c == '\n')
                        {
                            _line++;
                            _column = 1;
                        }
                        Advance();
                    }
                }

                if (IsAtEnd)
                {
                    throw new ParseException("Unterminated template literal expression.");
                }

                // Extract the expression text
                var expressionText = _source[expressionStart..(_current)];
                parts.Add(new TemplateExpression(expressionText));

                // Skip the closing }
                Advance();
            }
            else
            {
                if (Peek() == '\n')
                {
                    _line++;
                    _column = 1;
                }
                currentString.Append(Advance());
            }
        }

        if (IsAtEnd)
        {
            throw new ParseException("Unterminated template literal.");
        }

        // Add any remaining string content
        if (currentString.Length > 0)
        {
            parts.Add(currentString.ToString());
        }

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

    private char Peek() => IsAtEnd ? '\0' : _source[_current];

    private char PeekNext() => _current + 1 >= _source.Length ? '\0' : _source[_current + 1];

    private bool IsAtEnd => _current >= _source.Length;

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAlpha(char c) => c is >= 'a' and <= 'z' || c is >= 'A' and <= 'Z' || c == '_' || c == '$';

    private static bool IsAlphaNumeric(char c) => IsAlpha(c) || IsDigit(c);

    private void AddToken(TokenType type)
        => AddToken(type, null);

    private void AddToken(TokenType type, object? literal)
    {
        var text = _source[_start.._current];
        _tokens.Add(new Token(type, text, literal, _line, _column));
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
        var pattern = new System.Text.StringBuilder();

        // Read pattern until unescaped /
        while (!IsAtEnd && Peek() != '/')
        {
            if (Peek() == '\\')
            {
                // Include escape sequences in the pattern
                pattern.Append(Advance());
                if (!IsAtEnd)
                {
                    pattern.Append(Advance());
                }
            }
            else if (Peek() == '\n')
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
        var flags = new System.Text.StringBuilder();
        while (!IsAtEnd && IsAlpha(Peek()))
        {
            flags.Append(Advance());
        }

        var regexValue = new RegexLiteralValue(pattern.ToString(), flags.ToString());
        AddToken(TokenType.RegexLiteral, regexValue);
    }
}
