using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(JsObject iterator)
    {
        private object? InvokeIteratorNext(object? sendValue = null, bool hasSendValue = false)
        {
            if (!iterator.TryGetProperty("next", out var nextValue) || nextValue is not IJsCallable callable)
            {
                throw new InvalidOperationException("Iterator must expose a 'next' method.");
            }

            var args = hasSendValue ? new[] { sendValue } : Array.Empty<object?>();
            return callable.Invoke(args, iterator);
        }

        private bool TryInvokeIteratorMethod(string methodName,
            object? argument,
            EvaluationContext context,
            out object? result,
            bool hasArgument = true)
        {
            result = null;
            if (!iterator.TryGetProperty(methodName, out var methodValue))
            {
                return false;
            }

            if (methodValue is null)
            {
                return false;
            }

            if (methodValue is not IJsCallable callable)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError("Iterator method is not callable", context,
                    context.RealmState));
            }

            var args = hasArgument ? new[] { argument } : Array.Empty<object?>();
            result = callable.Invoke(args, iterator);
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

            if (!TryInvokeIteratorMethod(
                    iterator,
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
