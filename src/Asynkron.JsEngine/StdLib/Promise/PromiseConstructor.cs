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

    /// <summary>
    /// Validates args and extracts the array for Promise.all/race/allSettled/any.
    /// Returns false if args are invalid.
    /// </summary>
    private bool TryGetPromiseIterableArray(IReadOnlyList<JsValue> args, out JsArray array)
    {
        if (args.Count == 0 || !args[0].TryGetArray(out array!))
        {
            array = null!;
            return false;
        }
        return true;
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
        constructor.SetHostedProperty("resolve", (thisValue, args, _) => PromiseResolve(thisValue, args), Realm);
        constructor.SetHostedProperty("reject", (thisValue, args, _) => PromiseReject(thisValue, args), Realm);
        constructor.SetHostedProperty("all", (thisValue, args, _) => PromiseAll(thisValue, args), Realm);
        constructor.SetHostedProperty("race", (thisValue, args, _) => PromiseRace(thisValue, args), Realm);
        constructor.SetHostedProperty("allSettled", (thisValue, args, _) => PromiseAllSettled(thisValue, args), Realm);
        constructor.SetHostedProperty("any", (thisValue, args, _) => PromiseAny(thisValue, args), Realm);
        constructor.SetHostedProperty("withResolvers", (thisValue, _, _) => PromiseWithResolvers(thisValue), Realm);
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
        if (!TryGetPromiseIterableArray(args, out var array))
        {
            return JsValue.Undefined;
        }

        var resultPromise = CreatePromise(Realm);
        var remaining = array.Items.Count;
        var results = new object?[remaining];

        if (remaining == 0)
        {
            resultPromise.Resolve(JsValue.FromJsArray(new JsArray(Realm)));
            return new JsValue(resultPromise.JsObject);
        }

        for (var i = 0; i < array.Items.Count; i++)
        {
            // Handle case where item is already a boxed JsValue
            var rawItem = array.Items[i];

            if (TryGetThenMethod(rawItem, out var thenCallable))
            {
                var thenArgs = new[] { (JsValue)CreateAllResolve(i), (JsValue)CreateAllReject() };
                thenCallable.Invoke(thenArgs, rawItem);
            }
            else
            {
                results[i] = rawItem;
                remaining--;
                if (remaining == 0)
                {
                    resultPromise.Resolve(JsValue.FromJsArray(new JsArray(results, Realm)));
                }
            }
        }

        return new JsValue(resultPromise.JsObject);

        HostFunction CreateAllResolve(int index)
        {
            return new HostFunction(Resolve, Realm, false);

            JsValue Resolve(JsValue __, IReadOnlyList<JsValue> resolveArgs)
            {
                results[index] = resolveArgs.GetArgument(0);
                remaining--;
                if (remaining == 0)
                {
                    resultPromise.Resolve(JsValue.FromJsArray(new JsArray(results, Realm)));
                }

                return JsValue.Undefined;
            }
        }

        HostFunction CreateAllReject()
        {
            return new HostFunction(Reject, Realm, false);

            JsValue Reject(JsValue __, IReadOnlyList<JsValue> rejectArgs)
            {
                resultPromise.Reject(rejectArgs.GetArgument(0));
                return JsValue.Undefined;
            }
        }
    }

    private JsValue PromiseRace(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!TryGetPromiseIterableArray(args, out var array))
        {
            return JsValue.Undefined;
        }

        var resultPromise = CreatePromise(Realm);
        var settled = false;

        foreach (var item in array.Items)
        {
            if (TryGetThenMethod(item, out var thenCallable))
            {
                var thenArgs = new[] { (JsValue)CreateRaceResolve(), (JsValue)CreateRaceReject() };
                thenCallable.Invoke(thenArgs, item);
            }
            else if (!settled)
            {
                settled = true;
                resultPromise.Resolve(item);
            }
        }

        return new JsValue(resultPromise.JsObject);

        HostFunction CreateRaceResolve()
        {
            return new HostFunction(Resolve, Realm, false);

            JsValue Resolve(JsValue __, IReadOnlyList<JsValue> resolveArgs)
            {
                if (settled)
                {
                    return JsValue.Undefined;
                }

                settled = true;
                resultPromise.Resolve(resolveArgs.GetArgument(0));

                return JsValue.Undefined;
            }
        }

        HostFunction CreateRaceReject()
        {
            return new HostFunction(Reject, Realm, false);

            JsValue Reject(JsValue __, IReadOnlyList<JsValue> rejectArgs)
            {
                if (settled)
                {
                    return JsValue.Undefined;
                }

                settled = true;
                resultPromise.Reject(rejectArgs.GetArgument(0));
                return JsValue.Undefined;
            }
        }
    }

    private JsValue PromiseAllSettled(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!TryGetPromiseIterableArray(args, out var array))
        {
            return JsValue.Undefined;
        }

        var resultPromise = CreatePromise(Realm);
        var remaining = array.Items.Count;
        var results = new object?[remaining];

        if (remaining == 0)
        {
            resultPromise.Resolve(JsValue.FromJsArray(new JsArray(Realm)));
            return new JsValue(resultPromise.JsObject);
        }

        IteratePromiseArray(
            array,
            index => (JsValue)CreateResolve(index),
            index => (JsValue)CreateReject(index),
            (index, item) => Resolve(index, item, false));

        return new JsValue(resultPromise.JsObject);

        HostFunction CreateResolve(int index)
        {
            return new HostFunction(ResolveWrapper, Realm, false);

            JsValue ResolveWrapper(JsValue __, IReadOnlyList<JsValue> resolveArgs)
            {
                Resolve(index, resolveArgs.GetArgument(0), false);
                return JsValue.Undefined;
            }
        }

        HostFunction CreateReject(int index)
        {
            return new HostFunction(RejectWrapper, Realm, false);

            JsValue RejectWrapper(JsValue __, IReadOnlyList<JsValue> rejectArgs)
            {
                Resolve(index, rejectArgs.GetArgument(0), true);
                return JsValue.Undefined;
            }
        }

        void Resolve(int index, JsValue value, bool isRejected)
        {
            results[index] = CreateAllSettledResult(value, isRejected);
            remaining--;
            if (remaining != 0)
            {
                return;
            }

            var resultArray = new JsArray(Realm);
            foreach (var result in results)
            {
                resultArray.Push(result);
            }

            resultPromise.Resolve(JsValue.FromJsArray(resultArray));
        }

        JsObject CreateAllSettledResult(JsValue value, bool isRejected)
        {
            var result = new JsObject(Realm.ObjectPrototype) { RealmState = Realm };
            result.SetProperty("status", isRejected ? "rejected" : "fulfilled");
            result.SetProperty(isRejected ? "reason" : "value", value);
            return result;
        }
    }

    private JsValue PromiseAny(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!TryGetPromiseIterableArray(args, out var array))
        {
            return JsValue.Undefined;
        }

        var resultPromise = CreatePromise(Realm);
        var errors = new JsArray(Realm);
        var remaining = array.Items.Count;
        var resolved = false;

        if (remaining == 0)
        {
            resultPromise.Reject(JsValue.FromObjectUnsafe(CreateAggregateError(errors)));
            return new JsValue(resultPromise.JsObject);
        }

        IteratePromiseArray(
            array,
            _ => (JsValue)CreateResolve(),
            _ => (JsValue)CreateReject(),
            (_, item) => Resolve(item));

        return new JsValue(resultPromise.JsObject);

        HostFunction CreateResolve()
        {
            return new HostFunction(ResolveWrapper, Realm, false);

            JsValue ResolveWrapper(JsValue __, IReadOnlyList<JsValue> resolveArgs)
            {
                Resolve(resolveArgs.GetArgument(0));
                return JsValue.Undefined;
            }
        }

        HostFunction CreateReject()
        {
            return new HostFunction(RejectWrapper, Realm, false);

            JsValue RejectWrapper(JsValue __, IReadOnlyList<JsValue> rejectArgs)
            {
                Reject(rejectArgs.GetArgument(0));
                return JsValue.Undefined;
            }
        }

        void Resolve(JsValue value)
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            resultPromise.Resolve(value);
        }

        void Reject(JsValue reason)
        {
            if (resolved)
            {
                return;
            }

            errors.Push(reason);
            remaining--;
            if (remaining == 0)
            {
                resultPromise.Reject(JsValue.FromObjectUnsafe(CreateAggregateError(errors)));
            }
        }
    }

    private static void IteratePromiseArray(
        JsArray array,
        Func<int, JsValue> createResolve,
        Func<int, JsValue> createReject,
        Action<int, JsValue> resolveDirect)
    {
        var items = array.Items;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (TryGetThenMethod(item, out var thenCallable))
            {
                var thenArgs = new[] { createResolve(index), createReject(index) };
                thenCallable.Invoke(thenArgs, item);
            }
            else
            {
                resolveDirect(index, item);
            }
        }
    }

    private object CreateAggregateError(JsArray rejectionErrors)
    {
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
                return result.IsNullish ? rejectionErrors : (object?)result.AsObject() ?? rejectionErrors;
            }
            catch
            {
                // Fall through to returning the errors array
            }
        }

        return rejectionErrors;
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
