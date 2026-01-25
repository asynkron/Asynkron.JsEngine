#region

using System.Buffers.Binary;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript DataView - a low-level interface for reading and writing
///     multiple number types in a binary ArrayBuffer with control over endianness.
/// </summary>
public sealed class JsDataView : IJsPropertyAccessor, IAsJsValue
{
    /// <summary>
    ///     Attempts to extract a JsDataView from a JsValue, handling both direct instances
    ///     and objects with an internal _internalDataView property.
    /// </summary>
    internal static bool TryGetInternal(JsValue candidate, out JsDataView? dataView)
    {
        if (candidate is not { Kind: JsValueKind.Object, ObjectValue: { } objVal })
        {
            dataView = null;
            return false;
        }

        switch (objVal)
        {
            case JsDataView directDv:
                dataView = directDv;
                return true;
            case JsObject obj:
                {
                    var descriptor = obj.GetOwnPropertyDescriptor("_internalDataView");
                    if (descriptor is not null && descriptor.JsValue.TryGetObject<JsDataView>(out var internalDv))
                    {
                        dataView = internalDv;
                        return true;
                    }

                    break;
                }
        }

        dataView = null;
        return false;
    }

    private readonly HostFunction _getFloat32;
    private readonly HostFunction _getFloat64;
    private readonly HostFunction _getInt16;
    private readonly HostFunction _getInt32;

    private readonly HostFunction _getInt8;
    private readonly HostFunction _getUint16;
    private readonly HostFunction _getUint32;
    private readonly HostFunction _getUint8;
    private readonly JsObject _properties = new();
    private readonly HostFunction _setFloat32;
    private readonly HostFunction _setFloat64;
    private readonly HostFunction _setInt16;
    private readonly HostFunction _setInt32;
    private readonly HostFunction _setInt8;
    private readonly HostFunction _setUint16;
    private readonly HostFunction _setUint32;
    private readonly HostFunction _setUint8;
    private readonly JsValue _cachedJsValue;
    private readonly bool _isLengthTracking;
    private readonly int _initialByteLength;
    private readonly int _byteOffset;

