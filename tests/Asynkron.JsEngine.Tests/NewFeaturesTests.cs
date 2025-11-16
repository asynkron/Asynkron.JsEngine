namespace Asynkron.JsEngine.Tests;

public class NewFeaturesTests
{
    // Single-quoted strings tests
    [Fact(Timeout = 2000)]
    public async Task SingleQuotedString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let message = 'Hello World'; message;");
        Assert.Equal("Hello World", result);
    }

    [Fact(Timeout = 2000)]
    public async Task SingleQuotedStringWithDoubleQuotes()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let message = 'He said \"Hello\"'; message;");
        Assert.Equal("He said \"Hello\"", result);
    }

    // Multi-line comment tests
    [Fact(Timeout = 2000)]
    public async Task MultiLineComment()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       /* This is a multi-line comment
                                                          spanning multiple lines */
                                                       let x = 5;
                                                       x;

                                           """);
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task MultiLineCommentBetweenCode()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = 5 /* inline comment */ + 3;
                                                       x;

                                           """);
        Assert.Equal(8d, result);
    }

    // Modulo operator tests
    [Fact(Timeout = 2000)]
    public async Task ModuloOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10 % 3; x;");
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ModuloOperatorNegative()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = -10 % 3; x;");
        Assert.Equal(-1d, result);
    }

    // Increment/Decrement operator tests
    [Fact(Timeout = 2000)]
    public async Task PostIncrementOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; let y = x++; y;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PostIncrementSideEffect()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; x++; x;");
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PreIncrementOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; let y = ++x; y;");
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PostDecrementOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; let y = x--; y;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PreDecrementOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; let y = --x; y;");
        Assert.Equal(4d, result);
    }

    // Compound assignment operator tests
    [Fact(Timeout = 2000)]
    public async Task PlusEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; x += 3; x;");
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task MinusEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10; x -= 3; x;");
        Assert.Equal(7d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task StarEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; x *= 3; x;");
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task SlashEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 15; x /= 3; x;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PercentEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10; x %= 3; x;");
        Assert.Equal(1d, result);
    }

    // Bitwise operator tests
    [Fact(Timeout = 2000)]
    public async Task BitwiseAndOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5 & 3; x;");
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BitwiseOrOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5 | 3; x;");
        Assert.Equal(7d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BitwiseXorOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5 ^ 3; x;");
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BitwiseNotOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = ~5; x;");
        Assert.Equal(-6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task LeftShiftOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5 << 2; x;");
        Assert.Equal(20d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task RightShiftOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 20 >> 2; x;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnsignedRightShiftOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = -5 >>> 1; x;");
        Assert.Equal(2147483645d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BitwiseAndEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; x &= 3; x;");
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BitwiseOrEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; x |= 3; x;");
        Assert.Equal(7d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BitwiseXorEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; x ^= 3; x;");
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task LeftShiftEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5; x <<= 2; x;");
        Assert.Equal(20d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task RightShiftEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 20; x >>= 2; x;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnsignedRightShiftEqualOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = -5; x >>>= 1; x;");
        Assert.Equal(2147483645d, result);
    }

    // Exponentiation operator tests
    [Fact(Timeout = 2000)]
    public async Task ExponentiationOperator()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("2 ** 3;");
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExponentiationWithNegativeExponent()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("2 ** -2;");
        Assert.Equal(0.25d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExponentiationWithDecimal()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1.5 ** 2;");
        Assert.Equal(2.25d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExponentiationRightAssociative()
    {
        await using var engine = new JsEngine();
        // 2 ** 3 ** 2 should be 2 ** (3 ** 2) = 2 ** 9 = 512
        var result = await engine.Evaluate("2 ** 3 ** 2;");
        Assert.Equal(512d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExponentiationPrecedence()
    {
        await using var engine = new JsEngine();
        // 10 + 2 ** 3 * 5 should be 10 + (2 ** 3) * 5 = 10 + 8 * 5 = 10 + 40 = 50
        var result = await engine.Evaluate("10 + 2 ** 3 * 5;");
        Assert.Equal(50d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExponentiationCompoundAssignment()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 2; x **= 3; x;");
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExponentiationInExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let base = 3; let exp = 4; base ** exp;");
        Assert.Equal(81d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ExponentiationZeroPower()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("5 ** 0;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task VariableHoisting_ConditionalDeclaration()
    {
        await using var engine = new JsEngine();
        var script = @"
function test(condition) {
    if (condition) {
        var x = 5;
    }
    return typeof x;
}
test(false);
";
        var result = await engine.Evaluate(script);
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task VariableHoisting_ConditionalAccess()
    {
        await using var engine = new JsEngine();
        var script = @"
function test(condition) {
    if (condition) {
        var x = 5;
    }
    if (x) {
        return 'truthy';
    } else {
        return 'falsy';
    }
}
test(false);
";
        var result = await engine.Evaluate(script);
        Assert.Equal("falsy", result);
    }
}
