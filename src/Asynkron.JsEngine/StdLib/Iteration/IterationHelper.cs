#region

using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using static Asynkron.JsEngine.StdLib.PromiseHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;
using JsEngineInstance = Asynkron.JsEngine.JsEngine;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class IterationHelper
{
    public static HostFunction CreateGetAsyncIteratorHelper(JsEngineInstance engine)
    {
        return new HostFunction(GetAsyncIterator);

        JsValue GetAsyncIterator(IReadOnlyList<JsValue> args)
        {
            if (args.Count == 0)
            {
                throw new InvalidOperationException("__getAsyncIterator requires an iterable");
            }

            var iterable = args[0];

            if (iterable.TryGetObject(out var jsObject) && jsObject is not null)
            {
                if (HasCallableNext(jsObject))
                {
                    engine.WriteAsyncIteratorTrace("getAsyncIterator: branch=next-property");
                    return new JsValue(jsObject);
                }

                if (TryInvokeSymbolIterator(jsObject, Symbols.AsyncIterator, out var asyncIterator))
                {
                    engine.WriteAsyncIteratorTrace(
                        $"getAsyncIterator: branch=symbol-asyncIterator hasCallableNext={HasCallableNext(asyncIterator)}");
                    return new JsValue(asyncIterator!);
                }

                if (TryInvokeSymbolIterator(jsObject, Symbols.Iterator, out var iterator))
                {
                    engine.WriteAsyncIteratorTrace(
                        $"getAsyncIterator: branch=symbol-iterator hasCallableNext={HasCallableNext(iterator)}");
                    return new JsValue(iterator!);
                }

                throw new InvalidOperationException(
                    "Object is not iterable (no Symbol.asyncIterator or Symbol.iterator method)");
            }

            if (iterable.TryGetObject<JsArray>(out var jsArray) && jsArray is not null)
            {
                var iteratorObj = CreateArrayIterator(jsArray);
                engine.WriteAsyncIteratorTrace(
                    $"getAsyncIterator: branch=array length={jsArray.Length}");
                return new JsValue(iteratorObj);
            }

            if (iterable.TryGetString(out var str) && str is not null)
            {
                var iteratorObj = CreateStringIterator(str);
                engine.WriteAsyncIteratorTrace(
                    $"getAsyncIterator: branch=string hasCallableNext={HasCallableNext(iteratorObj)} length={str.Length}");
                return new JsValue(iteratorObj);
            }

            throw new InvalidOperationException($"Value is not iterable: {iterable.Kind}");

            static bool TryInvokeSymbolIterator(JsObject target, JsSymbol symbol, out JsObject? iterator)
            {
                var propertyName = JsSymbol.PropertyKey(symbol);
                if (target.TryGetProperty(propertyName, out var method) &&
                    method.TryGetObject<IJsCallable>(out var callable) && callable is not null)
                {
                    var result = callable.Invoke([], new JsValue(target));
                    if (result.TryGetObject(out var iteratorObj) && iteratorObj is not null)
                    {
                        iterator = iteratorObj;
                        return true;
                    }
                }

                iterator = null;
                return false;
            }

            JsObject CreateStringIterator(string str)
            {
                var iteratorObj = new JsObject();
                var index = 0;
                var nextFunc = new HostFunction(_ =>
                {
                    var result = new JsObject();
                    if (index < str.Length)
                    {
                        // Per ES spec 22.1.5.2.1 %StringIteratorPrototype%.next():
                        // Strings are iterated by code point, not UTF-16 code unit.
                        // Surrogate pairs should be returned as a single string.
                        var first = str[index];
                        string value;
                        if (char.IsHighSurrogate(first) && index + 1 < str.Length &&
                            char.IsLowSurrogate(str[index + 1]))
                        {
                            // High surrogate followed by low surrogate - return both as single string
                            value = str.Substring(index, 2);
                            index += 2;
                        }
                        else
                        {
                            // Single code unit (BMP character or unpaired surrogate)
                            value = first.ToString();
                            index++;
                        }

                        result.SetProperty("value", (JsValue)value);
                        result.SetProperty("done", false);
                    }
                    else
                    {
                        result.SetProperty("done", true);
                    }

                    return new JsValue(result);
                }, isConstructor: false);
                iteratorObj.SetProperty("next", (JsValue)nextFunc);
                return iteratorObj;
            }

            static JsObject CreateArrayIterator(JsArray array)
            {
                var iteratorObj = new JsObject();
                var index = 0;
                // Use _handler directly instead of _invokeWithContext because
                // CreateIteratorNextHelper uses Invoke() which only calls _handler
                var next = new HostFunction((_, _) =>
                {
                    var result = new JsObject();
                    if (index < array.Length)
                    {
                        var propertyName = index.ToString(CultureInfo.InvariantCulture);
                        JsValue value;
                        if (array.TryGetProperty(propertyName, out var val))
                        {
                            value = val;
                        }
                        else
                        {
                            value = JsValue.Undefined;
                        }

                        result.SetProperty("value", value);
                        result.SetProperty("done", false);
                        index++;
                    }
                    else
                    {
                        result.SetProperty("done", true);
                    }

                    return new JsValue(result);
                }, isConstructor: false);
                iteratorObj.SetProperty("next", (JsValue)next);
                return iteratorObj;
            }

            static bool HasCallableNext(object? candidate)
            {
                return candidate is JsObject obj &&
                       obj.TryGetProperty("next", out var nextProp) &&
                       nextProp.TryGetObject<IJsCallable>(out _);
            }
        }
    }

    /// <summary>
    ///     Helper method for async iteration: gets next value from iterator and wraps in Promise if needed.
    ///     This handles both sync and async iterators uniformly.
    /// </summary>
    public static HostFunction CreateIteratorNextHelper(JsEngineInstance engine)
    {
        return new HostFunction(IteratorNext);

        JsValue IteratorNext(IReadOnlyList<JsValue> args)
        {
            // args[0] should be the iterator object
            if (args.Count == 0 || !args[0].TryGetObject(out var iterator) || iterator is null)
            {
                throw new InvalidOperationException("__iteratorNext requires an iterator object");
            }

            // Call iterator.next()
            if (!iterator.TryGetProperty("next", out var nextMethod) ||
                !nextMethod.TryGetObject<IJsCallable>(out var nextCallable) || nextCallable is null)
            {
                throw new InvalidOperationException("Iterator must have a 'next' method");
            }

            engine.WriteAsyncIteratorTrace("iteratorNext: invoking next() on iterator");
            JsValue result;
            try
            {
                result = nextCallable.Invoke([], new JsValue(iterator));
                engine.WriteAsyncIteratorTrace("iteratorNext: next() invocation succeeded");
            }
            catch (Exception ex)
            {
                // Log the exception for debugging
                engine.LogException(ex, "Iterator.next() invocation");

                engine.WriteAsyncIteratorTrace($"iteratorNext: next() threw exception='{ex.Message}'");

                // If next() throws an error, wrap it in a rejected promise
                var rejectedPromise = new JsPromise(engine);
                AddPromiseInstanceMethods(rejectedPromise.JsObject, rejectedPromise, engine);
                rejectedPromise.Reject(ex.Message);
                engine.WriteAsyncIteratorTrace("iteratorNext: returning rejected promise due to exception");
                return new JsValue(rejectedPromise.JsObject);
            }

            // Iterator.next must return an object; if it doesn't, surface a
            // rejection so async iteration can stop instead of recursing forever.
            if (!result.TryGetObject(out var resultObject) || resultObject is null)
            {
                var rejectedPromise = new JsPromise(engine);
                AddPromiseInstanceMethods(rejectedPromise.JsObject, rejectedPromise, engine);
                var error = CreateTypeError("Iterator.next() did not return an object");
                rejectedPromise.Reject(error);
                engine.WriteAsyncIteratorTrace("iteratorNext: rejected promise because next() returned non-object");
                return new JsValue(rejectedPromise.JsObject);
            }

            // Check if result is already a promise (has a "then" method)
            if (resultObject.TryGetProperty("then", out var thenMethod) &&
                thenMethod.TryGetObject<IJsCallable>(out _))
            {
                engine.WriteAsyncIteratorTrace("iteratorNext: result already promise-like, returning as-is");
                // Already a promise, return as-is
                return new JsValue(resultObject);
            }

            // Not a promise, wrap in Promise.resolve()
            var promise = new JsPromise(engine);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);
            promise.Resolve(result);
            engine.WriteAsyncIteratorTrace("iteratorNext: wrapped result in resolved promise");
            return new JsValue(promise.JsObject);
        }
    }

    /// <summary>
    ///     Helper function for await expressions: wraps value in Promise if needed.
    ///     Checks if the value is already a promise (has a "then" method) before wrapping.
    /// </summary>
    public static HostFunction CreateAwaitHelper(JsEngineInstance engine)
    {
        return new HostFunction(AwaitValue);

        JsValue AwaitValue(IReadOnlyList<JsValue> args)
        {
            // args[0] should be the value to await
            var value = args.Count > 0 ? args[0] : JsValue.Undefined;

            // Check if value is already a promise (has a "then" method)
            if (value.TryGetObject(out var valueObj) && valueObj is not null &&
                valueObj.TryGetProperty("then", out var thenMethod) &&
                thenMethod.TryGetObject<IJsCallable>(out _))
            {
                // Already a promise, return as-is
                return value;
            }

            // Not a promise, wrap in Promise.resolve()
            var promise = new JsPromise(engine);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);
            promise.Resolve(value);
            return new JsValue(promise.JsObject);
        }
    }
}
