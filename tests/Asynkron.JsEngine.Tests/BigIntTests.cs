using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

public class BigIntTests
{
    [Fact(Timeout = 2000)]
    public async Task BigIntLiteralParsing()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("123n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(123), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntLiteralParsingLargeNumber()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("9007199254740991n;"); // MAX_SAFE_INTEGER
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt("9007199254740991"), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntLiteralParsingVeryLargeNumber()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("12345678901234567890n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt("12345678901234567890"), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntAddition()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n + 20n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(30), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntSubtraction()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("50n - 20n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(30), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntMultiplication()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("6n * 7n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(42), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntDivision()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("50n / 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(5), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntDivisionTruncates()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("7n / 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(3), result); // JavaScript BigInt division truncates towards zero
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntModulo()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("17n % 5n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(2), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntExponentiation()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("2n ** 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(1024), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntExponentiationLarge()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("2n ** 100n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt("1267650600228229401496703205376"), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntNegation()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("-42n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(-42), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntBitwiseAnd()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("12n & 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(8), result); // 1100 & 1010 = 1000
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntBitwiseOr()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("12n | 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(14), result); // 1100 | 1010 = 1110
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntBitwiseXor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("12n ^ 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(6), result); // 1100 ^ 1010 = 0110
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task BigIntBitwiseNot()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("~5n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(-6), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntLeftShift()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("5n << 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(20), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntRightShift()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("20n >> 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(5), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntStrictEquality()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n === 10n;");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntStrictInequality()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n === 20n;");
        Assert.Equal(false, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntNotStrictlyEqualToNumber()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n === 10;");
        Assert.Equal(false, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntLooseEqualityWithNumber()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n == 10;");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntLooseEqualityWithNumberFalse()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n == 11;");
        Assert.Equal(false, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntLooseEqualityWithDecimalFalse()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n == 10.5;");
        Assert.Equal(false, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntGreaterThan()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("20n > 10n;");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntGreaterThanOrEqual()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n >= 10n;");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntLessThan()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n < 20n;");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntLessThanOrEqual()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n <= 10n;");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntCompareWithNumber()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("20n > 10;");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntCompareWithNumberLess()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("5n < 10;");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntTypeof()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof 42n;");
        Assert.Equal("bigint", result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntCannotMixWithNumberInAddition()
    {
        await using var engine = new JsEngine();
        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate("10n + 5;"));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntCannotMixWithNumberInSubtraction()
    {
        await using var engine = new JsEngine();
        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate("10n - 5;"));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntCannotMixWithNumberInMultiplication()
    {
        await using var engine = new JsEngine();
        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate("10n * 5;"));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntCannotMixWithNumberInDivision()
    {
        await using var engine = new JsEngine();
        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate("10n / 5;"));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntCannotUseUnsignedRightShift()
    {
        await using var engine = new JsEngine();
        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate("10n >>> 2n;"));
        Assert.Contains("BigInts have no unsigned right shift", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntDivisionByZeroThrows()
    {
        await using var engine = new JsEngine();
        await Assert.ThrowsAsync<DivideByZeroException>(async () => await engine.Evaluate("10n / 0n;"));
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntModuloByZeroThrows()
    {
        await using var engine = new JsEngine();
        await Assert.ThrowsAsync<DivideByZeroException>(async () => await engine.Evaluate("10n % 0n;"));
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntNegativeExponentiationThrows()
    {
        await using var engine = new JsEngine();
        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate("2n ** -1n;"));
        Assert.Contains("Exponent must be non-negative", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntVariableAssignment()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 123n; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(123), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntArithmeticExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; let y = 20n; x + y * 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(50), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntIncrement()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; ++x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(11), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntDecrement()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; --x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(9), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntPostfixIncrement()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; let y = x++; y;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(10), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntPostfixIncrementValue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; x++; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(11), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntPostfixDecrement()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; let y = x--; y;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(10), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntPostfixDecrementValue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; x--; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(9), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntZero()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("0n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(0), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntNegativeValue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("-123n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(-123), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntConditionalExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n > 5n ? 100n : 200n; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(100), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntLooseEqualityWithString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n == '10';");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntLooseEqualityWithStringFalse()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n == '11';");
        Assert.Equal(false, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntNotStrictlyEqualToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10n === '10';");
        Assert.Equal(false, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntStringConcatenation()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("'Value: ' + 42n;");
        Assert.Equal("Value: 42", result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntWithParentheses()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("(10n + 5n) * 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(30), result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntComplexExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let a = 5n; let b = 3n; (a + b) * (a - b);");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(16), result); // (5+3) * (5-3) = 8 * 2 = 16
    }
}
