using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class MathF16RoundTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Math_F16round_ConvertsToFloat16Precision()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.f16round(1.5);");
        Assert.Equal(1.5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_LosesPrecision()
    {
        await using var engine = CreateEngine();
        // Float16 has limited precision, so 1.337 will be rounded
        var result = await engine.Evaluate("Math.f16round(1.337);");
        // Float16 representation of 1.337 is approximately 1.3369140625
        Assert.NotEqual(1.337d, result);
        Assert.InRange((double)result!, 1.33d, 1.34d);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_HandlesNaN()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.f16round(NaN);");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_HandlesInfinity()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.f16round(Infinity);");
        Assert.True(double.IsPositiveInfinity((double)result!));
    }

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_HandlesNegativeInfinity()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.f16round(-Infinity);");
        Assert.True(double.IsNegativeInfinity((double)result!));
    }

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_HandlesZero()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.f16round(0);");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_HandlesNegativeZero()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.f16round(-0);");
        // -0 should be preserved
        Assert.Equal(0d, Math.Abs((double)result!));
    }

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_HandlesSmallNumber()
    {
        await using var engine = CreateEngine();
        // Very small number that fits in float16
        var result = await engine.Evaluate("Math.f16round(0.1);");
        Assert.InRange((double)result!, 0.09d, 0.11d);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_HandlesLargeNumber()
    {
        await using var engine = CreateEngine();
        // Large number within float16 range
        var result = await engine.Evaluate("Math.f16round(1000);");
        Assert.Equal(1000d, result);
    }
}

public class MathF16RoundTests(ITestOutputHelper output) : MathF16RoundTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}
