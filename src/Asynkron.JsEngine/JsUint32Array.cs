using System.Buffers.Binary;

namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript Uint32Array - an array of 32-bit unsigned integers.
/// </summary>
internal sealed class JsUint32Array : TypedArrayBase
{
    public const int BYTES_PER_ELEMENT = 4;

    public JsUint32Array(JsArrayBuffer buffer, int byteOffset, int length)
        : base(buffer, byteOffset, length, BYTES_PER_ELEMENT)
    {
    }

    public static JsUint32Array FromLength(int length)
    {
        var buffer = new JsArrayBuffer(length * BYTES_PER_ELEMENT);
        return new JsUint32Array(buffer, 0, length);
    }

    public static JsUint32Array FromArray(JsArray array)
    {
        var length = array.Items.Count;
        var typedArray = FromLength(length);
        typedArray.Set(array, 0);
        return typedArray;
    }

    public override double GetElement(int index)
    {
        CheckBounds(index);
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        return BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    public override void SetElement(int index, double value)
    {
        CheckBounds(index);
        var intValue = double.IsNaN(value) ? 0 : (long)value;
        var span = new Span<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)intValue);
    }

    public override TypedArrayBase Subarray(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newByteOffset = _byteOffset + (start * BYTES_PER_ELEMENT);
        return new JsUint32Array(_buffer, newByteOffset, newLength);
    }

    public JsUint32Array Slice(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newArray = FromLength(newLength);
        
        for (int i = 0; i < newLength; i++)
        {
            newArray.SetElement(i, GetElement(start + i));
        }
        
        return newArray;
    }
}
