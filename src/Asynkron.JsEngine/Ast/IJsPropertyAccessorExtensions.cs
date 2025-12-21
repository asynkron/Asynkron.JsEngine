using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IJsPropertyAccessor target)
    {
        private bool TryInvokeSymbolMethod(JsValue thisArg, TypedAstSymbol symbol,
            EvaluationContext context,
            out JsValue result)
        {
            var symbolName = symbol.Description ?? symbol.ToString();
            var hashedName = TypedAstSymbol.PropertyKey(symbol);
            var realm = context.RealmState;
            var thisArgObj = thisArg.ToObject();
            realm?.Logger?.LogInformation("TryInvokeSymbolMethod name={Name} thisType={Type}", symbolName,
                thisArgObj?.GetType().Name ?? "null");

            if (TryGetCallable(symbol, out var callable) ||
                TryGetCallable(hashedName, out callable) ||
                TryGetCallable(symbolName, out callable) ||
                TryGetCallable(symbol.ToString(), out callable))
            {
                if (context.ShouldStopEvaluation)
                {
                    realm?.Logger?.LogInformation("TryInvokeSymbolMethod stopDuringLookup flowType={FlowType}",
                        context.FlowValue.GetType().Name ?? "null");
                    result = JsValue.FromObjectUnsafe(callable);
                    return true;
                }

                realm?.Logger?.LogInformation("TryInvokeSymbolMethod invoking callableType={CallableType}",
                    callable?.GetType().Name ?? "null");
                result = InvokeCallableJsValue(
                    callable!,
                    [],
                    thisArg,
                    context,
                    context.RealmState?.Engine?.GlobalEnvironment);
                realm?.Logger?.LogInformation("TryInvokeSymbolMethod completed stop={Stop} resultType={ResultType}",
                    context.ShouldStopEvaluation,
                    result.ToObject()?.GetType().Name ?? "null");
                return true;
            }

            // The property existed but was not callable; align with GetMethod
            // semantics by surfacing a TypeError instead of silently skipping it.
            if (context.ShouldStopEvaluation)
            {
                realm?.Logger?.LogInformation("TryInvokeSymbolMethod stopAfterLookup flowType={FlowType}",
                    context.FlowValue.GetType().Name ?? "null");
                result = context.FlowValue;
                return true;
            }

            result = JsValue.Undefined;
            return false;

            bool TryGetCallable(object propertyKey, out IJsCallable? callable)
            {
                if (JsOps.TryGetPropertyValue(target, propertyKey, out var candidate, context))
                {
                    // Unwrap JsValue if present
                    if (candidate is JsValue jsVal)
                    {
                        if (jsVal.TryGetObject<IJsCallable>(out var jsCallable))
                        {
                            callable = jsCallable;
                            return true;
                        }
                        candidate = jsVal.ObjectValue;
                    }

                    if (candidate is IJsCallable found)
                    {
                        callable = found;
                        return true;
                    }
                }

                if (context.ShouldStopEvaluation)
                {
                    callable = null;
                    return true;
                }

                if (candidate is not null && !ReferenceEquals(candidate, Symbol.Undefined))
                {
                    var error = StandardLibrary.CreateTypeError("Iterator method is not callable", context, realm);
                    context.SetThrow(JsValue.FromObjectUnsafe(error));
                    callable = null;
                    return true;
                }

                callable = null;
                return false;
            }
        }
    }

    extension(IJsPropertyAccessor accessor)
    {
        private IEnumerable<string> GetEnumerableOwnPropertyKeysInOrder()
        {
            if (accessor is JsObject jsObject)
            {
                foreach (var key in jsObject.GetOwnEnumerablePropertyKeysInOrder())
                {
                    yield return key;
                }

                yield break;
            }

            foreach (var key in accessor.GetEnumerablePropertyNames())
            {
                yield return key;
            }
        }
    }

    extension(IJsPropertyAccessor constructor)
    {
        private JsObject EnsurePrototype(RealmState realm)
        {
            if (constructor.TryGetProperty("prototype", out var prototypeValue) && prototypeValue.TryGetObject<JsObject>(out var prototype))
            {
                if (prototype.Prototype is null && realm.ObjectPrototype is not null)
                {
                    prototype.SetPrototype(realm.ObjectPrototype);
                }

                prototype.Origin ??= "constructor.prototype";
                return prototype;
            }

            var created = new JsObject(realm.ObjectPrototype)
            {
                Origin = "constructor.prototype (auto-created)"
            };

            constructor.SetProperty("prototype", (JsValue)created);
            return created;
        }
    }
}
