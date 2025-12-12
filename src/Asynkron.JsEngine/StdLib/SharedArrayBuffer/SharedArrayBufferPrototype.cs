using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("SharedArrayBuffer", ToStringTag = "SharedArrayBuffer")]
public sealed partial class SharedArrayBufferPrototype : JsPrototype
{
    [JsHostGetter("byteLength")]
    public object ByteLength(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        return (double)buffer.ByteLength;
    }

    [JsHostGetter("maxByteLength")]
    public object MaxByteLength(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        return (double)buffer.MaxByteLength;
    }

    [JsHostGetter("resizable")]
    public object Resizable(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        return buffer.Resizable;
    }

    [JsHostMethod("slice", Length = 2d)]
    public object? Slice(object? thisValue, IReadOnlyList<object?> args)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        var len = buffer.ByteLength;

        var begin = args.Count > 0 ? JsOps.ToNumber(args[0]) : 0d;
        var end = args.Count > 1 ? JsOps.ToNumber(args[1]) : len;

        var first = begin < 0 ? Math.Max(len + (int)begin, 0) : Math.Min((int)begin, len);
        var final = end < 0 ? Math.Max(len + (int)end, 0) : Math.Min((int)end, len);
        var newLen = Math.Max(final - first, 0);

        var speciesConstructor = ArrayBufferSpeciesCreate(thisValue, Realm, Realm.SharedArrayBufferConstructor!);

        object? newBuffer;
        if (JsOps.IsConstructor(speciesConstructor) && speciesConstructor is IJsCallable speciesCtor)
        {
            newBuffer = Construct(speciesCtor, [(double)newLen], speciesCtor, Realm);
        }
        else
        {
            var created = new JsArrayBuffer(newLen, null, Realm, isShared: true);
            created.SetPrototype(Realm.SharedArrayBufferPrototype);
            newBuffer = created;
        }

        var targetBuffer = RequireArrayBuffer(newBuffer, Realm);
        if (newLen > 0)
        {
            Array.Copy(buffer.Buffer, first, targetBuffer.Buffer, 0, newLen);
        }

        return newBuffer;
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.SharedArrayBufferPrototype ??= Prototype as JsObject;

        if (Prototype is JsObject proto)
        {
            DefineAccessor(proto, "byteLength", ByteLength, enumerable: false);
            DefineAccessor(proto, "maxByteLength", MaxByteLength, enumerable: false);
            DefineAccessor(proto, "resizable", Resizable, enumerable: false);
        }
    }

    private void EnsureShared(JsArrayBuffer buffer)
    {
        if (!buffer.IsShared)
        {
            throw ThrowTypeError("SharedArrayBuffer method called on non-shared buffer", realm: Realm);
        }

        if (buffer.IsDetached)
        {
            throw ThrowTypeError("SharedArrayBuffer is detached", realm: Realm);
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
