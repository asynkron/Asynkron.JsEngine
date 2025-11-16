using System.Buffers.Binary;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Represents a JavaScript DataView - a low-level interface for reading and writing
/// multiple number types in a binary ArrayBuffer with control over endianness.
/// </summary>
public sealed class JsDataView : IJsPropertyAccessor
{
    private readonly JsArrayBuffer _buffer;
    private readonly int _byteOffset;
    private readonly int _byteLength;
    private readonly JsObject _properties = new();

    private readonly HostFunction _getInt8;
    private readonly HostFunction _setInt8;
    private readonly HostFunction _getUint8;
    private readonly HostFunction _setUint8;
    private readonly HostFunction _getInt16;
    private readonly HostFunction _setInt16;
    private readonly HostFunction _getUint16;
    private readonly HostFunction _setUint16;
    private readonly HostFunction _getInt32;
    private readonly HostFunction _setInt32;
    private readonly HostFunction _getUint32;
    private readonly HostFunction _setUint32;
    private readonly HostFunction _getFloat32;
    private readonly HostFunction _setFloat32;
    private readonly HostFunction _getFloat64;
    private readonly HostFunction _setFloat64;

    /// <summary>
    /// Creates a new DataView.
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

        _buffer = buffer;
        _byteOffset = byteOffset;
        _byteLength = length;

