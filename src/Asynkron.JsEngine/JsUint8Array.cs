namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript Uint8Array - an array of 8-bit unsigned integers.
/// </summary>
internal sealed class JsUint8Array : TypedArrayBase
{
    public const int BYTES_PER_ELEMENT = 1;

    public JsUint8Array(JsArrayBuffer buffer, int byteOffset, int length)
        : base(buffer, byteOffset, length, BYTES_PER_ELEMENT)
    {
    }

    public static JsUint8Array FromLength(int length)
    {
        var buffer = new JsArrayBuffer(length * BYTES_PER_ELEMENT);
        return new JsUint8Array(buffer, 0, length);
    }

    public static JsUint8Array FromArray(JsArray array)
    {
        var length = array.Items.Count;
        var typedArray = FromLength(length);
        typedArray.Set(array, 0);
        return typedArray;
    }

    public override double GetElement(int index)
    {
        CheckBounds(index);
        return _buffer.Buffer[GetByteIndex(index)];
    }

    public override void SetElement(int index, double value)
    {
        CheckBounds(index);
        var intValue = double.IsNaN(value) ? 0 : (int)value;
        _buffer.Buffer[GetByteIndex(index)] = (byte)intValue;
    }

    public override TypedArrayBase Subarray(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newByteOffset = _byteOffset + (start * BYTES_PER_ELEMENT);
        return new JsUint8Array(_buffer, newByteOffset, newLength);
    }

    public JsUint8Array Slice(int begin, int end)
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
