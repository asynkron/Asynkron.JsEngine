#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.PromiseHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Promise", PrototypeType = typeof(PromisePrototype), Length = 1d, DisplayName = "Promise")]
public sealed partial class PromiseConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        return thisValue;
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Promise constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true })
        {
            var target = _constructor ?? ConstructFallback;
            return JsValue.FromObjectUnsafe(ConstructPromise(args, target, target));
        }

        throw ThrowTypeError("Constructor Promise requires 'new'", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.PromiseConstructor ??= constructor;
        Realm.PromisePrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!newTarget.TryGetCallable(out var callable))
            {
                throw ThrowTypeError("Constructor Promise requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return JsValue.FromObjectUnsafe(ConstructPromise(args, callable, target));
        });

        AttachStatics(constructor);
    }

    private object ConstructPromise(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        // Per ES spec 27.2.3.1 Promise(executor):
        // Step 2: If IsCallable(executor) is false, throw a TypeError exception.
        // Step 3: Let promise be OrdinaryCreateFromConstructor(...)
        // The executor callability check MUST happen before prototype resolution.
        IJsCallable? executor = null;
        if (args.Count > 0 && !args[0].TryGetCallable(out executor))
        {
            executor = null;
        }

        if (executor == null)
        {
            throw ThrowTypeError("Promise constructor requires an executor function", realm: Realm);
        }

        var promisePrototype = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var promise = CreatePromise(Realm, promisePrototype);

        var resolve = CreateResolvingFunction(promise, true, Realm);
        var reject = CreateResolvingFunction(promise, false, Realm);

        try
        {
            var executorArgs = new[] { (JsValue)resolve, (JsValue)reject };
            executor.Invoke(executorArgs, JsValue.Undefined);
        }
        catch (ThrowSignal signal)
        {
            promise.Reject(signal.ThrownValue);
        }
        catch (Exception)
        {
            promise.Reject(JsValue.Undefined);
        }

        return promise.JsObject;
    }

    /// <summary>
    /// Creates a spec-compliant Promise resolve or reject function with proper length/name properties.
    /// Per ES spec 25.4.1.3.1 (Reject) and 25.4.1.3.2 (Resolve):
    /// - length = 1
    /// - name = "" (anonymous built-in function)
    /// - not a constructor
    /// </summary>
    private static HostFunction CreateResolvingFunction(JsPromise promise, bool isResolve, RealmState realmState)
    {
        var fn = new HostFunction((_, callArgs) =>
        {
            var arg = callArgs.GetArgument(0);
            if (isResolve)
            {
                promise.Resolve(arg);
            }
            else
            {
                promise.Reject(arg);
            }
            return JsValue.Undefined;
        }, realmState, false);

        SetBuiltInFunctionProperties(fn, "", 1);
        return fn;
    }

    /// <summary>
    /// Sets the standard built-in function properties (length and name) with spec-compliant attributes.
    /// Per ES spec 17 ECMAScript Standard Built-in Objects:
    /// - length: { [[Writable]]: false, [[Enumerable]]: false, [[Configurable]]: true }
    /// - name: { [[Writable]]: false, [[Enumerable]]: false, [[Configurable]]: true }
    /// </summary>
    private static void SetBuiltInFunctionProperties(HostFunction fn, string name, int length)
    {
        fn.DefineProperty("length", new PropertyDescriptor
        {
            JsValue = new JsValue(length),
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        fn.DefineProperty("name", new PropertyDescriptor
        {
            JsValue = new JsValue(name),
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
    }

    private void AttachStatics(HostFunction constructor)
    {
        SetBuiltInStatic("resolve", 1, (thisValue, args) => PromiseResolve(thisValue, args));
        SetBuiltInStatic("reject", 1, (thisValue, args) => PromiseReject(thisValue, args));
        SetBuiltInStatic("all", 1, (thisValue, args) => PromiseAll(thisValue, args));
        SetBuiltInStatic("race", 1, (thisValue, args) => PromiseRace(thisValue, args));
        SetBuiltInStatic("allSettled", 1, (thisValue, args) => PromiseAllSettled(thisValue, args));
        SetBuiltInStatic("any", 1, (thisValue, args) => PromiseAny(thisValue, args));
        SetBuiltInStatic("withResolvers", 0, (thisValue, _) => PromiseWithResolvers(thisValue));

        void SetBuiltInStatic(string name, int length, JsHostHandler handler)
        {
            var fn = new HostFunction(handler, Realm, false);
            SetBuiltInFunctionProperties(fn, name, length);
            constructor.SetHostedProperty(name, fn, Realm);
        }
    }

    /// <summary>
    /// Implements NewPromiseCapability(C) per ES spec 27.2.1.5.
    /// Creates a new promise using the given constructor C, and returns the (promise, resolve, reject) triple.
    /// The executor function passed to C captures resolve and reject.
    /// </summary>
    private (JsValue promise, JsValue resolve, JsValue reject) NewPromiseCapability(JsValue c)
    {
        // Step 1: If IsConstructor(C) is false, throw a TypeError.
        if (!c.TryGetCallable(out var constructor) || !JsOps.IsConstructor(c))
        {
            throw ThrowTypeError("Promise constructor is not a constructor", realm: Realm);
        }

        JsValue capturedResolve = JsValue.Undefined;
        JsValue capturedReject = JsValue.Undefined;

        // Step 3: Create GetCapabilitiesExecutor function
        var executorFn = new HostFunction((_, executorArgs) =>
        {
            // Step 3.a: If resolve is not undefined, throw TypeError (called twice)
            if (!capturedResolve.IsUndefined)
            {
                throw ThrowTypeError("Promise executor already called", realm: Realm);
            }
            // Step 3.b: If reject is not undefined, throw TypeError (called twice)
            if (!capturedReject.IsUndefined)
            {
                throw ThrowTypeError("Promise executor already called", realm: Realm);
            }

            capturedResolve = executorArgs.GetArgument(0);
            capturedReject = executorArgs.GetArgument(1);
            return JsValue.Undefined;
        }, Realm, false);

        // Set proper function properties for the executor
        // Per ES spec 25.4.1.5.1: length = 2, name = ""
        SetBuiltInFunctionProperties(executorFn, "", 2);

        // Step 4: Let promise be ? Construct(C, << executor >>)
        var promiseResult = Construct(constructor, [(JsValue)executorFn], constructor, Realm);

        // Step 5-6: Validate resolve and reject are callable
        if (!capturedResolve.TryGetCallable(out _))
        {
            throw ThrowTypeError("Promise resolve is not callable", realm: Realm);
        }
        if (!capturedReject.TryGetCallable(out _))
        {
            throw ThrowTypeError("Promise reject is not callable", realm: Realm);
        }

        return (promiseResult, capturedResolve, capturedReject);
    }

    private JsValue PromiseResolve(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Per ES spec 27.2.4.7 Promise.resolve(x):
        // Step 1: Let C be the this value.
        // Step 2: If Type(C) is not Object, throw a TypeError.
        if (!thisValue.IsObject)
        {
            throw ThrowTypeError("Promise.resolve requires an object", realm: Realm);
        }

        var value = args.GetArgument(0);

        // Step 3: If IsPromise(x) is true, then
        //   a. Let xConstructor be Get(x, "constructor").
        //   b. If SameValue(xConstructor, C) is true, return x.
        if (value.TryGetObject<JsObject>(out var jsObj) &&
            JsPromise.TryGetInternalPromise(value, out _) &&
            jsObj.TryGetProperty("constructor", out var ctor))
        {
            // Check SameValue(xConstructor, C)
            if (JsOps.SameValue(ctor, thisValue))
            {
                return value;
            }
        }

        // Step 4: Let capability be ? NewPromiseCapability(C).
        // If C is the built-in Promise constructor, use the fast path
        if (thisValue.TryGetCallable(out var ctorCallable) &&
            ReferenceEquals(ctorCallable, _constructor ?? ConstructFallback))
        {
            // Fast path for built-in Promise
            var promise = CreatePromise(Realm);
            promise.Resolve(value);
            return new JsValue(promise.JsObject);
        }

        // Slow path: use NewPromiseCapability for subclass/custom constructors
        var capability = NewPromiseCapability(thisValue);
        // Step 5: Perform ? Call(capability.[[Resolve]], undefined, << x >>).
        capability.resolve.TryGetCallable(out var resolveFn);
        resolveFn!.Invoke([value], JsValue.Undefined);
        // Step 6: Return capability.[[Promise]].
        return capability.promise;
    }

    private JsValue PromiseReject(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Per ES spec 27.2.4.6 Promise.reject(r):
        // Step 1: Let C be the this value.
        // Step 2: Let capability be ? NewPromiseCapability(C).
        if (!thisValue.IsObject)
        {
            throw ThrowTypeError("Promise.reject requires an object", realm: Realm);
        }

        var reason = args.GetArgument(0);

        // Fast path for built-in Promise
        if (thisValue.TryGetCallable(out var ctorCallable) &&
            ReferenceEquals(ctorCallable, _constructor ?? ConstructFallback))
        {
            var promise = CreatePromise(Realm);
            promise.Reject(reason);
            return new JsValue(promise.JsObject);
        }

        // Slow path: use NewPromiseCapability for subclass/custom constructors
        var capability = NewPromiseCapability(thisValue);
        // Step 3: Perform ? Call(capability.[[Reject]], undefined, << r >>).
        capability.reject.TryGetCallable(out var rejectFn);
        rejectFn!.Invoke([reason], JsValue.Undefined);
        // Step 4: Return capability.[[Promise]].
        return capability.promise;
    }

    private JsValue PromiseAll(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var capability = NewPromiseCapability(thisValue);
        var operation = "Promise.all";

        IJsCallable promiseResolve;
        IJsObjectLike iterator;

        try
        {
            promiseResolve = GetPromiseResolveCallable(thisValue, operation);
            iterator = GetIterator(args.GetArgument(0), operation);
        }
        catch (ThrowSignal signal)
        {
            return RejectPromiseCapability(capability, signal.ThrownValue);
        }
        catch (Exception)
        {
            return RejectPromiseCapability(capability, JsValue.Undefined);
        }

        var results = new List<JsValue>();
        var remainingElements = 1;
        var index = 0;

        while (true)
        {
            IJsObjectLike? iteratorResult;
            try
            {
                iteratorResult = IteratorStep(iterator, operation);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapability(capability, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapability(capability, JsValue.Undefined);
            }

            if (iteratorResult is null)
            {
                break;
            }

            JsValue nextValue;
            try
            {
                nextValue = IteratorValue(iteratorResult);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapability(capability, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapability(capability, JsValue.Undefined);
            }

            EnsureCapacity(results, index);
            remainingElements++;
            var alreadyCalled = false;
            var currentIndex = index;

            var resolveElement = CreateBuiltInCallback(resolveArgs =>
            {
                if (alreadyCalled)
                {
                    return JsValue.Undefined;
                }

                alreadyCalled = true;
                results[currentIndex] = resolveArgs.GetArgument(0);
                remainingElements--;
                if (remainingElements == 0)
                {
                    return ResolveCapabilityWithArrayOrReject(capability, results);
                }

                return JsValue.Undefined;
            });

            try
            {
                var nextPromise = promiseResolve.Invoke([nextValue], thisValue);
                InvokeThen(nextPromise, resolveElement, capability.reject);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapabilityAfterIteratorClose(capability, iterator, operation, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapabilityAfterIteratorClose(capability, iterator, operation, JsValue.Undefined);
            }

            index++;
        }

        remainingElements--;
        if (remainingElements == 0)
        {
            ResolveCapabilityWithArrayOrReject(capability, results);
        }

        return capability.promise;
    }

    private JsValue PromiseRace(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var capability = NewPromiseCapability(thisValue);
        var operation = "Promise.race";

        IJsCallable promiseResolve;
        IJsObjectLike iterator;

        try
        {
            promiseResolve = GetPromiseResolveCallable(thisValue, operation);
            iterator = GetIterator(args.GetArgument(0), operation);
        }
        catch (ThrowSignal signal)
        {
            return RejectPromiseCapability(capability, signal.ThrownValue);
        }
        catch (Exception)
        {
            return RejectPromiseCapability(capability, JsValue.Undefined);
        }

        while (true)
        {
            IJsObjectLike? iteratorResult;
            try
            {
                iteratorResult = IteratorStep(iterator, operation);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapability(capability, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapability(capability, JsValue.Undefined);
            }

            if (iteratorResult is null)
            {
                break;
            }

            JsValue nextValue;
            try
            {
                nextValue = IteratorValue(iteratorResult);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapability(capability, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapability(capability, JsValue.Undefined);
            }

            try
            {
                var nextPromise = promiseResolve.Invoke([nextValue], thisValue);
                InvokeThen(nextPromise, capability.resolve, capability.reject);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapabilityAfterIteratorClose(capability, iterator, operation, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapabilityAfterIteratorClose(capability, iterator, operation, JsValue.Undefined);
            }
        }

        return capability.promise;
    }

    private JsValue PromiseAllSettled(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var capability = NewPromiseCapability(thisValue);
        var operation = "Promise.allSettled";

        IJsCallable promiseResolve;
        IJsObjectLike iterator;

        try
        {
            promiseResolve = GetPromiseResolveCallable(thisValue, operation);
            iterator = GetIterator(args.GetArgument(0), operation);
        }
        catch (ThrowSignal signal)
        {
            return RejectPromiseCapability(capability, signal.ThrownValue);
        }
        catch (Exception)
        {
            return RejectPromiseCapability(capability, JsValue.Undefined);
        }

        var results = new List<JsValue>();
        var remainingElements = 1;
        var index = 0;

        while (true)
        {
            IJsObjectLike? iteratorResult;
            try
            {
                iteratorResult = IteratorStep(iterator, operation);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapability(capability, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapability(capability, JsValue.Undefined);
            }

            if (iteratorResult is null)
            {
                break;
            }

            JsValue nextValue;
            try
            {
                nextValue = IteratorValue(iteratorResult);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapability(capability, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapability(capability, JsValue.Undefined);
            }

            EnsureCapacity(results, index);
            remainingElements++;
            var alreadyCalled = false;
            var currentIndex = index;

            var resolveElement = CreateBuiltInCallback(resolveArgs =>
            {
                if (alreadyCalled)
                {
                    return JsValue.Undefined;
                }

                alreadyCalled = true;
                results[currentIndex] = CreateAllSettledResult(resolveArgs.GetArgument(0), false);
                remainingElements--;
                if (remainingElements == 0)
                {
                    return ResolveCapabilityWithArrayOrReject(capability, results);
                }

                return JsValue.Undefined;
            });
            var rejectElement = CreateBuiltInCallback(rejectArgs =>
            {
                if (alreadyCalled)
                {
                    return JsValue.Undefined;
                }

                alreadyCalled = true;
                results[currentIndex] = CreateAllSettledResult(rejectArgs.GetArgument(0), true);
                remainingElements--;
                if (remainingElements == 0)
                {
                    return ResolveCapabilityWithArrayOrReject(capability, results);
                }

                return JsValue.Undefined;
            });

            try
            {
                var nextPromise = promiseResolve.Invoke([nextValue], thisValue);
                InvokeThen(nextPromise, resolveElement, rejectElement);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapabilityAfterIteratorClose(capability, iterator, operation, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapabilityAfterIteratorClose(capability, iterator, operation, JsValue.Undefined);
            }

            index++;
        }

        remainingElements--;
        if (remainingElements == 0)
        {
            ResolveCapabilityWithArrayOrReject(capability, results);
        }

        return capability.promise;
    }

    private JsValue PromiseAny(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var capability = NewPromiseCapability(thisValue);
        var operation = "Promise.any";

        IJsCallable promiseResolve;
        IJsObjectLike iterator;

        try
        {
            promiseResolve = GetPromiseResolveCallable(thisValue, operation);
            iterator = GetIterator(args.GetArgument(0), operation);
        }
        catch (ThrowSignal signal)
        {
            return RejectPromiseCapability(capability, signal.ThrownValue);
        }
        catch (Exception)
        {
            return RejectPromiseCapability(capability, JsValue.Undefined);
        }

        var errors = new List<JsValue>();
        var remainingElements = 1;
        var index = 0;

        while (true)
        {
            IJsObjectLike? iteratorResult;
            try
            {
                iteratorResult = IteratorStep(iterator, operation);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapability(capability, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapability(capability, JsValue.Undefined);
            }

            if (iteratorResult is null)
            {
                break;
            }

            JsValue nextValue;
            try
            {
                nextValue = IteratorValue(iteratorResult);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapability(capability, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapability(capability, JsValue.Undefined);
            }

            EnsureCapacity(errors, index);
            remainingElements++;
            var alreadyCalled = false;
            var currentIndex = index;

            var rejectElement = CreateBuiltInCallback(rejectArgs =>
            {
                if (alreadyCalled)
                {
                    return JsValue.Undefined;
                }

                alreadyCalled = true;
                errors[currentIndex] = rejectArgs.GetArgument(0);
                remainingElements--;
                if (remainingElements == 0)
                {
                    RejectCapabilityWithAggregateError(capability, errors);
                }

                return JsValue.Undefined;
            });

            try
            {
                var nextPromise = promiseResolve.Invoke([nextValue], thisValue);
                InvokeThen(nextPromise, capability.resolve, rejectElement);
            }
            catch (ThrowSignal signal)
            {
                return RejectPromiseCapabilityAfterIteratorClose(capability, iterator, operation, signal.ThrownValue);
            }
            catch (Exception)
            {
                return RejectPromiseCapabilityAfterIteratorClose(capability, iterator, operation, JsValue.Undefined);
            }

            index++;
        }

        remainingElements--;
        if (remainingElements == 0)
        {
            RejectCapabilityWithAggregateError(capability, errors);
        }

        return capability.promise;
    }

    private IJsObjectLike GetIterator(JsValue iterable, string operation)
    {
        return MapSetIterationHelper.GetIterator(iterable, Realm, operation);
    }

    private static IJsObjectLike? IteratorStep(IJsObjectLike iterator, string operation)
    {
        if (!iterator.TryGetProperty("next", out var nextValue) ||
            !nextValue.TryGetObject<IJsCallable>(out var nextMethod))
        {
            throw StandardLibrary.ThrowTypeError($"{operation} iterator must have a callable next method", null, null);
        }

        var iteratorResult = nextMethod.Invoke([], JsValue.FromObjectUnsafe(iterator));
        if (!iteratorResult.TryGetObjectLike(out var iteratorResultObject))
        {
            throw StandardLibrary.ThrowTypeError($"{operation} iterator result must be an object", null, null);
        }

        if (iteratorResultObject.TryGetProperty("done", out var doneValue) && JsOps.ToBoolean(doneValue))
        {
            return null;
        }

        return iteratorResultObject;
    }

    private static JsValue IteratorValue(IJsObjectLike iteratorResult)
    {
        return iteratorResult.TryGetProperty("value", out var currentValue)
            ? currentValue
            : JsValue.Undefined;
    }

    private IJsCallable GetPromiseResolveCallable(JsValue constructor, string operation)
    {
        if (!constructor.TryGetObjectLike(out var constructorObject))
        {
            throw ThrowTypeError($"{operation} requires a constructor object", realm: Realm);
        }

        if (!constructorObject.TryGetProperty("resolve", out var resolveValue) ||
            !resolveValue.TryGetCallable(out var resolveCallable))
        {
            throw ThrowTypeError($"{operation} resolve must be callable", realm: Realm);
        }

        return resolveCallable;
    }

    private void InvokeThen(JsValue promiseValue, JsValue onFulfilled, JsValue onRejected)
    {
        if (!promiseValue.TryGetObjectLike(out var promiseObject) ||
            !promiseObject.TryGetProperty("then", out var thenValue) ||
            !thenValue.TryGetCallable(out var thenCallable))
        {
            throw ThrowTypeError("Promise resolve result must provide a callable then", realm: Realm);
        }

        thenCallable.Invoke([onFulfilled, onRejected], promiseValue);
    }

    private JsValue RejectPromiseCapabilityAfterIteratorClose(
        (JsValue promise, JsValue resolve, JsValue reject) capability,
        IJsObjectLike iterator,
        string operation,
        JsValue reason)
    {
        try
        {
            StandardLibrary.IteratorClose(iterator, Realm, operation);
        }
        catch (Exception)
        {
            // Preserve the original abrupt completion when IteratorClose also fails.
        }

        return RejectPromiseCapability(capability, reason);
    }

    private JsValue RejectPromiseCapability((JsValue promise, JsValue resolve, JsValue reject) capability, JsValue reason)
    {
        capability.reject.TryGetCallable(out var rejectCallable);
        rejectCallable!.Invoke([reason], JsValue.Undefined);
        return capability.promise;
    }

    private void ResolveCapabilityWithArray((JsValue promise, JsValue resolve, JsValue reject) capability, List<JsValue> items)
    {
        var resultArray = new JsArray(Realm);
        foreach (var item in items)
        {
            resultArray.Push(item);
        }

        capability.resolve.TryGetCallable(out var resolveCallable);
        resolveCallable!.Invoke([JsValue.FromJsArray(resultArray)], JsValue.Undefined);
    }

    private JsValue ResolveCapabilityWithArrayOrReject(
        (JsValue promise, JsValue resolve, JsValue reject) capability,
        List<JsValue> items)
    {
        try
        {
            ResolveCapabilityWithArray(capability, items);
        }
        catch (ThrowSignal signal)
        {
            return RejectPromiseCapability(capability, signal.ThrownValue);
        }
        catch (Exception)
        {
            return RejectPromiseCapability(capability, JsValue.Undefined);
        }

        return JsValue.Undefined;
    }

    private void RejectCapabilityWithAggregateError(
        (JsValue promise, JsValue resolve, JsValue reject) capability,
        List<JsValue> errors)
    {
        var errorsArray = new JsArray(Realm);
        foreach (var error in errors)
        {
            errorsArray.Push(error);
        }

        RejectPromiseCapability(capability, JsValue.FromObjectUnsafe(CreateAggregateError(errorsArray)));
    }

    private JsValue CreateAllSettledResult(JsValue value, bool isRejected)
    {
        var result = new JsObject(Realm.ObjectPrototype) { RealmState = Realm };
        result.SetProperty("status", isRejected ? "rejected" : "fulfilled");
        result.SetProperty(isRejected ? "reason" : "value", value);
        return JsValue.FromJsObject(result);
    }

    private HostFunction CreateBuiltInCallback(Func<IReadOnlyList<JsValue>, JsValue> callback)
    {
        var fn = new HostFunction((_, callbackArgs) => callback(callbackArgs), Realm, false);
        SetBuiltInFunctionProperties(fn, "", 1);
        return fn;
    }

    private static void EnsureCapacity(List<JsValue> items, int index)
    {
        while (items.Count <= index)
        {
            items.Add(JsValue.Undefined);
        }
    }

    private object CreateAggregateError(JsArray rejectionErrors)
    {
        JsObject? aggregateErrorObject = null;

        if (Realm.Engine?.GlobalObject.TryGetProperty("AggregateError", out var aggregateCtor) == true &&
            // aggregateCtor is already JsValue from TryGetProperty
            aggregateCtor.TryGetCallable(out var callable))
        {
            try
            {
                var args = new[]
                {
                    JsValue.FromJsArray(rejectionErrors), new JsValue("All promises were rejected")
                };
                var result = callable.Invoke(args, JsValue.Undefined);
                if (result.TryGetObject<JsObject>(out var resultObject))
                {
                    aggregateErrorObject = resultObject;
                }
            }
            catch
            {
                // Fall through to a local AggregateError-shaped object.
            }
        }

        aggregateErrorObject ??= new JsObject(Realm.ErrorPrototype) { RealmState = Realm };
        aggregateErrorObject.SetProperty("name", "AggregateError");
        aggregateErrorObject.SetProperty("message", "All promises were rejected");
        aggregateErrorObject.DefineProperty("errors", new PropertyDescriptor
        {
            Value = JsValue.FromJsArray(rejectionErrors),
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        return aggregateErrorObject;
    }

    private JsValue PromiseWithResolvers(JsValue thisValue)
    {
        // Per ES spec 27.2.4.8 Promise.withResolvers():
        // Step 1: Let C be the this value.
        // Step 2: Let capability be ? NewPromiseCapability(C).

        // Fast path for built-in Promise
        if (thisValue.TryGetCallable(out var ctorCallable) &&
            ReferenceEquals(ctorCallable, _constructor ?? ConstructFallback))
        {
            var promise = CreatePromise(Realm, Realm.PromisePrototype);
            var resolve = CreateResolvingFunction(promise, true, Realm);
            var reject = CreateResolvingFunction(promise, false, Realm);

            var result = new JsObject { RealmState = Realm };
            result.SetProperty("promise", JsValue.FromJsPromise(promise));
            result.SetProperty("resolve", JsValue.FromObjectUnsafe(resolve));
            result.SetProperty("reject", JsValue.FromObjectUnsafe(reject));
            return JsValue.FromJsObject(result);
        }

        // Slow path: use NewPromiseCapability for subclass/custom constructors
        var capability = NewPromiseCapability(thisValue);
        var resultObj = new JsObject { RealmState = Realm };
        resultObj.SetProperty("promise", capability.promise);
        resultObj.SetProperty("resolve", capability.resolve);
        resultObj.SetProperty("reject", capability.reject);
        return JsValue.FromJsObject(resultObj);
    }

    /// <summary>
    /// Checks if a value is a thenable (has a callable "then" method).
    /// </summary>
    private static bool TryGetThenMethod(
        JsValue item,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IJsCallable? thenCallable)
    {
        thenCallable = null;
        return item.TryGetObject<JsObject>(out var itemObj) &&
               itemObj.TryGetProperty("then", out var thenMethod) &&
               thenMethod.TryGetCallable(out thenCallable);
    }
}
