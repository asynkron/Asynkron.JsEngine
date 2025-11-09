namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript ArrayBuffer - a fixed-length raw binary data buffer.
/// </summary>
internal sealed class JsArrayBuffer
{
    private readonly byte[] _buffer;

    /// <summary>
    /// Creates a new ArrayBuffer with the specified length in bytes.
    /// </summary>
    public JsArrayBuffer(int byteLength)
    {
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), "ArrayBuffer size cannot be negative");
        }
        
        _buffer = new byte[byteLength];
    }

    /// <summary>
    /// Gets the length of the buffer in bytes.
    /// </summary>
    public int ByteLength => _buffer.Length;

    /// <summary>
    /// Gets the underlying byte array.
    /// </summary>
    public byte[] Buffer => _buffer;

    /// <summary>
    /// Creates a copy of this ArrayBuffer containing a slice of the data.
    /// </summary>
    public JsArrayBuffer Slice(int begin, int end)
    {
        // Normalize negative indices
        var len = _buffer.Length;
        var relativeStart = begin < 0 ? Math.Max(len + begin, 0) : Math.Min(begin, len);
        var relativeEnd = end < 0 ? Math.Max(len + end, 0) : Math.Min(end, len);
        
        var newLen = Math.Max(relativeEnd - relativeStart, 0);
        var newBuffer = new JsArrayBuffer(newLen);
        
        Array.Copy(_buffer, relativeStart, newBuffer._buffer, 0, newLen);
        
        return newBuffer;
    }
}
