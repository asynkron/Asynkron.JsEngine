using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class BigIntTests
{
    [Fact]
    public void BigIntLiteralParsing()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("123n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(123), result);
    }

    [Fact]
    public void BigIntLiteralParsingLargeNumber()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("9007199254740991n;"); // MAX_SAFE_INTEGER
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt("9007199254740991"), result);
    }

    [Fact]
    public void BigIntLiteralParsingVeryLargeNumber()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("12345678901234567890n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt("12345678901234567890"), result);
    }

    [Fact]
    public void BigIntAddition()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n + 20n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(30), result);
    }

    [Fact]
    public void BigIntSubtraction()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("50n - 20n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(30), result);
    }

    [Fact]
    public void BigIntMultiplication()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("6n * 7n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(42), result);
    }

    [Fact]
    public void BigIntDivision()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("50n / 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(5), result);
    }

    [Fact]
    public void BigIntDivisionTruncates()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("7n / 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(3), result); // JavaScript BigInt division truncates towards zero
    }

    [Fact]
    public void BigIntModulo()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("17n % 5n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(2), result);
    }

    [Fact]
    public void BigIntExponentiation()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("2n ** 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(1024), result);
    }

    [Fact]
    public void BigIntExponentiationLarge()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("2n ** 100n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt("1267650600228229401496703205376"), result);
    }

    [Fact]
    public void BigIntNegation()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("-42n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(-42), result);
    }

    [Fact]
    public void BigIntBitwiseAnd()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("12n & 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(8), result); // 1100 & 1010 = 1000
    }

    [Fact]
    public void BigIntBitwiseOr()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("12n | 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(14), result); // 1100 | 1010 = 1110
    }

    [Fact]
    public void BigIntBitwiseXor()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("12n ^ 10n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(6), result); // 1100 ^ 1010 = 0110
    }

    [Fact]
    public void BigIntBitwiseNot()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("~5n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(-6), result);
    }

    [Fact]
    public void BigIntLeftShift()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("5n << 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(20), result);
    }

    [Fact]
    public void BigIntRightShift()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("20n >> 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(5), result);
    }

    [Fact]
    public void BigIntStrictEquality()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n === 10n;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void BigIntStrictInequality()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n === 20n;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void BigIntNotStrictlyEqualToNumber()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n === 10;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void BigIntLooseEqualityWithNumber()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n == 10;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void BigIntLooseEqualityWithNumberFalse()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n == 11;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void BigIntLooseEqualityWithDecimalFalse()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n == 10.5;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void BigIntGreaterThan()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("20n > 10n;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void BigIntGreaterThanOrEqual()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n >= 10n;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void BigIntLessThan()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n < 20n;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void BigIntLessThanOrEqual()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n <= 10n;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void BigIntCompareWithNumber()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("20n > 10;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void BigIntCompareWithNumberLess()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("5n < 10;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void BigIntTypeof()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("typeof 42n;");
        Assert.Equal("bigint", result);
    }

    [Fact]
    public void BigIntCannotMixWithNumberInAddition()
    {
        var engine = new JsEngine();
        var exception = Assert.Throws<InvalidOperationException>(() => engine.EvaluateSync("10n + 5;"));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact]
    public void BigIntCannotMixWithNumberInSubtraction()
    {
        var engine = new JsEngine();
        var exception = Assert.Throws<InvalidOperationException>(() => engine.EvaluateSync("10n - 5;"));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact]
    public void BigIntCannotMixWithNumberInMultiplication()
    {
        var engine = new JsEngine();
        var exception = Assert.Throws<InvalidOperationException>(() => engine.EvaluateSync("10n * 5;"));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact]
    public void BigIntCannotMixWithNumberInDivision()
    {
        var engine = new JsEngine();
        var exception = Assert.Throws<InvalidOperationException>(() => engine.EvaluateSync("10n / 5;"));
        Assert.Contains("Cannot mix BigInt and other types", exception.Message);
    }

    [Fact]
    public void BigIntCannotUseUnsignedRightShift()
    {
        var engine = new JsEngine();
        var exception = Assert.Throws<InvalidOperationException>(() => engine.EvaluateSync("10n >>> 2n;"));
        Assert.Contains("BigInts have no unsigned right shift", exception.Message);
    }

    [Fact]
    public void BigIntDivisionByZeroThrows()
    {
        var engine = new JsEngine();
        Assert.Throws<DivideByZeroException>(() => engine.EvaluateSync("10n / 0n;"));
    }

    [Fact]
    public void BigIntModuloByZeroThrows()
    {
        var engine = new JsEngine();
        Assert.Throws<DivideByZeroException>(() => engine.EvaluateSync("10n % 0n;"));
    }

    [Fact]
    public void BigIntNegativeExponentiationThrows()
    {
        var engine = new JsEngine();
        var exception = Assert.Throws<InvalidOperationException>(() => engine.EvaluateSync("2n ** -1n;"));
        Assert.Contains("Exponent must be non-negative", exception.Message);
    }

    [Fact]
    public void BigIntVariableAssignment()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 123n; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(123), result);
    }

    [Fact]
    public void BigIntArithmeticExpression()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10n; let y = 20n; x + y * 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(50), result);
    }

    [Fact]
    public void BigIntIncrement()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10n; ++x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(11), result);
    }

    [Fact]
    public void BigIntDecrement()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10n; --x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(9), result);
    }

    [Fact]
    public void BigIntPostfixIncrement()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10n; let y = x++; y;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(10), result);
    }

    [Fact]
    public void BigIntPostfixIncrementValue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10n; x++; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(11), result);
    }

    [Fact]
    public void BigIntPostfixDecrement()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10n; let y = x--; y;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(10), result);
    }

    [Fact]
    public void BigIntPostfixDecrementValue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10n; x--; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(9), result);
    }

    [Fact]
    public void BigIntZero()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("0n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(0), result);
    }

    [Fact]
    public void BigIntNegativeValue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("-123n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(-123), result);
    }

    [Fact]
    public void BigIntConditionalExpression()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let x = 10n > 5n ? 100n : 200n; x;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(100), result);
    }

    [Fact]
    public void BigIntLooseEqualityWithString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n == '10';");
        Assert.Equal(true, result);
    }

    [Fact]
    public void BigIntLooseEqualityWithStringFalse()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n == '11';");
        Assert.Equal(false, result);
    }

    [Fact]
    public void BigIntNotStrictlyEqualToString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("10n === '10';");
        Assert.Equal(false, result);
    }

    [Fact]
    public void BigIntStringConcatenation()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("'Value: ' + 42n;");
        Assert.Equal("Value: 42", result);
    }

    [Fact]
    public void BigIntWithParentheses()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("(10n + 5n) * 2n;");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(30), result);
    }

    [Fact]
    public void BigIntComplexExpression()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("let a = 5n; let b = 3n; (a + b) * (a - b);");
        Assert.IsType<JsBigInt>(result);
        Assert.Equal(new JsBigInt(16), result); // (5+3) * (5-3) = 8 * 2 = 16
    }
}
