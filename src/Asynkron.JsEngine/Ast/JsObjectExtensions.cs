#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue InvokeIteratorNext(this IJsObjectLike iterator, IJsCallable nextMethod,
        JsValue sendValue = default,
        bool hasSendValue = false,
        EvaluationContext? context = null,
        JsEnvironment? callingEnvironment = null)
    {
        var thisValue = JsValue.FromObjectUnsafe(iterator);
        // Use InvokeCallableSingleArg to avoid array allocation for single-arg case
        var result = hasSendValue
            ? InvokeCallableSingleArg(nextMethod, sendValue, thisValue, context, callingEnvironment)
            : InvokeCallableJsValue(nextMethod, [], thisValue, context, callingEnvironment);

        // Check if the iterator's next() method threw an error
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return result;
    }

    private static IJsCallable GetIteratorNextCallable(this IJsObjectLike iterator, EvaluationContext? context)
    {
        // Use context-aware property access to propagate getter errors
        if (!iterator.TryGetProperty("next", out var nextValue))
        {
            throw StandardLibrary.ThrowTypeError("Iterator must expose a 'next' method.", context,
                context?.RealmState);
        }

        // Check if the getter threw an error
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (!nextValue.TryGetObject<IJsCallable>(out var callable))
        {
            throw StandardLibrary.ThrowTypeError("Iterator.next is not callable.", context, context?.RealmState);
        }

        return callable;
    }

    private static bool TryInvokeIteratorMethod(this IJsObjectLike iterator, string methodName,
        JsValue argument,
        EvaluationContext context,
        out JsValue result,
        bool hasArgument = true)
    {
        result = JsValue.Undefined;
        // Use context-aware property access to propagate getter errors
        if (!iterator.TryGetProperty(methodName, out var methodValue))
        {
            return false;
        }

        // Check if the getter threw an error
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Per GetMethod spec: if value is null or undefined, return undefined (not an error)
        if (methodValue.IsNullish)
        {
            return false;
        }

        // Only throw if the method exists but is not callable
        if (!methodValue.TryGetObject<IJsCallable>(out var callable))
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError("Iterator method is not callable", context,
                context.RealmState));
        }

        var thisValue = JsValue.FromObjectUnsafe(iterator);
        // Use InvokeCallableSingleArg to avoid array allocation for single-arg case
        result = hasArgument
            ? InvokeCallableSingleArg(callable, argument, thisValue, context)
            : InvokeCallableJsValue(callable, [], thisValue, context);

        // Check if the method threw an error
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return true;
    }

    internal static void IteratorClose(this IJsObjectLike iterator, EvaluationContext context, bool preserveExistingThrow = false)
    {
        var savedSignal = preserveExistingThrow ? context.CurrentSignal : null;
        context.RealmState.Logger?.LogInformation(
            "IteratorClose enter preserveExistingThrow={Preserve} savedSignal={SavedSignalType} savedValueType={SavedValueType}",
            preserveExistingThrow,
            savedSignal?.GetType().Name ?? "null",
            savedSignal switch
            {
                ThrowFlowCompletionSignal throwSignal => throwSignal.JsValue.GetType().Name,
                BreakCompletionSignal => "Break",
                ContinueCompletionSignal => "Continue",
                PendingAwaitCompletionSignal => "PendingAwait",
                _ => "null"
            });

        // Per ES spec 7.4.7 IteratorClose: we need to temporarily clear any existing
        // completion to invoke return(), then restore it. The existing throw state
        // must not interfere with the GetMethod/Call for return.
        if (preserveExistingThrow && context.IsThrow)
        {
            context.Clear();
        }

        bool invokeSucceeded;
        JsValue closeResult;
        try
        {
            invokeSucceeded = iterator.TryInvokeIteratorMethod(
                "return",
                new JsValue(Symbol.Undefined),
                context,
                out closeResult,
                false);
        }
        catch (ThrowSignal)
        {
            // Per ES spec 7.4.6 IteratorClose step 6: if GetMethod throws,
            // and we're preserving an existing throw, ignore the getter error
            // and restore the original completion.
            if (preserveExistingThrow)
            {
                context.RestoreSignal(savedSignal);
                return;
            }

            throw;
        }

        if (!invokeSucceeded)
        {
            if (preserveExistingThrow)
            {
                context.RestoreSignal(savedSignal);
            }

            return;
        }

        try
        {
            if (!closeResult.TryGetObject<IJsObjectLike>(out var returnObject))
            {
                context.RealmState.Logger?.LogInformation(
                    "IteratorClose return non-object preserveExistingThrow={Preserve}",
                    preserveExistingThrow);
                if (preserveExistingThrow)
                {
                    context.RestoreSignal(savedSignal);
                    return;
                }

                var typeError = StandardLibrary.CreateTypeError("Iterator.return() must return an object",
                    context, context.RealmState);
                context.RealmState.Logger?.LogInformation(
                    "IteratorClose throwing: kind={Kind}", typeError.Kind);
                // Throw the error as a ThrowSignal so callers that catch ThrowSignal can handle it
                throw new ThrowSignal(typeError);
            }

            var returnValue = JsValue.FromObjectUnsafe(returnObject);
            if (IsPromiseLike(returnValue))
            {
                AwaitScheduler.TryAwaitPromiseSync(
                    returnValue,
                    context,
                    out _,
                    context.DrainAwaitMicrotasks);
            }
        }
        catch (ThrowSignal)
        {
            context.RealmState.Logger?.LogInformation(
                "IteratorClose caught ThrowSignal preserveExistingThrow={Preserve}",
                preserveExistingThrow);
            if (preserveExistingThrow)
            {
                context.RestoreSignal(savedSignal);
                return;
            }

            throw;
        }

        if (preserveExistingThrow)
        {
            context.RestoreSignal(savedSignal);
        }

        context.RealmState.Logger?.LogInformation(
            "IteratorClose exit preserveExistingThrow={Preserve} currentSignal={SignalType} valueType={ValueType}",
            preserveExistingThrow,
            context.CurrentSignal?.GetType().Name ?? "null",
            context.FlowValue.GetType().Name);
    }

    private static void DefineAccessorProperty(this JsObject obj, string name, IJsCallable? getter, IJsCallable? setter)
    {
        var descriptor = obj.GetOwnPropertyDescriptor(name) ??
                         new PropertyDescriptor { Enumerable = true, Configurable = true };

        descriptor.Get = getter ?? descriptor.Get;
        descriptor.Set = setter ?? descriptor.Set;
        obj.DefineProperty(name, descriptor);
    }
}
