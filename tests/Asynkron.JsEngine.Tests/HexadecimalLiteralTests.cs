using Xunit;

namespace Asynkron.JsEngine.Tests;

public class HexadecimalLiteralTests
{
    [Fact(Timeout = 2000)]
    public async Task Hexadecimal_Literal_LowercaseX_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0x04; x;");
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Hexadecimal_Literal_UppercaseX_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0X0A; x;");
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Hexadecimal_Literal_LowercaseLetters_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0xff; x;");
        Assert.Equal(255d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Hexadecimal_Literal_UppercaseLetters_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0xFF; x;");
        Assert.Equal(255d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Hexadecimal_Literal_MixedCase_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0xAbC; x;");
        Assert.Equal(2748d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Hexadecimal_Literal_Zero_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0x0; x;");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Hexadecimal_Literal_Large_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0x1A2B3C; x;");
        Assert.Equal(1715004d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Hexadecimal_Literal_CanBeUsedInArithmetic()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0x10 + 0x20; x;");
        Assert.Equal(48d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Hexadecimal_Literal_CanBeUsedAsArrayLength()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var arr = Array(0x05); arr.length;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Octal_Literal_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0o10; x;");
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Octal_Literal_UppercaseO_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0O77; x;");
        Assert.Equal(63d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Binary_Literal_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0b101; x;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Binary_Literal_UppercaseB_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0B1111; x;");
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Hexadecimal_BigInt_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0xFFn; typeof x;");
        Assert.Equal("bigint", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Octal_BigInt_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0o77n; typeof x;");
        Assert.Equal("bigint", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Binary_BigInt_ParsesCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 0b101n; typeof x;");
        Assert.Equal("bigint", result);
    }
}
