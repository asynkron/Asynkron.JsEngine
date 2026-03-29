#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.PromiseHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// AsyncIterator prototype provides the base for async iterators.
/// Per ES spec, %AsyncIteratorPrototype%[@@asyncIterator] is a function that returns 'this'.
/// </summary>
[JsPrototype("AsyncIterator", ToStringTag = "AsyncIterator")]
public sealed partial class AsyncIteratorPrototype : JsPrototype
{
    /// <summary>
    /// %AsyncIteratorPrototype%[@@asyncIterator] returns 'this'.
    /// Per ECMAScript spec, this enables async iterators to be used with for-await-of.
    /// </summary>
    [JsSymbolMethod("asyncIterator", Length = 0d)]
    public static JsValue SelfAsyncIterator(JsValue thisValue) => thisValue;

    [JsSymbolMethod("asyncDispose", Length = 0d)]
    public JsValue AsyncDispose(JsValue thisValue)
    {
        var context = new EvaluationContext(Realm);

        if (!JsOps.TryGetPropertyValueJsValue(thisValue, new JsValue("return"), out var returnMethod, context))
        {
            return CreateResolvedPromise(JsValue.Undefined);
        }

        if (context.IsThrow)
        {
            return CreateRejectedPromise(returnMethod);
        }

        if (returnMethod.IsUndefined || returnMethod.IsNull)
        {
            return CreateResolvedPromise(JsValue.Undefined);
        }

        if (!returnMethod.TryGetCallable(out var returnCallable))
        {
            return CreateRejectedPromise(
                CreateTypeError("Async iterator return method must be callable", context, Realm));
        }

        JsValue result;
        try
        {
            result = returnCallable.Invoke([JsValue.Undefined], thisValue);
        }
        catch (ThrowSignal signal)
        {
            return CreateRejectedPromise(signal.ThrownValue);
        }

        var wrapper = CreatePromise(Realm);
        wrapper.Resolve(result);

        var outerPromise = CreatePromise(Realm);
        var onFulfilled = new HostFunction((_, _) =>
        {
            outerPromise.Resolve(JsValue.Undefined);
            return JsValue.Undefined;
        }, Realm, isConstructor: false);
        var onRejected = new HostFunction((_, args) =>
        {
            outerPromise.Reject(args.GetArgument(0));
            return JsValue.Undefined;
        }, Realm,
            isConstructor: false);
        wrapper.Then(onFulfilled, onRejected);
        return JsValue.FromObjectUnsafe(outerPromise.JsObject);
    }

    private JsValue CreateResolvedPromise(JsValue value)
    {
        var promise = CreatePromise(Realm);
        promise.Resolve(value);
        return JsValue.FromObjectUnsafe(promise.JsObject);
    }

    private JsValue CreateRejectedPromise(JsValue reason)
    {
        var promise = CreatePromise(Realm);
        promise.Reject(reason);
        return JsValue.FromObjectUnsafe(promise.JsObject);
    }
}
