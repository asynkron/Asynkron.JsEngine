using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class NewFeaturesTests
{
    // Single-quoted strings tests
    [Fact]
    public async Task SingleQuotedString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let message = 'Hello World'; message;");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public async Task SingleQuotedStringWithDoubleQuotes()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let message = 'He said \"Hello\"'; message;");
        Assert.Equal("He said \"Hello\"", result);
    }

    // Multi-line comment tests
    [Fact]
    public async Task MultiLineComment()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            /* This is a multi-line comment
               spanning multiple lines */
            let x = 5;
            x;
        ");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task MultiLineCommentBetweenCode()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = 5 /* inline comment */ + 3;
            x;
        ");
        Assert.Equal(8d, result);
    }

    // Modulo operator tests
    [Fact]
    public async Task ModuloOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10 % 3; x;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task ModuloOperatorNegative()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = -10 % 3; x;");
        Assert.Equal(-1d, result);
    }

    // Increment/Decrement operator tests
    [Fact]
    public async Task PostIncrementOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; let y = x++; y;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task PostIncrementSideEffect()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; x++; x;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task PreIncrementOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; let y = ++x; y;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task PostDecrementOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; let y = x--; y;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task PreDecrementOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; let y = --x; y;");
        Assert.Equal(4d, result);
    }

    // Compound assignment operator tests
    [Fact]
    public async Task PlusEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; x += 3; x;");
        Assert.Equal(8d, result);
    }

    [Fact]
    public async Task MinusEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10; x -= 3; x;");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task StarEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; x *= 3; x;");
        Assert.Equal(15d, result);
    }

    [Fact]
    public async Task SlashEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 15; x /= 3; x;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task PercentEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10; x %= 3; x;");
        Assert.Equal(1d, result);
    }

    // Bitwise operator tests
    [Fact]
    public async Task BitwiseAndOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5 & 3; x;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task BitwiseOrOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5 | 3; x;");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task BitwiseXorOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5 ^ 3; x;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task BitwiseNotOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = ~5; x;");
        Assert.Equal(-6d, result);
    }

    [Fact]
    public async Task LeftShiftOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5 << 2; x;");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task RightShiftOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 20 >> 2; x;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task UnsignedRightShiftOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = -5 >>> 1; x;");
        Assert.Equal(2147483645d, result);
    }

    [Fact]
    public async Task BitwiseAndEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; x &= 3; x;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task BitwiseOrEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; x |= 3; x;");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task BitwiseXorEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; x ^= 3; x;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task LeftShiftEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 5; x <<= 2; x;");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task RightShiftEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 20; x >>= 2; x;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task UnsignedRightShiftEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = -5; x >>>= 1; x;");
        Assert.Equal(2147483645d, result);
    }

    // Exponentiation operator tests
    [Fact]
    public async Task ExponentiationOperator()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("2 ** 3;");
        Assert.Equal(8d, result);
    }

    [Fact]
    public async Task ExponentiationWithNegativeExponent()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("2 ** -2;");
        Assert.Equal(0.25d, result);
    }

    [Fact]
    public async Task ExponentiationWithDecimal()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("1.5 ** 2;");
        Assert.Equal(2.25d, result);
    }

    [Fact]
    public async Task ExponentiationRightAssociative()
    {
        var engine = new JsEngine();
        // 2 ** 3 ** 2 should be 2 ** (3 ** 2) = 2 ** 9 = 512
        var result = engine.EvaluateSync("2 ** 3 ** 2;");
        Assert.Equal(512d, result);
    }

    [Fact]
    public async Task ExponentiationPrecedence()
    {
        var engine = new JsEngine();
        // 10 + 2 ** 3 * 5 should be 10 + (2 ** 3) * 5 = 10 + 8 * 5 = 10 + 40 = 50
        var result = engine.EvaluateSync("10 + 2 ** 3 * 5;");
        Assert.Equal(50d, result);
    }

    [Fact]
    public async Task ExponentiationCompoundAssignment()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 2; x **= 3; x;");
        Assert.Equal(8d, result);
    }

    [Fact]
    public async Task ExponentiationInExpression()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let base = 3; let exp = 4; base ** exp;");
        Assert.Equal(81d, result);
    }

    [Fact]
    public async Task ExponentiationZeroPower()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("5 ** 0;");
        Assert.Equal(1d, result);
    }
}
