using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

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

        // Async generators currently execute through the shared
        // ExecutionPlanRunner.ExecuteAsyncStep contract. Each .next/.return/.throw
        // call drives one step and wraps the step result in a Promise. A dedicated
        // async-generator IR executor can later replace this bridge without changing
        // the external async-step contract used here.
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

            var executor = StepExecutor.Rent(this, mode, argument);

            if (promiseCtor is HostFunction hostCtor)
            {
                return hostCtor.InvokeWithContext([JsValue.FromObjectUnsafe(executor)], JsValue.Undefined, null,
                    (JsValue)hostCtor);
            }

            return promiseCtor.Invoke(new SingleValueArgs(JsValue.FromObjectUnsafe(executor)), JsValue.Undefined);
        }

        private static JsValue CreateAsyncIteratorResult(JsValue value, bool done) => IteratorResultObject.Create(value, done);

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
                        AsyncInvokeWithOneArg(resolve, iteratorResult);
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
                pendingPromise!.Then(onFulfilled, onRejected);
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
            private static readonly ObjectPool<AsyncResumeCallback> FulfilledPool =
                new(32, static () => new AsyncResumeCallback());
            private static readonly ObjectPool<AsyncResumeCallback> RejectedPool =
                new(32, static () => new AsyncResumeCallback());

            private AsyncGeneratorInvoker? _executor;
            private bool _isRejection;
            private IJsCallable? _reject;
            private IJsCallable? _resolve;
            private AsyncResumeCallback? _sibling;

            public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
            {
                AssertOwnership(nameof(Invoke));
                var executor = _executor!;
                var resolve = _resolve!;
                var reject = _reject!;
                var isRejection = _isRejection;
                var sibling = _sibling;

                // Clear state before execution
                ClearState();

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
                    if (isRejection)
                    {
                        RejectedPool.Return(this);
                        if (sibling is not null)
                        {
                            sibling.ClearState();
                            FulfilledPool.Return(sibling);
                        }
                    }
                    else
                    {
                        FulfilledPool.Return(this);
                        if (sibling is not null)
                        {
                            sibling.ClearState();
                            RejectedPool.Return(sibling);
                        }
                    }
                }

                return JsValue.Undefined;
            }

            private void ClearState()
            {
                _executor = null;
                _resolve = null;
                _reject = null;
                _sibling = null;
            }

            public static (AsyncResumeCallback fulfilled, AsyncResumeCallback rejected) Rent(
                AsyncGeneratorInvoker executor,
                IJsCallable resolve,
                IJsCallable reject)
            {
                var fulfilled = FulfilledPool.Rent();
                fulfilled._executor = executor;
                fulfilled._resolve = resolve;
                fulfilled._reject = reject;
                fulfilled._isRejection = false;

                var rejected = RejectedPool.Rent();
                rejected._executor = executor;
                rejected._resolve = resolve;
                rejected._reject = reject;
                rejected._isRejection = true;
                fulfilled._sibling = rejected;
                rejected._sibling = fulfilled;

                return (fulfilled, rejected);
            }

            [Conditional("DEBUG")]
            internal void AssertOwnership(string usage) => PoolDebug.AssertOwned(this, usage);
        }

        private sealed class StepExecutor : IJsCallable
        {
            private static readonly ObjectPool<StepExecutor> Pool = new(32, static () => new StepExecutor());

            private AsyncGeneratorInvoker? _executor;
            private JsValue _argument;
            private ExecutionPlanRunner.ResumeMode _mode;

            public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
            {
                AssertOwnership(nameof(Invoke));
                if (_executor is null)
                {
                    return JsValue.Undefined;
                }

                var executor = _executor;
                var mode = _mode;
                var argument = _argument;
                try
                {
                    if (!TryGetExecutorCallbacks(args, out var resolve, out var reject))
                    {
                        return JsValue.Undefined;
                    }

                    var step = executor._inner.ExecuteAsyncStep(mode, argument);
                    executor.ResolveFromStep(step, resolve, reject);
                    return JsValue.Undefined;
                }
                finally
                {
                    _executor = null;
                    _mode = default;
                    _argument = default;
                    Pool.Return(this);
                }
            }

            public static StepExecutor Rent(
                AsyncGeneratorInvoker executor,
                ExecutionPlanRunner.ResumeMode mode,
                JsValue argument)
            {
                var stepExecutor = Pool.Rent();
                stepExecutor._executor = executor;
                stepExecutor._mode = mode;
                stepExecutor._argument = argument;
                return stepExecutor;
            }

            [Conditional("DEBUG")]
            internal void AssertOwnership(string usage) => PoolDebug.AssertOwned(this, usage);
        }
    }
}
