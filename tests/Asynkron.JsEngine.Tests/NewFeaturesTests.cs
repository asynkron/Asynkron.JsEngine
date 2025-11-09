using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class NewFeaturesTests
{
    // Single-quoted strings tests
    [Fact]
    public void SingleQuotedString()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let message = 'Hello World'; message;");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void SingleQuotedStringWithDoubleQuotes()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let message = 'He said \"Hello\"'; message;");
        Assert.Equal("He said \"Hello\"", result);
    }

    // Multi-line comment tests
    [Fact]
    public void MultiLineComment()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            /* This is a multi-line comment
               spanning multiple lines */
            let x = 5;
            x;
        ");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void MultiLineCommentBetweenCode()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let x = 5 /* inline comment */ + 3;
            x;
        ");
        Assert.Equal(8d, result);
    }

    // Modulo operator tests
    [Fact]
    public void ModuloOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 10 % 3; x;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public void ModuloOperatorNegative()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = -10 % 3; x;");
        Assert.Equal(-1d, result);
    }

    // Increment/Decrement operator tests
    [Fact]
    public void PostIncrementOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; let y = x++; y;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void PostIncrementSideEffect()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; x++; x;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void PreIncrementOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; let y = ++x; y;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void PostDecrementOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; let y = x--; y;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void PreDecrementOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; let y = --x; y;");
        Assert.Equal(4d, result);
    }

    // Compound assignment operator tests
    [Fact]
    public void PlusEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; x += 3; x;");
        Assert.Equal(8d, result);
    }

    [Fact]
    public void MinusEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 10; x -= 3; x;");
        Assert.Equal(7d, result);
    }

    [Fact]
    public void StarEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; x *= 3; x;");
        Assert.Equal(15d, result);
    }

    [Fact]
    public void SlashEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 15; x /= 3; x;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void PercentEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 10; x %= 3; x;");
        Assert.Equal(1d, result);
    }

    // Bitwise operator tests
    [Fact]
    public void BitwiseAndOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5 & 3; x;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public void BitwiseOrOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5 | 3; x;");
        Assert.Equal(7d, result);
    }

    [Fact]
    public void BitwiseXorOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5 ^ 3; x;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void BitwiseNotOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = ~5; x;");
        Assert.Equal(-6d, result);
    }

    [Fact]
    public void LeftShiftOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5 << 2; x;");
        Assert.Equal(20d, result);
    }

    [Fact]
    public void RightShiftOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 20 >> 2; x;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void UnsignedRightShiftOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = -5 >>> 1; x;");
        Assert.Equal(2147483645d, result);
    }

    [Fact]
    public void BitwiseAndEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; x &= 3; x;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public void BitwiseOrEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; x |= 3; x;");
        Assert.Equal(7d, result);
    }

    [Fact]
    public void BitwiseXorEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; x ^= 3; x;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void LeftShiftEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 5; x <<= 2; x;");
        Assert.Equal(20d, result);
    }

    [Fact]
    public void RightShiftEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 20; x >>= 2; x;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void UnsignedRightShiftEqualOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = -5; x >>>= 1; x;");
        Assert.Equal(2147483645d, result);
    }

    // Exponentiation operator tests
    [Fact]
    public void ExponentiationOperator()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("2 ** 3;");
        Assert.Equal(8d, result);
    }

    [Fact]
    public void ExponentiationWithNegativeExponent()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("2 ** -2;");
        Assert.Equal(0.25d, result);
    }

    [Fact]
    public void ExponentiationWithDecimal()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("1.5 ** 2;");
        Assert.Equal(2.25d, result);
    }

    [Fact]
    public void ExponentiationRightAssociative()
    {
        var engine = new JsEngine();
        // 2 ** 3 ** 2 should be 2 ** (3 ** 2) = 2 ** 9 = 512
        var result = engine.Evaluate("2 ** 3 ** 2;");
        Assert.Equal(512d, result);
    }

    [Fact]
    public void ExponentiationPrecedence()
    {
        var engine = new JsEngine();
        // 10 + 2 ** 3 * 5 should be 10 + (2 ** 3) * 5 = 10 + 8 * 5 = 10 + 40 = 50
        var result = engine.Evaluate("10 + 2 ** 3 * 5;");
        Assert.Equal(50d, result);
    }

    [Fact]
    public void ExponentiationCompoundAssignment()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 2; x **= 3; x;");
        Assert.Equal(8d, result);
    }

    [Fact]
    public void ExponentiationInExpression()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let base = 3; let exp = 4; base ** exp;");
        Assert.Equal(81d, result);
    }

    [Fact]
    public void ExponentiationZeroPower()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("5 ** 0;");
        Assert.Equal(1d, result);
    }
}
