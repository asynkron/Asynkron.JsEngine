using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript Uint8ClampedArray - an array of 8-bit unsigned integers clamped to 0-255.
/// </summary>
public sealed class JsUint8ClampedArray(JsArrayBuffer buffer, int byteOffset, int length, bool isLengthTracking = false)
    : TypedArrayBase(buffer, byteOffset, length, BYTES_PER_ELEMENT, isLengthTracking)
{
    public const int BYTES_PER_ELEMENT = 1;

    public static JsUint8ClampedArray FromLength(int length, RealmState? realmState = null)
    {
        var buffer = new JsArrayBuffer(length * BYTES_PER_ELEMENT, null, realmState);
        return new JsUint8ClampedArray(buffer, 0, length);
    }

    public static JsUint8ClampedArray FromArray(JsArray array, RealmState? realmState = null)
    {
        var length = array.Items.Count;
        var typedArray = FromLength(length, realmState);
        typedArray.Set(array);
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
            clampedValue = (byte)Math.Round(value, MidpointRounding.ToEven);
        }

        _buffer.Buffer[GetByteIndex(index)] = clampedValue;
    }

    public override TypedArrayBase Subarray(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newByteOffset = _byteOffset + start * BYTES_PER_ELEMENT;
        return new JsUint8ClampedArray(_buffer, newByteOffset, newLength);
    }

    protected override TypedArrayBase CreateNewSameType(int length)
    {
        return FromLength(length);
    }
}
