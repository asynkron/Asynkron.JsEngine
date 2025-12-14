using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static void StoreInternalArrayBuffer(JsObject obj, JsArrayBuffer buffer)
    {
        obj.SetProperty("_internalArrayBuffer", buffer);
    }

    internal static JsArrayBuffer RequireArrayBuffer(object? thisVal, RealmState realm)
    {
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
        }

        if (thisVal is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("_internalArrayBuffer", out var internalVal) &&
            internalVal is JsArrayBuffer bufferFromAccessor)
        {
            return bufferFromAccessor;
        }

        throw ThrowTypeError("ArrayBuffer method called on incompatible receiver", realm: realm);
    }

    internal static IJsCallable ArrayBufferSpeciesCreate(object? thisVal, RealmState realm, HostFunction defaultConstructor)
    {
        if (thisVal is not IJsPropertyAccessor accessor ||
            !accessor.TryGetProperty("constructor", out var ctorVal))
        {
            return defaultConstructor;
        }

        if (ReferenceEquals(ctorVal, Symbol.Undefined))
        {
            return defaultConstructor;
        }

        if (ctorVal is null || ctorVal is not IJsPropertyAccessor ctorAccessor)
        {
            throw ThrowTypeError("Constructor is not an object", realm: realm);
        }

        var speciesKey = SymbolKeys.Species;
        var speciesVal = ctorAccessor.TryGetProperty(speciesKey, out var candidate)
            ? candidate
            : ctorVal;

        if (speciesVal is null || ReferenceEquals(speciesVal, Symbol.Undefined))
        {
            return defaultConstructor;
        }

        if (speciesVal is not IJsCallable callable || !JsOps.IsConstructor(speciesVal))
        {
            throw ThrowTypeError("ArrayBuffer species constructor is not a constructor", realm: realm);
        }

        return callable;
    }

    internal static JsValue ArrayBufferIsView(object? _, IReadOnlyList<JsValue> args, RealmState? __)
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
            dv is JsDataView)
        {
            return JsValue.True;
        }

        return JsValue.False;
    }
}
