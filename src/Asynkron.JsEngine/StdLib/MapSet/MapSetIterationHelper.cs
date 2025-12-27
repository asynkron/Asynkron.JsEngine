#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

internal static class MapSetIterationHelper
{
    internal static IJsObjectLike GetIterator(JsValue iterable, RealmState realm, string operation)
    {
        if (iterable.IsNullOrUndefined)
        {
            throw StandardLibrary.ThrowTypeError($"{operation} requires an iterable", realm: realm);
        }

        // Strings are iterable, so synthesize a string iterator when needed.
        if (iterable.TryGetString(out var str) && str is not null)
        {
            return CreateStringIterator(str, realm);
        }

        if (!iterable.TryGetObjectLike(out var obj) || obj is null)
        {
            throw StandardLibrary.ThrowTypeError($"{operation} requires an iterable", realm: realm);
        }

        // Prefer Symbol.iterator if present.
        if (obj.TryGetProperty(SymbolKeys.Iterator, out var iterMethod) &&
            iterMethod.TryGetObject<IJsCallable>(out var iterCallable) &&
            iterCallable is not null)
        {
            var iteratorValue = iterCallable.Invoke([], iterable);
            if (!iteratorValue.TryGetObjectLike(out var iteratorObj) || iteratorObj is null)
            {
                throw StandardLibrary.ThrowTypeError($"{operation} iterator method must return an object", realm: realm);
            }

            return iteratorObj;
        }

        // Fall back to iterator-like objects with a callable next().
        if (obj.TryGetProperty("next", out var nextProp) &&
            nextProp.TryGetObject<IJsCallable>(out _))
        {
            return obj;
        }

        throw StandardLibrary.ThrowTypeError($"{operation} requires an iterable", realm: realm);
    }

    internal static void Iterate(JsValue iterable, RealmState realm, string operation, Action<JsValue> onValue)
    {
        var iterator = GetIterator(iterable, realm, operation);
        while (true)
        {
            if (!TryIteratorStep(iterator, realm, operation, out var value))
            {
                break;
            }

            try
            {
                // Keep processing simple: delegate handling to the caller.
                onValue(value);
            }
            catch
            {
                StandardLibrary.IteratorClose(iterator, realm, operation);
                throw;
            }
        }
    }

    private static bool TryIteratorStep(IJsObjectLike iterator, RealmState realm, string operation, out JsValue value)
    {
        if (!iterator.TryGetProperty("next", out var nextProp) ||
            !nextProp.TryGetObject<IJsCallable>(out var nextMethod) ||
            nextMethod is null)
        {
            throw StandardLibrary.ThrowTypeError($"{operation} iterator must have a callable next method", realm: realm);
        }

        var resultValue = nextMethod.Invoke([], JsValue.FromObjectUnsafe(iterator));
        if (!resultValue.TryGetObjectLike(out var resultObj) || resultObj is null)
        {
            throw StandardLibrary.ThrowTypeError($"{operation} iterator result must be an object", realm: realm);
        }

        if (resultObj.TryGetProperty("done", out var doneProp) && JsOps.ToBoolean(doneProp))
        {
            value = JsValue.Undefined;
            return false;
        }

        value = resultObj.TryGetProperty("value", out var valueProp) ? valueProp : JsValue.Undefined;
        return true;
    }

    private static JsObject CreateStringIterator(string str, RealmState realm)
    {
        var iterator = new JsObject { RealmState = realm };
        var index = 0;

        iterator.SetHostedProperty("next", (_, _) =>
        {
            if (index >= str.Length)
            {
                return IteratorResultObject.DoneUndefined.AsJsValue;
            }

            // Iterate by code point to handle surrogate pairs correctly.
            var first = str[index];
            string charValue;
            if (char.IsHighSurrogate(first) && index + 1 < str.Length &&
                char.IsLowSurrogate(str[index + 1]))
            {
                charValue = str.Substring(index, 2);
                index += 2;
            }
            else
            {
                charValue = first.ToString();
                index++;
            }

            return IteratorResultObject.Create(new JsValue(charValue), false);
        });

        iterator.SetHostedProperty(SymbolKeys.Iterator, (_, _) => iterator);
        return iterator;
    }
}
