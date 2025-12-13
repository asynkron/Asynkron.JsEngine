using Asynkron.JsEngine.Ast;
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

    [JsHostGetter("growable")]
    public object Growable(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        return buffer.Resizable;
    }

    [JsHostMethod("grow", Length = 1d)]
    public object? Grow(object? thisValue, IReadOnlyList<object?> args)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);

        if (!buffer.Resizable)
        {
            throw ThrowTypeError("SharedArrayBuffer is not growable", realm: Realm);
        }

        var newLength = ToIndex(args.GetArgument(0), Realm);
        if (newLength < buffer.ByteLength)
        {
            throw ThrowRangeError("Invalid SharedArrayBuffer length", realm: Realm);
        }

        if (newLength > buffer.MaxByteLength)
        {
            throw ThrowRangeError("Invalid SharedArrayBuffer length", realm: Realm);
        }

        if (newLength == buffer.ByteLength)
        {
            return Symbol.Undefined;
        }

        buffer.Resize((int)newLength);
        return Symbol.Undefined;
    }

    [JsHostMethod("slice", Length = 2d)]
    public object? Slice(object? thisValue, IReadOnlyList<object?> args)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        var length = (long)buffer.ByteLength;

        var startIndex = args.Count > 0 && !ReferenceEquals(args[0], Symbol.Undefined)
            ? ToIntegerOrInfinity(args[0], Realm.CreateContext())
            : 0d;
        var endIndex = args.Count > 1 && !ReferenceEquals(args[1], Symbol.Undefined)
            ? ToIntegerOrInfinity(args[1], Realm.CreateContext())
            : length;

        var first = ClampRelativeIndex(startIndex, length);
        var final = double.IsPositiveInfinity(endIndex) ? length : ClampRelativeIndex(endIndex, length);
        var newLen = (int)Math.Max(final - first, 0);

        var speciesConstructor = ArrayBufferSpeciesCreate(thisValue, Realm, Realm.SharedArrayBufferConstructor!);

        object newBuffer;
        JsArrayBuffer targetBuffer;
        if (ReferenceEquals(speciesConstructor, Realm.SharedArrayBufferConstructor))
        {
            var created = new JsArrayBuffer(newLen, null, Realm, isShared: true);
            created.SetPrototype(Realm.SharedArrayBufferPrototype);
            newBuffer = created;
            targetBuffer = created;
        }
        else
        {
            newBuffer = Construct(speciesConstructor, [(double)newLen], speciesConstructor, Realm)!;
            if (newBuffer is not IJsPropertyAccessor)
            {
                throw ThrowTypeError("SharedArrayBuffer species constructor did not return an object", realm: Realm);
            }

            targetBuffer = RequireArrayBuffer(newBuffer, Realm);
            if (!ReferenceEquals(targetBuffer, newBuffer))
            {
                if (newBuffer is JsObject obj)
                {
                    StoreInternalArrayBuffer(obj, targetBuffer);
                }
                else
                {
                    throw ThrowTypeError("SharedArrayBuffer species constructor returned incompatible result",
                        realm: Realm);
                }
            }
        }

        if (!targetBuffer.IsShared)
        {
            throw ThrowTypeError("SharedArrayBuffer species constructor did not return a SharedArrayBuffer",
                realm: Realm);
        }

        if (ReferenceEquals(newBuffer, thisValue))
        {
            throw ThrowTypeError("SharedArrayBuffer species constructor returned this value", realm: Realm);
        }

        if (targetBuffer.ByteLength < newLen)
        {
            throw ThrowTypeError("SharedArrayBuffer species constructor returned too small buffer", realm: Realm);
        }

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
            DefineAccessor(proto, "growable", Growable, enumerable: false);
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
