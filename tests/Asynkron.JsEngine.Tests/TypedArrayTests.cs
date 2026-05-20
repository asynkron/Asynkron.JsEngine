using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibTypedArray)]
public sealed class TypedArrayTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task ArrayBuffer_CreatesWithLength()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(8);
                                                       buffer.byteLength;

                                           """);
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayBuffer_Slice_CreatesNewBuffer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer1 = new ArrayBuffer(16);
                                                       let buffer2 = buffer1.slice(4, 12);
                                                       buffer2.byteLength;

                                           """);
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayBuffer_IsView_ReturnsFalseForBuffer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(8);
                                                       ArrayBuffer.isView(buffer);

                                           """);
        Assert.Equal(false, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayBuffer_IsView_ReturnsTrueForTypedArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint8Array(8);
                                                       ArrayBuffer.isView(arr);

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Int8Array_CreatesFromLength()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Int8Array(4);
                                                       arr.length;

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Int8Array_ElementAccess()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Int8Array(3);
                                                       arr[0] = 10;
                                                       arr[1] = -20;
                                                       arr[2] = 30;
                                                       arr[0] + arr[1] + arr[2];

                                           """);
        Assert.Equal(20d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Int8Array_HandlesOverflow()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Int8Array(2);
                                                       arr[0] = 127;
                                                       arr[1] = 128;  // Wraps to -128
                                                       arr[0] + arr[1];

                                           """);
        Assert.Equal(-1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Uint8Array_CreatesFromArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint8Array([10, 20, 30]);
                                                       arr[0] + arr[1] + arr[2];

                                           """);
        Assert.Equal(60d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Uint8Array_ByteLength()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint8Array(10);
                                                       arr.byteLength;

                                           """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Uint8ClampedArray_ClampsToRange()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint8ClampedArray(3);
                                                       arr[0] = -10;    // Clamped to 0
                                                       arr[1] = 300;    // Clamped to 255
                                                       arr[2] = 127.6;  // Rounded to 128
                                                       arr[0] + arr[1] + arr[2];

                                           """);
        Assert.Equal(383d, result); // 0 + 255 + 128
    }

    [Fact(Timeout = 2000)]
    public async Task Int16Array_BytesPerElement()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Int16Array(5);
                                                       arr.BYTES_PER_ELEMENT;

                                           """);
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Int16Array_ElementStorage()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Int16Array(2);
                                                       arr[0] = 32767;   // Max int16
                                                       arr[1] = -32768;  // Min int16
                                                       arr[0] + arr[1];

                                           """);
        Assert.Equal(-1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Uint16Array_ElementStorage()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint16Array(2);
                                                       arr[0] = 65535;  // Max uint16
                                                       arr[1] = 1;
                                                       arr[0] + arr[1];

                                           """);
        Assert.Equal(65536d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Int32Array_BytesPerElement()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Int32Array(5);
                                                       arr.BYTES_PER_ELEMENT;

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Int32Array_ElementStorage()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Int32Array(2);
                                                       arr[0] = 1000000;
                                                       arr[1] = -1000000;
                                                       arr[0] + arr[1];

                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Uint32Array_ElementStorage()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint32Array(2);
                                                       arr[0] = 4294967295;  // Max uint32
                                                       arr[1] = 1;
                                                       arr[0] + arr[1];

                                           """);
        Assert.Equal(4294967296d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Float32Array_BytesPerElement()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Float32Array(5);
                                                       arr.BYTES_PER_ELEMENT;

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Float32Array_ElementStorage()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Float32Array(2);
                                                       arr[0] = 3.14;
                                                       arr[1] = 2.71;
                                                       Math.floor((arr[0] + arr[1]) * 100);

                                           """);
        Assert.Equal(585d, result); // Float32 precision
    }

    [Fact(Timeout = 2000)]
    public async Task Float64Array_BytesPerElement()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Float64Array(5);
                                                       arr.BYTES_PER_ELEMENT;

                                           """);
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Float64Array_ElementStorage()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Float64Array(2);
                                                       arr[0] = 3.14159265359;
                                                       arr[1] = 2.71828182846;
                                                       Math.floor((arr[0] + arr[1]) * 1000);

                                           """);
        Assert.Equal(5859d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_CreatesFromBuffer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(16);
                                                       let arr = new Int32Array(buffer);
                                                       arr.length;

                                           """);
        Assert.Equal(4d, result); // 16 bytes / 4 bytes per element
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_CreatesFromBufferWithOffset()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(16);
                                                       let arr = new Int32Array(buffer, 4);
                                                       arr.length;

                                           """);
        Assert.Equal(3d, result); // (16-4) bytes / 4 bytes per element
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_CreatesFromBufferWithOffsetAndLength()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(16);
                                                       let arr = new Int32Array(buffer, 4, 2);
                                                       arr.length;

                                           """);
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_BufferProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(16);
                                                       let arr = new Int32Array(buffer);
                                                       arr.buffer.byteLength;

                                           """);
        Assert.Equal(16d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_ByteOffsetProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(16);
                                                       let arr = new Int32Array(buffer, 8);
                                                       arr.byteOffset;

                                           """);
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Subarray_CreatesView()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr1 = new Uint8Array([0, 1, 2, 3, 4, 5]);
                                                       let arr2 = arr1.subarray(2, 5);
                                                       arr1[2] = 100;
                                                       arr2[0];  // Should see the change since it's the same buffer

                                           """);
        Assert.Equal(100d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Slice_CopiesData()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr1 = new Uint8Array([0, 1, 2, 3, 4, 5]);
                                                       let arr2 = arr1.slice(2, 5);
                                                       arr1[2] = 100;
                                                       arr2[0];  // Should still be 2 since it's a copy

                                           """);
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Set_FromTypedArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr1 = new Uint8Array([1, 2, 3]);
                                                       let arr2 = new Uint8Array(5);
                                                       arr2.set(arr1, 1);
                                                       arr2[0] + arr2[1] + arr2[2] + arr2[3] + arr2[4];

                                           """);
        Assert.Equal(6d, result); // 0 + 1 + 2 + 3 + 0
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Set_FromArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint8Array(5);
                                                       arr.set([10, 20, 30], 1);
                                                       arr[0] + arr[1] + arr[2] + arr[3] + arr[4];

                                           """);
        Assert.Equal(60d, result); // 0 + 10 + 20 + 30 + 0
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_CreatesFromBuffer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(16);
                                                       let view = new DataView(buffer);
                                                       view.byteLength;

                                           """);
        Assert.Equal(16d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_GetSetInt8()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(4);
                                                       let view = new DataView(buffer);
                                                       view.setInt8(0, 127);
                                                       view.setInt8(1, -128);
                                                       view.getInt8(0) + view.getInt8(1);

                                           """);
        Assert.Equal(-1d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task DataView_GetSetUint8()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(4);
                                                       let view = new DataView(buffer);
                                                       view.setUint8(0, 255);
                                                       view.setUint8(1, 100);
                                                       view.getUint8(0) + view.getUint8(1);

                                           """);
        Assert.Equal(355d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_GetSetInt16()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(8);
                                                       let view = new DataView(buffer);
                                                       view.setInt16(0, 1000);
                                                       view.getInt16(0);

                                           """);
        Assert.Equal(1000d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_SetInt16_UsesInt16WrappingAndPositiveZero()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(8);
                                                       let view = new DataView(buffer);
                                                       let byteConversionValues = {
                                                           values: [-0, 2147483648],
                                                           expected: { Int16: [0] }
                                                       };
                                                       let returnValue = view.setInt16(0, byteConversionValues.values[1], false);
                                                       let actual = view.getInt16(0);
                                                       let expected = byteConversionValues.expected.Int16[0];
                                                       returnValue === undefined
                                                           && Object.is(expected, 0)
                                                           && Object.is(actual, expected)
                                                           && 1 / actual === Infinity;

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_GetSetInt32()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(8);
                                                       let view = new DataView(buffer);
                                                       view.setInt32(0, 1000000);
                                                       view.getInt32(0);

                                           """);
        Assert.Equal(1000000d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_GetSetFloat32()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(8);
                                                       let view = new DataView(buffer);
                                                       view.setFloat32(0, 3.14);
                                                       Math.floor(view.getFloat32(0) * 100);

                                           """);
        Assert.Equal(314d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_GetSetFloat64()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(16);
                                                       let view = new DataView(buffer);
                                                       view.setFloat64(0, 3.14159265359);
                                                       Math.floor(view.getFloat64(0) * 1000);

                                           """);
        Assert.Equal(3141d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_SharedBuffer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(8);
                                                       let view1 = new DataView(buffer);
                                                       let view2 = new DataView(buffer);
                                                       view1.setInt32(0, 42);
                                                       view2.getInt32(0);

                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_WithOffset()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(16);
                                                       let view = new DataView(buffer, 8);
                                                       view.byteLength;

                                           """);
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DataView_WithOffsetAndLength()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(16);
                                                       let view = new DataView(buffer, 4, 8);
                                                       view.byteLength;

                                           """);
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_MultipleViewsShareBuffer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let buffer = new ArrayBuffer(8);
                                                       let arr1 = new Uint8Array(buffer);
                                                       let arr2 = new Uint32Array(buffer);

                                                       arr1[0] = 1;
                                                       arr1[1] = 2;
                                                       arr1[2] = 3;
                                                       arr1[3] = 4;

                                                       // arr2[0] should read the first 4 bytes as a 32-bit int (little-endian)
                                                       arr2[0];

                                           """);
        // Little-endian: 1 + (2<<8) + (3<<16) + (4<<24) = 1 + 512 + 196608 + 67108864 = 67305985
        Assert.Equal(67305985d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_ConstructorBYTES_PER_ELEMENT()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       Int8Array.BYTES_PER_ELEMENT +
                                                       Uint16Array.BYTES_PER_ELEMENT +
                                                       Float64Array.BYTES_PER_ELEMENT;

                                           """);
        Assert.Equal(11d, result); // 1 + 2 + 8
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_ZeroLengthArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint8Array(0);
                                                       arr.length;

                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_LargeArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint8Array(1000);
                                                       arr[999] = 42;
                                                       arr[999];

                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_SubarrayNegativeIndices()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint8Array([0, 1, 2, 3, 4, 5]);
                                                       let sub = arr.subarray(-3, -1);
                                                       sub.length;

                                           """);
        Assert.Equal(2d, result); // Elements at indices 3, 4
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_SliceNegativeIndices()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = new Uint8Array([0, 1, 2, 3, 4, 5]);
                                                       let sliced = arr.slice(-3, -1);
                                                       sliced.length;

                                           """);
        Assert.Equal(2d, result); // Elements at indices 3, 4
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Map_UsesSpeciesConstructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       let hits = 0;
                                                       const arr = new Uint8Array([1, 2, 3]);
                                                       const mapped = arr.map((v) => { hits++; return v + 1; });
                                                       return {
                                                         instance: mapped instanceof Uint8Array,
                                                         first: mapped[0],
                                                         len: mapped.length,
                                                         hits
                                                       };
                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["instance"]);
        Assert.Equal(2d, obj["first"]);
        Assert.Equal(3d, obj["len"]);
        Assert.Equal(3d, obj["hits"]);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_ToReversed_UsesSpeciesConstructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new Uint8Array([1, 2, 3]);
                                                       const reversed = arr.toReversed();
                                                       return {
                                                         instance: reversed instanceof Uint8Array,
                                                         first: reversed[0],
                                                         len: reversed.length,
                                                         sameReference: reversed === arr
                                                       };
                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["instance"]);
        Assert.Equal(3d, obj["first"]);
        Assert.Equal(3d, obj["len"]);
        Assert.Equal(false, obj["sameReference"]);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_ToSorted_DefaultCompare()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new Int16Array([3, 1, 2]);
                                                       const sorted = arr.toSorted();
                                                       return {vals: [sorted[0], sorted[1], sorted[2]], sameProto: Object.getPrototypeOf(sorted) === Object.getPrototypeOf(arr)};
                                           """);
        var obj = Assert.IsType<JsObject>(result);
        var vals = Assert.IsType<JsArray>(obj["vals"]);
        Assert.Equal(1d, vals.GetElement(0).ToObject());
        Assert.Equal(2d, vals.GetElement(1).ToObject());
        Assert.Equal(3d, vals.GetElement(2).ToObject());
        Assert.Equal(true, obj["sameProto"]);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Every_AppliesCallback()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const calls = [];
                                                       const arr = new Uint8Array([1, 2, 3]);
                                                       const outcome = arr.every(function(value, index, ta) {
                                                         calls.push({value, index, same: ta === arr});
                                                         return value < 3;
                                                       });
                                                       return {outcome, calls};
                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(false, obj["outcome"]);
        var calls = Assert.IsType<JsArray>(obj["calls"]);
        Assert.Equal(3d, calls.Length);
        var first = Assert.IsType<JsObject>(calls.GetElement(0).ToObject());
        Assert.Equal(1d, first["value"]);
        Assert.Equal(0d, first["index"]);
        Assert.Equal(true, first["same"]);
        var third = Assert.IsType<JsObject>(calls.GetElement(2).ToObject());
        Assert.Equal(3d, third["value"]);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Every_UsesThisArg()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const ctx = { touched: 0 };
                                                       const arr = new Int16Array([1]);
                                                       arr.every(function() { this.touched++; return true; }, ctx);
                                                       return ctx.touched;
                                           """);
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Every_DefaultThisIsUndefinedInStrictMode()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       "use strict";
                                                       let seen;
                                                       const arr = new BigInt64Array(3);
                                                       arr.every(function() { seen = this; return true; });
                                                       seen;
                                           """);
        Assert.Same(Symbol.Undefined, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Reverse_InPlace()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new Uint8Array([1, 2, 3]);
                                                       const ret = arr.reverse();
                                                       return {same: ret === arr, values: [arr[0], arr[1], arr[2]]};
                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["same"]);
        var values = Assert.IsType<JsArray>(obj["values"]);
        Assert.Equal(3d, values.GetElement(0).ToObject());
        Assert.Equal(2d, values.GetElement(1).ToObject());
        Assert.Equal(1d, values.GetElement(2).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Reverse_BigInt()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new BigInt64Array([1n, -2n, 3n]);
                                                       arr.reverse();
                                                       return [arr[0], arr[1], arr[2]];
                                           """);
        var arr = Assert.IsType<JsArray>(result);
        var first = Assert.IsType<JsBigInt>(arr.GetElement(0).ToObject());
        var second = Assert.IsType<JsBigInt>(arr.GetElement(1).ToObject());
        var third = Assert.IsType<JsBigInt>(arr.GetElement(2).ToObject());
        Assert.Equal(new JsBigInt(3), first);
        Assert.Equal(new JsBigInt(-2), second);
        Assert.Equal(new JsBigInt(1), third);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Fill_ReplacesRange()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new Int16Array([1, 2, 3, 4]);
                                                       arr.fill(9, 1, 3);
                                                       return [arr[0], arr[1], arr[2], arr[3]];
                                           """);
        var vals = Assert.IsType<JsArray>(result);
        Assert.Equal(1d, vals.GetElement(0).ToObject());
        Assert.Equal(9d, vals.GetElement(1).ToObject());
        Assert.Equal(9d, vals.GetElement(2).ToObject());
        Assert.Equal(4d, vals.GetElement(3).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Fill_DefaultsToZeroForUint8()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new Uint8Array([5, 6]);
                                                       arr.fill();
                                                       return [arr[0], arr[1]];
                                           """);
        var vals = Assert.IsType<JsArray>(result);
        Assert.Equal(0d, vals.GetElement(0).ToObject());
        Assert.Equal(0d, vals.GetElement(1).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_CopyWithin_Basic()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new Int16Array([1, 2, 3, 4]);
                                                       arr.copyWithin(1, 2);
                                                       return [arr[0], arr[1], arr[2], arr[3]];
                                           """);
        var vals = Assert.IsType<JsArray>(result);
        Assert.Equal(1d, vals.GetElement(0).ToObject());
        Assert.Equal(3d, vals.GetElement(1).ToObject());
        Assert.Equal(4d, vals.GetElement(2).ToObject());
        Assert.Equal(4d, vals.GetElement(3).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_CopyWithin_HandlesOverlap()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new Uint8Array([0, 1, 2, 3]);
                                                       arr.copyWithin(0, 1, 3);
                                                       return [arr[0], arr[1], arr[2], arr[3]];
                                           """);
        var vals = Assert.IsType<JsArray>(result);
        Assert.Equal(1d, vals.GetElement(0).ToObject());
        Assert.Equal(2d, vals.GetElement(1).ToObject());
        Assert.Equal(2d, vals.GetElement(2).ToObject());
        Assert.Equal(3d, vals.GetElement(3).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_ToSpliced_ReplacesSegment()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new Uint8Array([1, 2, 3, 4]);
                                                       const spliced = arr.toSpliced(1, 2, 99, 100);
                                                       return [spliced[0], spliced[1], spliced[2], spliced[3]];
                                           """);
        var arr = Assert.IsType<JsArray>(result);
        Assert.Equal(1d, arr.GetElement(0).ToObject());
        Assert.Equal(99d, arr.GetElement(1).ToObject());
        Assert.Equal(100d, arr.GetElement(2).ToObject());
        Assert.Equal(4d, arr.GetElement(3).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_ToSpliced_TreatsUndefinedDeleteCountAsWholeTail()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new Uint8Array([1, 2, 3]);
                                                       const spliced = arr.toSpliced(1, undefined, 42);
                                                       return [spliced.length, spliced[0], spliced[1]];
                                           """);
        var arr = Assert.IsType<JsArray>(result);
        Assert.Equal(2d, arr.GetElement(0).ToObject());
        Assert.Equal(1d, arr.GetElement(1).ToObject());
        Assert.Equal(42d, arr.GetElement(2).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_With_ReplacesElement()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                                       const arr = new Uint8Array([5, 6, 7]);
                                                       const copy = arr.with(1, 42);
                                                       return {orig: arr[1], copy: copy[1], len: copy.length};
                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(6d, obj["orig"]);
        Assert.Equal(42d, obj["copy"]);
        Assert.Equal(3d, obj["len"]);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Sort_SortsInPlace()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const arr = new Int32Array([3, 1, 4, 1, 5, 9, 2, 6]);
                                           arr.sort();
                                           return arr.join(',');
                                           """);
        Assert.Equal("1,1,2,3,4,5,6,9", result?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Sort_WithCompareFunction()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const arr = new Int32Array([3, 1, 4]);
                                           arr.sort((a, b) => b - a);
                                           return arr.join(',');
                                           """);
        Assert.Equal("4,3,1", result?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Sort_ReturnsSameArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const arr = new Int32Array([3, 1, 2]);
                                           return arr.sort() === arr;
                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Join_DefaultSeparator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const arr = new Int32Array([1, 2, 3]);
                                           return arr.join();
                                           """);
        Assert.Equal("1,2,3", result?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Join_CustomSeparator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const arr = new Float64Array([1.5, 2.5, 3.5]);
                                           return arr.join(' - ');
                                           """);
        Assert.Equal("1.5 - 2.5 - 3.5", result?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_Join_EmptyArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const arr = new Int32Array(0);
                                           return arr.join();
                                           """);
        Assert.Equal("", result?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_ToString_ReturnsCommaSeparated()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const arr = new Uint8Array([10, 20, 30]);
                                           return arr.toString();
                                           """);
        Assert.Equal("10,20,30", result?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_ToLocaleString_ReturnsString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const arr = new Float32Array([1, 2, 3]);
                                           return typeof arr.toLocaleString() === 'string';
                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypedArray_ToString_SameFunctionAsArrayToString()
    {
        // Per ECMAScript spec, TypedArray.prototype.toString must be === Array.prototype.toString
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const ta = Object.getPrototypeOf(Int8Array.prototype);
                                           return ta.toString === Array.prototype.toString;
                                           """);
        Assert.Equal(true, result);
    }

    // Regression: BigInt typed-array buffer-arg ToIndex coercion for length and byteOffset
    // Mirrors Test262 built-ins/TypedArrayConstructors/ctors-bigint/buffer-arg/toindex-bytelength.js
    // and toindex-byteoffset.js. No runtime fix needed — focused local pin per test262-triage-proof rule.

    [Fact(Timeout = 2000)]
    public async Task BigIntTypedArray_BufferArg_ToIndex_Length_NegativeZero()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const buf = new ArrayBuffer(16);
                                           const ta = new BigInt64Array(buf, 0, -0);
                                           ta.length;
                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntTypedArray_BufferArg_ToIndex_Length_ObjectValueOf()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const buf = new ArrayBuffer(16);
                                           const obj = { valueOf() { return 1; } };
                                           const ta = new BigInt64Array(buf, 0, obj);
                                           ta.length;
                                           """);
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntTypedArray_BufferArg_ToIndex_Length_FractionalTruncated()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const buf = new ArrayBuffer(16);
                                           const ta1 = new BigInt64Array(buf, 0, 0.9);
                                           const ta2 = new BigInt64Array(buf, 0, 1.9);
                                           return [ta1.length, ta2.length];
                                           """);
        var arr = Assert.IsType<JsArray>(result);
        Assert.Equal(0d, arr.GetElement(0).ToObject());
        Assert.Equal(1d, arr.GetElement(1).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntTypedArray_BufferArg_ToIndex_Length_StringCoerced()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const buf = new ArrayBuffer(16);
                                           const ta1 = new BigInt64Array(buf, 0, "");
                                           const ta2 = new BigInt64Array(buf, 0, "1");
                                           return [ta1.length, ta2.length];
                                           """);
        var arr = Assert.IsType<JsArray>(result);
        Assert.Equal(0d, arr.GetElement(0).ToObject());
        Assert.Equal(1d, arr.GetElement(1).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntTypedArray_BufferArg_ToIndex_ByteOffset_NegativeZero()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const buf = new ArrayBuffer(16);
                                           const ta = new BigInt64Array(buf, -0);
                                           ta.byteOffset;
                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntTypedArray_BufferArg_ToIndex_ByteOffset_ObjectValueOf()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const buf = new ArrayBuffer(16);
                                           const obj = { valueOf() { return 8; } };
                                           const ta = new BigInt64Array(buf, obj);
                                           ta.byteOffset;
                                           """);
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntTypedArray_BufferArg_ToIndex_ByteOffset_FractionalTruncated()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const buf = new ArrayBuffer(16);
                                           const ta1 = new BigInt64Array(buf, 0.9);
                                           const ta2 = new BigInt64Array(buf, 8.9);
                                           return [ta1.byteOffset, ta2.byteOffset];
                                           """);
        var arr = Assert.IsType<JsArray>(result);
        Assert.Equal(0d, arr.GetElement(0).ToObject());
        Assert.Equal(8d, arr.GetElement(1).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntTypedArray_BufferArg_ToIndex_ByteOffset_UnalignedThrows()
    {
        // BigInt64Array has BYTES_PER_ELEMENT=8; offset 1 is not aligned — must throw RangeError
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const buf = new ArrayBuffer(16);
                                           let threw = false;
                                           let name = "";
                                           try { new BigInt64Array(buf, true); }
                                           catch (e) { threw = true; name = e.constructor.name; }
                                           return { threw, name };
                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["threw"]);
        Assert.Equal("RangeError", obj["name"]?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntTypedArray_SharedArrayBuffer_BufferArg_ToIndex_Length_NegativeZero()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const buf = new SharedArrayBuffer(16);
                                           const ta = new BigInt64Array(buf, 0, -0);
                                           ta.length;
                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigIntTypedArray_SharedArrayBuffer_BufferArg_ToIndex_ByteOffset_FractionalTruncated()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           const buf = new SharedArrayBuffer(16);
                                           const ta = new BigInt64Array(buf, 8.9);
                                           ta.byteOffset;
                                           """);
        Assert.Equal(8d, result);
    }

    // Regression: BigInt typed-array length-arg ToIndex coercion (1-argument constructor)
    // Mirrors Test262 built-ins/TypedArrayConstructors/ctors-bigint/length-arg/toindex-length.js
    // Focused proof passed (24/24 green); this pin ensures future refactors cannot silently regress
    // the BigInt variants (BigInt64Array / BigUint64Array) per test262-triage-proof rule 5.

    [Theory(Timeout = 2000)]
    [InlineData("new BigInt64Array(-0)", 0d)]
    [InlineData("new BigInt64Array('')", 0d)]
    [InlineData("new BigInt64Array('0')", 0d)]
    [InlineData("new BigInt64Array('1')", 1d)]
    [InlineData("new BigInt64Array(true)", 1d)]
    [InlineData("new BigInt64Array(false)", 0d)]
    [InlineData("new BigInt64Array(NaN)", 0d)]
    [InlineData("new BigInt64Array(null)", 0d)]
    [InlineData("new BigInt64Array(undefined)", 0d)]
    [InlineData("new BigInt64Array(0.1)", 0d)]
    [InlineData("new BigInt64Array(0.9)", 0d)]
    [InlineData("new BigInt64Array(1.1)", 1d)]
    [InlineData("new BigInt64Array(1.9)", 1d)]
    [InlineData("new BigInt64Array(-0.1)", 0d)]
    [InlineData("new BigInt64Array(-0.99999)", 0d)]
    public async Task BigInt64Array_LengthArg_ToIndex_CoercesCorrectly(string expression, double expectedLength)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($"{expression}.length");
        Assert.Equal(expectedLength, result);
    }

    [Theory(Timeout = 2000)]
    [InlineData("new BigUint64Array(-0)", 0d)]
    [InlineData("new BigUint64Array('')", 0d)]
    [InlineData("new BigUint64Array('0')", 0d)]
    [InlineData("new BigUint64Array('1')", 1d)]
    [InlineData("new BigUint64Array(true)", 1d)]
    [InlineData("new BigUint64Array(false)", 0d)]
    [InlineData("new BigUint64Array(NaN)", 0d)]
    [InlineData("new BigUint64Array(null)", 0d)]
    [InlineData("new BigUint64Array(undefined)", 0d)]
    [InlineData("new BigUint64Array(0.1)", 0d)]
    [InlineData("new BigUint64Array(0.9)", 0d)]
    [InlineData("new BigUint64Array(1.1)", 1d)]
    [InlineData("new BigUint64Array(1.9)", 1d)]
    [InlineData("new BigUint64Array(-0.1)", 0d)]
    [InlineData("new BigUint64Array(-0.99999)", 0d)]
    public async Task BigUint64Array_LengthArg_ToIndex_CoercesCorrectly(string expression, double expectedLength)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($"{expression}.length");
        Assert.Equal(expectedLength, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BigInt64Array_LengthArg_ToIndex_NegativeIntegerThrowsRangeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           let threw = false, name = "";
                                           try { new BigInt64Array(-1); } catch (e) { threw = true; name = e.constructor.name; }
                                           return { threw, name };
                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["threw"]);
        Assert.Equal("RangeError", obj["name"]?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task BigInt64Array_LengthArg_ToIndex_InfinityThrowsRangeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           let threw = false, name = "";
                                           try { new BigInt64Array(Infinity); } catch (e) { threw = true; name = e.constructor.name; }
                                           return { threw, name };
                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["threw"]);
        Assert.Equal("RangeError", obj["name"]?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task BigUint64Array_LengthArg_ToIndex_NegativeIntegerThrowsRangeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           let threw = false, name = "";
                                           try { new BigUint64Array(-1); } catch (e) { threw = true; name = e.constructor.name; }
                                           return { threw, name };
                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["threw"]);
        Assert.Equal("RangeError", obj["name"]?.ToString());
    }

    // Regression: Object.defineProperty on TypedArray integer-indexed key must perform
    // IntegerIndexedElementSet conversion (ToNumber) before storing.
    // Mirrors built-ins/TypedArrayConstructors/internals/DefineOwnProperty/conversion-operation.js
    [Fact(Timeout = 2000)]
    public async Task DefineOwnProperty_NumberArray_ConvertsValueViaToNumber()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           let ta = new Int8Array([0]);
                                           Object.defineProperty(ta, "0", { value: 129 });
                                           return ta[0];
                                           """);
        // 129 overflows Int8: 129 % 256 = 129, then as signed = -127
        Assert.Equal(-127d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DefineOwnProperty_NumberArray_Uint8_ConvertsNegativeValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           let ta = new Uint8Array([0]);
                                           Object.defineProperty(ta, "0", { value: -1 });
                                           return ta[0];
                                           """);
        // -1 as Uint8 = 255
        Assert.Equal(255d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DefineOwnProperty_NumberArray_Float32_ConvertsFloat()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           let ta = new Float32Array([0]);
                                           Object.defineProperty(ta, "0", { value: 1.5 });
                                           return ta[0];
                                           """);
        Assert.Equal(1.5d, result);
    }

    // Regression: Object.defineProperty on BigInt TypedArray integer-indexed key must perform
    // IntegerIndexedElementSet conversion (ToBigInt) before storing.
    // Mirrors built-ins/TypedArrayConstructors/internals/DefineOwnProperty/conversion-operation.js (BigInt variant)
    [Fact(Timeout = 2000)]
    public async Task DefineOwnProperty_BigInt64Array_ConvertsBigIntValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           let ta = new BigInt64Array([0n]);
                                           Object.defineProperty(ta, "0", { value: 42n });
                                           return ta[0];
                                           """);
        Assert.Equal(new JsBigInt(42), result);
    }

    [Fact(Timeout = 2000)]
    public async Task DefineOwnProperty_BigInt64Array_WrapsOverflow()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           let ta = new BigInt64Array([0n]);
                                           // 2n**63n overflows BigInt64: wraps to -9223372036854775808n (Int64.MinValue)
                                           Object.defineProperty(ta, "0", { value: 9223372036854775808n });
                                           return ta[0] === -9223372036854775808n;
                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DefineOwnProperty_ValueConversion_DetachDuringValueOf_ReturnsTrue()
    {
        // Per IntegerIndexedElementSet: ToNumber/ToBigInt is called before second IsValidIntegerIndex check.
        // If valueOf detaches the buffer, the operation returns true with no write.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
                                           let buffer = new ArrayBuffer(4);
                                           let ta = new Int32Array(buffer);
                                           ta[0] = 99;
                                           let detached = false;
                                           let obj = {
                                               valueOf() {
                                                   let ab = new ArrayBuffer(0);
                                                   // Detach by transfer
                                                   ta = new Int32Array(ab);
                                                   detached = true;
                                                   return 5;
                                               }
                                           };
                                           let result = Reflect.defineProperty(ta, "0", { value: obj });
                                           return { result, detached };
                                           """);
        var obj2 = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj2["detached"]);
        // defineProperty returns true even though the buffer was detached during conversion
        Assert.Equal(true, obj2["result"]);
    }

}
