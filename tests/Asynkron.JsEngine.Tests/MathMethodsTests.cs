using Xunit;

namespace Asynkron.JsEngine.Tests;

public class MathMethodsTests
{
    [Fact]
    public void Math_Cbrt_CalculatesCubeRoot()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.cbrt(8);");
        Assert.Equal(2d, result);
    }

    [Fact]
    public void Math_Cbrt_NegativeValue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.cbrt(-27);");
        Assert.InRange((double)result!, -3.0001, -2.9999);
    }

    [Fact]
    public void Math_Clz32_CountsLeadingZeros()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.clz32(1);");
        Assert.Equal(31d, result);
    }

    [Fact]
    public void Math_Clz32_WithZero()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.clz32(0);");
        Assert.Equal(32d, result);
    }

    [Fact]
    public void Math_Imul_MultipliesIntegers()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.imul(5, 4);");
        Assert.Equal(20d, result);
    }

    [Fact]
    public void Math_Imul_WithLargeNumbers()
    {
        var engine = new JsEngine();
        // Test integer multiplication behavior with large number
        var result = engine.EvaluateSync("Math.imul(2147483647, 2);");
        Assert.Equal(-2d, result);
    }

    [Fact]
    public void Math_Fround_ConvertsToFloat32()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.fround(1.5);");
        Assert.Equal(1.5d, result);
    }

    [Fact]
    public void Math_Fround_WithHighPrecision()
    {
        var engine = new JsEngine();
        // Float32 loses precision compared to Float64
        var result = engine.EvaluateSync("Math.fround(1.337);");
        Assert.NotEqual(1.337d, result);
        Assert.InRange((double)result!, 1.336d, 1.338d);
    }

    [Fact]
    public void Math_Hypot_CalculatesHypotenuse()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.hypot(3, 4);");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void Math_Hypot_WithMultipleArguments()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.hypot(1, 2, 2);");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void Math_Hypot_WithNoArguments()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.hypot();");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Math_Acosh_CalculatesInverseHyperbolicCosine()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.acosh(1);");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Math_Asinh_CalculatesInverseHyperbolicSine()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.asinh(0);");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Math_Atanh_CalculatesInverseHyperbolicTangent()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.atanh(0);");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Math_Cosh_CalculatesHyperbolicCosine()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.cosh(0);");
        Assert.Equal(1d, result);
    }

    [Fact]
    public void Math_Sinh_CalculatesHyperbolicSine()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.sinh(0);");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Math_Tanh_CalculatesHyperbolicTangent()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.tanh(0);");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Math_Tanh_WithPositiveInfinity()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.tanh(Infinity);");
        Assert.Equal(1d, result);
    }

    [Fact]
    public void Math_Expm1_CalculatesExpMinusOne()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.expm1(0);");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Math_Expm1_WithPositiveValue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.expm1(1);");
        Assert.InRange((double)result!, Math.E - 1 - 0.0001, Math.E - 1 + 0.0001);
    }

    [Fact]
    public void Math_Log1p_CalculatesLogOnePlusX()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.log1p(0);");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Math_Log1p_WithPositiveValue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Math.log1p(Math.E - 1);");
        Assert.InRange((double)result!, 0.999, 1.001);
    }
}
