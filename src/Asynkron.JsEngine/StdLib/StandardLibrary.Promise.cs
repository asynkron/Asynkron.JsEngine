using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    /// <summary>
    ///     Creates a Promise constructor with static methods.
    /// </summary>
    public static IJsCallable CreatePromiseConstructor(JsEngine engine)
    {
        JsObject? promisePrototype = null;
        HostFunction promiseConstructor = null!;
        promiseConstructor = new HostFunction(args => PromiseConstructor(args, promiseConstructor));
        promiseConstructor.SetInvokeWithContext((args, _, _, newTarget) =>
            PromiseConstructor(args, newTarget as IJsCallable ?? promiseConstructor));
        if (promiseConstructor.TryGetProperty("prototype", out var proto) && proto is JsObject protoObj)
        {
            promisePrototype = protoObj;
        }

        promiseConstructor.SetHostedProperty("resolve", PromiseResolve);

        promiseConstructor.SetHostedProperty("reject", PromiseReject);

        promiseConstructor.SetHostedProperty("all", PromiseAll);

        promiseConstructor.SetHostedProperty("race", PromiseRace);

        if (promisePrototype is not null)
        {
            AttachPrototypeMethods(promisePrototype);
        }

        void AssignPromisePrototype(JsObject promiseObj, IJsPropertyAccessor? overridePrototype = null)
        {
            if (overridePrototype is not null)
            {
                promiseObj.SetPrototype(overridePrototype);
            }
            else if (promisePrototype is not null)
            {
                promiseObj.SetPrototype(promisePrototype);
            }
        }

        return promiseConstructor;

        void AttachPrototypeMethods(JsObject prototype)
        {
            prototype.SetHostedProperty("then", PromisePrototypeThen);
            prototype.SetHostedProperty("catch", PromisePrototypeCatch);
            prototype.SetHostedProperty("finally", PromisePrototypeFinally);
        }

        object? PromisePrototypeThen(object? thisValue, IReadOnlyList<object?> thenArgs)
        {
            if (!JsPromise.TryGetInternalPromise(thisValue, out var internalPromise) || internalPromise is null)
            {
                throw new InvalidOperationException("Promise.then called on a non-promise value.");
            }

            var onFulfilled = thenArgs.Count > 0 ? thenArgs[0] as IJsCallable : null;
            var onRejected = thenArgs.Count > 1 ? thenArgs[1] as IJsCallable : null;
            var result = internalPromise.Then(onFulfilled, onRejected);
            AssignPromisePrototype(result.JsObject);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        }

        object? PromisePrototypeCatch(object? thisValue, IReadOnlyList<object?> catchArgs)
        {
            if (!JsPromise.TryGetInternalPromise(thisValue, out var internalPromise) || internalPromise is null)
            {
                throw new InvalidOperationException("Promise.catch called on a non-promise value.");
            }

            var onRejected = catchArgs.Count > 0 ? catchArgs[0] as IJsCallable : null;
            var result = internalPromise.Then(null, onRejected);
            AssignPromisePrototype(result.JsObject);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        }

        object? PromisePrototypeFinally(object? thisValue, IReadOnlyList<object?> finallyArgs)
        {
            if (!JsPromise.TryGetInternalPromise(thisValue, out var internalPromise) || internalPromise is null)
            {
                throw new InvalidOperationException("Promise.finally called on a non-promise value.");
            }

            var onFinally = finallyArgs.Count > 0 ? finallyArgs[0] as IJsCallable : null;
            if (onFinally is null)
            {
                return thisValue;
            }

            var finallyWrapper = new HostFunction((object? _, IReadOnlyList<object?> wrapperArgs) =>
            {
                onFinally.Invoke([], null);
                return wrapperArgs.Count > 0 ? wrapperArgs[0] : null;
            });

            var result = internalPromise.Then(finallyWrapper, finallyWrapper);
            AssignPromisePrototype(result.JsObject);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        }

        object? PromiseConstructor(IReadOnlyList<object?> args, IJsCallable newTarget)
        {
            if (args.Count == 0 || args[0] is not IJsCallable executor)
            {
                throw new InvalidOperationException("Promise constructor requires an executor function");
            }

            var promise = new JsPromise(engine);
            var promiseObj = promise.JsObject;
            var realmState = engine.RealmState;
            var proto = realmState is null
                ? promisePrototype
                : ResolveConstructPrototype(newTarget, promiseConstructor, realmState);
            AssignPromisePrototype(promiseObj, proto as IJsPropertyAccessor);
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
            AssignPromisePrototype(promise.JsObject);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);

            promise.Resolve(value);
            return promise.JsObject;
        }

        object? PromiseReject(IReadOnlyList<object?> args)
        {
            var reason = args.Count > 0 ? args[0] : null;
            var promise = new JsPromise(engine);
            AssignPromisePrototype(promise.JsObject);
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
            AssignPromisePrototype(resultPromise.JsObject);
            AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);

            var results = new object?[array.Items.Count];
            var remaining = array.Items.Count;

            if (remaining == 0)
            {
                var emptyArray = new JsArray(engine.RealmState);
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

                    var resultArray = new JsArray(engine.RealmState);
                    foreach (var result in results)
                    {
                        resultArray.Push(result);
                    }

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

                    var resultArray = new JsArray(engine.RealmState);
                    foreach (var result in results)
                    {
                        resultArray.Push(result);
                    }

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
            AssignPromisePrototype(resultPromise.JsObject);
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
        if (!promiseObj.TryGetProperty("then", out _))
        {
            promiseObj.SetHostedProperty("then", PromiseThen);
        }

        if (!promiseObj.TryGetProperty("catch", out _))
        {
            promiseObj.SetHostedProperty("catch", PromiseCatch);
        }

        if (!promiseObj.TryGetProperty("finally", out _))
        {
            promiseObj.SetHostedProperty("finally", PromiseFinally);
        }
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
