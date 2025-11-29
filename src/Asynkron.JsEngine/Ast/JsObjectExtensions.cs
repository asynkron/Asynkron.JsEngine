using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

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

        private void IteratorClose(EvaluationContext context, bool preserveExistingThrow = false)
        {
            try
            {
                if (!TryInvokeIteratorMethod(
                        iterator,
                        "return",
                        Symbol.Undefined,
                        context,
                        out var closeResult,
                        false))
                {
                    return;
                }

                if (closeResult is not JsObject returnObject)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError("Iterator.return() must return an object",
                        context, context.RealmState));
                }

                if (IsPromiseLike(returnObject))
                {
                    AwaitScheduler.TryAwaitPromiseSync(returnObject, context, out _);
                }
            }
            catch (ThrowSignal) when (preserveExistingThrow || context.IsThrow)
            {
                // Preserve the original abrupt completion per IteratorClose when completion is already throw.
            }
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
