using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateGetAsyncIteratorHelper(JsEngine engine)
    {
        return new HostFunction(GetAsyncIterator);

        object? GetAsyncIterator(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                throw new InvalidOperationException("__getAsyncIterator requires an iterable");
            }

            var iterable = args[0];

            if (iterable is JsObject jsObject)
            {
                if (HasCallableNext(jsObject))
                {
                    engine.WriteAsyncIteratorTrace("getAsyncIterator: branch=next-property");
                    return jsObject;
                }

                if (TryInvokeSymbolIterator(jsObject, "Symbol.asyncIterator", out var asyncIterator))
                {
                    engine.WriteAsyncIteratorTrace(
                        $"getAsyncIterator: branch=symbol-asyncIterator hasCallableNext={HasCallableNext(asyncIterator)}");
                    return asyncIterator;
                }

                if (TryInvokeSymbolIterator(jsObject, "Symbol.iterator", out var iterator))
                {
                    engine.WriteAsyncIteratorTrace(
                        $"getAsyncIterator: branch=symbol-iterator hasCallableNext={HasCallableNext(iterator)}");
                    return iterator;
                }

                throw new InvalidOperationException(
                    "Object is not iterable (no Symbol.asyncIterator or Symbol.iterator method)");
            }

            if (iterable is JsArray jsArray)
            {
                var iteratorObj = CreateArrayIterator(jsArray);
                engine.WriteAsyncIteratorTrace(
                    $"getAsyncIterator: branch=array length={jsArray.Length}");
                return iteratorObj;
            }

            if (iterable is string str)
            {
                var iteratorObj = CreateStringIterator(str);
                engine.WriteAsyncIteratorTrace(
                    $"getAsyncIterator: branch=string hasCallableNext={HasCallableNext(iteratorObj)} length={str.Length}");
                return iteratorObj;
            }

            throw new InvalidOperationException($"Value is not iterable: {iterable?.GetType().Name}");

            static bool TryInvokeSymbolIterator(JsObject target, string symbolName, out JsObject? iterator)
            {
                var symbol = TypedAstSymbol.For(symbolName);
                var propertyName = $"@@symbol:{symbol.GetHashCode()}";
                if (target.TryGetProperty(propertyName, out var method) && method is IJsCallable callable)
                {
                    if (callable.Invoke([], target) is JsObject iteratorObj)
                    {
                        iterator = iteratorObj;
                        return true;
                    }
                }

                iterator = null;
                return false;
            }

            static JsObject CreateStringIterator(string str)
            {
                var iteratorObj = new JsObject();
                var index = 0;
                iteratorObj.SetHostedProperty("next", Next);
                return iteratorObj;

                object? Next(IReadOnlyList<object?> _)
                {
                    var result = new JsObject();
                    if (index < str.Length)
                    {
                        result.SetProperty("value", str[index].ToString());
                        result.SetProperty("done", false);
                        index++;
                    }
                    else
                    {
                        result.SetProperty("done", true);
                    }

                    return result;
                }
            }

            static JsObject CreateArrayIterator(JsArray array)
            {
                var iteratorObj = new JsObject();
                var index = 0;
                iteratorObj.SetHostedProperty("next", Next);
                return iteratorObj;

                object? Next(IReadOnlyList<object?> _)
                {
                    var result = new JsObject();
                    if (index < array.Length)
                    {
                        result.SetProperty("value", array.GetElement(index));
                        result.SetProperty("done", false);
                        index++;
                    }
                    else
                    {
                        result.SetProperty("done", true);
                    }

                    return result;
                }
            }

            static bool HasCallableNext(object? candidate)
            {
                return candidate is JsObject obj &&
                       obj.TryGetProperty("next", out var nextProp) &&
                       nextProp is IJsCallable;
            }
        }
    }

    /// <summary>
    ///     Helper method for async iteration: gets next value from iterator and wraps in Promise if needed.
    ///     This handles both sync and async iterators uniformly.
    /// </summary>
    public static HostFunction CreateIteratorNextHelper(JsEngine engine)
    {
        return new HostFunction(IteratorNext);

        object? IteratorNext(IReadOnlyList<object?> args)
        {
            // args[0] should be the iterator object
            if (args.Count == 0 || args[0] is not JsObject iterator)
            {
                throw new InvalidOperationException("__iteratorNext requires an iterator object");
            }

            // Call iterator.next()
            if (!iterator.TryGetProperty("next", out var nextMethod) || nextMethod is not IJsCallable nextCallable)
            {
                throw new InvalidOperationException("Iterator must have a 'next' method");
            }

            engine.WriteAsyncIteratorTrace("iteratorNext: invoking next() on iterator");
            object? result;
            try
            {
                result = nextCallable.Invoke([], iterator);
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
                return rejectedPromise.JsObject;
            }

            // Iterator.next must return an object; if it doesn't, surface a
            // rejection so async iteration can stop instead of recursing forever.
            if (result is not JsObject resultObject)
            {
                var rejectedPromise = new JsPromise(engine);
                AddPromiseInstanceMethods(rejectedPromise.JsObject, rejectedPromise, engine);
                var error = CreateTypeError("Iterator.next() did not return an object");
                rejectedPromise.Reject(error);
                engine.WriteAsyncIteratorTrace("iteratorNext: rejected promise because next() returned non-object");
                return rejectedPromise.JsObject;
            }

            // Check if result is already a promise (has a "then" method)
            if (resultObject.TryGetProperty("then", out var thenMethod) &&
                thenMethod is IJsCallable)
            {
                engine.WriteAsyncIteratorTrace("iteratorNext: result already promise-like, returning as-is");
                // Already a promise, return as-is
                return resultObject;
            }

            // Not a promise, wrap in Promise.resolve()
            var promise = new JsPromise(engine);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);
            promise.Resolve(result);
            engine.WriteAsyncIteratorTrace("iteratorNext: wrapped result in resolved promise");
            return promise.JsObject;
        }
    }

    /// <summary>
    ///     Helper function for await expressions: wraps value in Promise if needed.
    ///     Checks if the value is already a promise (has a "then" method) before wrapping.
    /// </summary>
    public static HostFunction CreateAwaitHelper(JsEngine engine)
    {
        return new HostFunction(AwaitValue);

        object? AwaitValue(IReadOnlyList<object?> args)
        {
            // args[0] should be the value to await
            var value = args.Count > 0 ? args[0] : null;

            // Check if value is already a promise (has a "then" method)
            if (value is JsObject valueObj && valueObj.TryGetProperty("then", out var thenMethod) &&
                thenMethod is IJsCallable)
                // Already a promise, return as-is
            {
                return value;
            }

            // Not a promise, wrap in Promise.resolve()
            var promise = new JsPromise(engine);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);
            promise.Resolve(value);
            return promise.JsObject;
        }
    }
}
