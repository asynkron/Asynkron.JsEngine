using System.Buffers.Binary;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Represents a JavaScript Float64Array - an array of 64-bit floating point numbers.
/// </summary>
public sealed class JsFloat64Array(JsArrayBuffer buffer, int byteOffset, int length)
    : TypedArrayBase(buffer, byteOffset, length, BYTES_PER_ELEMENT)
{
    public const int BYTES_PER_ELEMENT = 8;

    public static JsFloat64Array FromLength(int length)
    {
        var buffer = new JsArrayBuffer(length * BYTES_PER_ELEMENT);
        return new JsFloat64Array(buffer, 0, length);
    }

    public static JsFloat64Array FromArray(JsArray array)
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
        return BinaryPrimitives.ReadDoubleLittleEndian(span);
    }

    protected override TypedArrayBase CreateNewSameType(int length)
    {
        return FromLength(length);
    }

    public override void SetElement(int index, double value)
    {
        CheckBounds(index);
        var span = new Span<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        BinaryPrimitives.WriteDoubleLittleEndian(span, value);
    }

    public override TypedArrayBase Subarray(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newByteOffset = _byteOffset + start * BYTES_PER_ELEMENT;
        return new JsFloat64Array(_buffer, newByteOffset, newLength);
    }

}