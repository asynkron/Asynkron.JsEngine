using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class BigIntTests
{
    [Fact]
    public async Task BigIntLiteralParsing()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("123n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(123), result);
    }

    [Fact]
    public async Task BigIntLiteralParsingLargeNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("9007199254740991n;"); // MAX_SAFE_INTEGER
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt("9007199254740991"), result);
    }

    [Fact]
    public async Task BigIntLiteralParsingVeryLargeNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("12345678901234567890n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt("12345678901234567890"), result);
    }

    [Fact]
    public async Task BigIntAddition()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n + 20n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(30), result);
    }

    [Fact]
    public async Task BigIntSubtraction()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("50n - 20n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(30), result);
    }

    [Fact]
    public async Task BigIntMultiplication()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("6n * 7n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(42), result);
    }

    [Fact]
    public async Task BigIntDivision()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("50n / 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(5), result);
    }

    [Fact]
    public async Task BigIntDivisionTruncates()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("7n / 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(3), result); // JavaScript BigInt division truncates towards zero
    }

    [Fact]
    public async Task BigIntModulo()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("17n % 5n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(2), result);
    }

    [Fact]
    public async Task BigIntExponentiation()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("2n ** 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(1024), result);
    }

    [Fact]
    public async Task BigIntExponentiationLarge()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("2n ** 100n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt("1267650600228229401496703205376"), result);
    }

    [Fact]
    public async Task BigIntNegation()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("-42n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(-42), result);
    }

    [Fact]
    public async Task BigIntBitwiseAnd()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("12n & 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(8), result); // 1100 & 1010 = 1000
    }

    [Fact]
    public async Task BigIntBitwiseOr()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("12n | 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(14), result); // 1100 | 1010 = 1110
    }

    [Fact]
    public async Task BigIntBitwiseXor()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("12n ^ 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(6), result); // 1100 ^ 1010 = 0110
    }

    [Fact]
    public async Task BigIntBitwiseNot()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("~5n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(-6), result);
    }

    [Fact]
    public async Task BigIntLeftShift()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("5n << 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(20), result);
    }

    [Fact]
    public async Task BigIntRightShift()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("20n >> 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(5), result);
    }

    [Fact]
    public async Task BigIntStrictEquality()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n === 10n;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task BigIntStrictInequality()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n === 20n;");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task BigIntNotStrictlyEqualToNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n === 10;");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task BigIntLooseEqualityWithNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n == 10;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task BigIntLooseEqualityWithNumberFalse()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n == 11;");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task BigIntLooseEqualityWithDecimalFalse()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n == 10.5;");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task BigIntGreaterThan()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("20n > 10n;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task BigIntGreaterThanOrEqual()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n >= 10n;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task BigIntLessThan()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n < 20n;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task BigIntLessThanOrEqual()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n <= 10n;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task BigIntCompareWithNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("20n > 10;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task BigIntCompareWithNumberLess()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("5n < 10;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task BigIntTypeof()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof 42n;");
        Assert.Equal("bigint", result);
    }

    [Fact]
    public async Task BigIntCannotMixWithNumberInAddition()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => engine.EvaluateSync("10n + 5;")));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact]
    public async Task BigIntCannotMixWithNumberInSubtraction()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => engine.EvaluateSync("10n - 5;")));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact]
    public async Task BigIntCannotMixWithNumberInMultiplication()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => engine.EvaluateSync("10n * 5;")));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact]
    public async Task BigIntCannotMixWithNumberInDivision()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => engine.EvaluateSync("10n / 5;")));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact]
    public async Task BigIntCannotUseUnsignedRightShift()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => engine.EvaluateSync("10n >>> 2n;")));
        Assert.Contains("BigInts have no unsigned right shift", exception.Message);
    }

    [Fact]
    public async Task BigIntDivisionByZeroThrows()
    {
        var engine = new JsEngine();
        await Assert.ThrowsAsync<DivideByZeroException>(() => Task.Run(() => engine.EvaluateSync("10n / 0n;")));
    }

    [Fact]
    public async Task BigIntModuloByZeroThrows()
    {
        var engine = new JsEngine();
        await Assert.ThrowsAsync<DivideByZeroException>(() => Task.Run(() => engine.EvaluateSync("10n % 0n;")));
    }

    [Fact]
    public async Task BigIntNegativeExponentiationThrows()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => engine.EvaluateSync("2n ** -1n;")));
        Assert.Contains("Exponent must be non-negative", exception.Message);
    }

    [Fact]
    public async Task BigIntVariableAssignment()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 123n; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(123), result);
    }

    [Fact]
    public async Task BigIntArithmeticExpression()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; let y = 20n; x + y * 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(50), result);
    }

    [Fact]
    public async Task BigIntIncrement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; ++x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(11), result);
    }

    [Fact]
    public async Task BigIntDecrement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; --x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(9), result);
    }

    [Fact]
    public async Task BigIntPostfixIncrement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; let y = x++; y;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(10), result);
    }

    [Fact]
    public async Task BigIntPostfixIncrementValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; x++; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(11), result);
    }

    [Fact]
    public async Task BigIntPostfixDecrement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; let y = x--; y;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(10), result);
    }

    [Fact]
    public async Task BigIntPostfixDecrementValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n; x--; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(9), result);
    }

    [Fact]
    public async Task BigIntZero()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("0n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(0), result);
    }

    [Fact]
    public async Task BigIntNegativeValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("-123n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(-123), result);
    }

    [Fact]
    public async Task BigIntConditionalExpression()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10n > 5n ? 100n : 200n; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(100), result);
    }

    [Fact]
    public async Task BigIntLooseEqualityWithString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n == '10';");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task BigIntLooseEqualityWithStringFalse()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n == '11';");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task BigIntNotStrictlyEqualToString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("10n === '10';");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task BigIntStringConcatenation()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("'Value: ' + 42n;");
        Assert.Equal("Value: 42", result);
    }

    [Fact]
    public async Task BigIntWithParentheses()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("(10n + 5n) * 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(30), result);
    }

    [Fact]
    public async Task BigIntComplexExpression()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let a = 5n; let b = 3n; (a + b) * (a - b);");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(16), result); // (5+3) * (5-3) = 8 * 2 = 16
    }
}
