using System.Globalization;

namespace Asynkron.JsEngine;

internal sealed class Lexer
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
        ["while"] = TokenType.While,
        ["do"] = TokenType.Do,
        ["break"] = TokenType.Break,
        ["continue"] = TokenType.Continue,
        ["return"] = TokenType.Return,
        ["this"] = TokenType.This,
        ["super"] = TokenType.Super,
        ["new"] = TokenType.New,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["null"] = TokenType.Null
    };

    private readonly string _source;
    private readonly List<Token> _tokens = new();
    private int _start;
    private int _current;
    private int _line = 1;
    private int _column = 1;

    public Lexer(string source)
    {
        _source = source ?? string.Empty;
    }

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
                AddToken(TokenType.Plus);
                break;
            case '.':
                AddToken(TokenType.Dot);
                break;
            case '-':
                AddToken(TokenType.Minus);
                break;
            case '*':
                AddToken(TokenType.Star);
                break;
            case '&':
                if (Match('&'))
                {
                    AddToken(TokenType.AmpAmp);
                }
                else
                {
                    throw new ParseException("Unexpected '&' without a matching '&'.");
                }
                break;
            case '|':
                if (Match('|'))
                {
                    AddToken(TokenType.PipePipe);
                }
                else
                {
                    throw new ParseException("Unexpected '|' without a matching '|'.");
                }
                break;
            case '?':
                if (Match('?'))
                {
                    AddToken(TokenType.QuestionQuestion);
                }
                else
                {
                    throw new ParseException("Unexpected '?' – conditional expressions are not yet supported.");
                }
                break;
            case '/':
                if (Match('/'))
                {
                    SkipSingleLineComment();
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
                AddToken(Match('=') ? TokenType.GreaterEqual : TokenType.Greater);
                break;
            case '<':
                AddToken(Match('=') ? TokenType.LessEqual : TokenType.Less);
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
}
