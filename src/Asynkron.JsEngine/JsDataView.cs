using System.Buffers.Binary;

namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript DataView - a low-level interface for reading and writing 
/// multiple number types in a binary ArrayBuffer with control over endianness.
/// </summary>
internal sealed class JsDataView
{
    private readonly JsArrayBuffer _buffer;
    private readonly int _byteOffset;
    private readonly int _byteLength;

    /// <summary>
    /// Creates a new DataView.
    /// </summary>
    public JsDataView(JsArrayBuffer buffer, int byteOffset = 0, int? byteLength = null)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));

        if (byteOffset < 0 || byteOffset > buffer.ByteLength) throw new ArgumentOutOfRangeException(nameof(byteOffset));

        var length = byteLength ?? buffer.ByteLength - byteOffset;

        if (length < 0 || byteOffset + length > buffer.ByteLength)
            throw new ArgumentOutOfRangeException(nameof(byteLength));

        _buffer = buffer;
        _byteOffset = byteOffset;
        _byteLength = length;
    }

    public JsArrayBuffer Buffer => _buffer;
    public int ByteOffset => _byteOffset;
    public int ByteLength => _byteLength;

    private void CheckBounds(int offset, int size)
    {
        if (offset < 0 || offset + size > _byteLength)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is outside the bounds of the DataView");
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
            BinaryPrimitives.WriteInt16LittleEndian(span, value);
        else
            BinaryPrimitives.WriteInt16BigEndian(span, value);
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
            BinaryPrimitives.WriteUInt16LittleEndian(span, value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(span, value);
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
            BinaryPrimitives.WriteInt32LittleEndian(span, value);
        else
            BinaryPrimitives.WriteInt32BigEndian(span, value);
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
            BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        else
            BinaryPrimitives.WriteUInt32BigEndian(span, value);
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
            BinaryPrimitives.WriteSingleLittleEndian(span, value);
        else
            BinaryPrimitives.WriteSingleBigEndian(span, value);
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
            BinaryPrimitives.WriteDoubleLittleEndian(span, value);
        else
            BinaryPrimitives.WriteDoubleBigEndian(span, value);
    }
}