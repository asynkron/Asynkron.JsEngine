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

    internal static object? ArrayBufferSpeciesCreate(object? thisVal, RealmState realm, HostFunction defaultConstructor)
    {
        if (thisVal is not IJsPropertyAccessor accessor ||
            !accessor.TryGetProperty("constructor", out var ctorVal))
        {
            return defaultConstructor;
        }

        if (ctorVal is null || ReferenceEquals(ctorVal, Symbol.Undefined))
        {
            return defaultConstructor;
        }

        if (ctorVal is not IJsPropertyAccessor ctorAccessor)
        {
            throw ThrowTypeError("Constructor is not an object", realm: realm);
        }

        var speciesKey = SymbolKeys.Species;
        if (ctorAccessor.TryGetProperty(speciesKey, out var speciesVal))
        {
            if (speciesVal is null || ReferenceEquals(speciesVal, Symbol.Undefined))
            {
                return defaultConstructor;
            }

            return speciesVal;
        }

        return ctorVal;
    }

    internal static object? ArrayBufferIsView(object? _, IReadOnlyList<object?> args, RealmState? __)
    {
        if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
        {
            return false;
        }

        return args[0] is TypedArrayBase or JsDataView;
    }
}
