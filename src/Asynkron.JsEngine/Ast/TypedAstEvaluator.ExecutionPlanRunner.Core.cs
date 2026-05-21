#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Compatibility overloads remain for dynamic/resume seams; not proof of direct runner AST fallback.

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        public ExecutionPlanRunner(
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
            JsValue newTarget = default,
            JsEnvironment? lexicalThisEnvironment = null,
            IJsEnvironmentAwareCallable? superConstructor = null,
            IJsPropertyAccessor? superPrototype = null,
            EvaluationContext? evaluationContext = null,
            RealmState? derivedClassErrorRealm = null,
            ExecutionPlan? planOverride = null,
            ExecutionPlanBuildFailure? planFailureOverride = null)
        {
            _function = function;
            _closure = closure;
            _arguments = arguments;
            _thisValue = thisValue;
            _newTarget = newTarget;
            _callable = callable;
            _realmState = realmState;
            _derivedClassErrorRealm = derivedClassErrorRealm ?? realmState;
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
            _homeObject = homeObject;
            _privateNameScope = privateNameScope;
            _capturedPrivateNameScopes = capturedPrivateNameScopes;
            _lexicalThisEnvironment = lexicalThisEnvironment;
            _superConstructor = superConstructor;
            _superPrototype = superPrototype;
            _context = evaluationContext;
            _isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict;
            _isAsync = function.IsAsync;
            _isGenerator = function.IsGenerator;
            _allowIdentifierCache = AllowsIdentifierCaching(function) && !closure.HasWithObjectInChain();

            if (planOverride is not null)
            {
                _plan = planOverride;
                _programCounter = _plan.EntryPoint;
                return;
            }

            if (planFailureOverride is not null)
            {
                throw new NotSupportedException(
                    $"IR plan generation failed for function: {planFailureOverride.Detail}");
            }

            var planCache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
            if (!planCache.Succeeded || planCache.Plan is null)
            {
                var reason = planCache.FailureReason ?? "Generator contains unsupported construct for IR.";
                throw new NotSupportedException($"IR plan generation failed for function: {reason}");
            }

            _plan = planCache.Plan;
            _programCounter = _plan.EntryPoint;
        }

        /// <summary>
        /// Private constructor for script execution mode.
        /// Used by RunScript() to create a minimal runner without function context.
        /// </summary>
        /// <param name="plan">The execution plan to run.</param>
        /// <param name="environment">The pre-configured script environment.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="slotOffset">Offset to apply to slot indices to avoid overwriting existing GlobalEnvironment slots.</param>
        private ExecutionPlanRunner(
            ExecutionPlan plan,
            JsEnvironment environment,
            EvaluationContext context,
            int slotOffset)
        {
            _plan = plan;
            _programCounter = plan.EntryPoint;
            _executionEnvironment = environment;
            _closure = environment;
            _context = context;
            _realmState = context.RealmState;
            _arguments = [];
            _callable = null!;
            _function = null!;
            _thisValue = context.RealmState.Engine?.GlobalObject is { } go
                ? new JsValue(go)
                : JsValue.Undefined;
            _derivedClassErrorRealm = _realmState;
            _isStrict = environment.IsStrict;
            _isAsync = false; // Scripts run via RunScript are synchronous
            _isGenerator = false; // Scripts are not generators
            _allowIdentifierCache = context.AllowIdentifierCache;
            _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
            _isScriptMode = true;
            _slotOffset = slotOffset;
        }

        private ExecutionPlanRunner(
            JsEnvironment environment,
            EvaluationContext context,
            JsValue newTarget)
        {
            _executionEnvironment = environment;
            _closure = environment;
            _context = context;
            _realmState = context.RealmState;
            _arguments = [];
            _callable = null!;
            _function = null!;
            _thisValue = environment.TryFindBindingJsValue(Symbol.This, allowUninitialized: true, out _, out var thisValue)
                ? thisValue
                : JsValue.Undefined;
            _newTarget = newTarget;
            _derivedClassErrorRealm = _realmState;
            _isStrict = environment.IsStrict;
            _isAsync = false;
            _isGenerator = false;
            _allowIdentifierCache = context.AllowIdentifierCache;
            _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
        }

        internal static JsValue EvaluateStandaloneExpressionProgram(
            ExpressionProgram program,
            JsEnvironment environment,
            EvaluationContext context,
            JsValue newTarget = default)
        {
            var runner = new ExecutionPlanRunner(environment, context, newTarget);
            var previousContext = EvaluationContext.Current;
            var previousEnvironment = JsEnvironment.Current;
            EvaluationContext.Current = context;
            JsEnvironment.Current = environment;
            try
            {
                return runner.EvaluateExpressionProgram(program, environment, context);
            }
            finally
            {
                JsEnvironment.Current = previousEnvironment;
                EvaluationContext.Current = previousContext;
            }
        }

        internal static void ApplyStandaloneBindingTargetProgram(
            BindingTargetProgram target,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context,
            BindingMode mode,
            bool hasInitializer = true,
            bool allowNameInference = true,
            bool skipBlockedBindingLookup = false)
        {
            var runner = new ExecutionPlanRunner(environment, context, newTarget: default);
            var previousContext = EvaluationContext.Current;
            var previousEnvironment = JsEnvironment.Current;
            EvaluationContext.Current = context;
            JsEnvironment.Current = environment;
            try
            {
                runner.ApplyBindingTargetProgram(
                    target,
                    value,
                    environment,
                    context,
                    mode,
                    hasInitializer,
                    allowNameInference,
                    skipBlockedBindingLookup);
            }
            finally
            {
                JsEnvironment.Current = previousEnvironment;
                EvaluationContext.Current = previousContext;
            }
        }

        /// <summary>
        /// Runs an execution plan for script-level code.
        /// This is a lightweight path that skips generator/async machinery setup.
        /// The environment is already configured with hoisted declarations.
        /// </summary>
        /// <param name="plan">The execution plan to run.</param>
        /// <param name="environment">The pre-configured script environment with hoisted bindings.</param>
        /// <param name="context">The evaluation context.</param>
        /// <returns>The completion value of the script.</returns>
        public static JsValue RunScript(
            ExecutionPlan plan,
            JsEnvironment environment,
            EvaluationContext context)
        {
            // When script entry already initialized the
            // slot layout (strict-wrapper scripts where HasSlots was false), the plan's
            // synthetic slots are at index 0..N-1 and hoisted vars were appended after
            // them. Skip re-initialization and use offset 0 so IR 0-based indices map
            // directly to the correct slots.
            if (environment.LayoutId == plan.LayoutId && plan.LayoutId > 0)
            {
                var strictRunner = new ExecutionPlanRunner(plan, environment, context, slotOffset: 0);
                var previousContext = EvaluationContext.Current;
                EvaluationContext.Current = context;
                try
                {
                    return strictRunner.RunScriptInternal();
                }
                finally
                {
                    EvaluationContext.Current = previousContext;
                }
            }

            // Capture existing slot count before InitializeSlots appends new slots.
            // This offset is used to adjust IR slot indices at runtime for the GlobalEnvironment.
            // IR uses 0-based indices, but GlobalEnvironment already has slots (Symbol.This at 0, etc.)
            var existingSlotCount = environment.SlotCount;

            // For scripts with synthetic variables (loop iterators, etc.), initialize slots
            // even though user variables aren't slot-based. This enables slot-based access
            // for internal variables like __forIn_value_X, __forOf_iter_X, etc.
            if (plan.SlotCount > 0)
            {
                environment.InitializeSlots(plan.SlotCount, plan.RootScopeId);

                // Populate slot metadata (names) from the plan at the offset position
                // so they don't overwrite existing slot names (Symbol.This, etc.)
                environment.PopulateSyntheticSlotNames(plan.SlotSymbols, existingSlotCount);
            }

            var globalRunner = new ExecutionPlanRunner(plan, environment, context, existingSlotCount);
            var previousEvaluationContext = EvaluationContext.Current;
            EvaluationContext.Current = context;
            try
            {
                return globalRunner.RunScriptInternal();
            }
            finally
            {
                EvaluationContext.Current = previousEvaluationContext;
            }
        }

        /// <summary>
        /// Internal method to run script without generator overhead.
        /// Returns the raw completion value, not an iterator result.
        /// </summary>
        private JsValue RunScriptInternal()
        {
            var executionEnvironment = EnsureExecutionEnvironment();
            var previousEnvironment = JsEnvironment.Current;
            JsEnvironment.Current = executionEnvironment;
            try
            {
                // Run the plan - for scripts this completes immediately (no yield/async)
                // The completion value is tracked in _scriptCompletionValue during execution
                var result = ExecutePlan(ResumeMode.Next, JsValue.Undefined);

                // ExecutePlan returns an iterator result {value, done} for generators.
                // For script execution with explicit return statements, extract the return value.
                if (result.TryGetObject<IteratorResultObject>(out var iteratorResult))
                {
                    iteratorResult.TryGetProperty("value", out var returnValue);
                    // If there was an explicit return, use that value
                    // Otherwise fall back to tracked completion value
                    return returnValue.IsUndefined ? GetFinalCompletionValue() : returnValue;
                }

                if (result.TryGetObject<JsObject>(out var jsObject) &&
                    jsObject.TryGetProperty("value", out var jsValue))
                {
                    return jsValue.IsUndefined ? GetFinalCompletionValue() : jsValue;
                }

                // No iterator result wrapper - use tracked completion value
                return GetFinalCompletionValue();
            }
            finally
            {
                JsEnvironment.Current = previousEnvironment;
            }
        }

        /// <summary>
        /// Gets the final completion value, converting Unit sentinel to undefined.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue GetFinalCompletionValue()
        {
            return _scriptCompletionValue.IsUnit ? JsValue.Undefined : _scriptCompletionValue;
        }

        public JsObject CreateGeneratorObject()
        {
            var prototype = ResolveGeneratorPrototype();
            var iterator = CreateGeneratorIteratorObject(
                args => Next(args.GetArgument(0)),
                args => Return(args.Count > 0 ? args[0] : JsValue.Undefined),
                args => Throw(args.Count > 0 ? args[0] : JsValue.Undefined),
                prototype);
            iterator.SetProperty(IteratorSymbolPropertyName,
                (JsValue)new HostFunction((_, _) => new JsValue(iterator)));
            iterator.SetProperty(GeneratorBrandPropertyName, JsValue.FromObjectUnsafe(GeneratorBrandMarker));
            return iterator;
        }

        public void Initialize()
        {
            EnsureExecutionEnvironment();
        }

        /// <summary>
        /// Runs the execution plan synchronously to completion.
        /// Unlike generator iteration, this returns the raw completion value
        /// rather than an iterator result object.
        /// Used for sync function execution via IR.
        /// </summary>
        public JsValue RunSync()
        {
            var executionEnvironment = EnsureExecutionEnvironment();
            var previousEnvironment = JsEnvironment.Current;
            JsEnvironment.Current = executionEnvironment;
            try
            {
                // Run the plan - for sync functions this completes immediately
                var result = ExecutePlan(ResumeMode.Next, JsValue.Undefined);

                // ExecutePlan returns an iterator result {value, done} for generators.
                // For sync execution, extract the raw value.
                // Handle both IteratorResultObject (lightweight) and JsObject (full) cases.
                JsValue returnValue;
                if (result.TryGetObject<IteratorResultObject>(out var iteratorResult))
                {
                    iteratorResult.TryGetProperty("value", out returnValue);
                }
                else if (result.TryGetObject<JsObject>(out var jsObject) &&
                         jsObject.TryGetProperty("value", out var jsValue))
                {
                    returnValue = jsValue;
                }
                else
                {
                    // If no iterator result (shouldn't happen), return as-is
                    returnValue = result;
                }

                // For class constructors, apply ES spec [[Construct]] semantics.
                if (_callable is not SyncFunctionInvoker syncInvoker || !syncInvoker.IsClassConstructor)
                {
                    return returnValue;
                }

            if (returnValue.IsObject)
            {
                return returnValue;
            }

            if (syncInvoker.IsDerivedClassConstructor)
            {
                if (!returnValue.IsUndefined)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        "Derived constructors may only return object or undefined",
                        _context,
                        _derivedClassErrorRealm));
                }

                if (_executionEnvironment is null ||
                    !_executionEnvironment.TryGetJsValue(Symbol.This, out var derivedThis) ||
                    derivedThis.IsUninitialized ||
                    ReferenceEquals(derivedThis.ObjectValue, JsEnvironment.Uninitialized))
                {
                    throw new ThrowSignal(StandardLibrary.CreateReferenceError(
                        "ReferenceError: this is not defined - must call super() in derived class constructor",
                        _context,
                        _derivedClassErrorRealm));
                }

                return derivedThis;
            }

            // Base class constructors ignore non-object returns and yield `this`.
            if (_executionEnvironment is not null &&
                _executionEnvironment.TryGetJsValue(Symbol.This, out var thisValue))
            {
                return thisValue;
            }

                return returnValue;
            }
            finally
            {
                JsEnvironment.Current = previousEnvironment;
            }
        }

        private JsValue Next(JsValue value)
        {
            return ExecutePlan(ResumeMode.Next, value);
        }

        private JsValue Return(JsValue value)
        {
            return ExecutePlan(ResumeMode.Return, value);
        }

        private JsValue Throw(JsValue error)
        {
            return ExecutePlan(ResumeMode.Throw, error);
        }

        private JsObject? ResolveGeneratorPrototype()
        {
            // Per spec: OrdinaryCreateFromConstructor with intrinsicDefaultProto = "%GeneratorPrototype%"
            // 1. Try to get the generator function's .prototype property
            // 2. If it's an object, use it
            // 3. Otherwise, fall back to %GeneratorPrototype% (the intrinsic default)
            if (_callable is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("prototype", out var protoValue))
            {
                // protoValue is already a JsValue from TryGetProperty
                if (protoValue.TryGetObject<JsObject>(out var prototypeObject))
                {
                    return prototypeObject;
                }
            }

            // Fall back to %GeneratorPrototype% if the function's .prototype is not an object
            return _realmState.GeneratorPrototype ?? _realmState.ObjectPrototype;
        }

        internal AsyncGeneratorStepResult ExecuteAsyncStep(ResumeMode mode, JsValue resumeValue)
        {
            // Reuse the existing ExecutePlan logic but translate its iterator
            // result / exceptions into a structured step result that async
            // generators can consume without throwing. This entrypoint also
            // marks the executor as async-aware so future steps can surface
            // pending Promises instead of blocking.
            var previousAsyncStepMode = AsyncStateRef.AsyncStepMode;
            AsyncStateRef.AsyncStepMode = true;
            AsyncStateRef.PendingPromise = JsValue.Undefined;

            try
            {
                var result = ExecutePlan(mode, resumeValue);

                if (HasPendingPromise())
                {
                    return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, JsValue.Undefined, false,
                        AsyncStateRef.PendingPromise);
                }

                if (result.TryGetObject<IJsPropertyAccessor>(out var obj) &&
                    obj.TryGetProperty("done", out var doneRaw) &&
                    obj.TryGetProperty("value", out var value))
                {
                    // doneRaw and value are already JsValue from TryGetProperty
                    var done = doneRaw.IsTruthy;
                    return done
                        ? new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, value, true, JsValue.Undefined)
                        : new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Yield, value, false, JsValue.Undefined);
                }

                // If the plan completed without producing a well-formed iterator
                // result, treat it as a completed step with undefined.
                return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, JsValue.Undefined, true,
                    JsValue.Undefined);
            }
            finally
            {
                AsyncStateRef.AsyncStepMode = previousAsyncStepMode;
                AsyncStateRef.PendingPromise = JsValue.Undefined;
            }
        }
    }
}
