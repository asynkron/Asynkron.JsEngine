using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    /// <summary>
    ///     Creates a Promise constructor with static methods.
    /// </summary>
    public static IJsCallable CreatePromiseConstructor(JsEngine engine)
    {
        var promiseConstructor = new HostFunction((_, args) =>
        {
            // Promise constructor takes an executor function: function(resolve, reject) { ... }
            if (args.Count == 0 || args[0] is not IJsCallable executor)
            {
                throw new InvalidOperationException("Promise constructor requires an executor function");
            }

            var promise = new JsPromise(engine);
            var promiseObj = promise.JsObject;

            // Create resolve and reject callbacks
            var resolve = new HostFunction(resolveArgs =>
            {
                promise.Resolve(resolveArgs.Count > 0 ? resolveArgs[0] : null);
                return null;
            });

            var reject = new HostFunction(rejectArgs =>
            {
                promise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);
                return null;
            });

            // Add then, catch, and finally methods
            promiseObj["then"] = new HostFunction((_, thenArgs) =>
            {
                var onFulfilled = thenArgs.Count > 0 ? thenArgs[0] as IJsCallable : null;
                var onRejected = thenArgs.Count > 1 ? thenArgs[1] as IJsCallable : null;
                var resultPromise = promise.Then(onFulfilled, onRejected);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);
                return resultPromise.JsObject;
            });

            promiseObj["catch"] = new HostFunction((_, catchArgs) =>
            {
                var onRejected = catchArgs.Count > 0 ? catchArgs[0] as IJsCallable : null;
                var resultPromise = promise.Then(null, onRejected);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);
                return resultPromise.JsObject;
            });

            promiseObj["finally"] = new HostFunction((_, finallyArgs) =>
            {
                var onFinally = finallyArgs.Count > 0 ? finallyArgs[0] as IJsCallable : null;
                if (onFinally == null)
                {
                    return promiseObj;
                }

                var finallyWrapper = new HostFunction(wrapperArgs =>
                {
                    onFinally.Invoke([], null);
                    return wrapperArgs.Count > 0 ? wrapperArgs[0] : null;
                });

                var resultPromise = promise.Then(finallyWrapper, finallyWrapper);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);
                return resultPromise.JsObject;
            });

            // Execute the executor function immediately
            try
            {
                executor.Invoke([resolve, reject], null);
            }
            catch (Exception ex)
            {
                promise.Reject(ex.Message);
            }

            return promiseObj;
        });

        // Add static methods to Promise constructor
        // Promise.resolve(value)
        promiseConstructor.SetProperty("resolve", new HostFunction(args =>
        {
            var value = args.Count > 0 ? args[0] : null;
            var promise = new JsPromise(engine);

            // Add instance methods
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);

            promise.Resolve(value);
            return promise.JsObject;
        }));

        // Promise.reject(reason)
        promiseConstructor.SetProperty("reject", new HostFunction(args =>
        {
            var reason = args.Count > 0 ? args[0] : null;
            var promise = new JsPromise(engine);

            // Add instance methods
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);

            promise.Reject(reason);
            return promise.JsObject;
        }));

        // Promise.all(iterable)
        promiseConstructor.SetProperty("all", new HostFunction(args =>
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

                // Check if item is a promise (JsObject with "then" method)
                if (item is JsObject itemObj && itemObj.TryGetProperty("then", out var thenMethod) &&
                    thenMethod is IJsCallable thenCallable)
                {
                    thenCallable.Invoke([
                        new HostFunction(resolveArgs =>
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
                        }),
                        new HostFunction(rejectArgs =>
                        {
                            resultPromise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);
                            return null;
                        })
                    ], itemObj);
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
        }));

        // Promise.race(iterable)
        promiseConstructor.SetProperty("race", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsArray array)
            {
                return null;
            }

            var resultPromise = new JsPromise(engine);
            AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);

            var settled = false;

            foreach (var item in array.Items)
                // Check if item is a promise (JsObject with "then" method)
            {
                if (item is JsObject itemObj && itemObj.TryGetProperty("then", out var thenMethod) &&
                    thenMethod is IJsCallable thenCallable)
                {
                    thenCallable.Invoke([
                        new HostFunction(resolveArgs =>
                        {
                            if (settled)
                            {
                                return null;
                            }

                            settled = true;
                            resultPromise.Resolve(resolveArgs.Count > 0 ? resolveArgs[0] : null);

                            return null;
                        }),
                        new HostFunction(rejectArgs =>
                        {
                            if (settled)
                            {
                                return null;
                            }

                            settled = true;
                            resultPromise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);

                            return null;
                        })
                    ], itemObj);
                }
                else if (!settled)
                {
                    settled = true;
                    resultPromise.Resolve(item);
                }
            }

            return resultPromise.JsObject;
        }));

        return promiseConstructor;
    }

    /// <summary>
    ///     Helper method to add instance methods to a promise.
    /// </summary>
    internal static void AddPromiseInstanceMethods(JsObject promiseObj, JsPromise promise, JsEngine engine)
    {
        promiseObj["then"] = new HostFunction((_, thenArgs) =>
        {
            var onFulfilled = thenArgs.Count > 0 ? thenArgs[0] as IJsCallable : null;
            var onRejected = thenArgs.Count > 1 ? thenArgs[1] as IJsCallable : null;
            var result = promise.Then(onFulfilled, onRejected);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        });

        promiseObj["catch"] = new HostFunction((_, catchArgs) =>
        {
            var onRejected = catchArgs.Count > 0 ? catchArgs[0] as IJsCallable : null;
            var result = promise.Then(null, onRejected);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        });

        promiseObj["finally"] = new HostFunction((_, finallyArgs) =>
        {
            var onFinally = finallyArgs.Count > 0 ? finallyArgs[0] as IJsCallable : null;
            if (onFinally == null)
            {
                return promiseObj;
            }

            var finallyWrapper = new HostFunction(wrapperArgs =>
            {
                onFinally.Invoke([], null);
                return wrapperArgs.Count > 0 ? wrapperArgs[0] : null;
            });

            var result = promise.Then(finallyWrapper, finallyWrapper);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        });
    }
}