        // Lazily created host functions that delegate to whichever DataView
        // instance is used as the `this` value when called from JavaScript.
        _getInt8 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
            return (double)target.GetInt8(offset);
        });

        _setInt8 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var value = args.Count > 1 && args[1] is double d2 ? (sbyte)(int)d2 : (sbyte)0;
            target.SetInt8(offset, value);
            return JsSymbols.Undefined;
        });

        _getUint8 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
            return (double)target.GetUint8(offset);
        });

        _setUint8 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var value = args.Count > 1 && args[1] is double d2 ? (byte)(int)d2 : (byte)0;
            target.SetUint8(offset, value);
            return JsSymbols.Undefined;
        });

        _getInt16 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
            var littleEndian = args.Count > 1 && args[1] is bool and true;
            return (double)target.GetInt16(offset, littleEndian);
        });

        _setInt16 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var value = args.Count > 1 && args[1] is double d2 ? (short)(int)d2 : (short)0;
            var littleEndian = args.Count > 2 && args[2] is bool and true;
            target.SetInt16(offset, value, littleEndian);
            return JsSymbols.Undefined;
        });

        _getUint16 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
            var littleEndian = args.Count > 1 && args[1] is bool and true;
            return (double)target.GetUint16(offset, littleEndian);
        });

        _setUint16 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var value = args.Count > 1 && args[1] is double d2 ? (ushort)(int)d2 : (ushort)0;
            var littleEndian = args.Count > 2 && args[2] is bool and true;
            target.SetUint16(offset, value, littleEndian);
            return JsSymbols.Undefined;
        });

        _getInt32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
            var littleEndian = args.Count > 1 && args[1] is bool and true;
            return (double)target.GetInt32(offset, littleEndian);
        });

        _setInt32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var value = args.Count > 1 && args[1] is double d2 ? (int)d2 : 0;
            var littleEndian = args.Count > 2 && args[2] is bool and true;
            target.SetInt32(offset, value, littleEndian);
            return JsSymbols.Undefined;
        });

        _getUint32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
            var littleEndian = args.Count > 1 && args[1] is bool and true;
            return (double)target.GetUint32(offset, littleEndian);
        });

        _setUint32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var value = args.Count > 1 && args[1] is double d2 ? (uint)d2 : 0u;
            var littleEndian = args.Count > 2 && args[2] is bool and true;
            target.SetUint32(offset, value, littleEndian);
            return JsSymbols.Undefined;
        });

        _getFloat32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
            var littleEndian = args.Count > 1 && args[1] is bool and true;
            return (double)target.GetFloat32(offset, littleEndian);
        });

        _setFloat32 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var value = args.Count > 1 && args[1] is double d2 ? (float)d2 : 0f;
            var littleEndian = args.Count > 2 && args[2] is bool and true;
            target.SetFloat32(offset, value, littleEndian);
            return JsSymbols.Undefined;
        });

        _getFloat64 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
            var littleEndian = args.Count > 1 && args[1] is bool and true;
            return target.GetFloat64(offset, littleEndian);
        });

        _setFloat64 = CreateMethod((target, args) =>
        {
            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var value = args.Count > 1 && args[1] is double d2 ? d2 : 0.0;
            var littleEndian = args.Count > 2 && args[2] is bool and true;
            target.SetFloat64(offset, value, littleEndian);
            return JsSymbols.Undefined;
        });
    }

    public JsArrayBuffer Buffer => _buffer;
    public int ByteOffset => _byteOffset;
    public int ByteLength => _byteLength;

    /// <summary>
    /// Allows attaching a prototype chain to mirror JavaScript semantics.
    /// </summary>
    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    private void CheckBounds(int offset, int size)
    {
        if (offset < 0 || offset + size > _byteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is outside the bounds of the DataView");
        }
    }

    // Int8
    public sbyte GetInt8(int byteOffset)
    {
        CheckBounds(byteOffset, 1);
        return (sbyte)_buffer.Buffer[_byteOffset + byteOffset];
    }

    public void SetInt8(int byteOffset, sbyte value)
    {
        CheckBounds(byteOffset, 1);
        _buffer.Buffer[_byteOffset + byteOffset] = (byte)value;
    }

    // Uint8
    public byte GetUint8(int byteOffset)
    {
        CheckBounds(byteOffset, 1);
        return _buffer.Buffer[_byteOffset + byteOffset];
    }

    public void SetUint8(int byteOffset, byte value)
    {
        CheckBounds(byteOffset, 1);
        _buffer.Buffer[_byteOffset + byteOffset] = value;
    }

    // Int16
    public short GetInt16(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, _byteOffset + byteOffset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadInt16LittleEndian(span)
            : BinaryPrimitives.ReadInt16BigEndian(span);
    }

    public void SetInt16(int byteOffset, short value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = new Span<byte>(_buffer.Buffer, _byteOffset + byteOffset, 2);
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
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, _byteOffset + byteOffset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(span)
            : BinaryPrimitives.ReadUInt16BigEndian(span);
    }

    public void SetUint16(int byteOffset, ushort value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = new Span<byte>(_buffer.Buffer, _byteOffset + byteOffset, 2);
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
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, _byteOffset + byteOffset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(span)
            : BinaryPrimitives.ReadInt32BigEndian(span);
    }

    public void SetInt32(int byteOffset, int value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = new Span<byte>(_buffer.Buffer, _byteOffset + byteOffset, 4);
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
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, _byteOffset + byteOffset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(span)
            : BinaryPrimitives.ReadUInt32BigEndian(span);
    }

    public void SetUint32(int byteOffset, uint value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = new Span<byte>(_buffer.Buffer, _byteOffset + byteOffset, 4);
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
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, _byteOffset + byteOffset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadSingleLittleEndian(span)
            : BinaryPrimitives.ReadSingleBigEndian(span);
    }

    public void SetFloat32(int byteOffset, float value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = new Span<byte>(_buffer.Buffer, _byteOffset + byteOffset, 4);
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
        var span = new ReadOnlySpan<byte>(_buffer.Buffer, _byteOffset + byteOffset, 8);
        return littleEndian
            ? BinaryPrimitives.ReadDoubleLittleEndian(span)
            : BinaryPrimitives.ReadDoubleBigEndian(span);
    }

    public void SetFloat64(int byteOffset, double value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = new Span<byte>(_buffer.Buffer, _byteOffset + byteOffset, 8);
        if (littleEndian)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteDoubleBigEndian(span, value);
        }
    }

    public bool TryGetProperty(string name, out object? value)
    {
        if (_properties.TryGetProperty(name, out value))
        {
            return true;
        }

        switch (name)
        {
            case "buffer":
                value = Buffer;
                return true;
            case "byteLength":
                value = (double)ByteLength;
                return true;
            case "byteOffset":
                value = (double)ByteOffset;
                return true;
            case "getInt8":
                value = _getInt8;
                return true;
            case "setInt8":
                value = _setInt8;
                return true;
            case "getUint8":
                value = _getUint8;
                return true;
            case "setUint8":
                value = _setUint8;
                return true;
            case "getInt16":
                value = _getInt16;
                return true;
            case "setInt16":
                value = _setInt16;
                return true;
            case "getUint16":
                value = _getUint16;
                return true;
            case "setUint16":
                value = _setUint16;
                return true;
            case "getInt32":
                value = _getInt32;
                return true;
            case "setInt32":
                value = _setInt32;
                return true;
            case "getUint32":
                value = _getUint32;
                return true;
            case "setUint32":
                value = _setUint32;
                return true;
            case "getFloat32":
                value = _getFloat32;
                return true;
            case "setFloat32":
                value = _setFloat32;
                return true;
            case "getFloat64":
                value = _getFloat64;
                return true;
            case "setFloat64":
                value = _setFloat64;
                return true;
        }

        value = null;
        return false;
    }

    public void SetProperty(string name, object? value)
    {
        switch (name)
        {
            case "buffer":
            case "byteLength":
            case "byteOffset":
                throw new InvalidOperationException($"Cannot assign to read-only property '{name}' on DataView.");
        }

        _properties.SetProperty(name, value);
    }

    private HostFunction CreateMethod(Func<JsDataView, IReadOnlyList<object?>, object?> implementation)
    {
        return new HostFunction((thisValue, args) =>
        {
            var target = ResolveThis(thisValue, this);
            return implementation(target, args);
        });
    }

    private static JsDataView ResolveThis(object? thisValue, JsDataView fallback)
    {
        return thisValue as JsDataView ?? fallback;
    }
}
