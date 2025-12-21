using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.PromiseHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Promise", ToStringTag = "Promise")]
public sealed partial class PromisePrototype
{
    [JsHostMethod("then", Length = 2d)]
    public JsValue Then(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var promise = RequirePromiseInstance(thisValue, Realm);
        var onFulfilled = args.Count > 0 && args[0].TryGetObject<IJsCallable>(out var f) ? f : null;
        var onRejected = args.Count > 1 && args[1].TryGetObject<IJsCallable>(out var r) ? r : null;

        var result = promise.Then(onFulfilled, onRejected);
        ApplyPromisePrototype(result, Realm);
        return new JsValue(result.JsObject);
    }

    [JsHostMethod("catch", Length = 1d)]
    public JsValue Catch(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var promise = RequirePromiseInstance(thisValue, Realm);
        var onRejected = args.Count > 0 && args[0].TryGetObject<IJsCallable>(out var r) ? r : null;

        var result = promise.Then(null, onRejected);
        ApplyPromisePrototype(result, Realm);
        return new JsValue(result.JsObject);
    }

    [JsHostMethod("finally", Length = 1d)]
    public JsValue Finally(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var promise = RequirePromiseInstance(thisValue, Realm);
        var onFinally = args.Count > 0 && args[0].TryGetObject<IJsCallable>(out var f) ? f : null;
        if (onFinally is null)
        {
            return thisValue;
        }

        var wrapper = new HostFunction((_, wrapperArgs) =>
        {
            onFinally.Invoke([], JsValue.Undefined);
            return wrapperArgs.GetArgument(0);
        }, Realm, isConstructor: false);

        var result = promise.Then(wrapper, wrapper);
        ApplyPromisePrototype(result, Realm);
        return new JsValue(result.JsObject);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.PromisePrototype ??= Prototype as JsObject;
    }
}
