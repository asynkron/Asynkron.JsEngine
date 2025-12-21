using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

public static class ArrayBufferHelper
{
    internal static void StoreInternalArrayBuffer(JsObject obj, JsArrayBuffer buffer)
    {
        obj.SetProperty("_internalArrayBuffer", JsValue.FromObjectUnsafe(buffer));
    }

    internal static JsArrayBuffer RequireArrayBuffer(object? thisVal, RealmState realm)
    {
        // Handle boxed JsValue struct first - unwrap to get the underlying object
        if (thisVal is JsValue jsVal)
        {
            thisVal = jsVal.ToObject();
        }

        if (thisVal is JsArrayBuffer directBuffer)
        {
            return directBuffer;
        }

        if (thisVal is JsObject obj)
        {
            var descriptor = obj.GetOwnPropertyDescriptor("_internalArrayBuffer");
            if (descriptor?.Value is JsArrayBuffer internalBuffer)
            {
                return internalBuffer;
            }
            // Handle case where value is boxed JsValue struct
            if (descriptor?.Value is JsValue bufJsVal && bufJsVal.TryGetObject<JsArrayBuffer>(out var bufferFromJsValue))
            {
                return bufferFromJsValue;
            }
        }

        if (thisVal is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("_internalArrayBuffer", out var internalVal) &&
            internalVal.TryGetObject<JsArrayBuffer>(out var bufferFromAccessor))
        {
            return bufferFromAccessor;
        }

        throw ThrowTypeError("ArrayBuffer method called on incompatible receiver", realm: realm);
    }

    internal static IJsCallable ArrayBufferSpeciesCreate(object? thisVal, RealmState realm, HostFunction defaultConstructor)
    {
        // Handle boxed JsValue struct first - unwrap to get the underlying object
        if (thisVal is JsValue jsVal)
        {
            thisVal = jsVal.ToObject();
        }

        if (thisVal is not IJsPropertyAccessor accessor ||
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

    internal static JsValue ArrayBufferIsView(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (args.Count == 0 || args[0].IsNullOrUndefined)
        {
            return JsValue.False;
        }

        var arg = args[0];
        if (arg.TryGetObject<TypedArrayBase>(out _) || arg.TryGetObject<JsDataView>(out _))
        {
            return JsValue.True;
        }

        if (arg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty("_internalDataView", out var dv) &&
            dv.TryGetObject<JsDataView>(out _))
        {
            return JsValue.True;
        }

        return JsValue.False;
    }
}
