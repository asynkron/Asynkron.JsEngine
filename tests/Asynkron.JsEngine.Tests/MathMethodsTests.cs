using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class MathMethodsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Math_Cbrt_CalculatesCubeRoot()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.cbrt(8);");
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Cbrt_NegativeValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.cbrt(-27);");
        Assert.InRange((double)result!, -3.0001, -2.9999);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Clz32_CountsLeadingZeros()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.clz32(1);");
        Assert.Equal(31d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task Math_Clz32_WithZero()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.clz32(0);");
        Assert.Equal(32d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Imul_MultipliesIntegers()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.imul(5, 4);");
        Assert.Equal(20d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Imul_WithLargeNumbers()
    {
        await using var engine = CreateEngine();
        // Test integer multiplication behavior with large number
        var result = await engine.Evaluate("Math.imul(2147483647, 2);");
        Assert.Equal(-2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Fround_ConvertsToFloat32()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.fround(1.5);");
        Assert.Equal(1.5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Fround_WithHighPrecision()
    {
        await using var engine = CreateEngine();
        // Float32 loses precision compared to Float64
        var result = await engine.Evaluate("Math.fround(1.337);");
        Assert.NotEqual(1.337d, result);
        Assert.InRange((double)result!, 1.336d, 1.338d);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Hypot_CalculatesHypotenuse()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.hypot(3, 4);");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Hypot_WithMultipleArguments()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.hypot(1, 2, 2);");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Hypot_WithNoArguments()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.hypot();");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Acosh_CalculatesInverseHyperbolicCosine()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.acosh(1);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Asinh_CalculatesInverseHyperbolicSine()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.asinh(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Atanh_CalculatesInverseHyperbolicTangent()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.atanh(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Cosh_CalculatesHyperbolicCosine()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.cosh(0);");
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Sinh_CalculatesHyperbolicSine()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.sinh(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Tanh_CalculatesHyperbolicTangent()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.tanh(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Tanh_WithPositiveInfinity()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.tanh(Infinity);");
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Expm1_CalculatesExpMinusOne()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.expm1(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Expm1_WithPositiveValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.expm1(1);");
        Assert.InRange((double)result!, Math.E - 1 - 0.0001, Math.E - 1 + 0.0001);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Log1p_CalculatesLogOnePlusX()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.log1p(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Log1p_WithPositiveValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.log1p(Math.E - 1);");
        Assert.InRange((double)result!, 0.999, 1.001);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Round_NegativeZeroEdgeCases()
    {
        await using var engine = CreateEngine();

        // Math.round(-0.5) should return -0 (negative zero)
        var result1 = (double)(await engine.Evaluate("1 / Math.round(-0.5);"))!;
        Assert.Equal(double.NegativeInfinity, result1);

        // Math.round(-0.25) should return -0 (negative zero)
        var result2 = (double)(await engine.Evaluate("1 / Math.round(-0.25);"))!;
        Assert.Equal(double.NegativeInfinity, result2);

        // Math.round(-0) should return -0 (negative zero)
        var result3 = (double)(await engine.Evaluate("1 / Math.round(-0);"))!;
        Assert.Equal(double.NegativeInfinity, result3);

        // Math.round(0.5 - Number.EPSILON / 4) should return +0 (positive zero)
        // This tests that Number.EPSILON is correctly set to 2^-52, not double.Epsilon
        var result4 = (double)(await engine.Evaluate("1 / Math.round(0.5 - Number.EPSILON / 4);"))!;
        Assert.Equal(double.PositiveInfinity, result4);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_Round_LargeIntegersUnchanged()
    {
        await using var engine = CreateEngine();

        // Large odd integers near 1/Number.EPSILON boundaries should be unchanged
        var result = (bool)(await engine.Evaluate(@"
            var x1 = -(2 / Number.EPSILON - 1);
            var x2 = -(1 / Number.EPSILON + 1);
            var x3 = 1 / Number.EPSILON + 1;
            var x4 = 2 / Number.EPSILON - 1;
            Math.round(x1) === x1 && Math.round(x2) === x2 &&
            Math.round(x3) === x3 && Math.round(x4) === x4;
        "))!;
        Assert.True(result);
    }
}
