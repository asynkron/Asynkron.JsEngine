using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(JsObject iterator)
    {
        private object? InvokeIteratorNext(object? sendValue = null, bool hasSendValue = false,
            EvaluationContext? context = null, JsEnvironment? callingEnvironment = null)
        {
            var nextCallable = iterator.GetIteratorNextCallable(context);
            return iterator.InvokeIteratorNext(nextCallable, sendValue, hasSendValue, context, callingEnvironment);
        }

        private object? InvokeIteratorNext(IJsCallable nextMethod,
            object? sendValue = null,
            bool hasSendValue = false,
            EvaluationContext? context = null,
            JsEnvironment? callingEnvironment = null)
        {
            var args = hasSendValue ? new[] { sendValue } : Array.Empty<object?>();
            return InvokeCallable(nextMethod, args, iterator, context, callingEnvironment);
        }

        private IJsCallable GetIteratorNextCallable(EvaluationContext? context)
        {
            // Use context-aware property access to propagate getter errors
            if (!iterator.TryGetProperty("next", iterator, context, out var nextValue))
            {
                throw StandardLibrary.ThrowTypeError("Iterator must expose a 'next' method.", context, context?.RealmState);
            }

            // Check if the getter threw an error
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            if (nextValue is not IJsCallable callable)
            {
                throw StandardLibrary.ThrowTypeError("Iterator.next is not callable.", context, context?.RealmState);
            }

            return callable;
        }

        private bool TryInvokeIteratorMethod(string methodName,
            object? argument,
            EvaluationContext context,
            out object? result,
            bool hasArgument = true)
        {
            result = null;
            // Use context-aware property access to propagate getter errors
            if (!iterator.TryGetProperty(methodName, iterator, context, out var methodValue))
            {
                return false;
            }

            // Check if the getter threw an error
            if (context.IsThrow)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            // Per GetMethod spec: if value is null or undefined, return undefined (not an error)
            if (methodValue is null || ReferenceEquals(methodValue, Symbol.Undefined))
            {
                return false;
            }

            // Only throw if the method exists but is not callable
            if (methodValue is not IJsCallable callable)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError("Iterator method is not callable", context,
                    context.RealmState));
            }

            var args = hasArgument ? new[] { argument } : Array.Empty<object?>();
            result = InvokeCallable(callable, args, iterator, context, iterator.RealmState?.Engine?.GlobalEnvironment);
            return true;
        }

        private void IteratorClose(EvaluationContext context, bool preserveExistingThrow = false,
            object? existingThrowOverride = null)
        {
            var savedSignal = preserveExistingThrow ? context.CurrentSignal : null;
            context.RealmState.Logger?.LogInformation(
                "IteratorClose enter preserveExistingThrow={Preserve} savedSignal={SavedSignalType} savedValueType={SavedValueType}",
                preserveExistingThrow,
                savedSignal?.GetType().Name ?? "null",
                savedSignal switch
                {
                    ThrowFlowSignal throwSignal => throwSignal.Value?.GetType().Name ?? "null",
                    ReturnSignal returnSignal => returnSignal.Value?.GetType().Name ?? "null",
                    BreakSignal => "Break",
                    ContinueSignal => "Continue",
                    _ => "null"
                });

            // Per ES spec 7.4.7 IteratorClose: we need to temporarily clear any existing
            // completion to invoke return(), then restore it. The existing throw state
            // must not interfere with the GetMethod/Call for return.
            if (preserveExistingThrow && context.IsThrow)
            {
                context.Clear();
            }

            if (!iterator.TryInvokeIteratorMethod(
                    "return",
                    Symbol.Undefined,
                    context,
                    out var closeResult,
                    false))
            {
                if (preserveExistingThrow)
                {
                    RestoreSignal(context, savedSignal);
                }

                return;
            }

            try
            {
                if (closeResult is not JsObject returnObject)
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
                    context.SetThrow(typeError);
                    context.RealmState.Logger?.LogInformation(
                        "IteratorClose set throw: type={Type}", typeError?.GetType().Name ?? "null");
                    return;
                }

                if (IsPromiseLike(returnObject))
                {
                    AwaitScheduler.TryAwaitPromiseSync(returnObject, context, out _);
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
                context.FlowValue?.GetType().Name ?? "null");
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
