using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static bool TryGetExecutorCallbacks(
        IReadOnlyList<JsValue> execArgs,
        [NotNullWhen(true)] out IJsCallable? resolve,
        [NotNullWhen(true)] out IJsCallable? reject)
    {
        if (execArgs.Count < 2 ||
            !execArgs[0].TryUnwrap(out resolve) ||
            !execArgs[1].TryUnwrap(out reject))
        {
            resolve = null;
            reject = null;
            return false;
        }

        return true;
    }

    private sealed class AsyncGeneratorInvoker(
        FunctionExpression function,
        JsEnvironment closure,
        IReadOnlyList<JsValue> arguments,
        JsValue thisValue,
        IJsCallable callable,
        RealmState realmState,
        bool isLexicallyStrict,
        IJsObjectLike? homeObject,
        PrivateNameScope? privateNameScope,
        ImmutableArray<PrivateNameScope> capturedPrivateNameScopes,
        FunctionExecutionPlanSeed planSeed)
    {
        private readonly Queue<AsyncGeneratorRequest> _queuedRequests = new();
        private UnifiedBytecodeResumeState? _unifiedState;
        private bool _isExecutingStep;

        // Each .next/.return/.throw call drives one async-generator step and wraps
        // the step result in a Promise. Admitted bodies are VM-owned through
        // UnifiedBytecodeVirtualMachine.ExecuteResumable; declined bodies fail
        // explicitly until the VM owns the missing semantics.
        public void Initialize()
        {
            if (TryInitializeUnifiedBytecode(out var declineReason))
            {
                return;
            }

            throw new NotSupportedException(
                $"Async-generator body '{function.Name?.Name ?? "<anonymous>"}' is not eligible for unified bytecode execution: {declineReason}");
        }

        public JsObject CreateAsyncIteratorObject()
        {
            var prototype = ResolveGeneratorPrototype();
            var asyncIterator = CreateGeneratorIteratorObject(
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return CreateStepPromise(AsyncGeneratorResumeMode.Next, argValue);
                },
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return CreateStepPromise(AsyncGeneratorResumeMode.Return, argValue);
                },
                args =>
                {
                    var argValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                    return CreateStepPromise(AsyncGeneratorResumeMode.Throw, argValue);
                },
                prototype);

            // asyncIterator[Symbol.asyncIterator] returns itself.
            var asyncKey = SymbolKeys.AsyncIterator;
            asyncIterator.SetProperty(asyncKey, (JsValue)new HostFunction((thisValue, _) => thisValue));

            return asyncIterator;
        }

        private JsValue CreateStepPromise(AsyncGeneratorResumeMode mode, JsValue argument)
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

        private bool TryInitializeUnifiedBytecode(out string declineReason)
        {
            // Non-simple parameter lists (destructuring patterns, defaults, rest) bind through
            // eager IteratorBindingInitialization below (TryBindNonSimpleResumableParameters),
            // matching FunctionDeclarationInstantiation's run-at-call-time semantics before the
            // async iterator object is produced. Parameter expressions that create closures or
            // contain direct eval stay declined: the transient binding environment is discarded
            // after seeding the flat slots, so a retained closure over it would desynchronize
            // from later slot mutations.
            if (!function.HasOnlySimpleIdentifierParameters() &&
                !ResumableParameterShapeAllowsEagerBinding(function))
            {
                declineReason =
                    "Non-simple async-generator parameter lists with closure-creating or direct-eval parameter expressions are not eligible for resumable invocation.";
                return false;
            }

            if (!TryGetExecutionPlan(out var plan))
            {
                declineReason = planSeed.Failure?.ToString() ?? "Execution plan is not available.";
                return false;
            }

            if (!TryCollectResumableRootHoistedFunctionDeclarations(
                    function,
                    plan,
                    allowCapturedActivationSlots: true,
                    out var hoistedFunctionDeclarations))
            {
                declineReason =
                    "Root hoisted function declarations are not eligible for async-generator resumable routing.";
                return false;
            }

            var needsFunctionEnvironmentForDisposal =
                UnifiedBytecodeProductionEligibility.PlanNeedsResumableFunctionEnvironmentForDisposal(plan);
            var needsDirectEvalArgumentsObject =
                HasResumableDirectEvalImplicitArgumentsAccess(function);
            var needsMaterializedBodyEnvironment =
                UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan) ||
                HoistedFunctionDeclarationsNeedMaterializedBodyEnvironment(hoistedFunctionDeclarations) ||
                UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableClassDeclarationEnvironment(plan) ||
                needsFunctionEnvironmentForDisposal ||
                needsDirectEvalArgumentsObject;
            var needsNestedFunctionLiteralLexicalThisOrPrivateNameContext =
                UnifiedBytecodeProductionEligibility.PlanNeedsNestedFunctionLiteralLexicalThisOrPrivateNameContext(plan);
            var activation = new UnifiedBytecodeProductionActivationDescriptor(
                IsAsyncLike: true,
                IsGenerator: true,
                HasCapturedOrDynamicActivation: HasResumableCapturedOrDynamicActivationDecline(
                    function,
                    closure,
                    allowDeclarationFreeDirectEval: true),
                HasArgumentsObjectDependency: HasResumableArgumentsObjectDependency(
                    function,
                    allowDeclarationFreeDirectEval: true),
                AllowsRootFunctionDeclarationInstructions: !hoistedFunctionDeclarations.IsEmpty,
                AllowsMaterializedBodyEnvironmentFunctionLiterals: needsMaterializedBodyEnvironment,
                AllowsNestedFunctionLiteralLexicalThisOrPrivateNameContext:
                needsNestedFunctionLiteralLexicalThisOrPrivateNameContext);
            var eligibility = UnifiedBytecodeProductionEligibility.EvaluateResumable(plan, activation);
            if (!eligibility.IsEligible)
            {
                declineReason = eligibility.Reason;
                return false;
            }

            var isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict;
            var boundThis = isStrict
                ? thisValue
                : SyncFunctionInvoker.CoerceThisValueForNonStrict(thisValue, realmState);
            var resumableEnvironment = CreateResumableInvocationEnvironment(
                closure,
                boundThis,
                isStrict,
                function.Source,
                homeObject,
                forceFunctionEnvironment: needsNestedFunctionLiteralLexicalThisOrPrivateNameContext);
            var program = eligibility.Program;
            var context = realmState.CreateContext();
            if (!TryInitializeResumableSlots(
                    plan,
                    program,
                    arguments,
                    out var slots))
            {
                declineReason = "Resumable slot initialization failed.";
                return false;
            }

            if (!function.HasOnlySimpleIdentifierParameters() &&
                !TryBindNonSimpleResumableParameters(
                    function,
                    arguments,
                    plan,
                    program,
                    slots,
                    resumableEnvironment,
                    isStrict,
                    context))
            {
                declineReason =
                    "Non-simple async-generator parameter bindings could not be resolved to flat activation slots.";
                return false;
            }

            var callingEnvironment = resumableEnvironment;
            SuperBinding? resumableSuperBinding = null;
            var requiresResumableSuperBinding = RequiresResumableSuperEnvironment(program);
            if (needsMaterializedBodyEnvironment && requiresResumableSuperBinding)
            {
                declineReason =
                    "Materialized async-generator body environments with resumable super binding are not eligible.";
                return false;
            }

            if (requiresResumableSuperBinding &&
                !TryCreateResumableSuperBinding(closure, boundThis, homeObject, out resumableSuperBinding))
            {
                declineReason = "Resumable super binding could not be created.";
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
                    declineReason = "Materialized resumable body environment could not be created.";
                    return false;
                }
            }

            if (needsDirectEvalArgumentsObject)
            {
                DefineResumableArgumentsObject(function, arguments, callingEnvironment, realmState, callable, isStrict);
            }

            if (!TryPopulateResumableRootHoistedFunctionDeclarations(
                    hoistedFunctionDeclarations,
                    plan,
                    program,
                    slots,
                    callingEnvironment,
                    context))
            {
                declineReason = "Root hoisted function declarations could not be populated into resumable slots.";
                return false;
            }

            _unifiedState = new UnifiedBytecodeResumeState(
                program,
                slots,
                boundThis,
                callingEnvironment,
                isStrict,
                JsValue.Undefined)
            {
                IsAsyncLike = true,
                IsAsyncGenerator = true,
                HasMaterializedBodyEnvironment = needsMaterializedBodyEnvironment,
                ResumableSuperBinding = resumableSuperBinding,
                // Thread the private-name scopes lexically active where this async-generator body was
                // defined so the resumable VM can re-enter them on each per-step context and resolve
                // `#name in obj` correctly across yield/await.
                PrivateNameScopes = UnifiedBytecodeResumeState.CombinePrivateNameScopes(
                    capturedPrivateNameScopes,
                    privateNameScope),
            };

            realmState.Logger?.LogInformation(
                "unified-bytecode-resumable-async-generator-fast-path func={Function} argc={ArgumentCount}",
                function.Name?.Name ?? "<anonymous>",
                arguments.Count);
            declineReason = string.Empty;
            return true;
        }

        private bool TryGetExecutionPlan(out ExecutionPlan plan)
        {
            if (planSeed.Plan is { } seededPlan)
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

        private AsyncGeneratorStepResult ExecuteStep(
            AsyncGeneratorResumeMode mode,
            JsValue argument)
        {
            if (_unifiedState is { } unifiedState)
            {
                return ExecuteUnifiedBytecodeStep(ToUnifiedResumeMode(mode), argument, unifiedState);
            }

            throw new InvalidOperationException("Async-generator unified bytecode state is not initialized.");
        }

        private AsyncGeneratorStepResult ExecuteUnifiedBytecodeStep(
            UnifiedBytecodeResumeMode mode,
            JsValue argument,
            UnifiedBytecodeResumeState unifiedState)
        {
            var context = realmState.CreateContext();
            try
            {
                var step = UnifiedBytecodeVirtualMachine.DisposeCompletedResumableStep(
                    unifiedState,
                    UnifiedBytecodeVirtualMachine.ExecuteResumable(unifiedState, mode, argument, context));
                return step.Kind switch
                {
                    UnifiedBytecodeStepKind.Yield => new AsyncGeneratorStepResult(
                        AsyncGeneratorStepKind.Yield,
                        step.Value,
                        false,
                        JsValue.Undefined),
                    UnifiedBytecodeStepKind.Completed => new AsyncGeneratorStepResult(
                        AsyncGeneratorStepKind.Completed,
                        step.Value,
                        true,
                        JsValue.Undefined),
                    UnifiedBytecodeStepKind.Throw => new AsyncGeneratorStepResult(
                        AsyncGeneratorStepKind.Throw,
                        step.Value,
                        true,
                        JsValue.Undefined),
                    UnifiedBytecodeStepKind.PendingAwait => new AsyncGeneratorStepResult(
                        AsyncGeneratorStepKind.Pending,
                        JsValue.Undefined,
                        false,
                        step.PendingPromise),
                    _ => throw new NotSupportedException(
                        $"Unified bytecode async-generator step '{step.Kind}' is not supported.")
                };
            }
            catch (ThrowSignal signal)
            {
                return new AsyncGeneratorStepResult(
                    AsyncGeneratorStepKind.Throw,
                    signal.ThrownValue,
                    true,
                    JsValue.Undefined);
            }
            catch (Exception ex)
            {
                return new AsyncGeneratorStepResult(
                    AsyncGeneratorStepKind.Throw,
                    (JsValue)ex.Message,
                    true,
                    JsValue.Undefined);
            }
        }

        private static UnifiedBytecodeResumeMode ToUnifiedResumeMode(AsyncGeneratorResumeMode mode) =>
            mode switch
            {
                AsyncGeneratorResumeMode.Throw => UnifiedBytecodeResumeMode.Throw,
                AsyncGeneratorResumeMode.Return => UnifiedBytecodeResumeMode.Return,
                _ => UnifiedBytecodeResumeMode.Next
            };

        private enum AsyncGeneratorResumeMode : byte
        {
            Next,
            Return,
            Throw
        }

        private enum AsyncGeneratorStepKind : byte
        {
            Yield,
            Completed,
            Throw,
            Pending
        }

        private readonly record struct AsyncGeneratorStepResult(
            AsyncGeneratorStepKind Kind,
            JsValue Value,
            bool Done,
            JsValue PendingPromise);

        private static JsValue CreateAsyncIteratorResult(JsValue value, bool done)
        {
            var iteratorResult = IteratorResultObject.Create(value, done);
            if (iteratorResult.TryGetObject<IteratorResultObject>(out var poolable))
            {
                poolable.MarkSkipPromiseSettlementCapture();
            }

            return iteratorResult;
        }

        private void ResolveFromStep(
            AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            switch (step.Kind)
            {
                case AsyncGeneratorStepKind.Yield:
                case AsyncGeneratorStepKind.Completed:
                    {
                        var iteratorResult = CreateAsyncIteratorResult(step.Value, step.Done);
                        AsyncInvokeWithOneArg(resolve, iteratorResult);
                        ReturnIteratorResultAfterPromiseReactions(iteratorResult);
                        break;
                    }
                case AsyncGeneratorStepKind.Throw:
                    // step.Value is already JsValue
                    AsyncInvokeWithOneArg(reject, step.Value);
                    break;
                case AsyncGeneratorStepKind.Pending:
                    HandlePendingStep(step, resolve, reject);
                    break;
            }
        }

        private void HandlePendingStep(
            AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            var (onFulfilled, onRejected) = AsyncResumeCallback.Rent(this, resolve, reject);
            if (JsPromise.TryGetInternalPromise(step.PendingPromise, out var pendingPromise))
            {
                pendingPromise!.Then(onFulfilled, onRejected);
                return;
            }

            if (!TryGetPendingThenMethod(step.PendingPromise, reject, out var thenCallable))
            {
                return;
            }

            AsyncInvokeWithTwoArgs(
                thenCallable,
                JsValue.FromObjectUnsafe(onFulfilled),
                JsValue.FromObjectUnsafe(onRejected),
                step.PendingPromise);
        }

        private void ReturnIteratorResultAfterPromiseReactions(JsValue iteratorResult)
        {
            if (!iteratorResult.TryGetObject<IteratorResultObject>(out var poolableResult))
            {
                return;
            }

            if (realmState.Engine is { } engine)
            {
                engine.QueueMicrotask(IteratorResultReturnMicrotask.Rent(poolableResult, engine));
                return;
            }

            IteratorResultObjectPool.Return(poolableResult);
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
                    ? AsyncGeneratorResumeMode.Throw
                    : AsyncGeneratorResumeMode.Next;

                try
                {
                    executor.ExecuteAndResolveStep(mode, value, resolve, reject);
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
            private AsyncGeneratorResumeMode _mode;

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

                    executor.EnqueueOrExecuteStep(mode, argument, resolve, reject);
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
                AsyncGeneratorResumeMode mode,
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

        private readonly record struct AsyncGeneratorRequest(
            AsyncGeneratorResumeMode Mode,
            JsValue Argument,
            IJsCallable Resolve,
            IJsCallable Reject);

        private void EnqueueOrExecuteStep(
            AsyncGeneratorResumeMode mode,
            JsValue argument,
            IJsCallable resolve,
            IJsCallable reject)
        {
            if (_isExecutingStep)
            {
                _queuedRequests.Enqueue(new AsyncGeneratorRequest(mode, argument, resolve, reject));
                return;
            }

            ExecuteAndResolveStep(mode, argument, resolve, reject);
        }

        private void ExecuteAndResolveStep(
            AsyncGeneratorResumeMode mode,
            JsValue argument,
            IJsCallable resolve,
            IJsCallable reject)
        {
            _isExecutingStep = true;
            AsyncGeneratorStepResult step;
            try
            {
                step = ExecuteStep(mode, argument);
            }
            finally
            {
                _isExecutingStep = false;
            }

            ResolveFromStep(step, resolve, reject);
            if (step.Kind != AsyncGeneratorStepKind.Pending)
            {
                DrainQueuedRequests();
            }
        }

        private void DrainQueuedRequests()
        {
            if (_isExecutingStep)
            {
                return;
            }

            while (_queuedRequests.Count > 0 && !_isExecutingStep)
            {
                var request = _queuedRequests.Dequeue();
                ExecuteAndResolveStep(request.Mode, request.Argument, request.Resolve, request.Reject);
            }
        }

        private sealed class IteratorResultReturnMicrotask : IMicrotask, IRentable
        {
            private static readonly ObjectPool<IteratorResultReturnMicrotask> Pool =
                new(32, static () => new IteratorResultReturnMicrotask());

            private IteratorResultObject? _result;
            private JsEngine? _engine;
            private byte _deferredCount;

            public int Epoch { get; set; }

            public static IMicrotask Rent(IteratorResultObject result, JsEngine engine)
            {
                var task = Pool.Rent();
                task._result = result;
                task._engine = engine;
                return task;
            }

            public void Execute()
            {
                AssertOwnership(nameof(Execute));
                // Give settled and late-attached handlers time to observe the iterator result
                // before it is recycled back into the pool.
                if (_deferredCount < 2)
                {
                    _deferredCount++;
                    _engine?.QueueMicrotask(this);
                    return;
                }

                var result = _result;
                if (result is not null)
                {
                    IteratorResultObjectPool.Return(result);
                }

                Pool.Return(this);
            }

            public void OnRent(Microsoft.Extensions.Logging.ILogger? logger)
            {
            }

            public void OnReturn(Microsoft.Extensions.Logging.ILogger? logger)
            {
                _result = null;
                _engine = null;
                _deferredCount = 0;
                Epoch = 0;
            }

            [Conditional("DEBUG")]
            internal void AssertOwnership(string usage) => PoolDebug.AssertOwned(this, usage);
        }

    }
}
