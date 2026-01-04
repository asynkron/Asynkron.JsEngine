#region

using System.Buffers.Binary;
using System.Numerics;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.JsTypes;

public sealed class JsBigUint64Array(JsArrayBuffer buffer, int byteOffset, int length, bool isLengthTracking = false)
    : BigIntTypedArrayBase<JsBigUint64Array>(buffer, byteOffset, length, isLengthTracking)
{
    public static JsBigUint64Array FromLength(int length, RealmState? realmState = null)
    {
        return CreateFromLength(length, realmState, (buf, off, len) => new JsBigUint64Array(buf, off, len));
    }

    public static JsBigUint64Array FromArray(JsArray array, RealmState? realmState = null)
    {
        return CreateFromArray(array, realmState, FromLength);
    }

    protected override double ReadAsDouble(ReadOnlySpan<byte> span)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(span);
    }

    protected override BigInteger ReadAsBigInteger(ReadOnlySpan<byte> span)
    {
        return new BigInteger(BinaryPrimitives.ReadUInt64LittleEndian(span));
    }

    protected override void WriteCoercedBigInt(Span<byte> span, BigInteger value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(span, StandardLibrary.ToBigUint64(value));
    }

    protected override JsBigUint64Array CreateFromBuffer(JsArrayBuffer buffer, int byteOffset, int length)
    {
        return new JsBigUint64Array(buffer, byteOffset, length);
    }

    protected override TypedArrayBase CreateNewSameType(int length)
    {
        return FromLength(length);
    }
}
