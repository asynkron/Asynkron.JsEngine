using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IJsPropertyAccessor target)
    {
        private bool TryInvokeSymbolMethod(object? thisArg, string symbolName,
            out object? result)
        {
            var symbol = TypedAstSymbol.For(symbolName);
            var hashedName = $"@@symbol:{symbol.GetHashCode()}";

            if (TryGetCallable(hashedName, out var callable) ||
                TryGetCallable(symbolName, out callable) ||
                TryGetCallable(symbol.ToString(), out callable))
            {
                result = callable!.Invoke([], thisArg);
                return true;
            }

            result = null;
            return false;

            bool TryGetCallable(string propertyName, out IJsCallable? callable)
            {
                if (target.TryGetProperty(propertyName, out var candidate) && candidate is IJsCallable found)
                {
                    callable = found;
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

            var created = new JsObject();
            if (realm.ObjectPrototype is not null)
            {
                created.SetPrototype(realm.ObjectPrototype);
            }

            constructor.SetProperty("prototype", created);
            return created;
        }
    }
}
