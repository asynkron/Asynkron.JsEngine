using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class AsyncGeneratorInstance(
        FunctionExpression function,
        JsEnvironment closure,
        IReadOnlyList<object?> arguments,
        object? thisValue,
        IJsCallable callable,
        RealmState realmState)
    {
        private readonly TypedGeneratorInstance _inner = new(function, closure, arguments, thisValue, callable,
            realmState);

        // WAITING ON FULL ASYNC GENERATOR IR SUPPORT:
        // For now we reuse the sync generator IR plan and runtime to execute
        // the body. Async semantics are modeled by driving the shared plan
        // through a small step API and wrapping each step in a Promise. Once
        // we have a dedicated async-generator IR executor, this wiring
        // should be revisited so await/yield drive a single non-blocking
        // state machine.
        public void Initialize()
        {
            _inner.Initialize();
        }

        public JsObject CreateAsyncIteratorObject()
        {
            var asyncIterator = CreateGeneratorIteratorObject(
                args => CreateStepPromise(TypedGeneratorInstance.ResumeMode.Next,
                    args.Count > 0 ? args[0] : Symbol.Undefined),
                args => CreateStepPromise(TypedGeneratorInstance.ResumeMode.Return,
                    args.Count > 0 ? args[0] : null),
                args => CreateStepPromise(TypedGeneratorInstance.ResumeMode.Throw,
                    args.Count > 0 ? args[0] : null));

            // asyncIterator[Symbol.asyncIterator] returns itself.
            var asyncSymbol = TypedAstSymbol.For("Symbol.asyncIterator");
            var asyncKey = $"@@symbol:{asyncSymbol.GetHashCode()}";
            asyncIterator.SetProperty(asyncKey, new HostFunction((thisValue, _) => thisValue));

            return asyncIterator;
        }

        private object? CreateStepPromise(TypedGeneratorInstance.ResumeMode mode, object? argument)
        {
            // Look up the global Promise constructor from the closure environment.
            if (!closure.TryGet(Symbol.Intern("Promise"), out var promiseCtorObj) ||
                promiseCtorObj is not IJsCallable promiseCtor)
            {
                throw new InvalidOperationException("Promise constructor is not available in the current environment.");
            }

            var executor = new HostFunction((_, execArgs) =>
            {
                if (execArgs.Count < 2 ||
                    execArgs[0] is not IJsCallable resolve ||
                    execArgs[1] is not IJsCallable reject)
                {
                    return null;
                }

                // Drive the underlying generator plan by a single step and
                // resolve/reject the Promise based on the step outcome.
                var step = _inner.ExecuteAsyncStep(mode, argument);
                switch (step.Kind)
                {
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Yield:
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Completed:
                    {
                        var iteratorResult = CreateAsyncIteratorResult(step.Value, step.Done);
                        resolve.Invoke([iteratorResult], null);
                        break;
                    }
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Throw:
                        reject.Invoke([step.Value], null);
                        break;
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Pending:
                        HandlePendingStep(step, resolve, reject);
                        break;
                }

                return null;
            });

            var promiseObj = promiseCtor.Invoke([executor], null);
            return promiseObj;
        }

        private static JsObject CreateAsyncIteratorResult(object? value, bool done)
        {
            var result = new JsObject();
            result.SetProperty("value", value);
            result.SetProperty("done", done);
            return result;
        }

        private void ResolveFromStep(
            TypedGeneratorInstance.AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            switch (step.Kind)
            {
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Yield:
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Completed:
                {
                    var iteratorResult = CreateAsyncIteratorResult(step.Value, step.Done);
                    resolve.Invoke([iteratorResult], null);
                    break;
                }
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Throw:
                    reject.Invoke([step.Value], null);
                    break;
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Pending:
                    HandlePendingStep(step, resolve, reject);
                    break;
            }
        }

        private void HandlePendingStep(
            TypedGeneratorInstance.AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            if (step.PendingPromise is not JsObject pendingPromise ||
                !pendingPromise.TryGetProperty("then", out var thenValue) ||
                thenValue is not IJsCallable thenCallable)
            {
                reject.Invoke(["Awaited value is not a promise"], null);
                return;
            }

            var onFulfilled = new HostFunction(args =>
            {
                var value = args.Count > 0 ? args[0] : Symbol.Undefined;
                var resumed = _inner.ExecuteAsyncStep(TypedGeneratorInstance.ResumeMode.Next, value);
                ResolveFromStep(resumed, resolve, reject);
                return null;
            });

            var onRejected = new HostFunction(args =>
            {
                var reason = args.Count > 0 ? args[0] : Symbol.Undefined;
                var resumed = _inner.ExecuteAsyncStep(TypedGeneratorInstance.ResumeMode.Throw, reason);
                ResolveFromStep(resumed, resolve, reject);
                return null;
            });

            thenCallable.Invoke([onFulfilled, onRejected], pendingPromise);
        }
    }
}
