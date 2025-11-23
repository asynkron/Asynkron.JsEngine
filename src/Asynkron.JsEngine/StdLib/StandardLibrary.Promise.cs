using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    /// <summary>
    ///     Creates a Promise constructor with static methods.
    /// </summary>
    public static IJsCallable CreatePromiseConstructor(JsEngine engine)
    {
        var promiseConstructor = new HostFunction(PromiseConstructor);

        promiseConstructor.SetHostedProperty("resolve", PromiseResolve);

        promiseConstructor.SetHostedProperty("reject", PromiseReject);

        promiseConstructor.SetHostedProperty("all", PromiseAll);

        promiseConstructor.SetHostedProperty("race", PromiseRace);

        return promiseConstructor;

        object? PromiseConstructor(object? _, IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] is not IJsCallable executor)
            {
                throw new InvalidOperationException("Promise constructor requires an executor function");
            }

            var promise = new JsPromise(engine);
            var promiseObj = promise.JsObject;
            AddPromiseInstanceMethods(promiseObj, promise, engine);

            var resolve = new HostFunction(Resolve);

            var reject = new HostFunction(Reject);

            try
            {
                executor.Invoke([resolve, reject], null);
            }
            catch (Exception ex)
            {
                promise.Reject(ex.Message);
            }

            return promiseObj;

            object? Resolve(IReadOnlyList<object?> resolveArgs)
            {
                promise.Resolve(resolveArgs.Count > 0 ? resolveArgs[0] : null);
                return null;
            }

            object? Reject(IReadOnlyList<object?> rejectArgs)
            {
                promise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);
                return null;
            }
        }

        object? PromiseResolve(IReadOnlyList<object?> args)
        {
            var value = args.Count > 0 ? args[0] : null;
            var promise = new JsPromise(engine);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);

            promise.Resolve(value);
            return promise.JsObject;
        }

        object? PromiseReject(IReadOnlyList<object?> args)
        {
            var reason = args.Count > 0 ? args[0] : null;
            var promise = new JsPromise(engine);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);

            promise.Reject(reason);
            return promise.JsObject;
        }

        object? PromiseAll(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] is not JsArray array)
            {
                return null;
            }

            var resultPromise = new JsPromise(engine);
            AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);

            var results = new object?[array.Items.Count];
            var remaining = array.Items.Count;

            if (remaining == 0)
            {
                var emptyArray = new JsArray();
                AddArrayMethods(emptyArray, engine.RealmState);
                resultPromise.Resolve(emptyArray);
                return resultPromise.JsObject;
            }

            for (var i = 0; i < array.Items.Count; i++)
            {
                var index = i;
                var item = array.Items[i];

                if (item is JsObject itemObj && itemObj.TryGetProperty("then", out var thenMethod) &&
                    thenMethod is IJsCallable thenCallable)
                {
                    thenCallable.Invoke([CreateAllResolve(index), CreateAllReject()], itemObj);
                }
                else
                {
                    results[index] = item;
                    remaining--;

                    if (remaining != 0)
                    {
                        continue;
                    }

                    var resultArray = new JsArray();
                    foreach (var result in results)
                    {
                        resultArray.Push(result);
                    }

                    AddArrayMethods(resultArray, engine.RealmState);
                    resultPromise.Resolve(resultArray);
                }
            }

            return resultPromise.JsObject;

            HostFunction CreateAllResolve(int index)
            {
                object? Resolve(object? _, IReadOnlyList<object?> resolveArgs)
                {
                    results[index] = resolveArgs.Count > 0 ? resolveArgs[0] : null;
                    remaining--;

                    if (remaining != 0)
                    {
                        return null;
                    }

                    var resultArray = new JsArray();
                    foreach (var result in results)
                    {
                        resultArray.Push(result);
                    }

                    AddArrayMethods(resultArray, engine.RealmState);
                    resultPromise.Resolve(resultArray);

                    return null;
                }

                return new HostFunction(Resolve);
            }

            HostFunction CreateAllReject()
            {
                object? Reject(object? _, IReadOnlyList<object?> rejectArgs)
                {
                    resultPromise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);
                    return null;
                }

                return new HostFunction(Reject);
            }
        }

        object? PromiseRace(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] is not JsArray array)
            {
                return null;
            }

            var resultPromise = new JsPromise(engine);
            AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);

            var settled = false;

            foreach (var item in array.Items)
            {
                if (item is JsObject itemObj && itemObj.TryGetProperty("then", out var thenMethod) &&
                    thenMethod is IJsCallable thenCallable)
                {
                    thenCallable.Invoke([CreateRaceResolve(), CreateRaceReject()], itemObj);
                }
                else if (!settled)
                {
                    settled = true;
                    resultPromise.Resolve(item);
                }
            }

            return resultPromise.JsObject;

            HostFunction CreateRaceResolve()
            {
                object? Resolve(object? _, IReadOnlyList<object?> resolveArgs)
                {
                    if (settled)
                    {
                        return null;
                    }

                    settled = true;
                    resultPromise.Resolve(resolveArgs.Count > 0 ? resolveArgs[0] : null);

                    return null;
                }

                return new HostFunction(Resolve);
            }

            HostFunction CreateRaceReject()
            {
                object? Reject(object? _, IReadOnlyList<object?> rejectArgs)
                {
                    if (settled)
                    {
                        return null;
                    }

                    settled = true;
                    resultPromise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);

                    return null;
                }

                return new HostFunction(Reject);
            }
        }
    }

    /// <summary>
    ///     Helper method to add instance methods to a promise.
    /// </summary>
    internal static void AddPromiseInstanceMethods(JsObject promiseObj, JsPromise promise, JsEngine engine)
    {
        promiseObj.SetHostedProperty("then", PromiseThen);

        promiseObj.SetHostedProperty("catch", PromiseCatch);

        promiseObj.SetHostedProperty("finally", PromiseFinally);
        return;

        object? PromiseThen(object? _, IReadOnlyList<object?> thenArgs)
        {
            var onFulfilled = thenArgs.Count > 0 ? thenArgs[0] as IJsCallable : null;
            var onRejected = thenArgs.Count > 1 ? thenArgs[1] as IJsCallable : null;
            var result = promise.Then(onFulfilled, onRejected);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        }

        object? PromiseCatch(object? _, IReadOnlyList<object?> catchArgs)
        {
            var onRejected = catchArgs.Count > 0 ? catchArgs[0] as IJsCallable : null;
            var result = promise.Then(null, onRejected);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        }

        object? PromiseFinally(object? _, IReadOnlyList<object?> finallyArgs)
        {
            var onFinally = finallyArgs.Count > 0 ? finallyArgs[0] as IJsCallable : null;
            if (onFinally == null)
            {
                return promiseObj;
            }

            var finallyWrapper = new HostFunction(Wrapper);

            var result = promise.Then(finallyWrapper, finallyWrapper);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;

            object? Wrapper(object? __, IReadOnlyList<object?> wrapperArgs)
            {
                onFinally.Invoke([], null);
                return wrapperArgs.Count > 0 ? wrapperArgs[0] : null;
            }
        }
    }
}
