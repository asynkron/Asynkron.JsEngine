#region

using System.Buffers.Binary;
using System.Numerics;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.JsTypes;

public sealed class JsBigInt64Array(JsArrayBuffer buffer, int byteOffset, int length, bool isLengthTracking = false)
    : BigIntTypedArrayBase<JsBigInt64Array>(buffer, byteOffset, length, isLengthTracking)
{
    public static JsBigInt64Array FromLength(int length, RealmState? realmState = null)
    {
        return CreateFromLength(length, realmState, (buf, off, len) => new JsBigInt64Array(buf, off, len));
    }

    public static JsBigInt64Array FromArray(JsArray array, RealmState? realmState = null)
    {
        return CreateFromArray(array, realmState, FromLength);
    }

    protected override double ReadAsDouble(ReadOnlySpan<byte> span)
    {
        return BinaryPrimitives.ReadInt64LittleEndian(span);
    }

    protected override BigInteger ReadAsBigInteger(ReadOnlySpan<byte> span)
    {
        return new BigInteger(BinaryPrimitives.ReadInt64LittleEndian(span));
    }

    protected override void WriteCoercedBigInt(Span<byte> span, BigInteger value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(span, StandardLibrary.ToBigInt64(value));
    }

    protected override JsBigInt64Array CreateFromBuffer(JsArrayBuffer buffer, int byteOffset, int length)
    {
        return new JsBigInt64Array(buffer, byteOffset, length);
    }

    protected override TypedArrayBase CreateNewSameType(int length)
    {
        return FromLength(length);
    }
}
