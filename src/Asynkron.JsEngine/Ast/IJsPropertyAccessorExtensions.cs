using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IJsPropertyAccessor target)
    {
        private bool TryInvokeSymbolMethod(object? thisArg, string symbolName,
            EvaluationContext context,
            out object? result)
        {
            var symbol = TypedAstSymbol.For(symbolName);
            var hashedName = $"@@symbol:{symbol.GetHashCode()}";
            var realm = context.RealmState;

            if (TryGetCallable(hashedName, out var callable) ||
                TryGetCallable(symbolName, out callable) ||
                TryGetCallable(symbol.ToString(), out callable))
            {
                if (context.ShouldStopEvaluation)
                {
                    result = callable;
                    return true;
                }

                result = InvokeCallable(
                    callable!,
                    Array.Empty<object?>(),
                    thisArg,
                    context,
                    context.RealmState?.Engine?.GlobalEnvironment);
                return true;
            }

            // The property existed but was not callable; align with GetMethod
            // semantics by surfacing a TypeError instead of silently skipping it.
            if (context.ShouldStopEvaluation)
            {
                result = context.FlowValue;
                return true;
            }

            result = null;
            return false;

            bool TryGetCallable(string propertyName, out IJsCallable? callable)
            {
                if (JsOps.TryGetPropertyValue(target, propertyName, out var candidate, context) &&
                    candidate is IJsCallable found)
                {
                    callable = found;
                    return true;
                }

                if (context.ShouldStopEvaluation)
                {
                    callable = null;
                    return true;
                }

                if (candidate is not null && !ReferenceEquals(candidate, Symbol.Undefined))
                {
                    var error = StandardLibrary.CreateTypeError("Iterator method is not callable", context, realm);
                    context.SetThrow(error);
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
            if (constructor.TryGetProperty("prototype", out var prototypeValue) && prototypeValue is JsObject prototype)
            {
                if (prototype.Prototype is null && realm.ObjectPrototype is not null)
                {
                    prototype.SetPrototype(realm.ObjectPrototype);
                }

                return prototype;
            }

            var created = new JsObject(realm.ObjectPrototype);

            constructor.SetProperty("prototype", created);
            return created;
        }
    }
}
