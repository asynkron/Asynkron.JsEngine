namespace Asynkron.JsEngine;

/// <summary>
/// Abstract base class for all JavaScript typed arrays.
/// </summary>
internal abstract class TypedArrayBase
{
    protected readonly JsArrayBuffer _buffer;
    protected readonly int _byteOffset;
    protected readonly int _length;
    protected readonly int _bytesPerElement;

    protected TypedArrayBase(JsArrayBuffer buffer, int byteOffset, int length, int bytesPerElement)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

        if (byteOffset < 0 || byteOffset > buffer.ByteLength) throw new ArgumentOutOfRangeException(nameof(byteOffset));

        if (byteOffset % bytesPerElement != 0)
            throw new ArgumentException("Byte offset must be aligned to element size", nameof(byteOffset));

        if (length < 0 || byteOffset + length * bytesPerElement > buffer.ByteLength)
            throw new ArgumentOutOfRangeException(nameof(length));

        _byteOffset = byteOffset;
        _length = length;
        _bytesPerElement = bytesPerElement;
    }

    /// <summary>
    /// Gets the ArrayBuffer referenced by this typed array.
    /// </summary>
    public JsArrayBuffer Buffer => _buffer;

    /// <summary>
    /// Gets the offset in bytes from the start of the ArrayBuffer.
    /// </summary>
    public int ByteOffset => _byteOffset;

    /// <summary>
    /// Gets the length in bytes of the typed array.
    /// </summary>
    public int ByteLength => _length * _bytesPerElement;

    /// <summary>
    /// Gets the number of elements in the typed array.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Gets the size in bytes of each element in the array.
    /// </summary>
    public int BytesPerElement => _bytesPerElement;

    /// <summary>
    /// Checks if the given index is valid for this typed array.
    /// </summary>
    protected void CheckBounds(int index)
    {
        if (index < 0 || index >= _length)
            throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range");
    }

    /// <summary>
    /// Gets the byte offset for a given element index.
    /// </summary>
    protected int GetByteIndex(int index)
    {
        return _byteOffset + index * _bytesPerElement;
    }

    /// <summary>
    /// Creates a new typed array that is a view on the same buffer, from begin (inclusive) to end (exclusive).
    /// </summary>
    public abstract TypedArrayBase Subarray(int begin, int end);

    /// <summary>
    /// Gets the element at the specified index as a double (for JavaScript compatibility).
    /// </summary>
    public abstract double GetElement(int index);

    /// <summary>
    /// Sets the element at the specified index from a double (for JavaScript compatibility).
    /// </summary>
    public abstract void SetElement(int index, double value);

    /// <summary>
    /// Copies elements from source array into this array.
    /// </summary>
    public void Set(TypedArrayBase source, int offset = 0)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        if (offset < 0 || offset + source.Length > _length) throw new ArgumentOutOfRangeException(nameof(offset));

        for (var i = 0; i < source.Length; i++) SetElement(offset + i, source.GetElement(i));
    }

    /// <summary>
    /// Copies elements from a regular array into this typed array.
    /// </summary>
    public void Set(JsArray source, int offset = 0)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        if (offset < 0 || offset + source.Items.Count > _length) throw new ArgumentOutOfRangeException(nameof(offset));

        for (var i = 0; i < source.Items.Count; i++)
        {
            var value = source.Items[i];
            var numValue = value switch
            {
                double d => d,
                int iv => (double)iv,
                _ => 0.0
            };
            SetElement(offset + i, numValue);
        }
    }

    /// <summary>
    /// Helper method to normalize slice indices.
    /// </summary>
    protected (int start, int end) NormalizeSliceIndices(int begin, int end)
    {
        var len = _length;
        var relativeStart = begin < 0 ? Math.Max(len + begin, 0) : Math.Min(begin, len);
        var relativeEnd = end < 0 ? Math.Max(len + end, 0) : Math.Min(end, len);
        return (relativeStart, relativeEnd);
    }
}