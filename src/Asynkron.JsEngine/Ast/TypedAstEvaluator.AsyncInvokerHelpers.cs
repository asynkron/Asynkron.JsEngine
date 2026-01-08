
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Validates a pending step and extracts the 'then' method from the pending promise.
    /// Returns false and invokes reject if validation fails.
    /// Shared by AsyncFunctionInvoker and AsyncGeneratorInvoker.
    /// </summary>
    private static bool TryGetPendingThenMethod(
        ExecutionPlanRunner.AsyncGeneratorStepResult step,
        IJsCallable reject,
        out IJsCallable thenCallable)
    {
        thenCallable = null!;

        if (!step.PendingPromise.TryGetPropertyAccessor(out var pendingPromise))
        {
            AsyncInvokeWithOneArg(reject, (JsValue)"Awaited value is not a promise");
            return false;
        }

        if (!pendingPromise.TryGetProperty("then", out var thenValue))
        {
            AsyncInvokeWithOneArg(reject, (JsValue)"Awaited value has no 'then' method");
            return false;
        }

        if (!thenValue.TryUnwrap(out IJsCallable? callable))
        {
            AsyncInvokeWithOneArg(reject, (JsValue)"'then' is not callable");
            return false;
        }

        thenCallable = callable;
        return true;
    }

    /// <summary>
    /// Invokes a callable with a single argument. Used for resolve/reject calls.
    /// </summary>
    private static void AsyncInvokeWithOneArg(IJsCallable callable, JsValue arg0)
    {
        var args = JsValueCache.RentJsValueArray(1);
        try
        {
            args[0] = arg0;
            callable.Invoke(args, JsValue.Undefined);
        }
        finally
        {
            JsValueCache.ReturnJsValueArray(args);
        }
    }

    /// <summary>
    /// Invokes a callable with two arguments. Used for then(onFulfilled, onRejected) calls.
    /// </summary>
    private static void AsyncInvokeWithTwoArgs(IJsCallable callable, JsValue arg0, JsValue arg1, JsValue thisValue)
    {
        var args = JsValueCache.RentJsValueArray(2);
        try
        {
            args[0] = arg0;
            args[1] = arg1;
            callable.Invoke(args, thisValue);
        }
        finally
        {
            JsValueCache.ReturnJsValueArray(args);
        }
    }

    /// <summary>
    /// Sets the name property on anonymous functions for proper display names.
    /// Shared by ClassFieldInitializer and SyncFunctionInvoker.
    /// </summary>
    private static void SetAnonymousFunctionName(JsValue value, string displayName)
    {
        switch (value.ObjectValue)
        {
            case SyncFunctionInvoker typedFunction:
                typedFunction.EnsureHasName(displayName, true);
                break;
            case SyncGeneratorInvoker generatorFactory:
                generatorFactory.EnsureHasName(displayName, true);
                break;
            case AsyncGeneratorFunctionInvoker asyncGeneratorFactory:
                asyncGeneratorFactory.EnsureHasName(displayName, true);
                break;
        }
    }
}
