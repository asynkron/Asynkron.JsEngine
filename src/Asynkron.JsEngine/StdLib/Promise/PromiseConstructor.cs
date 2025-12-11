using System.Collections.Generic;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Promise", PrototypeType = typeof(PromisePrototype), Length = 1d, DisplayName = "Promise")]
public sealed partial class PromiseConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is JsObject { IsConstructing: true })
        {
            var target = _constructor ?? ConstructFallback;
            return ConstructPromise(args, target, target);
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

    private object ConstructPromise(IReadOnlyList<object?> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        if (args.Count == 0 || args[0] is not IJsCallable executor)
        {
            throw ThrowTypeError("Promise constructor requires an executor function", realm: Realm);
        }

        var prototype = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var promise = CreatePromise(Realm, prototype);

        var resolve = new HostFunction((_, resolveArgs) =>
        {
            promise.Resolve(resolveArgs.GetArgument(0));
            return null;
        }, Realm, isConstructor: false);

        var reject = new HostFunction((_, rejectArgs) =>
        {
            promise.Reject(rejectArgs.GetArgument(0));
            return null;
        }, Realm, isConstructor: false);

        try
        {
            executor.Invoke([resolve, reject], null);
        }
        catch (Exception ex)
        {
            promise.Reject(ex.Message);
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

    private object? PromiseResolve(object? _, IReadOnlyList<object?> args)
    {
        var value = args.GetArgument(0);

        if (value is JsObject jsObj &&
            JsPromise.TryGetInternalPromise(jsObj, out JsPromise? _) &&
            jsObj.TryGetProperty("constructor", out var ctor) &&
            ReferenceEquals(ctor, _constructor ?? ConstructFallback))
        {
            return value;
        }

        var promise = CreatePromise(Realm);
        promise.Resolve(value);
        return promise.JsObject;
    }

    private object? PromiseReject(object? _, IReadOnlyList<object?> args)
    {
        var reason = args.GetArgument(0);
        var promise = CreatePromise(Realm);
        promise.Reject(reason);
        return promise.JsObject;
    }

    private object? PromiseAll(object? _, IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not JsArray array)
        {
            return null;
        }

        var resultPromise = CreatePromise(Realm);
        var remaining = array.Items.Count;
        var results = new object?[remaining];

        if (remaining == 0)
        {
            resultPromise.Resolve(new JsArray(Realm));
            return resultPromise.JsObject;
        }

        HostFunction CreateAllResolve(int index)
        {
            object? Resolve(object? __, IReadOnlyList<object?> resolveArgs)
            {
                results[index] = resolveArgs.GetArgument(0);
                remaining--;
                if (remaining == 0)
                {
                    resultPromise.Resolve(new JsArray(results, Realm));
                }

                return null;
            }

            return new HostFunction(Resolve, Realm, isConstructor: false);
        }

        HostFunction CreateAllReject()
        {
            object? Reject(object? __, IReadOnlyList<object?> rejectArgs)
            {
                resultPromise.Reject(rejectArgs.GetArgument(0));
                return null;
            }

            return new HostFunction(Reject, Realm, isConstructor: false);
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
                if (remaining == 0)
                {
                    resultPromise.Resolve(new JsArray(results, Realm));
                }
            }
        }

        return resultPromise.JsObject;
    }

    private object? PromiseRace(object? _, IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not JsArray array)
        {
            return null;
        }

        var resultPromise = CreatePromise(Realm);
        var settled = false;

        HostFunction CreateRaceResolve()
        {
            object? Resolve(object? __, IReadOnlyList<object?> resolveArgs)
            {
                if (settled)
                {
                    return null;
                }

                settled = true;
                resultPromise.Resolve(resolveArgs.GetArgument(0));

                return null;
            }

            return new HostFunction(Resolve, Realm, isConstructor: false);
        }

        HostFunction CreateRaceReject()
        {
            object? Reject(object? __, IReadOnlyList<object?> rejectArgs)
            {
                if (settled)
                {
                    return null;
                }

                settled = true;
                resultPromise.Reject(rejectArgs.GetArgument(0));
                return null;
            }

            return new HostFunction(Reject, Realm, isConstructor: false);
        }

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
    }

    private object? PromiseAllSettled(object? _, IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not JsArray array)
        {
            return null;
        }

        var resultPromise = CreatePromise(Realm);
        var remaining = array.Items.Count;
        var results = new object?[remaining];

        if (remaining == 0)
        {
            resultPromise.Resolve(new JsArray(Realm));
            return resultPromise.JsObject;
        }

        HostFunction CreateResolve(int index)
        {
            object? ResolveWrapper(object? __, IReadOnlyList<object?> resolveArgs)
            {
                Resolve(index, resolveArgs.GetArgument(0), isRejected: false);
                return null;
            }

            return new HostFunction(ResolveWrapper, Realm, isConstructor: false);
        }

        HostFunction CreateReject(int index)
        {
            object? RejectWrapper(object? __, IReadOnlyList<object?> rejectArgs)
            {
                Resolve(index, rejectArgs.GetArgument(0), isRejected: true);
                return null;
            }

            return new HostFunction(RejectWrapper, Realm, isConstructor: false);
        }

        void Resolve(int index, object? value, bool isRejected)
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

            resultPromise.Resolve(resultArray);
        }

        JsObject CreateAllSettledResult(object? value, bool isRejected)
        {
            var result = new JsObject(Realm.ObjectPrototype) { RealmState = Realm };
            result.SetProperty("status", isRejected ? "rejected" : "fulfilled");
            result.SetProperty(isRejected ? "reason" : "value", value);
            return result;
        }

        for (var i = 0; i < array.Items.Count; i++)
        {
            var index = i;
            var item = array.Items[i];
            if (item is JsObject itemObj && itemObj.TryGetProperty("then", out var thenMethod) &&
                thenMethod is IJsCallable thenCallable)
            {
                thenCallable.Invoke([CreateResolve(index), CreateReject(index)], itemObj);
            }
            else
            {
                Resolve(index, item, isRejected: false);
            }
        }

        return resultPromise.JsObject;
    }

    private object? PromiseAny(object? _, IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not JsArray array)
        {
            return null;
        }

        var resultPromise = CreatePromise(Realm);
        var errors = new JsArray(Realm);
        var remaining = array.Items.Count;
        var resolved = false;

        if (remaining == 0)
        {
            resultPromise.Reject(CreateAggregateError(errors));
            return resultPromise.JsObject;
        }

        HostFunction CreateResolve()
        {
            object? ResolveWrapper(object? __, IReadOnlyList<object?> resolveArgs)
            {
                Resolve(resolveArgs.GetArgument(0));
                return null;
            }

            return new HostFunction(ResolveWrapper, Realm, isConstructor: false);
        }

        HostFunction CreateReject()
        {
            object? RejectWrapper(object? __, IReadOnlyList<object?> rejectArgs)
            {
                Reject(rejectArgs.GetArgument(0));
                return null;
            }

            return new HostFunction(RejectWrapper, Realm, isConstructor: false);
        }

        void Resolve(object? value)
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            resultPromise.Resolve(value);
        }

        void Reject(object? reason)
        {
            if (resolved)
            {
                return;
            }

            errors.Push(reason);
            remaining--;
            if (remaining == 0)
            {
                resultPromise.Reject(CreateAggregateError(errors));
            }
        }

        for (var i = 0; i < array.Items.Count; i++)
        {
            var item = array.Items[i];
            if (item is JsObject itemObj && itemObj.TryGetProperty("then", out var thenMethod) &&
                thenMethod is IJsCallable thenCallable)
            {
                thenCallable.Invoke([CreateResolve(), CreateReject()], itemObj);
            }
            else
            {
                Resolve(item);
            }
        }

        return resultPromise.JsObject;
    }

    private object CreateAggregateError(JsArray rejectionErrors)
    {
        if (Realm.Engine?.GlobalObject.TryGetProperty("AggregateError", out var aggregateCtor) == true &&
            aggregateCtor is IJsCallable callable)
        {
            try
            {
                return callable.Invoke([rejectionErrors, "All promises were rejected"], null) ?? rejectionErrors;
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
