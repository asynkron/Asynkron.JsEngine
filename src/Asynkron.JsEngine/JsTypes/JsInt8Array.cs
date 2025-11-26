using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript Int8Array - an array of 8-bit signed integers.
/// </summary>
public sealed class JsInt8Array(JsArrayBuffer buffer, int byteOffset, int length, bool isLengthTracking = false)
    : TypedArrayBase(buffer, byteOffset, length, BYTES_PER_ELEMENT, isLengthTracking)
{
    public const int BYTES_PER_ELEMENT = 1;

    public static JsInt8Array FromLength(int length, RealmState? realmState = null)
    {
        var buffer = new JsArrayBuffer(length * BYTES_PER_ELEMENT, null, realmState);
        return new JsInt8Array(buffer, 0, length);
    }

    public static JsInt8Array FromArray(JsArray array, RealmState? realmState = null)
    {
        var length = array.Items.Count;
        var typedArray = FromLength(length, realmState);
        typedArray.Set(array);
        return typedArray;
    }

    public override double GetElement(int index)
    {
        CheckBounds(index);
        return (sbyte)_buffer.Buffer[GetByteIndex(index)];
    }

    public override void SetElement(int index, double value)
    {
        CheckBounds(index);
        var intValue = double.IsNaN(value) ? 0 : (int)value;
        _buffer.Buffer[GetByteIndex(index)] = (byte)(sbyte)intValue;
    }

    public override TypedArrayBase Subarray(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newByteOffset = _byteOffset + start * BYTES_PER_ELEMENT;
        return new JsInt8Array(_buffer, newByteOffset, newLength);
    }

    protected override TypedArrayBase CreateNewSameType(int length)
    {
        return FromLength(length);
    }
}
