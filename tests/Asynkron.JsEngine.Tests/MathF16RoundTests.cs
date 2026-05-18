using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibMath)]
public sealed class MathF16RoundTests(ITestOutputHelper output) : InternalTestBase(output)
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
        Assert.True((bool)(await engine.Evaluate("Object.is(Math.f16round(-0), -0);"))!);
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

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_MatchesTest262ValueConversionEdges()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            const cases = [
                [32767, 32768],
                [2147483647, Infinity],
                [1.1, 1.099609375],
                [0.1, 0.0999755859375],
                [undefined, NaN],
                [-0, -0],
                [-32767, -32768],
                [-2147483647, -Infinity],
                [2049, 2048],
                [2051, 2052],
                [0.00006103515625, 0.00006103515625],
                [0.00006097555160522461, 0.00006097555160522461],
                [5.960464477539063e-8, 5.960464477539063e-8],
                [2.9802322387695312e-8, 0],
                [2.980232238769532e-8, 5.960464477539063e-8],
                [8.940696716308594e-8, 1.1920928955078125e-7],
                [65504, 65504],
                [65520, Infinity],
                [65519.99999999999, 65504]
            ];

            for (const [value, expected] of cases) {
                const actual = Math.f16round(value);
                if (!Object.is(actual, expected)) {
                    throw new Error("value: " + value + ", actual: " + actual + ", expected: " + expected);
                }
            }

            true;
            """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_AppliesToNumberBeforeRounding()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let valueOfCount = 0;
            const roundedObject = Math.f16round({
                valueOf() {
                    valueOfCount++;
                    return "0.1";
                }
            });

            let symbolThrows = false;
            try {
                Math.f16round(Symbol("x"));
            } catch (error) {
                symbolThrows = error instanceof TypeError;
            }

            let bigintThrows = false;
            try {
                Math.f16round(1n);
            } catch (error) {
                bigintThrows = error instanceof TypeError;
            }

            valueOfCount === 1
                && Object.is(roundedObject, 0.0999755859375)
                && Number.isNaN(Math.f16round())
                && symbolThrows
                && bigintThrows;
            """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_F16round_RoundsDirectlyFromBinary64()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            const k = 1.00048828125000022204;
            Object.is(Math.f16round(k), 1.0009765625)
                && Object.is(Math.f16round(Math.fround(k)), 1);
            """);

        Assert.True((bool)result!);
    }
}
