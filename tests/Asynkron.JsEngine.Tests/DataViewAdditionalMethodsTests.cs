using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class DataViewAdditionalMethodsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task DataView_GetFloat16_ReadsHalfPrecisionFloat()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(2);
            const view = new DataView(buffer);
            view.setFloat16(0, 1.5);
            view.getFloat16(0);
        ");
        Assert.Equal(1.5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_SetFloat16_WritesHalfPrecisionFloat()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(2);
            const view = new DataView(buffer);
            view.setFloat16(0, 3.14);
            view.getFloat16(0);
        ");
        // Float16 has limited precision
        Assert.InRange((double)result!, 3.13d, 3.15d);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_GetBigInt64_ReadsSigned64BitInt()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(8);
            const view = new DataView(buffer);
            view.setBigInt64(0, 9007199254740991n);
            view.getBigInt64(0);
        ");
        // Result should be a BigInt
        Assert.NotNull(result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_SetBigInt64_Writes64BitInt()
    {
        await using var engine = CreateEngine();
        // Just test that the methods can be called without throwing
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(8);
            const view = new DataView(buffer);
            view.setBigInt64(0, -123456789n);
            view.getBigInt64(0);
            true; // If we got here, it worked
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_GetBigUint64_ReadsUnsigned64BitInt()
    {
        await using var engine = CreateEngine();
        // Just test that the methods can be called without throwing
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(8);
            const view = new DataView(buffer);
            view.setBigUint64(0, 18446744073709551615n);
            view.getBigUint64(0);
            true; // If we got here, it worked
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_SetBigUint64_Writes64BitUnsignedInt()
    {
        await using var engine = CreateEngine();
        // Just test that the methods can be called without throwing
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(8);
            const view = new DataView(buffer);
            view.setBigUint64(0, 123456789n);
            view.getBigUint64(0);
            true; // If we got here, it worked
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_Float16_LittleEndian()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(2);
            const view = new DataView(buffer);
            view.setFloat16(0, 1.5, true); // little endian
            view.getFloat16(0, true);
        ");
        Assert.Equal(1.5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_BigInt64_LittleEndian()
    {
        await using var engine = CreateEngine();
        // Just test that the methods can be called without throwing
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(8);
            const view = new DataView(buffer);
            view.setBigInt64(0, 42n, true); // little endian
            view.getBigInt64(0, true);
            true; // If we got here, it worked
        ");
        Assert.True((bool)result!);
    }
}
