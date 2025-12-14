using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript ArrayBuffer - a fixed-length raw binary data buffer.
/// </summary>
public sealed class JsArrayBuffer : IJsPropertyAccessor, IPrototypeAccessorProvider, IJsObjectLike, IPropertyDefinitionHost,
    IExtensibilityControl
{
    private readonly JsObject _properties = new();

    private readonly HostFunction _resizeFunction;
    private readonly HostFunction _sliceFunction;

    /// <summary>
    ///     Creates a new ArrayBuffer with the specified length in bytes.
    /// </summary>
    public JsArrayBuffer(int byteLength, int? maxByteLength = null, RealmState? realmState = null, bool isShared = false)
    {
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), "ArrayBuffer size cannot be negative");
        }

        RealmState = realmState;
        MaxByteLength = maxByteLength.HasValue ? Math.Max(maxByteLength.Value, byteLength) : byteLength;
        Resizable = maxByteLength.HasValue;
        IsShared = isShared;

        Buffer = new byte[byteLength];

        _sliceFunction = new HostFunction((thisValue, args) =>
        {
            var target = thisValue.TryGetObject<JsArrayBuffer>(out var buf) ? buf : this;
            var begin = args.Count > 0 && args[0].TryGetDouble(out var d1) ? (int)d1 : 0;
            var end = args.Count > 1 && args[1].TryGetDouble(out var d2) ? (int)d2 : target.ByteLength;

            return new JsValue(target.Slice(begin, end));
        });

        _resizeFunction = new HostFunction((thisValue, args) =>
        {
            var target = thisValue.TryGetObject<JsArrayBuffer>(out var buf) ? buf : this;
            if (!Resizable)
            {
                throw new ThrowSignal(CreateTypeError("ArrayBuffer is not resizable"));
            }

            if (args.Count == 0 || !args[0].TryGetDouble(out var d))
            {
                throw new ThrowSignal(CreateTypeError("resize requires a new length"));
            }

            var newLength = (int)d;
            target.Resize(newLength);
            return JsValue.Undefined;
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

    public bool IsShared { get; }

    public bool Resizable { get; }

    public int MaxByteLength { get; }

    public IJsPropertyAccessor? PrototypeAccessor => _properties.PrototypeAccessor;

    public JsObject? Prototype => _properties.Prototype;
    public bool IsSealed => _properties.IsSealed;
    public bool IsFrozen => _properties.IsFrozen;
    public bool IsExtensible => _properties.IsExtensible;
    IEnumerable<string> IJsObjectLike.Keys => _properties.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _properties.DefineProperty(name, descriptor);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return _properties.TryDefineProperty(name, descriptor);
    }

    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public void Seal()
    {
        _properties.Seal();
    }

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
    }

    public bool Delete(string name)
    {
        return _properties.Delete(name);
    }

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        if (_properties.TryGetProperty(name, receiver ?? this, out value))
        {
            return true;
        }

        value = JsValue.Undefined.ToObject();
        return false;
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return TryGetProperty(name, this, out value);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        if (string.Equals(name, "byteLength", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot assign to read-only property 'byteLength' on ArrayBuffer.");
        }

        _properties.SetProperty(name, value, receiver ?? this);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
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
        Buffer = [];
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
