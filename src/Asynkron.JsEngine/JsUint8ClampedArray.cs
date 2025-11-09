namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript Uint8ClampedArray - an array of 8-bit unsigned integers clamped to 0-255.
/// </summary>
internal sealed class JsUint8ClampedArray : TypedArrayBase
{
    public const int BYTES_PER_ELEMENT = 1;

    public JsUint8ClampedArray(JsArrayBuffer buffer, int byteOffset, int length)
        : base(buffer, byteOffset, length, BYTES_PER_ELEMENT)
    {
    }

    public static JsUint8ClampedArray FromLength(int length)
    {
        var buffer = new JsArrayBuffer(length * BYTES_PER_ELEMENT);
        return new JsUint8ClampedArray(buffer, 0, length);
    }

    public static JsUint8ClampedArray FromArray(JsArray array)
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
        // Clamp to 0-255 range, with proper rounding
        byte clampedValue;
        if (double.IsNaN(value))
        {
            clampedValue = 0;
        }
        else if (value <= 0)
        {
            clampedValue = 0;
        }
        else if (value >= 255)
        {
            clampedValue = 255;
        }
        else
        {
            // Round to nearest, ties to even (matches JavaScript spec)
            clampedValue = (byte)Math.Round(value, MidpointRounding.ToEven);
        }
        
        _buffer.Buffer[GetByteIndex(index)] = clampedValue;
    }

    public override TypedArrayBase Subarray(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newByteOffset = _byteOffset + (start * BYTES_PER_ELEMENT);
        return new JsUint8ClampedArray(_buffer, newByteOffset, newLength);
    }

    public JsUint8ClampedArray Slice(int begin, int end)
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
