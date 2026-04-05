#region

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Runtime;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static bool TryGetExecutorCallbacks(
        IReadOnlyList<JsValue> execArgs,
        [NotNullWhen(true)] out IJsCallable? resolve,
        [NotNullWhen(true)] out IJsCallable? reject)
    {
        resolve = execArgs.GetOrDefault<IJsCallable>(0);
        reject = execArgs.GetOrDefault<IJsCallable>(1);
        return resolve is not null && reject is not null;
    }

    private sealed class AsyncGeneratorInvoker(
        FunctionExpression function,
        JsEnvironment closure,
        IReadOnlyList<JsValue> arguments,
        JsValue thisValue,
        IJsCallable callable,
        RealmState realmState,
        bool isLexicallyStrict,
        bool hasFunctionNameEnvironment,
        IJsObjectLike? homeObject,
        PrivateNameScope? privateNameScope,
        ImmutableArray<PrivateNameScope> capturedPrivateNameScopes,
        FunctionExecutionPlanSeed planSeed)
    {
        private readonly ExecutionPlanRunner _inner = new(function, closure, arguments, thisValue, callable,
            realmState, isLexicallyStrict, hasFunctionNameEnvironment, homeObject, privateNameScope,
            capturedPrivateNameScopes,
            planOverride: planSeed.Plan,
            planFailureOverride: planSeed.Failure);

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
            var prototype = ResolveGeneratorPrototype();
            var asyncIterator = CreateGeneratorIteratorObject(
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return CreateStepPromise(ExecutionPlanRunner.ResumeMode.Next, argValue);
                },
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return CreateStepPromise(ExecutionPlanRunner.ResumeMode.Return, argValue);
                },
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return CreateStepPromise(ExecutionPlanRunner.ResumeMode.Throw, argValue);
                },
                prototype);

            // asyncIterator[Symbol.asyncIterator] returns itself.
            var asyncKey = SymbolKeys.AsyncIterator;
            asyncIterator.SetProperty(asyncKey, (JsValue)new HostFunction((thisValue, _) => thisValue));

            return asyncIterator;
        }

        private JsValue CreateStepPromise(ExecutionPlanRunner.ResumeMode mode, JsValue argument)
        {
            // Look up the global Promise constructor from the closure environment.
            IJsCallable? promiseCtor = null;
            if (closure.TryGetObject<IJsCallable>(Symbol.PromiseIdentifier, out var promiseFromEnv))
            {
                promiseCtor = promiseFromEnv;
            }
            else if (realmState.PromiseConstructor is { } promiseFromRealm)
            {
                promiseCtor = promiseFromRealm;
            }

            if (promiseCtor is null)
            {
                throw new InvalidOperationException("Promise constructor is not available in the current environment.");
            }

            var executor = new HostFunction((_, execArgs) =>
            {
                if (!TryGetExecutorCallbacks(execArgs, out var resolve, out var reject))
                {
                    return JsValue.Undefined;
                }

                // Drive the underlying generator plan by a single step and
                // resolve/reject the Promise based on the step outcome.
                var step = _inner.ExecuteAsyncStep(mode, argument);
                switch (step.Kind)
                {
                    case ExecutionPlanRunner.AsyncGeneratorStepKind.Yield:
                    case ExecutionPlanRunner.AsyncGeneratorStepKind.Completed:
                        {
                            var iteratorResult = CreateAsyncIteratorResult(step.Value, step.Done);
                            resolve.Invoke(new SingleValueArgs((JsValue)iteratorResult), JsValue.Undefined);
                            break;
                        }
                    case ExecutionPlanRunner.AsyncGeneratorStepKind.Throw:
                        // step.Value is already JsValue
                        reject.Invoke(new SingleValueArgs(step.Value), JsValue.Undefined);
                        break;
                    case ExecutionPlanRunner.AsyncGeneratorStepKind.Pending:
                        HandlePendingStep(step, resolve, reject);
                        break;
                }

                return JsValue.Undefined;
            });

            if (promiseCtor is HostFunction hostCtor)
            {
                return hostCtor.InvokeWithContext([(JsValue)executor], JsValue.Undefined, null, (JsValue)hostCtor);
            }

            return promiseCtor.Invoke(new SingleValueArgs((JsValue)executor), JsValue.Undefined);
        }

        private static JsObject CreateAsyncIteratorResult(JsValue value, bool done)
        {
            var result = new JsObject();
            // value is already JsValue
            result.SetProperty("value", value);
            result.SetProperty("done", done ? JsValue.True : JsValue.False);
            return result;
        }

        private void ResolveFromStep(
            ExecutionPlanRunner.AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            switch (step.Kind)
            {
                case ExecutionPlanRunner.AsyncGeneratorStepKind.Yield:
                case ExecutionPlanRunner.AsyncGeneratorStepKind.Completed:
                    {
                        var iteratorResult = CreateAsyncIteratorResult(step.Value, step.Done);
                        AsyncInvokeWithOneArg(resolve, (JsValue)iteratorResult);
                        break;
                    }
                case ExecutionPlanRunner.AsyncGeneratorStepKind.Throw:
                    // step.Value is already JsValue
                    AsyncInvokeWithOneArg(reject, step.Value);
                    break;
                case ExecutionPlanRunner.AsyncGeneratorStepKind.Pending:
                    HandlePendingStep(step, resolve, reject);
                    break;
            }
        }

        private void HandlePendingStep(
            ExecutionPlanRunner.AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            var (onFulfilled, onRejected) = AsyncResumeCallback.Rent(this, resolve, reject);
            if (JsPromise.TryGetInternalPromise(step.PendingPromise, out var pendingPromise))
            {
                pendingPromise.Then(onFulfilled, onRejected);
                return;
            }

            if (!TryGetPendingThenMethod(step, reject, out var thenCallable))
            {
                return;
            }

            AsyncInvokeWithTwoArgs(
                thenCallable,
                JsValue.FromObjectUnsafe(onFulfilled),
                JsValue.FromObjectUnsafe(onRejected),
                step.PendingPromise);
        }

        private JsObject? ResolveGeneratorPrototype()
        {
            if (callable is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("prototype", out var protoValue) &&
                protoValue.TryGetObject<JsObject>(out var prototypeObject))
            {
                return prototypeObject;
            }

            return realmState.AsyncGeneratorPrototype ?? realmState.ObjectPrototype;
        }

        /// <summary>
        ///     Poolable callback for async generator resume - avoids allocation on hot path.
        /// </summary>
        private sealed class AsyncResumeCallback : IJsCallable
        {
            [ThreadStatic] private static AsyncResumeCallback? TCachedFulfilled;

            [ThreadStatic] private static AsyncResumeCallback? TCachedRejected;

            private AsyncGeneratorInvoker? _executor;
            private bool _isRejection;
            private IJsCallable? _reject;
            private IJsCallable? _resolve;

            public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
            {
                var executor = _executor!;
                var resolve = _resolve!;
                var reject = _reject!;
                var isRejection = _isRejection;

                // Clear state before execution
                _executor = null;
                _resolve = null;
                _reject = null;

                var value = args.Count > 0 ? args[0] : JsValue.Undefined;
                var mode = isRejection
                    ? ExecutionPlanRunner.ResumeMode.Throw
                    : ExecutionPlanRunner.ResumeMode.Next;

                try
                {
                    var resumed = executor._inner.ExecuteAsyncStep(mode, value);
                    executor.ResolveFromStep(resumed, resolve, reject);
                }
                finally
                {
                    // Return to appropriate pool
                    if (isRejection)
                    {
                        TCachedRejected = this;
                    }
                    else
                    {
                        TCachedFulfilled = this;
                    }
                }

                return JsValue.Undefined;
            }

            public static (AsyncResumeCallback fulfilled, AsyncResumeCallback rejected) Rent(
                AsyncGeneratorInvoker executor,
                IJsCallable resolve,
                IJsCallable reject)
            {
                var fulfilled = TCachedFulfilled ?? new AsyncResumeCallback();
                TCachedFulfilled = null;
                fulfilled._executor = executor;
                fulfilled._resolve = resolve;
                fulfilled._reject = reject;
                fulfilled._isRejection = false;

                var rejected = TCachedRejected ?? new AsyncResumeCallback();
                TCachedRejected = null;
                rejected._executor = executor;
                rejected._resolve = resolve;
                rejected._reject = reject;
                rejected._isRejection = true;

                return (fulfilled, rejected);
            }
        }
    }
}
