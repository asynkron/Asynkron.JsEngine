#region

using System.Numerics;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Abstract base class for BigInt typed arrays (BigInt64Array and BigUint64Array).
///     Consolidates common logic for 64-bit integer array operations.
/// </summary>
public abstract class BigIntTypedArrayBase<TSelf>(
    JsArrayBuffer buffer,
    int byteOffset,
    int length,
    bool isLengthTracking = false)
    : TypedArrayBase(buffer, byteOffset, length, BYTES_PER_ELEMENT, isLengthTracking)
    where TSelf : BigIntTypedArrayBase<TSelf>
{
    public const int BYTES_PER_ELEMENT = 8;

    public override bool IsBigIntArray => true;

    /// <summary>
    ///     Reads the raw 64-bit value from the span as a double (for GetElement).
    /// </summary>
    protected abstract double ReadAsDouble(ReadOnlySpan<byte> span);

    /// <summary>
    ///     Reads the raw 64-bit value from the span as a BigInteger (for GetBigIntElement).
    /// </summary>
    protected abstract BigInteger ReadAsBigInteger(ReadOnlySpan<byte> span);

    /// <summary>
    ///     Writes the coerced BigInt value to the span.
    /// </summary>
    protected abstract void WriteCoercedBigInt(Span<byte> span, BigInteger value);

    /// <summary>
    ///     Creates a new instance of this array type from a buffer.
    /// </summary>
    protected abstract TSelf CreateFromBuffer(JsArrayBuffer buffer, int byteOffset, int length);

    /// <summary>
    ///     Creates a new instance of this array type with the specified length.
    /// </summary>
    protected static TSelf CreateFromLength(int length, RealmState? realmState, Func<JsArrayBuffer, int, int, TSelf> factory)
    {
        var buffer = new JsArrayBuffer(length * BYTES_PER_ELEMENT, null, realmState);
        return factory(buffer, 0, length);
    }

    /// <summary>
    ///     Creates a new instance from a JsArray.
    /// </summary>
    protected static TSelf CreateFromArray(JsArray array, RealmState? realmState, Func<int, RealmState?, TSelf> fromLength)
    {
        var length = array.Items.Count;
        var typedArray = fromLength(length, realmState);
        typedArray.Set(array);
        return typedArray;
    }

    public override double GetElement(int index)
    {
        CheckBounds(index);
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        return ReadAsDouble(span);
    }

    public override void SetElement(int index, double value)
    {
        CheckBounds(index);
        SetValue(index, value);
    }

    public void SetElement(int index, JsBigInt value)
    {
        CheckBounds(index);
        var span = new Span<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        WriteCoercedBigInt(span, value.Value);
    }

    public override void SetValue(int index, JsValue value)
    {
        var coerced = StandardLibrary.ToBigInt(value, realmState: _buffer.RealmState);

        // Per spec, if the index is no longer valid after coercion, silently return.
        if (IsDetachedOrOutOfBounds() || index < 0 || index >= Length)
        {
            return;
        }

        SetElement(index, coerced);
    }

    public JsBigInt GetBigIntElement(int index)
    {
        CheckBounds(index);
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, GetByteIndex(index), BYTES_PER_ELEMENT);
        return new JsBigInt(ReadAsBigInteger(span));
    }

    internal override JsValue GetValueForIndex(int index)
    {
        if (_buffer.IsDetached || IsDetachedOrOutOfBounds())
        {
            return JsValue.Undefined;
        }

        var currentLength = Length;
        if (index < 0 || index >= currentLength)
        {
            return JsValue.Undefined;
        }

        return new JsValue(GetBigIntElement(index));
    }

    public override TypedArrayBase Subarray(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newByteOffset = _byteOffset + start * BYTES_PER_ELEMENT;
        return CreateFromBuffer(_buffer, newByteOffset, newLength);
    }

    public override TypedArrayBase CreateSubarrayView(JsArrayBuffer buffer, int byteOffset, int length)
    {
        return CreateFromBuffer(buffer, byteOffset, length);
    }
}
