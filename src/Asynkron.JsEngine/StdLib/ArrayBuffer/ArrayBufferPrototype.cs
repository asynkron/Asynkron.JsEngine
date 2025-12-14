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

        var speciesConstructor = ArrayBufferSpeciesCreate(thisValue, Realm, Realm.ArrayBufferConstructor!);

        object newBuffer;
        JsArrayBuffer targetBuffer;
        if (ReferenceEquals(speciesConstructor, Realm.ArrayBufferConstructor))
        {
            targetBuffer = new JsArrayBuffer(newLen, null, Realm);
            newBuffer = targetBuffer;
        }
        else
        {
            newBuffer = Construct(speciesConstructor, [(double)newLen], speciesConstructor, Realm)!;
            if (newBuffer is not IJsPropertyAccessor)
            {
                throw ThrowTypeError("ArrayBuffer species constructor did not return an object", realm: Realm);
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
                    throw ThrowTypeError("ArrayBuffer species constructor returned incompatible result",
                        realm: Realm);
                }
            }
        }

        if (targetBuffer.IsShared)
        {
            throw ThrowTypeError("ArrayBuffer species constructor returned a SharedArrayBuffer", realm: Realm);
        }

        if (ReferenceEquals(newBuffer, thisValue))
        {
            throw ThrowTypeError("ArrayBuffer species constructor returned this value", realm: Realm);
        }

        if (targetBuffer.ByteLength < newLen)
        {
            throw ThrowTypeError("ArrayBuffer species constructor returned too small buffer", realm: Realm);
        }

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

        var newLength = ToIndex(args.GetArgument(0), Realm);
        EnsureNotDetached(buffer, "ArrayBuffer.prototype.resize");

        if (!buffer.Resizable)
        {
            throw ThrowTypeError("ArrayBuffer is not resizable", realm: Realm);
        }

        buffer.Resize(newLength);
        return Symbol.Undefined;
    }

    [JsHostGetter("byteLength")]
    public object ByteLength(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureNotShared(buffer);
        return buffer.IsDetached ? 0d : buffer.ByteLength;
    }

    [JsHostGetter("maxByteLength")]
    public object MaxByteLength(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureNotShared(buffer);
        return buffer.IsDetached ? 0d : buffer.MaxByteLength;
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
