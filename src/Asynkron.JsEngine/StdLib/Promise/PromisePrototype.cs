#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.PromiseHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Promise", ToStringTag = "Promise", InstanceType = typeof(JsPromise), TryGetMethod = "TryGetInternalPromise")]
public sealed partial class PromisePrototype
{
    [JsHostMethod("then", Length = 2d)]
    public JsValue Then(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var promise = RequireInstance(thisValue);
        var onFulfilled = args.Count > 0 && args[0].TryGetObject<IJsCallable>(out var f) ? f : null;
        var onRejected = args.Count > 1 && args[1].TryGetObject<IJsCallable>(out var r) ? r : null;
        var constructor = GetSpeciesConstructor(thisValue);
        var capability = NewPromiseCapability(constructor);

        capability.resolve.TryGetCallable(out var resolve);
        capability.reject.TryGetCallable(out var reject);
        promise.PerformThen(onFulfilled, onRejected, resolve!, reject!);
        return capability.promise;
    }

    [JsHostMethod("catch", Length = 1d)]
    public JsValue Catch(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return InvokeThenMethod(thisValue, JsValue.Undefined, args.GetArgument(0));
    }

    [JsHostMethod("finally", Length = 1d)]
    public JsValue Finally(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var onFinally = args.Count > 0 && args[0].TryGetObject<IJsCallable>(out var f) ? f : null;
        if (onFinally is null)
        {
            var passThrough = args.GetArgument(0);
            return InvokeThenMethod(thisValue, passThrough, passThrough);
        }

        var constructor = GetSpeciesConstructor(thisValue);
        var thenFinally = new HostFunction((_, wrapperArgs) =>
        {
            var value = wrapperArgs.GetArgument(0);
            var result = onFinally.Invoke([], JsValue.Undefined);
            var promise = CallPromiseResolve(constructor, result);

            var valueThunk = new HostFunction((_, _) => value, Realm, false);
            SetBuiltInFunctionProperties(valueThunk, "", 1);
            return InvokeThen(promise, valueThunk, JsValue.Undefined);
        }, Realm, false);
        SetBuiltInFunctionProperties(thenFinally, "", 1);

        var catchFinally = new HostFunction((_, wrapperArgs) =>
        {
            var reason = wrapperArgs.GetArgument(0);
            var result = onFinally.Invoke([], JsValue.Undefined);
            var promise = CallPromiseResolve(constructor, result);

            var thrower = new HostFunction((_, _) => throw new ThrowSignal(reason), Realm, false);
            SetBuiltInFunctionProperties(thrower, "", 1);
            return InvokeThen(promise, thrower, JsValue.Undefined);
        }, Realm, false);
        SetBuiltInFunctionProperties(catchFinally, "", 1);

        return InvokeThenMethod(
            thisValue,
            JsValue.FromObjectUnsafe(thenFinally),
            JsValue.FromObjectUnsafe(catchFinally));
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.PromisePrototype ??= Prototype as JsObject;
    }

    private JsValue GetSpeciesConstructor(JsValue promiseValue)
    {
        var defaultConstructor = Realm.PromiseConstructor is not null
            ? JsValue.FromObjectUnsafe(Realm.PromiseConstructor)
            : throw new InvalidOperationException("Promise constructor not initialized.");

        var constructorValue = GetPropertyOrThrow(promiseValue, "constructor");
        if (constructorValue.IsUndefined)
        {
            return defaultConstructor;
        }

        if (!constructorValue.IsObject)
        {
            throw ThrowTypeError("Promise constructor must be an object", realm: Realm);
        }

        var speciesValue = GetPropertyOrThrow(constructorValue, SymbolKeys.Species);
        if (speciesValue.IsNullOrUndefined)
        {
            return defaultConstructor;
        }

        constructorValue = speciesValue;

        if (!JsOps.IsConstructor(constructorValue))
        {
            throw ThrowTypeError("Promise species constructor must be a constructor", realm: Realm);
        }

        return constructorValue;
    }

    private (JsValue promise, JsValue resolve, JsValue reject) NewPromiseCapability(JsValue constructorValue)
    {
        if (!constructorValue.TryGetCallable(out var constructor) || !JsOps.IsConstructor(constructorValue))
        {
            throw ThrowTypeError("Promise constructor is not a constructor", realm: Realm);
        }

        JsValue capturedResolve = JsValue.Undefined;
        JsValue capturedReject = JsValue.Undefined;

        var executorFn = new HostFunction((_, executorArgs) =>
        {
            if (!capturedResolve.IsUndefined || !capturedReject.IsUndefined)
            {
                throw ThrowTypeError("Promise executor already called", realm: Realm);
            }

            capturedResolve = executorArgs.GetArgument(0);
            capturedReject = executorArgs.GetArgument(1);
            return JsValue.Undefined;
        }, Realm, false);

        SetBuiltInFunctionProperties(executorFn, "", 2);

        var promise = Construct(constructor, [JsValue.FromObjectUnsafe(executorFn)], constructor, Realm);
        if (!capturedResolve.TryGetCallable(out _) || !capturedReject.TryGetCallable(out _))
        {
            throw ThrowTypeError("Promise capability executor did not capture callable resolve/reject", realm: Realm);
        }

        return (promise, capturedResolve, capturedReject);
    }

    private JsValue CallPromiseResolve(JsValue constructorValue, JsValue value)
    {
        if (!constructorValue.TryGetObjectLike(out var constructorObject) ||
            !constructorObject.TryGetProperty("resolve", out var resolveValue) ||
            !resolveValue.TryGetCallable(out var resolveCallable))
        {
            throw ThrowTypeError("Promise resolve must be callable", realm: Realm);
        }

        return resolveCallable.Invoke([value], constructorValue);
    }

    private JsValue InvokeThen(JsValue promiseValue, JsValue onFulfilled, JsValue onRejected)
    {
        return InvokeThenMethod(promiseValue, onFulfilled, onRejected);
    }

    private JsValue InvokeThenMethod(JsValue receiver, JsValue onFulfilled, JsValue onRejected)
    {
        var thenValue = GetPropertyOrThrow(receiver, "then");
        if (!thenValue.TryGetCallable(out var thenCallable))
        {
            throw ThrowTypeError("Promise receiver must provide a callable then method", realm: Realm);
        }

        return thenCallable.Invoke([onFulfilled, onRejected], receiver);
    }

    private JsValue GetPropertyOrThrow(JsValue target, string propertyName)
    {
        var context = new EvaluationContext(Realm);
        var found = JsOps.TryGetPropertyValue(target, propertyName, out var value, context);
        if (context.IsThrow)
        {
            throw new ThrowSignal(value);
        }

        return found ? value : JsValue.Undefined;
    }

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
}
