using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Promise", PrototypeType = typeof(PromisePrototype), Length = 1d, DisplayName = "Promise")]
public sealed partial class PromiseConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is JsObject { IsConstructing: true })
        {
            var target = _constructor ?? ConstructFallback;
            return new JsValue(ConstructPromise(args, target, target));
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
            if (newTarget is not IJsCallable callable)
            {
                throw ThrowTypeError("Constructor Promise requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return ConstructPromise(args, callable, target);
        });

        AttachStatics(constructor);
    }

    private object ConstructPromise(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        if (args.Count == 0 || !args[0].IsObject || args[0].AsObject() is not IJsCallable executor)
        {
            throw ThrowTypeError("Promise constructor requires an executor function", realm: Realm);
        }

        var prototype = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var promise = CreatePromise(Realm, prototype);

        var resolve = new HostFunction((_, resolveArgs) =>
        {
            promise.Resolve(resolveArgs.GetArgument(0));
            return JsValue.Undefined;
        }, Realm, isConstructor: false);

        var reject = new HostFunction((_, rejectArgs) =>
        {
            promise.Reject(rejectArgs.GetArgument(0));
            return JsValue.Undefined;
        }, Realm, isConstructor: false);

        try
        {
            executor.Invoke([new JsValue(resolve), new JsValue(reject)], null);
        }
        catch (Exception ex)
        {
            promise.Reject(new JsValue(ex.Message));
        }

        return promise.JsObject;
    }

    private void AttachStatics(HostFunction constructor)
    {
        constructor.SetHostedProperty("resolve", (thisValue, args, _) => PromiseResolve(thisValue, args), Realm);
        constructor.SetHostedProperty("reject", (thisValue, args, _) => PromiseReject(thisValue, args), Realm);
        constructor.SetHostedProperty("all", (thisValue, args, _) => PromiseAll(thisValue, args), Realm);
        constructor.SetHostedProperty("race", (thisValue, args, _) => PromiseRace(thisValue, args), Realm);
        constructor.SetHostedProperty("allSettled", (thisValue, args, _) => PromiseAllSettled(thisValue, args), Realm);
        constructor.SetHostedProperty("any", (thisValue, args, _) => PromiseAny(thisValue, args), Realm);
    }

    private JsValue PromiseResolve(JsValue _, IReadOnlyList<JsValue> args)
    {
        var value = args.GetArgument(0);

        if (value.TryGetObject<JsObject>(out var jsObj) &&
            JsPromise.TryGetInternalPromise(jsObj, out JsPromise? _) &&
            jsObj.TryGetProperty("constructor", out var ctor) &&
            ReferenceEquals(ctor, _constructor ?? ConstructFallback))
        {
            return value;
        }

        var promise = CreatePromise(Realm);
        promise.Resolve(value);
        return new JsValue(promise.JsObject);
    }

    private JsValue PromiseReject(JsValue _, IReadOnlyList<JsValue> args)
    {
        var reason = args.GetArgument(0);
        var promise = CreatePromise(Realm);
        promise.Reject(reason);
        return new JsValue(promise.JsObject);
    }

    private JsValue PromiseAll(JsValue _, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetObject<JsArray>(out var array))
        {
            return JsValue.Undefined;
        }

        var resultPromise = CreatePromise(Realm);
        var remaining = array.Items.Count;
        var results = new JsValue[remaining];

        if (remaining == 0)
        {
            resultPromise.Resolve(new JsValue(new JsArray(Realm)));
            return new JsValue(resultPromise.JsObject);
        }

        HostFunction CreateAllResolve(int index)
        {
            JsValue Resolve(JsValue __, IReadOnlyList<JsValue> resolveArgs)
            {
                results[index] = resolveArgs.GetArgument(0);
                remaining--;
                if (remaining == 0)
                {
                    resultPromise.Resolve(new JsValue(new JsArray(results, Realm)));
                }

                return JsValue.Undefined;
            }

            return new HostFunction(Resolve, Realm, isConstructor: false);
        }

        HostFunction CreateAllReject()
        {
            JsValue Reject(JsValue __, IReadOnlyList<JsValue> rejectArgs)
            {
                resultPromise.Reject(rejectArgs.GetArgument(0));
                return JsValue.Undefined;
            }

            return new HostFunction(Reject, Realm, isConstructor: false);
        }

        for (var i = 0; i < array.Items.Count; i++)
        {
            var index = i;
            var item = array.Items[i];

            if (item.TryGetObject<JsObject>(out var itemObj) && itemObj.TryGetProperty("then", out var thenMethod) &&
                thenMethod is IJsCallable thenCallable)
            {
                thenCallable.Invoke([new JsValue(CreateAllResolve(index)), new JsValue(CreateAllReject())], itemObj);
            }
            else
            {
                results[index] = item;
                remaining--;
                if (remaining == 0)
                {
                    resultPromise.Resolve(new JsValue(new JsArray(results, Realm)));
                }
            }
        }

        return new JsValue(resultPromise.JsObject);
    }

    private JsValue PromiseRace(JsValue _, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetObject<JsArray>(out var array))
        {
            return JsValue.Undefined;
        }

        var resultPromise = CreatePromise(Realm);
        var settled = false;

        HostFunction CreateRaceResolve()
        {
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

            return new HostFunction(Resolve, Realm, isConstructor: false);
        }

        HostFunction CreateRaceReject()
        {
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

            return new HostFunction(Reject, Realm, isConstructor: false);
        }

        foreach (var item in array.Items)
        {
            if (item.TryGetObject<JsObject>(out var itemObj) && itemObj.TryGetProperty("then", out var thenMethod) &&
                thenMethod is IJsCallable thenCallable)
            {
                thenCallable.Invoke([new JsValue(CreateRaceResolve()), new JsValue(CreateRaceReject())], itemObj);
            }
            else if (!settled)
            {
                settled = true;
                resultPromise.Resolve(item);
            }
        }

        return new JsValue(resultPromise.JsObject);
    }

    private JsValue PromiseAllSettled(JsValue _, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetObject<JsArray>(out var array))
        {
            return JsValue.Undefined;
        }

        var resultPromise = CreatePromise(Realm);
        var remaining = array.Items.Count;
        var results = new JsValue[remaining];

        if (remaining == 0)
        {
            resultPromise.Resolve(new JsValue(new JsArray(Realm)));
            return new JsValue(resultPromise.JsObject);
        }

        HostFunction CreateResolve(int index)
        {
            JsValue ResolveWrapper(JsValue __, IReadOnlyList<JsValue> resolveArgs)
            {
                Resolve(index, resolveArgs.GetArgument(0), isRejected: false);
                return JsValue.Undefined;
            }

            return new HostFunction(ResolveWrapper, Realm, isConstructor: false);
        }

        HostFunction CreateReject(int index)
        {
            JsValue RejectWrapper(JsValue __, IReadOnlyList<JsValue> rejectArgs)
            {
                Resolve(index, rejectArgs.GetArgument(0), isRejected: true);
                return JsValue.Undefined;
            }

            return new HostFunction(RejectWrapper, Realm, isConstructor: false);
        }

        void Resolve(int index, JsValue value, bool isRejected)
        {
            results[index] = new JsValue(CreateAllSettledResult(value, isRejected));
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

            resultPromise.Resolve(new JsValue(resultArray));
        }

        JsObject CreateAllSettledResult(JsValue value, bool isRejected)
        {
            var result = new JsObject(Realm.ObjectPrototype) { RealmState = Realm };
            result.SetProperty("status", new JsValue(isRejected ? "rejected" : "fulfilled"));
            result.SetProperty(isRejected ? "reason" : "value", value);
            return result;
        }

        for (var i = 0; i < array.Items.Count; i++)
        {
            var index = i;
            var item = array.Items[i];
            if (item.TryGetObject<JsObject>(out var itemObj) && itemObj.TryGetProperty("then", out var thenMethod) &&
                thenMethod is IJsCallable thenCallable)
            {
                thenCallable.Invoke([new JsValue(CreateResolve(index)), new JsValue(CreateReject(index))], itemObj);
            }
            else
            {
                Resolve(index, item, isRejected: false);
            }
        }

        return new JsValue(resultPromise.JsObject);
    }

    private JsValue PromiseAny(JsValue _, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetObject<JsArray>(out var array))
        {
            return JsValue.Undefined;
        }

        var resultPromise = CreatePromise(Realm);
        var errors = new JsArray(Realm);
        var remaining = array.Items.Count;
        var resolved = false;

        if (remaining == 0)
        {
            resultPromise.Reject(new JsValue(CreateAggregateError(errors)));
            return new JsValue(resultPromise.JsObject);
        }

        HostFunction CreateResolve()
        {
            JsValue ResolveWrapper(JsValue __, IReadOnlyList<JsValue> resolveArgs)
            {
                Resolve(resolveArgs.GetArgument(0));
                return JsValue.Undefined;
            }

            return new HostFunction(ResolveWrapper, Realm, isConstructor: false);
        }

        HostFunction CreateReject()
        {
            JsValue RejectWrapper(JsValue __, IReadOnlyList<JsValue> rejectArgs)
            {
                Reject(rejectArgs.GetArgument(0));
                return JsValue.Undefined;
            }

            return new HostFunction(RejectWrapper, Realm, isConstructor: false);
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
                resultPromise.Reject(new JsValue(CreateAggregateError(errors)));
            }
        }

        for (var i = 0; i < array.Items.Count; i++)
        {
            var item = array.Items[i];
            if (item.TryGetObject<JsObject>(out var itemObj) && itemObj.TryGetProperty("then", out var thenMethod) &&
                thenMethod is IJsCallable thenCallable)
            {
                thenCallable.Invoke([new JsValue(CreateResolve()), new JsValue(CreateReject())], itemObj);
            }
            else
            {
                Resolve(item);
            }
        }

        return new JsValue(resultPromise.JsObject);
    }

    private object CreateAggregateError(JsArray rejectionErrors)
    {
        if (Realm.Engine?.GlobalObject.TryGetProperty("AggregateError", out var aggregateCtor) == true &&
            aggregateCtor is IJsCallable callable)
        {
            try
            {
                var result = callable.Invoke([new JsValue(rejectionErrors), new JsValue("All promises were rejected")], null);
                return result.IsNullish ? (object)rejectionErrors : result.AsObject() ?? rejectionErrors;
            }
            catch
            {
                // Fall through to returning the errors array
            }
        }

        return rejectionErrors;
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Promise constructor not initialized");
}
