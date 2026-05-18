using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibDataView)]
public sealed class DataViewAdditionalMethodsTests(ITestOutputHelper output) : InternalTestBase(output)
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
    public async Task DataView_SetFloat16_ReturnsUndefinedAndWritesExpectedBytes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(2);
            const view = new DataView(buffer);
            const cases = [
                [0, 0x00, 0x00],
                [-0, 0x80, 0x00],
                [1, 0x3c, 0x00],
                [-2, 0xc0, 0x00],
                [65504, 0x7b, 0xff],
                [Infinity, 0x7c, 0x00],
                [-Infinity, 0xfc, 0x00],
            ];

            for (const [value, high, low] of cases) {
                if (view.setFloat16(0, value) !== undefined) {
                    throw new Error('setFloat16 must return undefined');
                }

                if (view.getUint8(0) !== high || view.getUint8(1) !== low) {
                    throw new Error('big-endian bytes mismatch for ' + value);
                }

                if (view.setFloat16(0, value, true) !== undefined) {
                    throw new Error('setFloat16 little-endian must return undefined');
                }

                if (view.getUint8(0) !== low || view.getUint8(1) !== high) {
                    throw new Error('little-endian bytes mismatch for ' + value);
                }
            }

            view.setUint8(0, 0);
            view.setUint8(1, 0);
            if (view.setFloat16(0) !== undefined) {
                throw new Error('missing value must still return undefined');
            }

            const nanBits = view.getUint16(0);
            ((nanBits & 0x7c00) === 0x7c00) && ((nanBits & 0x03ff) !== 0);
        ");

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_SetUint8_ReturnsUndefinedAndUsesUint8ModuloConversion()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(1);
            const view = new DataView(buffer);
            const typedArray = new Uint8Array(buffer);
            const cases = [
                [2147483648, 0],
                [4294967295, 255],
                [4294967296, 0],
                [-1, 255],
                [-255, 1],
                [255.99999999999, 255],
                [undefined, 0],
                [NaN, 0],
                [Infinity, 0],
                [-Infinity, 0],
            ];

            for (const [value, expected] of cases) {
                const actualReturn = view.setUint8(0, value);
                if (actualReturn !== undefined) {
                    throw new Error('setUint8 must return undefined for ' + value);
                }

                if (typedArray[0] !== expected) {
                    throw new Error('setUint8 conversion mismatch for ' + value);
                }
            }

            true;
        ");

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_SetUint16_ReturnsUndefinedAndUsesUint16ModuloConversion()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const buffer = new ArrayBuffer(2);
            const view = new DataView(buffer);
            const cases = [
                [2147483647, 65535],
                [2147483648, 0],
                [4294967295, 65535],
                [4294967296, 0],
                [-1, 65535],
                [-65535, 1],
                [65519.99999999999, 65519],
                [undefined, 0],
                [NaN, 0],
                [Infinity, 0],
            ];

            for (const [value, expected] of cases) {
                const actualReturn = view.setUint16(0, value);
                if (actualReturn !== undefined) {
                    throw new Error('setUint16 must return undefined for ' + value);
                }

                if (view.getUint16(0) !== expected) {
                    throw new Error('setUint16 conversion mismatch for ' + value);
                }
            }

            true;
        ");

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_SetUint16_FallbackUsesSameUint16Conversion()
    {
        await using var engine = CreateEngine();
        var buffer = new JsArrayBuffer(2, realmState: engine.RealmState);
        var view = new JsDataView(buffer);

        Assert.True(view.TryGetProperty("setUint16", out var methodValue));
        var method = Assert.IsAssignableFrom<IJsCallable>(methodValue.ObjectValue);

        var result = method.Invoke(
            [new JsValue(0d), new JsValue(2147483647d), JsValue.False],
            JsValue.FromObjectUnsafe(view));

        Assert.True(result.IsUndefined);
        Assert.Equal((ushort)65535, view.GetUint16(0));
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
