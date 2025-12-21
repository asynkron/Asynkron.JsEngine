using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

public static class SymbolHelper
{
    public static HostFunction CreateSymbolConstructor(RealmState realm)
    {
        return SymbolConstructor.CreateConstructor(realm);
    }

    public static JsObject CreateSymbolWrapper(TypedAstSymbol symbol, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        var wrapper = new JsObject { ["__value__"] = new JsValue(JsValueKind.Symbol, 0.0, symbol) };

        var proto = context?.RealmState?.SymbolPrototype ?? realm?.SymbolPrototype;
        if (proto is not null)
        {
            wrapper.SetPrototype(proto);
        }
        else
        {
            var valueOf = new HostFunction((thisValue, _) =>
            {
                var sym = RequireSymbolReceiver(thisValue, realm);
                return new JsValue(JsValueKind.Symbol, 0.0, sym);
            }, realm, isConstructor: false);

            var toString = new HostFunction((thisValue, _) =>
            {
                var sym = RequireSymbolReceiver(thisValue, realm);
                return new JsValue(sym.ToString());
            }, realm, isConstructor: false);

            wrapper.SetHostedProperty("valueOf", valueOf);
            wrapper.SetHostedProperty("toString", toString);

            var toPrimitiveKey = SymbolKeys.ToPrimitive;
            wrapper.SetProperty(toPrimitiveKey,
                new HostFunction((thisValue, _) =>
                {
                    var sym = RequireSymbolReceiver(thisValue, realm);
                    return new JsValue(JsValueKind.Symbol, 0.0, sym);
                }, realm, isConstructor: false));

            var toStringTagKey = SymbolKeys.ToStringTag;
            wrapper.SetProperty(toStringTagKey, "Symbol");
        }

        return wrapper;
    }

    internal static TypedAstSymbol RequireSymbolReceiver(JsValue receiver, RealmState? realm = null)
    {
        if (receiver.IsSymbol && receiver.TryUnwrap<TypedAstSymbol>(out var sym))
        {
            return sym;
        }

        if (receiver.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("__value__", out var inner))
        {
            // inner is JsValue, need to extract TypedAstSymbol from it
            if (inner.IsSymbol && inner.TryUnwrap<TypedAstSymbol>(out var innerSym))
            {
                return innerSym;
            }
            // Also check if inner.ObjectValue is directly a TypedAstSymbol (backward compatibility)
            if (inner.ObjectValue is TypedAstSymbol directSym)
            {
                return directSym;
            }
        }

        throw ThrowTypeError("Symbol.prototype valueOf called on incompatible receiver", realm: realm);
    }
}
