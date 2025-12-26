#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ArrayBufferHelper;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("SharedArrayBuffer", ToStringTag = "SharedArrayBuffer")]
public sealed partial class SharedArrayBufferPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostGetter("byteLength")]
    public JsValue ByteLength(JsValue thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        return (double)buffer.ByteLength;
    }

    /* FLAKY */
    [JsHostGetter("maxByteLength")]
    public JsValue MaxByteLength(JsValue thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        return (double)buffer.MaxByteLength;
    }

    /* FLAKY */
    [JsHostGetter("resizable")]
    public JsValue Resizable(JsValue thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        return buffer.Resizable;
    }

    /* FLAKY */
    [JsHostGetter("growable")]
    public JsValue Growable(JsValue thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        return buffer.Resizable;
    }

    /* FLAKY */
    [JsHostMethod("grow", Length = 1d)]
    public JsValue Grow(JsValue thisValue, IReadOnlyList<JsValue> args)
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
            return JsValue.Undefined;
        }

        buffer.Resize(newLength);
        return JsValue.Undefined;
    }

    /* FLAKY */
    [JsHostMethod("slice", Length = 2d)]
    public JsValue Slice(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        EnsureShared(buffer);
        var length = (long)buffer.ByteLength;

        var startIndex = args.Count > 0 && !args[0].IsUndefined
            ? ToIntegerOrInfinity(args[0], Realm.CreateContext())
            : 0d;
        var endIndex = args.Count > 1 && !args[1].IsUndefined
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
            var created = new JsArrayBuffer(newLen, null, Realm, true);
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

            targetBuffer = RequireArrayBuffer(JsValue.FromObjectUnsafe(newBuffer), Realm);
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

        if (ReferenceEquals(newBuffer, thisValue.ObjectValue))
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

        return JsValue.FromObjectUnsafe(newBuffer);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.SharedArrayBufferPrototype ??= Prototype as JsObject;
        // Getters are registered via code generation from [JsHostGetter] attributes
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
}
