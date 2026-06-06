using System.Collections.Immutable;
using System.Diagnostics;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

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
        private UnifiedBytecodeResumeState? _unifiedState;

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
                    if (TryExecuteUnifiedBytecode(resolve, reject))
                    {
                        return JsValue.Undefined;
                    }

                    // Initialize generator inside Promise to capture any early errors.
                    _inner = CreateClassifiedAsyncDeclinedBodyRunner();
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

        private bool TryExecuteUnifiedBytecode(IJsCallable resolve, IJsCallable reject)
        {
            if (!TryGetExecutionPlan(out var plan))
            {
                return false;
            }

            if (!TryCollectResumableRootHoistedFunctionDeclarations(
                    function,
                    plan,
                    allowCapturedActivationSlots: true,
                    out var hoistedFunctionDeclarations))
            {
                return false;
            }

            var needsMaterializedBodyEnvironment =
                UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan) ||
                HoistedFunctionDeclarationsNeedMaterializedBodyEnvironment(hoistedFunctionDeclarations);
            var activation = new UnifiedBytecodeProductionActivationDescriptor(
                IsAsyncLike: true,
                IsGenerator: false,
                HasCapturedOrDynamicActivation: !AllowsIdentifierCaching(function) || closure.HasWithObjectInChain(),
                HasArgumentsObjectDependency: !function.IsArrow && NeedsArgumentsBinding(function),
                AllowsRootFunctionDeclarationInstructions: !hoistedFunctionDeclarations.IsEmpty,
                AllowsMaterializedBodyEnvironmentFunctionLiterals: needsMaterializedBodyEnvironment);
            var eligibility = UnifiedBytecodeProductionEligibility.EvaluateResumable(plan, activation);
            if (!eligibility.IsEligible)
            {
                return false;
            }

            var isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict;
            var boundThis = isStrict
                ? thisValue
                : SyncFunctionInvoker.CoerceThisValueForNonStrict(thisValue, _realmState);
            var resumableEnvironment = CreateResumableInvocationEnvironment(
                closure,
                boundThis,
                isStrict,
                function.Source,
                homeObject);
            var program = eligibility.Program;
            var context = _realmState.CreateContext();
            if (!TryInitializeResumableSlots(
                    plan,
                    program,
                    arguments,
                    out var slots))
            {
                return false;
            }

            // new.target: an ordinary async function is never a constructor, so its own new.target is
            // undefined. An async ARROW has no new.target binding of its own and lexically inherits the
            // enclosing function's new.target (resolved once here against the captured closure), so it
            // survives suspension as a fixed value rather than via an unbounded chain walk at read time.
            var newTarget = function.IsArrow && closure.TryGetJsValue(Symbol.NewTarget, out var inheritedNewTarget)
                ? inheritedNewTarget
                : JsValue.Undefined;
            var callingEnvironment = resumableEnvironment;
            SuperBinding? resumableSuperBinding = null;
            var requiresResumableSuperBinding = RequiresResumableSuperEnvironment(program);
            if (needsMaterializedBodyEnvironment && requiresResumableSuperBinding)
            {
                return false;
            }

            if (requiresResumableSuperBinding &&
                !TryCreateResumableSuperBinding(closure, boundThis, homeObject, out resumableSuperBinding))
            {
                return false;
            }

            if (needsMaterializedBodyEnvironment)
            {
                if (!TryCreateMaterializedResumableBodyEnvironment(
                        plan,
                        program,
                        slots,
                        resumableEnvironment,
                        isStrict,
                        function.Source,
                        out callingEnvironment))
                {
                    return false;
                }
            }

            if (!TryPopulateResumableRootHoistedFunctionDeclarations(
                    hoistedFunctionDeclarations,
                    plan,
                    program,
                    slots,
                    callingEnvironment,
                    context))
            {
                return false;
            }

            _unifiedState = new UnifiedBytecodeResumeState(program, slots, boundThis, callingEnvironment, isStrict, newTarget)
            {
                IsAsyncLike = true,
                HasMaterializedBodyEnvironment = needsMaterializedBodyEnvironment,
                ResumableSuperBinding = resumableSuperBinding,
                // Thread the private-name scopes lexically active where this async body was defined so the
                // resumable VM can re-enter them on each per-step continuation and resolve `#name in obj`.
                PrivateNameScopes = UnifiedBytecodeResumeState.CombinePrivateNameScopes(
                    capturedPrivateNameScopes,
                    privateNameScope),
            };

            _realmState.Logger?.LogInformation(
                "unified-bytecode-resumable-async-fast-path func={Function} argc={ArgumentCount}",
                function.Name?.Name ?? "<anonymous>",
                arguments.Count);

            DriveUnifiedBytecodeToCompletion(
                UnifiedBytecodeResumeMode.Next,
                JsValue.Undefined,
                resolve,
                reject);
            return true;
        }

        private bool TryGetExecutionPlan(out ExecutionPlan plan)
        {
            if (_planSeed.Plan is { } seededPlan)
            {
                plan = seededPlan;
                return true;
            }

            var cache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
            if (cache.Plan is { } cachedPlan)
            {
                plan = cachedPlan;
                return true;
            }

            plan = null!;
            return false;
        }

        private ExecutionPlanRunner CreateClassifiedAsyncDeclinedBodyRunner()
        {
            return CreateExecutionPlanRunner();
        }

        private ExecutionPlanRunner CreateExecutionPlanRunner()
        {
            return new ExecutionPlanRunner(
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
        }

        private void DriveUnifiedBytecodeToCompletion(
            UnifiedBytecodeResumeMode mode,
            JsValue argument,
            IJsCallable resolve,
            IJsCallable reject)
        {
            var context = _realmState.CreateContext();
            try
            {
                var step = UnifiedBytecodeVirtualMachine.ExecuteResumable(_unifiedState!, mode, argument, context);
                switch (step.Kind)
                {
                    case UnifiedBytecodeStepKind.Completed:
                        AsyncInvokeWithOneArg(resolve, step.Value);
                        break;
                    case UnifiedBytecodeStepKind.Throw:
                        AsyncInvokeWithOneArg(reject, step.Value);
                        break;
                    case UnifiedBytecodeStepKind.PendingAwait:
                        HandlePendingPromise(step.PendingPromise, resolve, reject);
                        break;
                    case UnifiedBytecodeStepKind.Yield:
                        DriveUnifiedBytecodeToCompletion(
                            UnifiedBytecodeResumeMode.Next,
                            step.Value,
                            resolve,
                            reject);
                        break;
                }
            }
            catch (ThrowSignal signal)
            {
                // An opcode whose error surfaces as a ThrowSignal CLR exception (e.g. a strict-mode
                // write to a non-writable property) rather than via the resumable Throw step must still
                // reject the async function's promise. The first-step executor and the IR DriveToCompletion
                // path both catch this; the bytecode resume drive must too, otherwise a post-await throw is
                // swallowed and the promise hangs permanently pending.
                AsyncInvokeWithOneArg(reject, signal.ThrownValue);
            }
            catch (Exception ex)
            {
                AsyncInvokeWithOneArg(reject, (JsValue)ex.Message);
            }
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
                        HandlePendingPromise(step.PendingPromise, resolve, reject);
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

        private void HandlePendingPromise(
            JsValue pendingPromiseValue,
            IJsCallable resolve,
            IJsCallable reject)
        {
            var (onFulfilled, onRejected) = AsyncResumeCallback.Rent(this, resolve, reject);
            if (JsPromise.TryGetInternalPromise(pendingPromiseValue, out var pendingPromise))
            {
                pendingPromise!.Then(onFulfilled, onRejected);
                return;
            }

            if (!TryGetPendingThenMethod(pendingPromiseValue, reject, out var thenCallable))
            {
                return;
            }

            AsyncInvokeWithTwoArgs(
                thenCallable,
                JsValue.FromObjectUnsafe(onFulfilled),
                JsValue.FromObjectUnsafe(onRejected),
                pendingPromiseValue);
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

                // Clear state before execution
                ClearState();

                var value = args.Count > 0 ? args[0] : JsValue.Undefined;
                var mode = isRejection
                    ? ExecutionPlanRunner.ResumeMode.Throw
                    : ExecutionPlanRunner.ResumeMode.Next;

                try
                {
                    if (executor._unifiedState is not null)
                    {
                        executor.DriveUnifiedBytecodeToCompletion(
                            isRejection ? UnifiedBytecodeResumeMode.Throw : UnifiedBytecodeResumeMode.Next,
                            value,
                            resolve,
                            reject);
                    }
                    else
                    {
                        executor.DriveToCompletion(mode, value, resolve, reject);
                    }
                }
                finally
                {
                    // Return both this callback AND its sibling to pools
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
