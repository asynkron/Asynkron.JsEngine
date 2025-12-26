namespace Asynkron.JsParser.Tests;

public class LexerTests
{
    [Fact]
    public void Tokenize_SimpleVariable_ReturnsCorrectTokens()
    {
        var lexer = new Lexer("let x = 10;");
        var tokens = lexer.Tokenize();

        Assert.Equal(6, tokens.Count); // let, x, =, 10, ;, EOF
        Assert.Equal(TokenType.Let, tokens[0].Type);
        Assert.Equal(TokenType.Identifier, tokens[1].Type);
        Assert.Equal("x", tokens[1].Lexeme);
        Assert.Equal(TokenType.Equal, tokens[2].Type);
        Assert.Equal(TokenType.Number, tokens[3].Type);
        Assert.Equal(10.0, tokens[3].Literal);
        Assert.Equal(TokenType.Semicolon, tokens[4].Type);
        Assert.Equal(TokenType.Eof, tokens[5].Type);
    }

    [Fact]
    public void Tokenize_HexadecimalLiteral_ParsesCorrectly()
    {
        var lexer = new Lexer("0xFF");
        var tokens = lexer.Tokenize();

        Assert.Equal(2, tokens.Count); // number, EOF
        Assert.Equal(TokenType.Number, tokens[0].Type);
        Assert.Equal(255.0, tokens[0].Literal);
    }

