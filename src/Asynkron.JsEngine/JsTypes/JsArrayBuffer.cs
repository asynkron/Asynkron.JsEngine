using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript ArrayBuffer - a fixed-length raw binary data buffer.
/// </summary>
public sealed class JsArrayBuffer : IJsPropertyAccessor
{
    private readonly JsObject _properties = new();

    private readonly HostFunction _resizeFunction;
    private readonly HostFunction _sliceFunction;

    /// <summary>
    ///     Creates a new ArrayBuffer with the specified length in bytes.
    /// </summary>
    public JsArrayBuffer(int byteLength, int? maxByteLength = null, RealmState? realmState = null)
    {
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), "ArrayBuffer size cannot be negative");
        }

        RealmState = realmState;
        MaxByteLength = maxByteLength.HasValue ? Math.Max(maxByteLength.Value, byteLength) : byteLength;
        Resizable = maxByteLength.HasValue;

        Buffer = new byte[byteLength];

        _sliceFunction = new HostFunction((thisValue, args) =>
        {
            var target = thisValue as JsArrayBuffer ?? this;
            var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : target.ByteLength;

            return target.Slice(begin, end);
        });

        _resizeFunction = new HostFunction((thisValue, args) =>
        {
            var target = thisValue as JsArrayBuffer ?? this;
            if (!Resizable)
            {
                throw new ThrowSignal(CreateTypeError("ArrayBuffer is not resizable"));
            }

            if (args.Count == 0 || args[0] is not double d)
            {
                throw new ThrowSignal(CreateTypeError("resize requires a new length"));
            }

            var newLength = (int)d;
            target.Resize(newLength);
            return Symbols.Undefined;
        });

        if (realmState?.ArrayBufferPrototype is not null)
        {
            _properties.SetPrototype(realmState.ArrayBufferPrototype);
        }
    }

    /// <summary>
    ///     Gets the length of the buffer in bytes.
    /// </summary>
    public int ByteLength => Buffer.Length;

    public bool IsDetached { get; private set; }

    internal RealmState? RealmState { get; }

    /// <summary>
    ///     Gets the underlying byte array.
    /// </summary>
    public byte[] Buffer { get; private set; }

    public bool Resizable { get; }

    public int MaxByteLength { get; }

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
            case "resize":
                if (Resizable)
                {
                    value = _resizeFunction;
                    return true;
                }

                break;
            case "maxByteLength":
                value = (double)MaxByteLength;
                return true;
            case "resizable":
                value = Resizable;
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

    /// <summary>
    ///     Allows external callers to attach a prototype object.
    /// </summary>
    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    /// <summary>
    ///     Creates a copy of this ArrayBuffer containing a slice of the data.
    /// </summary>
    public JsArrayBuffer Slice(int begin, int end)
    {
        // Normalize negative indices
        var len = Buffer.Length;
        var relativeStart = begin < 0 ? Math.Max(len + begin, 0) : Math.Min(begin, len);
        var relativeEnd = end < 0 ? Math.Max(len + end, 0) : Math.Min(end, len);

        var newLen = Math.Max(relativeEnd - relativeStart, 0);
        var newBuffer = new JsArrayBuffer(newLen, null, RealmState);

        Array.Copy(Buffer, relativeStart, newBuffer.Buffer, 0, newLen);

        return newBuffer;
    }

    public void Resize(int newByteLength)
    {
        if (!Resizable)
        {
            throw new ThrowSignal(CreateTypeError("ArrayBuffer is not resizable"));
        }

        if (newByteLength < 0 || newByteLength > MaxByteLength)
        {
            throw new ThrowSignal(CreateRangeError("Invalid ArrayBuffer length"));
        }

        if (newByteLength == Buffer.Length)
        {
            return;
        }

        var newBuffer = new byte[newByteLength];
        var bytesToCopy = Math.Min(Buffer.Length, newByteLength);
        Array.Copy(Buffer, newBuffer, bytesToCopy);
        Buffer = newBuffer;
    }

    public void Detach()
    {
        Buffer = Array.Empty<byte>();
        IsDetached = true;
    }

    internal JsObject CreateTypeError(string message)
    {
        if (RealmState?.TypeErrorConstructor is IJsCallable ctor)
        {
            var created = ctor.Invoke([message], null);
            if (created is JsObject jsObj)
            {
                return jsObj;
            }
        }

        return new JsObject { ["name"] = "TypeError", ["message"] = message };
    }

    internal JsObject CreateRangeError(string message)
    {
        if (RealmState?.RangeErrorConstructor is IJsCallable ctor)
        {
            var created = ctor.Invoke([message], null);
            if (created is JsObject jsObj)
            {
                return jsObj;
            }
        }

        return new JsObject { ["name"] = "RangeError", ["message"] = message };
    }
}
