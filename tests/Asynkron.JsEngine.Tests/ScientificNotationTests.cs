namespace Asynkron.JsEngine.Tests;

public class ScientificNotationTests
{
    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_PositiveExponent_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1e5");
        Assert.Equal(100000d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_NegativeExponent_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1e-13");
        Assert.Equal(1e-13, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_ExplicitPositiveExponent_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1e+5");
        Assert.Equal(1e+5, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_WithDecimalPart_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("4.84143144246472090e+00");
        Assert.Equal(4.84143144246472090e+00, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_SmallDecimal_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("2.5e-3");
        Assert.Equal(0.0025, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_LargeNumber_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("3.14e10");
        Assert.Equal(3.14e10, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_UppercaseE_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("5E3");
        Assert.Equal(5000d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_UppercaseEWithSign_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1.5E-2");
        Assert.Equal(0.015, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_InVariableDeclaration_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var epsilon = 1e-13; epsilon;");
        Assert.Equal(1e-13, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_InExpression_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("2e3 + 3e2");
        Assert.Equal(2300d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_ZeroExponent_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("5e0");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_MultipleDigitsInExponent_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1e123");
        Assert.Equal(1e123, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_VerySmallNumber_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("9.99e-308");
        Assert.Equal(9.99e-308, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task ScientificNotation_InArithmeticOperation_ParsesCorrectly()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1e2 * 5");
        Assert.Equal(500d, result);
    }
}