    /// <summary>
    ///     Creates a new DataView.
    /// </summary>
    public JsDataView(JsArrayBuffer buffer, int byteOffset = 0, int? byteLength = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (byteOffset < 0 || byteOffset > buffer.ByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }

        var length = byteLength ?? buffer.ByteLength - byteOffset;

        if (length < 0 || byteOffset + length > buffer.ByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
        Buffer = buffer;
        _byteOffset = byteOffset;
        _isLengthTracking = byteLength is null;
        _initialByteLength = length;

        // Lazily created host functions that delegate to whichever DataView
        // instance is used as the `this` value when called from JavaScript.
        _getInt8 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d) ? (int)d : 0;
            return new JsValue((double)target.GetInt8(offset));
        });

        _setInt8 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d1) ? (int)d1 : 0;
            var value = args.Count > 1 && args[1].TryGetDouble(out var d2) ? (sbyte)(int)d2 : (sbyte)0;
            target.SetInt8(offset, value);
            return JsValue.Undefined;
        });

        _getUint8 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d) ? (int)d : 0;
            return new JsValue((double)target.GetUint8(offset));
        });

        _setUint8 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d1) ? (int)d1 : 0;
            var value = args.Count > 1 && args[1].TryGetDouble(out var d2) ? (byte)(int)d2 : (byte)0;
            target.SetUint8(offset, value);
            return JsValue.Undefined;
        });

        _getInt16 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d) ? (int)d : 0;
            var littleEndian = args.Count > 1 && args[1].IsTruthy;
            return new JsValue((double)target.GetInt16(offset, littleEndian));
        });

        _setInt16 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d1) ? (int)d1 : 0;
            var value = args.Count > 1 && args[1].TryGetDouble(out var d2) ? (short)(int)d2 : (short)0;
            var littleEndian = args.Count > 2 && args[2].IsTruthy;
            target.SetInt16(offset, value, littleEndian);
            return JsValue.Undefined;
        });

        _getUint16 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d) ? (int)d : 0;
            var littleEndian = args.Count > 1 && args[1].IsTruthy;
            return new JsValue((double)target.GetUint16(offset, littleEndian));
        });

        _setUint16 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d1) ? (int)d1 : 0;
            var value = args.Count > 1 && args[1].TryGetDouble(out var d2) ? (ushort)(int)d2 : (ushort)0;
            var littleEndian = args.Count > 2 && args[2].IsTruthy;
            target.SetUint16(offset, value, littleEndian);
            return JsValue.Undefined;
        });

        _getInt32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 ? ToIndex(args[0], target.Buffer.RealmState) : 0;
            var littleEndian = args.Count > 1 && args[1].IsTruthy;
            return new JsValue((double)target.GetInt32(offset, littleEndian));
        });

        _setInt32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d1) ? (int)d1 : 0;
            var value = args.Count > 1 && args[1].TryGetDouble(out var d2) ? (int)d2 : 0;
            var littleEndian = args.Count > 2 && args[2].IsTruthy;
            target.SetInt32(offset, value, littleEndian);
            return JsValue.Undefined;
        });

        _getUint32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 ? ToIndex(args[0], target.Buffer.RealmState) : 0;
            var littleEndian = args.Count > 1 && args[1].IsTruthy;
            return new JsValue((double)target.GetUint32(offset, littleEndian));
        });

        _setUint32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d1) ? (int)d1 : 0;
            var value = args.Count > 1 && args[1].TryGetDouble(out var d2) ? (uint)d2 : 0u;
            var littleEndian = args.Count > 2 && args[2].IsTruthy;
            target.SetUint32(offset, value, littleEndian);
            return JsValue.Undefined;
        });

        _getFloat32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d) ? (int)d : 0;
            var littleEndian = args.Count > 1 && args[1].IsTruthy;
            return new JsValue(target.GetFloat32(offset, littleEndian));
        });

        _setFloat32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d1) ? (int)d1 : 0;
            var value = args.Count > 1 && args[1].TryGetDouble(out var d2) ? (float)d2 : 0f;
            var littleEndian = args.Count > 2 && args[2].IsTruthy;
            target.SetFloat32(offset, value, littleEndian);
            return JsValue.Undefined;
        });

        _getFloat64 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d) ? (int)d : 0;
            var littleEndian = args.Count > 1 && args[1].IsTruthy;
            return new JsValue(target.GetFloat64(offset, littleEndian));
        });

        _setFloat64 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0].TryGetDouble(out var d1) ? (int)d1 : 0;
            var value = args.Count > 1 && args[1].TryGetDouble(out var d2) ? d2 : 0.0;
            var littleEndian = args.Count > 2 && args[2].IsTruthy;
            target.SetFloat64(offset, value, littleEndian);
            return JsValue.Undefined;
        });
    }

    public JsArrayBuffer Buffer { get; }

    public int ByteOffset
    {
        get
        {
            _ = ValidateDataViewAndGetBufferByteLength();
            return _byteOffset;
        }
    }

    public int ByteLength
    {
        get
        {
            var bufferByteLength = ValidateDataViewAndGetBufferByteLength();
            return _isLengthTracking ? bufferByteLength - _byteOffset : _initialByteLength;
        }
    }

    private int ValidateDataViewAndGetBufferByteLength()
    {
        if (Buffer.IsDetached)
        {
            throw ThrowTypeError(
                "Cannot perform DataView operation on a detached ArrayBuffer",
                realm: Buffer.RealmState);
        }

        var bufferByteLength = Buffer.ByteLength;
        if (_isLengthTracking)
        {
            if (_byteOffset > bufferByteLength)
            {
                throw ThrowTypeError("DataView is out of bounds", realm: Buffer.RealmState);
            }
        }
        else
        {
            var requiredEnd = (long)_byteOffset + _initialByteLength;
            if (requiredEnd > bufferByteLength)
            {
                throw ThrowTypeError("DataView is out of bounds", realm: Buffer.RealmState);
            }
        }

        return bufferByteLength;
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        if (_properties.TryGetProperty(name, receiver.IsUndefined ? _cachedJsValue : receiver, out value))
        {
            return true;
        }

        switch (name)
        {
            case "buffer":
                value = JsValue.FromObjectUnsafe(Buffer);
                return true;
            case "byteLength":
                value = new JsValue((double)ByteLength);
                return true;
            case "byteOffset":
                value = new JsValue((double)ByteOffset);
                return true;
            case "getInt8":
                value = (JsValue)_getInt8;
                return true;
            case "setInt8":
                value = (JsValue)_setInt8;
                return true;
            case "getUint8":
                value = (JsValue)_getUint8;
                return true;
            case "setUint8":
                value = (JsValue)_setUint8;
                return true;
            case "getInt16":
                value = (JsValue)_getInt16;
                return true;
            case "setInt16":
                value = (JsValue)_setInt16;
                return true;
            case "getUint16":
                value = (JsValue)_getUint16;
                return true;
            case "setUint16":
                value = (JsValue)_setUint16;
                return true;
            case "getInt32":
                value = (JsValue)_getInt32;
                return true;
            case "setInt32":
                value = (JsValue)_setInt32;
                return true;
            case "getUint32":
                value = (JsValue)_getUint32;
                return true;
            case "setUint32":
                value = (JsValue)_setUint32;
                return true;
            case "getFloat32":
                value = (JsValue)_getFloat32;
                return true;
            case "setFloat32":
                value = (JsValue)_setFloat32;
                return true;
            case "getFloat64":
                value = (JsValue)_getFloat64;
                return true;
            case "setFloat64":
                value = (JsValue)_setFloat64;
                return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, _cachedJsValue, out value);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, _cachedJsValue);
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        switch (name)
        {
            case "buffer":
            case "byteLength":
            case "byteOffset":
                throw new InvalidOperationException($"Cannot assign to read-only property '{name}' on DataView.");
        }

        _properties.SetProperty(name, value, receiver.IsUndefined ? _cachedJsValue : receiver);
    }

    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    /// <summary>
    ///     Allows attaching a prototype chain to mirror JavaScript semantics.
    /// </summary>
    public void SetPrototype(IJsPropertyAccessor? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    private void CheckBounds(int offset, int size)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is outside the bounds of the DataView");
        }

        var requiredEnd = (long)offset + size;
        if (requiredEnd > ByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is outside the bounds of the DataView");
        }
    }

    // Int8
    public sbyte GetInt8(int byteOffset)
    {
        CheckBounds(byteOffset, 1);
        return (sbyte)Buffer.Buffer[_byteOffset + byteOffset];
    }

    public void SetInt8(int byteOffset, sbyte value)
    {
        CheckBounds(byteOffset, 1);
        Buffer.Buffer[_byteOffset + byteOffset] = (byte)value;
    }

    // Uint8
    public byte GetUint8(int byteOffset)
    {
        CheckBounds(byteOffset, 1);
        return Buffer.Buffer[_byteOffset + byteOffset];
    }

    public void SetUint8(int byteOffset, byte value)
    {
        CheckBounds(byteOffset, 1);
        Buffer.Buffer[_byteOffset + byteOffset] = value;
    }

    // Int16
    public short GetInt16(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = new ReadOnlySpan<byte>(Buffer.Buffer, _byteOffset + byteOffset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadInt16LittleEndian(span)
            : BinaryPrimitives.ReadInt16BigEndian(span);
    }

    public void SetInt16(int byteOffset, short value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = new Span<byte>(Buffer.Buffer, _byteOffset + byteOffset, 2);
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt16LittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteInt16BigEndian(span, value);
        }
    }

    // Uint16
    public ushort GetUint16(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = new ReadOnlySpan<byte>(Buffer.Buffer, _byteOffset + byteOffset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(span)
            : BinaryPrimitives.ReadUInt16BigEndian(span);
    }

    public void SetUint16(int byteOffset, ushort value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = new Span<byte>(Buffer.Buffer, _byteOffset + byteOffset, 2);
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(span, value);
        }
    }

    // Int32
    public int GetInt32(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = new ReadOnlySpan<byte>(Buffer.Buffer, _byteOffset + byteOffset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(span)
            : BinaryPrimitives.ReadInt32BigEndian(span);
    }

    public void SetInt32(int byteOffset, int value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = new Span<byte>(Buffer.Buffer, _byteOffset + byteOffset, 4);
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(span, value);
        }
    }

    // Uint32
    public uint GetUint32(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = new ReadOnlySpan<byte>(Buffer.Buffer, _byteOffset + byteOffset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(span)
            : BinaryPrimitives.ReadUInt32BigEndian(span);
    }

    public void SetUint32(int byteOffset, uint value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = new Span<byte>(Buffer.Buffer, _byteOffset + byteOffset, 4);
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(span, value);
        }
    }

    // Float32
    public float GetFloat32(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = new ReadOnlySpan<byte>(Buffer.Buffer, _byteOffset + byteOffset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadSingleLittleEndian(span)
            : BinaryPrimitives.ReadSingleBigEndian(span);
    }

    public void SetFloat32(int byteOffset, float value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = new Span<byte>(Buffer.Buffer, _byteOffset + byteOffset, 4);
        if (littleEndian)
        {
            BinaryPrimitives.WriteSingleLittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteSingleBigEndian(span, value);
        }
    }

    // Float64
    public double GetFloat64(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = new ReadOnlySpan<byte>(Buffer.Buffer, _byteOffset + byteOffset, 8);
        return littleEndian
            ? BinaryPrimitives.ReadDoubleLittleEndian(span)
            : BinaryPrimitives.ReadDoubleBigEndian(span);
    }

    public void SetFloat64(int byteOffset, double value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = new Span<byte>(Buffer.Buffer, _byteOffset + byteOffset, 8);
        if (littleEndian)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteDoubleBigEndian(span, value);
        }
    }

    // BigInt64
    public long GetBigInt64(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = new ReadOnlySpan<byte>(Buffer.Buffer, _byteOffset + byteOffset, 8);
        return littleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(span)
            : BinaryPrimitives.ReadInt64BigEndian(span);
    }

    public void SetBigInt64(int byteOffset, long value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = new Span<byte>(Buffer.Buffer, _byteOffset + byteOffset, 8);
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt64LittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteInt64BigEndian(span, value);
        }
    }

    // BigUint64
    public ulong GetBigUint64(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = new ReadOnlySpan<byte>(Buffer.Buffer, _byteOffset + byteOffset, 8);
        return littleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(span)
            : BinaryPrimitives.ReadUInt64BigEndian(span);
    }

    public void SetBigUint64(int byteOffset, ulong value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = new Span<byte>(Buffer.Buffer, _byteOffset + byteOffset, 8);
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(span, value);
        }
    }

    // Float16 (Half)
    public Half GetFloat16(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = new ReadOnlySpan<byte>(Buffer.Buffer, _byteOffset + byteOffset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadHalfLittleEndian(span)
            : BinaryPrimitives.ReadHalfBigEndian(span);
    }

    public void SetFloat16(int byteOffset, Half value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = new Span<byte>(Buffer.Buffer, _byteOffset + byteOffset, 2);
        if (littleEndian)
        {
            BinaryPrimitives.WriteHalfLittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteHalfBigEndian(span, value);
        }
    }

    private HostFunction CreateMethod(Func<JsDataView, IReadOnlyList<JsValue>, JsValue> implementation)
    {
        return new HostFunction((thisValue, args) =>
        {
            var target = ResolveThis(thisValue, this);
            return implementation(target, args);
        });
    }

    private static JsDataView ResolveThis(JsValue thisValue, JsDataView fallback)
    {
        return thisValue.TryGetObject<JsDataView>(out var dv) ? dv : fallback;
    }
}
