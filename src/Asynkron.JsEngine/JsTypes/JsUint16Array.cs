using System.Buffers.Binary;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript Uint16Array - an array of 16-bit unsigned integers.
/// </summary>
public sealed class JsUint16Array(JsArrayBuffer buffer, int byteOffset, int length, bool isLengthTracking = false)
    : TypedArrayBase(buffer, byteOffset, length, BYTES_PER_ELEMENT, isLengthTracking)
{
    public const int BYTES_PER_ELEMENT = 2;

    public static JsUint16Array FromLength(int length, RealmState? realmState = null)
    {
        var buffer = new JsArrayBuffer(length * BYTES_PER_ELEMENT, null, realmState);
        return new JsUint16Array(buffer, 0, length);
    }

    public static JsUint16Array FromArray(JsArray array, RealmState? realmState = null)
    {
        var length = array.Items.Count;
        var typedArray = FromLength(length, realmState);
        typedArray.Set(array);
        return typedArray;
    }

    public override double GetElement(int index)
    {
        CheckBounds(index);
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        return BinaryPrimitives.ReadUInt16LittleEndian(span);
    }

    protected override TypedArrayBase CreateNewSameType(int length)
    {
        return FromLength(length);
    }

    public override void SetElement(int index, double value)
    {
        CheckBounds(index);
        var intValue = double.IsNaN(value) ? 0 : (int)value;
        var span = new Span<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)intValue);
    }

    public override TypedArrayBase Subarray(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newByteOffset = _byteOffset + start * BYTES_PER_ELEMENT;
        return new JsUint16Array(_buffer, newByteOffset, newLength);
    }
}
