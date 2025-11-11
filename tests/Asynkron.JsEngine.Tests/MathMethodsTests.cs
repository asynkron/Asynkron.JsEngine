using Xunit;

namespace Asynkron.JsEngine.Tests;

public class MathMethodsTests
{
    [Fact(Timeout = 2000)]
    public async Task Math_Cbrt_CalculatesCubeRoot()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.cbrt(8);");
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Cbrt_NegativeValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.cbrt(-27);");
        Assert.InRange((double)result!, -3.0001, -2.9999);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Clz32_CountsLeadingZeros()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.clz32(1);");
        Assert.Equal(31d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Clz32_WithZero()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.clz32(0);");
        Assert.Equal(32d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Imul_MultipliesIntegers()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.imul(5, 4);");
        Assert.Equal(20d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Imul_WithLargeNumbers()
    {
        var engine = new JsEngine();
        // Test integer multiplication behavior with large number
        var result = await engine.Evaluate("Math.imul(2147483647, 2);");
        Assert.Equal(-2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Fround_ConvertsToFloat32()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.fround(1.5);");
        Assert.Equal(1.5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Fround_WithHighPrecision()
    {
        var engine = new JsEngine();
        // Float32 loses precision compared to Float64
        var result = await engine.Evaluate("Math.fround(1.337);");
        Assert.NotEqual(1.337d, result);
        Assert.InRange((double)result!, 1.336d, 1.338d);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Hypot_CalculatesHypotenuse()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.hypot(3, 4);");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Hypot_WithMultipleArguments()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.hypot(1, 2, 2);");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Hypot_WithNoArguments()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.hypot();");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Acosh_CalculatesInverseHyperbolicCosine()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.acosh(1);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Asinh_CalculatesInverseHyperbolicSine()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.asinh(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Atanh_CalculatesInverseHyperbolicTangent()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.atanh(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Cosh_CalculatesHyperbolicCosine()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.cosh(0);");
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Sinh_CalculatesHyperbolicSine()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.sinh(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Tanh_CalculatesHyperbolicTangent()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.tanh(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Tanh_WithPositiveInfinity()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.tanh(Infinity);");
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Expm1_CalculatesExpMinusOne()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.expm1(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Expm1_WithPositiveValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.expm1(1);");
        Assert.InRange((double)result!, Math.E - 1 - 0.0001, Math.E - 1 + 0.0001);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Log1p_CalculatesLogOnePlusX()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.log1p(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Log1p_WithPositiveValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.log1p(Math.E - 1);");
        Assert.InRange((double)result!, 0.999, 1.001);
    }
}