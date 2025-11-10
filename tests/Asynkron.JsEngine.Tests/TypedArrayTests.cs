using Xunit;

namespace Asynkron.JsEngine.Tests;

public class TypedArrayTests
{
    [Fact]
    public void ArrayBuffer_CreatesWithLength()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(8);
            buffer.byteLength;
        ");
        Assert.Equal(8d, result);
    }

    [Fact]
    public void ArrayBuffer_Slice_CreatesNewBuffer()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer1 = new ArrayBuffer(16);
            let buffer2 = buffer1.slice(4, 12);
            buffer2.byteLength;
        ");
        Assert.Equal(8d, result);
    }

    [Fact]
    public void ArrayBuffer_IsView_ReturnsFalseForBuffer()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(8);
            ArrayBuffer.isView(buffer);
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void ArrayBuffer_IsView_ReturnsTrueForTypedArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint8Array(8);
            ArrayBuffer.isView(arr);
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Int8Array_CreatesFromLength()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Int8Array(4);
            arr.length;
        ");
        Assert.Equal(4d, result);
    }

    [Fact]
    public void Int8Array_ElementAccess()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Int8Array(3);
            arr[0] = 10;
            arr[1] = -20;
            arr[2] = 30;
            arr[0] + arr[1] + arr[2];
        ");
        Assert.Equal(20d, result);
    }

    [Fact]
    public void Int8Array_HandlesOverflow()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Int8Array(2);
            arr[0] = 127;
            arr[1] = 128;  // Wraps to -128
            arr[0] + arr[1];
        ");
        Assert.Equal(-1d, result);
    }

    [Fact]
    public void Uint8Array_CreatesFromArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint8Array([10, 20, 30]);
            arr[0] + arr[1] + arr[2];
        ");
        Assert.Equal(60d, result);
    }

    [Fact]
    public void Uint8Array_ByteLength()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint8Array(10);
            arr.byteLength;
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public void Uint8ClampedArray_ClampsToRange()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint8ClampedArray(3);
            arr[0] = -10;    // Clamped to 0
            arr[1] = 300;    // Clamped to 255
            arr[2] = 127.6;  // Rounded to 128
            arr[0] + arr[1] + arr[2];
        ");
        Assert.Equal(383d, result); // 0 + 255 + 128
    }

    [Fact]
    public void Int16Array_BytesPerElement()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Int16Array(5);
            arr.BYTES_PER_ELEMENT;
        ");
        Assert.Equal(2d, result);
    }

    [Fact]
    public void Int16Array_ElementStorage()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Int16Array(2);
            arr[0] = 32767;   // Max int16
            arr[1] = -32768;  // Min int16
            arr[0] + arr[1];
        ");
        Assert.Equal(-1d, result);
    }

    [Fact]
    public void Uint16Array_ElementStorage()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint16Array(2);
            arr[0] = 65535;  // Max uint16
            arr[1] = 1;
            arr[0] + arr[1];
        ");
        Assert.Equal(65536d, result);
    }

    [Fact]
    public void Int32Array_BytesPerElement()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Int32Array(5);
            arr.BYTES_PER_ELEMENT;
        ");
        Assert.Equal(4d, result);
    }

    [Fact]
    public void Int32Array_ElementStorage()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Int32Array(2);
            arr[0] = 1000000;
            arr[1] = -1000000;
            arr[0] + arr[1];
        ");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Uint32Array_ElementStorage()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint32Array(2);
            arr[0] = 4294967295;  // Max uint32
            arr[1] = 1;
            arr[0] + arr[1];
        ");
        Assert.Equal(4294967296d, result);
    }

    [Fact]
    public void Float32Array_BytesPerElement()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Float32Array(5);
            arr.BYTES_PER_ELEMENT;
        ");
        Assert.Equal(4d, result);
    }

    [Fact]
    public void Float32Array_ElementStorage()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Float32Array(2);
            arr[0] = 3.14;
            arr[1] = 2.71;
            Math.floor((arr[0] + arr[1]) * 100);
        ");
        Assert.Equal(585d, result); // Float32 precision
    }

    [Fact]
    public void Float64Array_BytesPerElement()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Float64Array(5);
            arr.BYTES_PER_ELEMENT;
        ");
        Assert.Equal(8d, result);
    }

    [Fact]
    public void Float64Array_ElementStorage()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Float64Array(2);
            arr[0] = 3.14159265359;
            arr[1] = 2.71828182846;
            Math.floor((arr[0] + arr[1]) * 1000);
        ");
        Assert.Equal(5859d, result);
    }

    [Fact]
    public void TypedArray_CreatesFromBuffer()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(16);
            let arr = new Int32Array(buffer);
            arr.length;
        ");
        Assert.Equal(4d, result); // 16 bytes / 4 bytes per element
    }

    [Fact]
    public void TypedArray_CreatesFromBufferWithOffset()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(16);
            let arr = new Int32Array(buffer, 4);
            arr.length;
        ");
        Assert.Equal(3d, result); // (16-4) bytes / 4 bytes per element
    }

    [Fact]
    public void TypedArray_CreatesFromBufferWithOffsetAndLength()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(16);
            let arr = new Int32Array(buffer, 4, 2);
            arr.length;
        ");
        Assert.Equal(2d, result);
    }

    [Fact]
    public void TypedArray_BufferProperty()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(16);
            let arr = new Int32Array(buffer);
            arr.buffer.byteLength;
        ");
        Assert.Equal(16d, result);
    }

    [Fact]
    public void TypedArray_ByteOffsetProperty()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(16);
            let arr = new Int32Array(buffer, 8);
            arr.byteOffset;
        ");
        Assert.Equal(8d, result);
    }

    [Fact]
    public void TypedArray_Subarray_CreatesView()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr1 = new Uint8Array([0, 1, 2, 3, 4, 5]);
            let arr2 = arr1.subarray(2, 5);
            arr1[2] = 100;
            arr2[0];  // Should see the change since it's the same buffer
        ");
        Assert.Equal(100d, result);
    }

    [Fact]
    public void TypedArray_Slice_CopiesData()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr1 = new Uint8Array([0, 1, 2, 3, 4, 5]);
            let arr2 = arr1.slice(2, 5);
            arr1[2] = 100;
            arr2[0];  // Should still be 2 since it's a copy
        ");
        Assert.Equal(2d, result);
    }

    [Fact]
    public void TypedArray_Set_FromTypedArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr1 = new Uint8Array([1, 2, 3]);
            let arr2 = new Uint8Array(5);
            arr2.set(arr1, 1);
            arr2[0] + arr2[1] + arr2[2] + arr2[3] + arr2[4];
        ");
        Assert.Equal(6d, result); // 0 + 1 + 2 + 3 + 0
    }

    [Fact]
    public void TypedArray_Set_FromArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint8Array(5);
            arr.set([10, 20, 30], 1);
            arr[0] + arr[1] + arr[2] + arr[3] + arr[4];
        ");
        Assert.Equal(60d, result); // 0 + 10 + 20 + 30 + 0
    }

    [Fact]
    public void DataView_CreatesFromBuffer()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(16);
            let view = new DataView(buffer);
            view.byteLength;
        ");
        Assert.Equal(16d, result);
    }

    [Fact]
    public void DataView_GetSetInt8()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(4);
            let view = new DataView(buffer);
            view.setInt8(0, 127);
            view.setInt8(1, -128);
            view.getInt8(0) + view.getInt8(1);
        ");
        Assert.Equal(-1d, result);
    }

    [Fact]
    public void DataView_GetSetUint8()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(4);
            let view = new DataView(buffer);
            view.setUint8(0, 255);
            view.setUint8(1, 100);
            view.getUint8(0) + view.getUint8(1);
        ");
        Assert.Equal(355d, result);
    }

    [Fact]
    public void DataView_GetSetInt16()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(8);
            let view = new DataView(buffer);
            view.setInt16(0, 1000);
            view.getInt16(0);
        ");
        Assert.Equal(1000d, result);
    }

    [Fact]
    public void DataView_GetSetInt32()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(8);
            let view = new DataView(buffer);
            view.setInt32(0, 1000000);
            view.getInt32(0);
        ");
        Assert.Equal(1000000d, result);
    }

    [Fact]
    public void DataView_GetSetFloat32()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(8);
            let view = new DataView(buffer);
            view.setFloat32(0, 3.14);
            Math.floor(view.getFloat32(0) * 100);
        ");
        Assert.Equal(314d, result);
    }

    [Fact]
    public void DataView_GetSetFloat64()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(16);
            let view = new DataView(buffer);
            view.setFloat64(0, 3.14159265359);
            Math.floor(view.getFloat64(0) * 1000);
        ");
        Assert.Equal(3141d, result);
    }

    [Fact]
    public void DataView_SharedBuffer()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(8);
            let view1 = new DataView(buffer);
            let view2 = new DataView(buffer);
            view1.setInt32(0, 42);
            view2.getInt32(0);
        ");
        Assert.Equal(42d, result);
    }

    [Fact]
    public void DataView_WithOffset()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(16);
            let view = new DataView(buffer, 8);
            view.byteLength;
        ");
        Assert.Equal(8d, result);
    }

    [Fact]
    public void DataView_WithOffsetAndLength()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(16);
            let view = new DataView(buffer, 4, 8);
            view.byteLength;
        ");
        Assert.Equal(8d, result);
    }

    [Fact]
    public void TypedArray_MultipleViewsShareBuffer()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let buffer = new ArrayBuffer(8);
            let arr1 = new Uint8Array(buffer);
            let arr2 = new Uint32Array(buffer);
            
            arr1[0] = 1;
            arr1[1] = 2;
            arr1[2] = 3;
            arr1[3] = 4;
            
            // arr2[0] should read the first 4 bytes as a 32-bit int (little-endian)
            arr2[0];
        ");
        // Little-endian: 1 + (2<<8) + (3<<16) + (4<<24) = 1 + 512 + 196608 + 67108864 = 67305985
        Assert.Equal(67305985d, result);
    }

    [Fact]
    public void TypedArray_ConstructorBYTES_PER_ELEMENT()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            Int8Array.BYTES_PER_ELEMENT + 
            Uint16Array.BYTES_PER_ELEMENT + 
            Float64Array.BYTES_PER_ELEMENT;
        ");
        Assert.Equal(11d, result); // 1 + 2 + 8
    }

    [Fact]
    public void TypedArray_ZeroLengthArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint8Array(0);
            arr.length;
        ");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void TypedArray_LargeArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint8Array(1000);
            arr[999] = 42;
            arr[999];
        ");
        Assert.Equal(42d, result);
    }

    [Fact]
    public void TypedArray_SubarrayNegativeIndices()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint8Array([0, 1, 2, 3, 4, 5]);
            let sub = arr.subarray(-3, -1);
            sub.length;
        ");
        Assert.Equal(2d, result); // Elements at indices 3, 4
    }

    [Fact]
    public void TypedArray_SliceNegativeIndices()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = new Uint8Array([0, 1, 2, 3, 4, 5]);
            let sliced = arr.slice(-3, -1);
            sliced.length;
        ");
        Assert.Equal(2d, result); // Elements at indices 3, 4
    }
}
