namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Represents a JavaScript ArrayBuffer - a fixed-length raw binary data buffer.
/// </summary>
public sealed class JsArrayBuffer : IJsPropertyAccessor
{
    private readonly byte[] _buffer;
    private readonly JsObject _properties = new();
    private readonly HostFunction _sliceFunction;

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

        _sliceFunction = new HostFunction((thisValue, args) =>
        {
            var target = thisValue as JsArrayBuffer ?? this;
            var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : target.ByteLength;

            return target.Slice(begin, end);
        });
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
    /// Allows external callers to attach a prototype object.
    /// </summary>
    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

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

    public bool TryGetProperty(string name, out object? value)
    {
        if (_properties.TryGetProperty(name, out value))
        {
            return true;
        }

        switch (name)
        {
            case "byteLength":
                value = (double)ByteLength;
                return true;
            case "slice":
                value = _sliceFunction;
                return true;
        }

        value = null;
        return false;
    }

    public void SetProperty(string name, object? value)
    {
        if (string.Equals(name, "byteLength", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot assign to read-only property 'byteLength' on ArrayBuffer.");
        }

        _properties.SetProperty(name, value);
    }
}
