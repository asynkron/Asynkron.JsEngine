using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IJsObjectLike iterator)
    {
        private JsValue InvokeIteratorNext(JsValue sendValue = default, bool hasSendValue = false,
            EvaluationContext? context = null, JsEnvironment? callingEnvironment = null)
        {
            var nextCallable = iterator.GetIteratorNextCallable(context);
            return iterator.InvokeIteratorNext(nextCallable, sendValue, hasSendValue, context, callingEnvironment);
        }

        private JsValue InvokeIteratorNext(IJsCallable nextMethod,
            JsValue sendValue = default,
            bool hasSendValue = false,
            EvaluationContext? context = null,
            JsEnvironment? callingEnvironment = null)
        {
            var args = hasSendValue ? new[] { sendValue } : Array.Empty<JsValue>();
            // Use InvokeCallable to properly handle HostFunction.InvokeWithContext
            // which is required for array iterators that use SetInvokeWithContext
            var result = InvokeCallable(nextMethod, args, new JsValue((JsObject)iterator), context, callingEnvironment);
            return JsValue.FromObject(result);
        }

        private IJsCallable GetIteratorNextCallable(EvaluationContext? context)
        {
            // Use context-aware property access to propagate getter errors
            if (!iterator.TryGetProperty("next", out var nextValue))
            {
                throw StandardLibrary.ThrowTypeError("Iterator must expose a 'next' method.", context, context?.RealmState);
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

        private bool TryInvokeIteratorMethod(string methodName,
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
                throw new ThrowSignal(JsValue.FromObject(StandardLibrary.CreateTypeError("Iterator method is not callable", context,
                    context.RealmState)));
            }

            var args = hasArgument ? new[] { argument } : Array.Empty<JsValue>();
            // Use InvokeCallable to properly handle HostFunction.InvokeWithContext
            var invokeResult = InvokeCallable(callable, args, JsValue.FromObject((JsObject)iterator), context, null);
            result = JsValue.FromObject(invokeResult);

            // Check if the method threw an error
            if (context.IsThrow)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            return true;
        }

        private void IteratorClose(EvaluationContext context, bool preserveExistingThrow = false,
            JsValue existingThrowOverride = default)
        {
            var savedSignal = preserveExistingThrow ? context.CurrentSignal : null;
            context.RealmState.Logger?.LogInformation(
                "IteratorClose enter preserveExistingThrow={Preserve} savedSignal={SavedSignalType} savedValueType={SavedValueType}",
                preserveExistingThrow,
                savedSignal?.GetType().Name ?? "null",
                savedSignal switch
                {
                    ThrowFlowCompletionSignal throwSignal => throwSignal.JsValue.GetType().Name,
                    ReturnCompletionSignal returnSignal => returnSignal.JsValue.GetType().Name,
                    BreakCompletionSignal => "Break",
                    ContinueCompletionSignal => "Continue",
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
            JsValue closeResult = JsValue.Undefined;
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
                    RestoreSignal(context, savedSignal);
                    return;
                }
                throw;
            }

            if (!invokeSucceeded)
            {
                if (preserveExistingThrow)
                {
                    RestoreSignal(context, savedSignal);
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
                        RestoreSignal(context, savedSignal);
                        return;
                    }

                    var typeError = StandardLibrary.CreateTypeError("Iterator.return() must return an object",
                        context, context.RealmState);
                    context.SetThrow(JsValue.FromObject(typeError));
                    context.RealmState.Logger?.LogInformation(
                        "IteratorClose set throw: type={Type}", typeError?.GetType().Name ?? "null");
                    return;
                }

                if (IsPromiseLike(new JsValue((JsObject)returnObject)))
                {
                    AwaitScheduler.TryAwaitPromiseSync(
                        new JsValue((JsObject)returnObject),
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
                    RestoreSignal(context, savedSignal);
                    return;
                }

                throw;
            }

            if (preserveExistingThrow)
            {
                RestoreSignal(context, savedSignal);
            }

            context.RealmState.Logger?.LogInformation(
                "IteratorClose exit preserveExistingThrow={Preserve} currentSignal={SignalType} valueType={ValueType}",
                preserveExistingThrow,
                context.CurrentSignal?.GetType().Name ?? "null",
                context.FlowValue.GetType().Name);
        }
    }


    extension(JsObject obj)
    {
        private void DefineAccessorProperty(string name, IJsCallable? getter, IJsCallable? setter)
        {
            var descriptor = obj.GetOwnPropertyDescriptor(name) ??
                             new PropertyDescriptor { Enumerable = true, Configurable = true };

            descriptor.Get = getter ?? descriptor.Get;
            descriptor.Set = setter ?? descriptor.Set;
            obj.DefineProperty(name, descriptor);
        }
    }
}
