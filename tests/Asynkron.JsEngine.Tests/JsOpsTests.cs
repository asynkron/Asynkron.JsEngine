using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.TypeSystem)]
public sealed class JsOpsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    #region Arithmetic Operations Tests

    [Fact]
    public async Task Add_TwoNumbers_ShouldReturnSum()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 + 3;");
        Assert.Equal(8d, result);
    }

    [Fact]
    public async Task Add_StringAndNumber_ShouldConcatenate()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello' + 5;");
        Assert.Equal("hello5", result);
    }

    [Fact]
    public async Task Sub_TwoNumbers_ShouldReturnDifference()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("10 - 3;");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task Mul_TwoNumbers_ShouldReturnProduct()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("4 * 3;");
        Assert.Equal(12d, result);
    }

    [Fact]
    public async Task Div_TwoNumbers_ShouldReturnQuotient()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("10 / 2;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Mod_TwoNumbers_ShouldReturnRemainder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("10 % 3;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Exp_TwoNumbers_ShouldReturnPower()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("2 ** 3;");
        Assert.Equal(8d, result);
    }

    [Fact]
    public async Task Add_BigInts_ShouldReturnSum()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5n + 3n;");
        var bigInt = Assert.IsType<JsBigInt>(result);
        Assert.Equal(8, (int)bigInt.Value);
    }

    #endregion

    #region Comparison Operations Tests

    [Fact]
    public async Task Eq_LooseEquality_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 == '5';");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task StrictEq_StrictEquality_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 === '5';");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task Lt_LessThan_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("3 < 5;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Lte_LessThanOrEqual_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 <= 5;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Gt_GreaterThan_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 > 3;");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Gte_GreaterThanOrEqual_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 >= 5;");
        Assert.Equal(true, result);
    }

    #endregion

    #region Unary Operations Tests

    [Fact]
    public async Task Neg_UnaryMinus_ShouldNegate()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("-5;");
        Assert.Equal(-5d, result);
    }

    [Fact]
    public async Task Not_LogicalNot_ShouldInvert()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("!true;");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task TypeOf_Number_ShouldReturnNumber()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof 5;");
        Assert.Equal("number", result);
    }

    [Fact]
    public async Task TypeOf_String_ShouldReturnString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof 'hello';");
        Assert.Equal("string", result);
    }

    #endregion

    #region Bitwise Operations Tests

    [Fact]
    public async Task BitAnd_BitwiseAnd_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 & 3;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task BitOr_BitwiseOr_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 | 3;");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task BitXor_BitwiseXor_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 ^ 3;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task BitNot_BitwiseNot_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("~5;");
        Assert.Equal(-6d, result);
    }

    [Fact]
    public async Task LeftShift_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 << 2;");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task RightShift_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("20 >> 2;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task UnsignedRightShift_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("-1 >>> 1;");
        Assert.Equal(2147483647d, result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task Add_NaN_ShouldReturnNaN()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("NaN + 5;");
        Assert.True(double.IsNaN((double)result));
    }

    [Fact]
    public async Task Div_ByZero_ShouldReturnInfinity()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 / 0;");
        Assert.True(double.IsPositiveInfinity((double)result));
    }

    [Fact]
    public async Task Mod_NegativeZero_ShouldPreserveSign()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("-0 % 5;");
        var d = (double)result;
        Assert.Equal(0d, d);
        // Check if it's negative zero
        Assert.True(BitConverter.DoubleToInt64Bits(d) == BitConverter.DoubleToInt64Bits(-0.0));
    }

    #endregion
}
