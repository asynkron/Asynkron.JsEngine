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
}
