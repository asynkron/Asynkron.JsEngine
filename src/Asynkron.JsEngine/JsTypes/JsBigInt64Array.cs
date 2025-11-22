using System.Buffers.Binary;
using System.Numerics;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Minimal BigInt64Array implementation backed by a shared ArrayBuffer.
/// </summary>
public sealed class JsBigInt64Array(JsArrayBuffer buffer, int byteOffset, int length)
    : TypedArrayBase(buffer, byteOffset, length, BYTES_PER_ELEMENT)
{
    public const int BYTES_PER_ELEMENT = 8;
    public override bool IsBigIntArray => true;

    public static JsBigInt64Array FromLength(int length)
    {
        var buffer = new JsArrayBuffer(length * BYTES_PER_ELEMENT);
        return new JsBigInt64Array(buffer, 0, length);
    }

    public static JsBigInt64Array FromArray(JsArray array)
    {
        var length = array.Items.Count;
        var typedArray = FromLength(length);
        typedArray.Set(array);
        return typedArray;
    }

    public override double GetElement(int index)
    {
        CheckBounds(index);
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        var value = BinaryPrimitives.ReadInt64LittleEndian(span);
        return value;
    }

    protected override TypedArrayBase CreateNewSameType(int length)
    {
        return FromLength(length);
    }

    public override void SetElement(int index, double value)
    {
        CheckBounds(index);
        var coerced = double.IsNaN(value) || double.IsInfinity(value)
            ? 0L
            : (long)value;
        var span = new Span<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        BinaryPrimitives.WriteInt64LittleEndian(span, coerced);
    }

    public void SetElement(int index, JsBigInt value)
    {
        CheckBounds(index);
        var span = new Span<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        var coerced = (long)(value.Value & ((BigInteger.One << 64) - 1));
        BinaryPrimitives.WriteInt64LittleEndian(span, coerced);
    }

    public override void SetValue(int index, object? value)
    {
        SetElement(index, StandardLibrary.ToBigInt(value));
    }

    public JsBigInt GetBigIntElement(int index)
    {
        CheckBounds(index);
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        var value = BinaryPrimitives.ReadInt64LittleEndian(span);
        return new JsBigInt(new BigInteger(value));
    }

    public override TypedArrayBase Subarray(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newByteOffset = _byteOffset + start * BYTES_PER_ELEMENT;
        return new JsBigInt64Array(_buffer, newByteOffset, newLength);
    }
}