    [Fact]
    public void Tokenize_HexadecimalLiteral_UppercaseX_ParsesCorrectly()
    {
        var lexer = new Lexer("0X0A");
        var tokens = lexer.Tokenize();

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Number, tokens[0].Type);
        Assert.Equal(10.0, tokens[0].Literal);
    }

    [Fact]
    public void Tokenize_OctalLiteral_ParsesCorrectly()
    {
        var lexer = new Lexer("0o77");
        var tokens = lexer.Tokenize();

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Number, tokens[0].Type);
        Assert.Equal(63.0, tokens[0].Literal);
    }

    [Fact]
    public void Tokenize_BinaryLiteral_ParsesCorrectly()
    {
        var lexer = new Lexer("0b101");
        var tokens = lexer.Tokenize();

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Number, tokens[0].Type);
        Assert.Equal(5.0, tokens[0].Literal);
    }

    [Fact]
    public void Tokenize_ScientificNotation_PositiveExponent()
    {
        var lexer = new Lexer("1e5");
        var tokens = lexer.Tokenize();

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Number, tokens[0].Type);
        Assert.Equal(100000.0, tokens[0].Literal);
    }

    [Fact]
    public void Tokenize_ScientificNotation_NegativeExponent()
    {
        var lexer = new Lexer("1e-3");
        var tokens = lexer.Tokenize();

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Number, tokens[0].Type);
        Assert.Equal(0.001, tokens[0].Literal);
    }

    [Fact]
    public void Tokenize_String_SingleQuotes()
    {
        var lexer = new Lexer("'hello'");
        var tokens = lexer.Tokenize();

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.String, tokens[0].Type);
        Assert.Equal("hello", tokens[0].Literal);
    }

    [Fact]
    public void Tokenize_String_DoubleQuotes()
    {
        var lexer = new Lexer("\"world\"");
        var tokens = lexer.Tokenize();

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.String, tokens[0].Type);
        Assert.Equal("world", tokens[0].Literal);
    }

    [Fact]
    public void Tokenize_Keywords_AllRecognized()
    {
        var keywords = new[]
        {
            ("let", TokenType.Let),
            ("var", TokenType.Var),
            ("const", TokenType.Const),
            ("function", TokenType.Function),
            ("class", TokenType.Class),
            ("if", TokenType.If),
            ("else", TokenType.Else),
            ("for", TokenType.For),
            ("while", TokenType.While),
            ("do", TokenType.Do),
            ("return", TokenType.Return),
            ("break", TokenType.Break),
            ("continue", TokenType.Continue),
            ("switch", TokenType.Switch),
            ("case", TokenType.Case),
            ("default", TokenType.Default),
            ("try", TokenType.Try),
            ("catch", TokenType.Catch),
            ("finally", TokenType.Finally),
            ("throw", TokenType.Throw),
            ("new", TokenType.New),
            ("this", TokenType.This),
            ("super", TokenType.Super),
            ("typeof", TokenType.Typeof),
            ("instanceof", TokenType.Instanceof),
            ("void", TokenType.Void),
            ("delete", TokenType.Delete),
            ("in", TokenType.In),
            ("of", TokenType.Of),
            ("true", TokenType.True),
            ("false", TokenType.False),
            ("null", TokenType.Null),
            ("async", TokenType.Async),
            ("await", TokenType.Await),
            ("yield", TokenType.Yield),
            ("import", TokenType.Import),
            ("export", TokenType.Export),
            ("extends", TokenType.Extends),
            ("static", TokenType.Static),
            ("get", TokenType.Get),
            ("set", TokenType.Set)
        };

        foreach (var (keyword, expectedType) in keywords)
        {
            var lexer = new Lexer(keyword);
            var tokens = lexer.Tokenize();
            Assert.Equal(expectedType, tokens[0].Type);
        }
    }

    [Fact]
    public void Tokenize_Operators_Recognized()
    {
        var lexer = new Lexer("+ - * / % ** && || ! == === != !== < > <= >= ?? ?. ++ --");
        var tokens = lexer.Tokenize();

        // Remove EOF token for easier testing
        var operatorTokens = tokens.Take(tokens.Count - 1).ToList();

        Assert.Contains(operatorTokens, t => t.Type == TokenType.Plus);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.Minus);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.Star);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.Slash);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.Percent);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.StarStar);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.AmpAmp);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.PipePipe);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.Bang);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.EqualEqual);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.EqualEqualEqual);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.BangEqual);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.BangEqualEqual);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.Less);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.Greater);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.LessEqual);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.GreaterEqual);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.QuestionQuestion);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.QuestionDot);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.PlusPlus);
        Assert.Contains(operatorTokens, t => t.Type == TokenType.MinusMinus);
    }

    [Fact]
    public void Tokenize_Punctuation_Recognized()
    {
        var lexer = new Lexer("{ } [ ] ( ) , . ; : ?");
        var tokens = lexer.Tokenize();

        var punctTokens = tokens.Take(tokens.Count - 1).ToList();

        Assert.Contains(punctTokens, t => t.Type == TokenType.LeftBrace);
        Assert.Contains(punctTokens, t => t.Type == TokenType.RightBrace);
        Assert.Contains(punctTokens, t => t.Type == TokenType.LeftBracket);
        Assert.Contains(punctTokens, t => t.Type == TokenType.RightBracket);
        Assert.Contains(punctTokens, t => t.Type == TokenType.LeftParen);
        Assert.Contains(punctTokens, t => t.Type == TokenType.RightParen);
        Assert.Contains(punctTokens, t => t.Type == TokenType.Comma);
        Assert.Contains(punctTokens, t => t.Type == TokenType.Dot);
        Assert.Contains(punctTokens, t => t.Type == TokenType.Semicolon);
        Assert.Contains(punctTokens, t => t.Type == TokenType.Colon);
        Assert.Contains(punctTokens, t => t.Type == TokenType.Question);
    }

    [Fact]
    public void Tokenize_LineComment_Skipped()
    {
        var lexer = new Lexer("let x = 1; // comment\nlet y = 2;");
        var tokens = lexer.Tokenize();

        // Should have tokens for both let statements, no comment token
        Assert.DoesNotContain(tokens, t => t.Literal?.ToString()?.Contains("comment") == true);
    }

    [Fact]
    public void Tokenize_BlockComment_Skipped()
    {
        var lexer = new Lexer("let x = /* inline comment */ 1;");
        var tokens = lexer.Tokenize();

        // Should parse correctly without comment
        Assert.DoesNotContain(tokens, t => t.Literal?.ToString()?.Contains("comment") == true);
    }

    [Fact]
    public void Tokenize_ArrowFunction()
    {
        var lexer = new Lexer("(x) => x + 1");
        var tokens = lexer.Tokenize();

        Assert.Contains(tokens, t => t.Type == TokenType.Arrow);
    }

    [Fact]
    public void Tokenize_SpreadOperator()
    {
        var lexer = new Lexer("...args");
        var tokens = lexer.Tokenize();

        Assert.Equal(TokenType.DotDotDot, tokens[0].Type);
        Assert.Equal(TokenType.Identifier, tokens[1].Type);
    }

    [Fact]
    public void Tokenize_TemplateLiteral()
    {
        var lexer = new Lexer("`hello ${name}`");
        var tokens = lexer.Tokenize();

        Assert.Contains(tokens, t => t.Type == TokenType.TemplateLiteral);
    }

    [Fact]
    public void Tokenize_BigInt()
    {
        var lexer = new Lexer("123n");
        var tokens = lexer.Tokenize();

        Assert.Equal(TokenType.BigInt, tokens[0].Type);
    }

    [Fact]
    public void Tokenize_TrackLineAndColumn()
    {
        var lexer = new Lexer("let\nx");
        var tokens = lexer.Tokenize();

        // 'let' should be on line 1
        Assert.Equal(1, tokens[0].Line);

        // 'x' should be on line 2
        Assert.Equal(2, tokens[1].Line);
    }
}
