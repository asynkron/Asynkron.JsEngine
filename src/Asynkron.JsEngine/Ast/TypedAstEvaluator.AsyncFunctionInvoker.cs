#region

using System.Collections.Immutable;
using System.Diagnostics;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    ///     Drives an async function to completion using the generator IR executor.
    ///     Unlike AsyncGeneratorInvoker which exposes .next()/.return()/.throw() methods,
    ///     this class drives execution automatically and returns a single Promise.
    /// </summary>
    internal sealed class AsyncFunctionInvoker(
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
        FunctionExecutionPlanSeed planSeed = default)
    {
        private readonly RealmState _realmState = realmState;
        private readonly FunctionExecutionPlanSeed _planSeed = planSeed;
        private ExecutionPlanRunner? _inner;

        /// <summary>
        ///     Executes the async function and returns a Promise that resolves/rejects
        ///     with the function's completion value or thrown error.
        /// </summary>
        public JsValue Execute()
        {
            var promiseCtor = _realmState.PromiseConstructor;
            if (promiseCtor is null)
            {
                throw new InvalidOperationException("Promise constructor is not available.");
            }

            var executor = new HostFunction((_, execArgs) =>
            {
                if (!TryGetExecutorCallbacks(execArgs, out var resolve, out var reject))
                {
                    return JsValue.Undefined;
                }

                try
                {
                    // Initialize generator inside Promise to capture any early errors
                    _inner = new ExecutionPlanRunner(
                        function,
                        closure,
                        arguments,
                        thisValue,
                        callable,
                        _realmState,
                        isLexicallyStrict,
                        hasFunctionNameEnvironment,
                        homeObject,
                        privateNameScope,
                        capturedPrivateNameScopes,
                        planOverride: _planSeed.Plan,
                        planFailureOverride: _planSeed.Failure);
                    _inner.Initialize();

                    // Start execution - async functions don't receive an argument on first call
                    DriveToCompletion(ExecutionPlanRunner.ResumeMode.Next, JsValue.Undefined, resolve, reject);
                }
                catch (ThrowSignal signal)
                {
                    // Early error during initialization - reject the promise
                    AsyncInvokeWithOneArg(reject, signal.ThrownValue);
                }
                catch (Exception ex)
                {
                    // Non-JS exception during initialization - wrap in error message
                    AsyncInvokeWithOneArg(reject, (JsValue)ex.Message);
                }

                return JsValue.Undefined;
            });

            if (promiseCtor is HostFunction hostCtor)
            {
                return hostCtor.InvokeWithContext([(JsValue)executor], JsValue.Undefined, null, (JsValue)hostCtor);
            }

            return promiseCtor.Invoke(new SingleValueArgs((JsValue)executor), JsValue.Undefined);
        }

        private void DriveToCompletion(
            ExecutionPlanRunner.ResumeMode mode,
            JsValue argument,
            IJsCallable resolve,
            IJsCallable reject)
        {
            _realmState.Logger?.LogInformation(
                "[AsyncFunctionInvoker] DriveToCompletion mode={Mode} argKind={Kind}",
                mode,
                argument.Kind);
            System.Console.WriteLine($"[AsyncFunctionInvoker] DriveToCompletion mode={mode} argKind={argument.Kind}");
            try
            {
                var step = _inner!.ExecuteAsyncStep(mode, argument);

                switch (step.Kind)
                {
                    case ExecutionPlanRunner.AsyncGeneratorStepKind.Completed:
                        // Async function completed - resolve with the return value
                        AsyncInvokeWithOneArg(resolve, step.Value);
                        break;

                    case ExecutionPlanRunner.AsyncGeneratorStepKind.Yield:
                        // Async functions don't yield externally - treat as await
                        // and continue driving to completion
                        DriveToCompletion(ExecutionPlanRunner.ResumeMode.Next, step.Value, resolve, reject);
                        break;

                    case ExecutionPlanRunner.AsyncGeneratorStepKind.Throw:
                        // Async function threw - reject the promise
                        AsyncInvokeWithOneArg(reject, step.Value);
                        break;

                    case ExecutionPlanRunner.AsyncGeneratorStepKind.Pending:
                        // Await hit a pending promise - attach handlers to resume
                        HandlePendingStep(step, resolve, reject);
                        break;
                }
            }
            catch (ThrowSignal signal)
            {
                // Uncaught exception - reject the promise
                AsyncInvokeWithOneArg(reject, signal.ThrownValue);
            }
            catch (Exception ex)
            {
                // Non-JS exception - wrap in error message
                AsyncInvokeWithOneArg(reject, (JsValue)ex.Message);
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

        /// <summary>
        ///     Poolable callback for async function resume - avoids allocation on hot path.
        ///     Callbacks are created in pairs (fulfilled + rejected) but only one is invoked.
        ///     The invoked callback returns both itself AND its sibling to the pool.
        /// </summary>
        private sealed class AsyncResumeCallback : IJsCallable
        {
            private static readonly ObjectPool<AsyncResumeCallback> FulfilledPool =
                new(32, static () => new AsyncResumeCallback());

            private static readonly ObjectPool<AsyncResumeCallback> RejectedPool = new(32,
                static () => new AsyncResumeCallback());

            private AsyncFunctionInvoker? _executor;
            private bool _isRejection;
            private IJsCallable? _reject;
            private IJsCallable? _resolve;
            private AsyncResumeCallback? _sibling; // The other callback in the pair

            public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
            {
                AssertOwnership(nameof(Invoke));
                var executor = _executor!;
                var resolve = _resolve!;
                var reject = _reject!;
                var isRejection = _isRejection;
                var sibling = _sibling;

                executor._realmState.Logger?.LogInformation(
                    "[AsyncFunctionInvoker] ResumeCallback isRejection={IsRejection} argKind={Kind}",
                    isRejection,
                    args.Count > 0 ? args[0].Kind.ToString() : "none");
                System.Console.WriteLine($"[AsyncFunctionInvoker] ResumeCallback isRejection={isRejection} argKind={(args.Count > 0 ? args[0].Kind.ToString() : "none")}");

                // Clear state before execution
                _executor = null;
                _resolve = null;
                _reject = null;
                _sibling = null;

                var value = args.Count > 0 ? args[0] : JsValue.Undefined;
                var mode = isRejection
                    ? ExecutionPlanRunner.ResumeMode.Throw
                    : ExecutionPlanRunner.ResumeMode.Next;

                try
                {
                    executor.DriveToCompletion(mode, value, resolve, reject);
                }
                finally
                {
                    // Return both this callback AND its sibling to pools
                    if (isRejection)
                    {
                        RejectedPool.Return(this);
                        if (sibling is not null)
                        {
                            sibling._executor = null;
                            sibling._resolve = null;
                            sibling._reject = null;
                            sibling._sibling = null;
                            FulfilledPool.Return(sibling);
                        }
                    }
                    else
                    {
                        FulfilledPool.Return(this);
                        if (sibling is not null)
                        {
                            sibling._executor = null;
                            sibling._resolve = null;
                            sibling._reject = null;
                            sibling._sibling = null;
                            RejectedPool.Return(sibling);
                        }
                    }
                }

                return JsValue.Undefined;
            }

            public static (AsyncResumeCallback fulfilled, AsyncResumeCallback rejected) Rent(
                AsyncFunctionInvoker executor,
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

                // Link siblings so the invoked one can return both
                fulfilled._sibling = rejected;
                rejected._sibling = fulfilled;

                return (fulfilled, rejected);
            }

            [Conditional("DEBUG")]
            internal void AssertOwnership(string usage) => PoolDebug.AssertOwned(this, usage);
        }
    }
}
