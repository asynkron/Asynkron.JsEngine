#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

internal static class DisposableStackHelper
{
    internal static IJsCallable? GetDisposeMethod(JsValue value, bool preferAsync, RealmState realm)
    {
        if (value.IsNull || value.IsUndefined)
        {
            return null;
        }

        if (!value.TryGetObject<IJsPropertyAccessor>(out var accessor) || accessor is null)
        {
            throw ThrowTypeError("DisposableStack value must be an object", realm: realm);
        }

        if (preferAsync)
        {
            if (TryGetCallable(accessor, Symbols.AsyncDispose, realm, out var asyncMethod))
            {
                return asyncMethod;
            }

            if (TryGetCallable(accessor, Symbols.Dispose, realm, out var syncMethod))
            {
                return syncMethod;
            }
        }
        else
        {
            if (TryGetCallable(accessor, Symbols.Dispose, realm, out var disposeMethod))
            {
                return disposeMethod;
            }
        }

        throw ThrowTypeError("Object is not disposable", realm: realm);
    }

    private static bool TryGetCallable(IJsPropertyAccessor accessor, JsSymbol symbol, RealmState realm,
        out IJsCallable? callable)
    {
        var key = JsSymbol.PropertyKey(symbol);
        if (!accessor.TryGetProperty(key, out var candidate) || candidate.IsUndefined || candidate.IsNull)
        {
            callable = null;
            return false;
        }

        if (!candidate.TryGetObject<IJsCallable>(out var method) || method is null)
        {
            throw ThrowTypeError("Dispose method is not callable", realm: realm);
        }

        callable = method;
        return true;
    }
}
