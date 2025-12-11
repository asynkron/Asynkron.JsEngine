using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateSymbolConstructor(RealmState realm)
    {
        return SymbolConstructor.CreateConstructor(realm);
    }

    public static JsObject CreateSymbolWrapper(TypedAstSymbol symbol, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        var wrapper = new JsObject { ["__value__"] = symbol };

        var proto = context?.RealmState?.SymbolPrototype ?? realm?.SymbolPrototype;
        if (proto is not null)
        {
            wrapper.SetPrototype(proto);
        }
        else
        {
            var valueOf = new HostFunction((thisValue, _) => RequireSymbolReceiver(thisValue, realm), realm,
                isConstructor: false);
            var toString = new HostFunction((thisValue, _) => RequireSymbolReceiver(thisValue, realm).ToString(),
                realm, isConstructor: false);

            wrapper.SetHostedProperty("valueOf", valueOf);
            wrapper.SetHostedProperty("toString", toString);

            var toPrimitiveKey = SymbolKeys.ToPrimitive;
            wrapper.SetProperty(toPrimitiveKey,
                new HostFunction((thisValue, _) => RequireSymbolReceiver(thisValue, realm), realm,
                    isConstructor: false));

            var toStringTagKey = SymbolKeys.ToStringTag;
            wrapper.SetProperty(toStringTagKey, "Symbol");
        }

        return wrapper;
    }

    internal static TypedAstSymbol RequireSymbolReceiver(object? receiver, RealmState? realm = null)
    {
        return receiver switch
        {
            TypedAstSymbol sym => sym,
            JsObject obj when obj.TryGetProperty("__value__", out var inner) && inner is TypedAstSymbol sym => sym,
            _ => throw ThrowTypeError("Symbol.prototype valueOf called on incompatible receiver", realm: realm)
        };
    }
}
