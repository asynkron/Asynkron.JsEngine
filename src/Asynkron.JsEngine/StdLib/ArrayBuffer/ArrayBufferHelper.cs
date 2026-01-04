#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class ArrayBufferHelper
{
    internal static void StoreInternalArrayBuffer(JsObject obj, JsArrayBuffer buffer)
    {
        obj.SetProperty("_internalArrayBuffer", JsValue.FromObjectUnsafe(buffer));
    }

    internal static JsArrayBuffer RequireArrayBuffer(JsValue thisVal, RealmState realm)
    {
        if (thisVal.IsNullOrUndefined)
        {
            throw ThrowTypeError("ArrayBuffer method called on incompatible receiver", realm: realm);
        }

        // Direct JsArrayBuffer
        if (thisVal.TryGetObject<JsArrayBuffer>(out var directBuffer))
        {
            return directBuffer;
        }

        // JsObject with internal slot
        if (thisVal.TryGetObject<JsObject>(out var obj))
        {
            var descriptor = obj.GetOwnPropertyDescriptor("_internalArrayBuffer");
            if (descriptor?.JsValue.TryGetObject<JsArrayBuffer>(out var internalBuffer) == true)
            {
                return internalBuffer;
            }
        }

        // IJsPropertyAccessor with internal slot
        if (thisVal.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty("_internalArrayBuffer", out var internalVal) &&
            internalVal.TryGetObject<JsArrayBuffer>(out var bufferFromAccessor))
        {
            return bufferFromAccessor;
        }

        throw ThrowTypeError("ArrayBuffer method called on incompatible receiver", realm: realm);
    }

    internal static IJsCallable ArrayBufferSpeciesCreate(JsValue thisVal, RealmState realm,
        HostFunction defaultConstructor)
    {
        if (!thisVal.TryGetObject<IJsPropertyAccessor>(out var accessor) ||
            !accessor.TryGetProperty("constructor", out var ctorVal))
        {
            return defaultConstructor;
        }

        if (ctorVal.IsUndefined)
        {
            return defaultConstructor;
        }

        if (!ctorVal.TryGetObject<IJsPropertyAccessor>(out var ctorAccessor))
        {
            throw ThrowTypeError("Constructor is not an object", realm: realm);
        }

        var speciesKey = SymbolKeys.Species;
        var speciesVal = ctorAccessor.TryGetProperty(speciesKey, out var candidate)
            ? candidate
            : ctorVal;

        if (speciesVal.IsNullOrUndefined)
        {
            return defaultConstructor;
        }

        if (!speciesVal.TryGetObject<IJsCallable>(out var callable) || !JsOps.IsConstructor(speciesVal))
        {
            throw ThrowTypeError("ArrayBuffer species constructor is not a constructor", realm: realm);
        }

        return callable;
    }

    /// <summary>
    /// Parses the maxByteLength option from an options object.
    /// Shared by ArrayBuffer and SharedArrayBuffer constructors.
    /// </summary>
    internal static long? GetRequestedMaxByteLength(JsValue options, RealmState realm)
    {
        if (options.IsUndefined || options.IsNull)
        {
            return null;
        }

        if (!options.IsObject || options.AsObject() is not IJsPropertyAccessor accessor)
        {
            return null;
        }

        var context = realm.CreateContext();
        if (!JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(accessor), "maxByteLength", out var maxVal, context))
        {
            return null;
        }

        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (maxVal.IsUndefined)
        {
            return null;
        }

        return NumberHelper.ToIndexAsLong(maxVal, realm);
    }

    /// <summary>
    /// Validates that the length is allocatable (fits in int).
    /// Shared by ArrayBuffer and SharedArrayBuffer constructors.
    /// </summary>
    internal static int RequireAllocatableLength(long length, RealmState realm)
    {
        if (length > int.MaxValue)
        {
            throw ThrowRangeError("Invalid ArrayBuffer length", realm: realm);
        }

        return (int)length;
    }

    /// <summary>
    /// Shared buffer construction logic for ArrayBuffer and SharedArrayBuffer.
    /// </summary>
    internal static object ConstructBufferCore(
        IReadOnlyList<JsValue> args,
        IJsCallable newTarget,
        HostFunction? constructor,
        IJsObjectLike? prototype,
        RealmState realm,
        bool isShared,
        string typeName,
        Func<JsValue, bool, JsObject> prepareThisObject)
    {
        if (newTarget is null)
        {
            throw ThrowTypeError($"{typeName} constructor requires 'new'", realm: realm);
        }

        var byteLength = args.Count > 0 && !args[0].IsUndefined
            ? NumberHelper.ToIndexAsLong(args[0], realm)
            : 0L;

        var requestedMax = GetRequestedMaxByteLength(args.Count > 1 ? args[1] : JsValue.Undefined, realm);
        if (requestedMax is { } maxValue && byteLength > maxValue)
        {
            throw ThrowRangeError($"Invalid {typeName} length", realm: realm);
        }

        var allocLength = RequireAllocatableLength(byteLength, realm);
        int? allocMax = requestedMax is { } maxIndex ? RequireAllocatableLength(maxIndex, realm) : null;

        if (ReferenceEquals(newTarget, constructor ?? newTarget))
        {
            var directBuffer = new JsArrayBuffer(allocLength, allocMax, realm, isShared);
            if (isShared && prototype is not null)
            {
                directBuffer.SetPrototype(prototype);
            }
            return directBuffer;
        }

        var instance = prepareThisObject(JsValue.Undefined, false);
        var proto = ReflectHelper.ResolveConstructPrototype(newTarget, constructor ?? newTarget, realm) ?? prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }

        var buffer = new JsArrayBuffer(allocLength, allocMax, realm, isShared);
        StoreInternalArrayBuffer(instance, buffer);
        return instance;
    }
}
