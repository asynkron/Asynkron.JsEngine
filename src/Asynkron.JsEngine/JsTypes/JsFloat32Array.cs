using System.Buffers.Binary;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Represents a JavaScript Float32Array - an array of 32-bit floating point numbers.
/// </summary>
public sealed class JsFloat32Array(JsArrayBuffer buffer, int byteOffset, int length)
    : TypedArrayBase(buffer, byteOffset, length, BYTES_PER_ELEMENT)
{
    public const int BYTES_PER_ELEMENT = 4;

    public static JsFloat32Array FromLength(int length)
    {
        var buffer = new JsArrayBuffer(length * BYTES_PER_ELEMENT);
        return new JsFloat32Array(buffer, 0, length);
    }

    public static JsFloat32Array FromArray(JsArray array)
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
        return BinaryPrimitives.ReadSingleLittleEndian(span);
    }

    public override void SetElement(int index, double value)
    {
        CheckBounds(index);
        var floatValue = (float)value;
        var span = new Span<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        BinaryPrimitives.WriteSingleLittleEndian(span, floatValue);
    }

    public override TypedArrayBase Subarray(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newByteOffset = _byteOffset + start * BYTES_PER_ELEMENT;
        return new JsFloat32Array(_buffer, newByteOffset, newLength);
    }

    public JsFloat32Array Slice(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newArray = FromLength(newLength);

        for (var i = 0; i < newLength; i++) newArray.SetElement(i, GetElement(start + i));

        return newArray;
    }
}