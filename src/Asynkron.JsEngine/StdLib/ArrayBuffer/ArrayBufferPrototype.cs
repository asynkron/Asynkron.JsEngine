using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("ArrayBuffer", ToStringTag = "ArrayBuffer")]
public sealed partial class ArrayBufferPrototype : JsPrototype
{
    [JsHostMethod("slice", Length = 2d)]
    public object? Slice(object? thisValue, IReadOnlyList<object?> args)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureNotShared(buffer);
        EnsureNotDetached(buffer, "ArrayBuffer.prototype.slice");

        var len = buffer.ByteLength;

        var begin = args.Count > 0 ? JsOps.ToNumber(args[0]) : 0d;
        var end = args.Count > 1 ? JsOps.ToNumber(args[1]) : len;

        var first = begin < 0 ? Math.Max(len + (int)begin, 0) : Math.Min((int)begin, len);
        var final = end < 0 ? Math.Max(len + (int)end, 0) : Math.Min((int)end, len);
        var newLen = Math.Max(final - first, 0);

        var speciesConstructor = ArrayBufferSpeciesCreate(thisValue, Realm, Realm.ArrayBufferConstructor!);

        object? newBuffer;
        if (JsOps.IsConstructor(speciesConstructor) && speciesConstructor is IJsCallable speciesCtor)
        {
            newBuffer = Construct(speciesCtor, [(double)newLen], speciesCtor, Realm);
        }
        else
        {
            newBuffer = new JsArrayBuffer(newLen, null, Realm);
        }

        var targetBuffer = RequireArrayBuffer(newBuffer, Realm);
        if (newLen > 0)
        {
            Array.Copy(buffer.Buffer, first, targetBuffer.Buffer, 0, newLen);
        }

        return newBuffer;
    }

    [JsHostMethod("resize", Length = 1d)]
    public object? Resize(object? thisValue, IReadOnlyList<object?> args)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureNotShared(buffer);
        EnsureNotDetached(buffer, "ArrayBuffer.prototype.resize");

        if (!buffer.Resizable)
        {
            throw ThrowTypeError("ArrayBuffer is not resizable", realm: Realm);
        }

        var newLength = ToIndex(args.GetArgument(0), Realm);
        buffer.Resize(newLength);
        return Symbol.Undefined;
    }

    [JsHostGetter("byteLength")]
    public object ByteLength(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureNotShared(buffer);
        return buffer.IsDetached ? 0d : (double)buffer.ByteLength;
    }

    [JsHostGetter("maxByteLength")]
    public object MaxByteLength(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureNotShared(buffer);
        return buffer.IsDetached ? 0d : (double)buffer.MaxByteLength;
    }

    [JsHostGetter("resizable")]
    public object Resizable(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureNotShared(buffer);
        return buffer.Resizable;
    }

    [JsHostGetter("detached")]
    public object Detached(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureNotShared(buffer);
        return buffer.IsDetached;
    }

    [JsHostMethod("transfer", Length = 0d)]
    public object? Transfer(object? thisValue, IReadOnlyList<object?> args)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureNotShared(buffer);
        EnsureNotDetached(buffer, "ArrayBuffer.prototype.transfer");

        var newByteLength = args.Count > 0 && !ReferenceEquals(args[0], Symbol.Undefined)
            ? ToIndex(args[0], Realm)
            : buffer.ByteLength;

        if (buffer.Resizable && newByteLength > buffer.MaxByteLength)
        {
            throw ThrowRangeError("Invalid ArrayBuffer length", realm: Realm);
        }

        var target = new JsArrayBuffer(newByteLength, buffer.Resizable ? buffer.MaxByteLength : null, Realm);
        var copyLength = Math.Min(buffer.ByteLength, newByteLength);
        if (copyLength > 0)
        {
            Array.Copy(buffer.Buffer, 0, target.Buffer, 0, copyLength);
        }

        buffer.Detach();
        return target;
    }

    [JsHostMethod("transferToFixedLength", Length = 0d)]
    public object? TransferToFixedLength(object? thisValue, IReadOnlyList<object?> args)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureNotShared(buffer);
        EnsureNotDetached(buffer, "ArrayBuffer.prototype.transferToFixedLength");

        var newByteLength = args.Count > 0 && !ReferenceEquals(args[0], Symbol.Undefined)
            ? ToIndex(args[0], Realm)
            : buffer.ByteLength;

        if (buffer.Resizable && newByteLength > buffer.MaxByteLength)
        {
            throw ThrowRangeError("Invalid ArrayBuffer length", realm: Realm);
        }

        var target = new JsArrayBuffer(newByteLength, null, Realm);
        var copyLength = Math.Min(buffer.ByteLength, newByteLength);
        if (copyLength > 0)
        {
            Array.Copy(buffer.Buffer, 0, target.Buffer, 0, copyLength);
        }

        buffer.Detach();
        return target;
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.ArrayBufferPrototype ??= Prototype as JsObject;

        if (Prototype is JsObject proto)
        {
            DefineAccessor(proto, "byteLength", ByteLength, enumerable: false);
            DefineAccessor(proto, "maxByteLength", MaxByteLength, enumerable: false);
            DefineAccessor(proto, "resizable", Resizable, enumerable: false);
            DefineAccessor(proto, "detached", Detached, enumerable: false);
        }
    }

    private void EnsureNotShared(JsArrayBuffer buffer)
    {
        if (buffer.IsShared)
        {
            throw ThrowTypeError("ArrayBuffer method called on SharedArrayBuffer", realm: Realm);
        }
    }

    private void EnsureNotDetached(JsArrayBuffer buffer, string methodName)
    {
        if (buffer.IsDetached)
        {
            throw ThrowTypeError($"{methodName} called on a detached ArrayBuffer", realm: Realm);
        }
    }

    private void DefineAccessor(JsObject target, string name, Func<object?, object> getter, bool enumerable)
    {
        var getterFn = new HostFunction((thisVal, _) => getter(thisVal), Realm)
        {
            IsConstructor = false
        };

        getterFn.DefineProperty("name", new PropertyDescriptor
        {
            Value = $"get {name}",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        getterFn.DefineProperty("length", new PropertyDescriptor
        {
            Value = 0d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        target.DefineProperty(name, new PropertyDescriptor
        {
            Get = getterFn,
            Enumerable = enumerable,
            Configurable = true
        });
    }
}
