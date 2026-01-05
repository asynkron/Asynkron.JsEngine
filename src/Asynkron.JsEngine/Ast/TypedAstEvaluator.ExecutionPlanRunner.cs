#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Executes an IR execution plan (compiled from AST).
    /// </summary>
    /// <remarks>
    /// ## Script Completion Value (_scriptCompletionValue)
    ///
    /// In script/eval mode, we track the completion value per ES spec.
    /// The completion value is what eval() returns.
    ///
    /// ### Sentinel Pattern
    ///
    /// We use JsValue.Unit as a sentinel meaning "no value produced yet".
    ///
    /// - Script start: _scriptCompletionValue = Unit
    /// - Expression statement (e.g., 5+5;): _scriptCompletionValue = 10
    /// - At script end: if still Unit → return undefined, else return the value
    ///
    /// ### Loops, Try, Catch
    ///
    /// These constructs have their own internal completion value per ES spec.
    /// They all follow the same pattern:
    ///
    /// 1. On ENTER: _scriptCompletionValue = Unit (reset to sentinel)
    /// 2. Body executes: may or may not update _scriptCompletionValue
    /// 3. On EXIT: if (_scriptCompletionValue.IsUnit) → set to undefined
    ///
    /// This ensures:
    /// - eval('7; for (...) {}') returns undefined (not 7)
    /// - eval('7; for (...) { 9; }') returns 9
    /// - eval('for (...) { 9; break; }') returns 9 (break doesn't touch completion value)
    ///
    /// ### Finally (Special Case)
    ///
    /// Finally is different: its completion value is DISCARDED if it completes normally.
    /// The try/catch completion value is restored.
    ///
    /// - eval('try { 7; } finally { 8; }') returns 7 (not 8)
    ///
    /// Implementation:
    /// 1. When entering finally: frame.SavedCompletionValue = _scriptCompletionValue
    /// 2. Finally body executes (its value is irrelevant if normal completion)
    /// 3. On normal exit: _scriptCompletionValue = SavedCompletionValue.IsUnit ? undefined : SavedCompletionValue
    /// 4. On abrupt exit (return/throw): abrupt completion takes over, completion value doesn't matter
    /// </remarks>
    private sealed partial class ExecutionPlanRunner
    {
        // Core fields - always needed
        private readonly bool _allowIdentifierCache;
        private readonly IReadOnlyList<JsValue> _arguments;
        private readonly IJsCallable _callable;
        private readonly ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes;
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly bool _hasFunctionNameEnvironment;
        private readonly IJsObjectLike? _homeObject;
        private readonly bool _isAsync;
        private readonly bool _isGenerator;
        private readonly bool _isScriptMode;
        private readonly bool _isStrict;
        private readonly JsEnvironment? _lexicalThisEnvironment;
        private readonly JsValue _newTarget;
        private readonly ExecutionPlan? _plan;
        private readonly PrivateNameScope? _privateNameScope;
        private readonly RealmState _realmState;
        private readonly JsValue _thisValue;
        private EvaluationContext? _context;
        private int _currentInstructionIndex;
        private bool _done;
        private JsEnvironment? _executionEnvironment;
        private bool _privateScopesApplied;
        private int _programCounter;
        private JsValue _scriptCompletionValue = JsValue.Unit;
        private GeneratorState _state = GeneratorState.Start;
        private bool _rootScopeLogged;

        // Lazy state objects - only allocated when needed
        // TryCatchState needs explicit backing field for hot-path null check without allocation
        private TryCatchState? _tryCatchState;

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
            JsEnvironment? lexicalThisEnvironment = null)
        {
            _function = function;
            _closure = closure;
            _arguments = arguments;
            _thisValue = thisValue;
            _newTarget = newTarget;
            _callable = callable;
            _realmState = realmState;
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
            _homeObject = homeObject;
            _privateNameScope = privateNameScope;
            _capturedPrivateNameScopes = capturedPrivateNameScopes;
            _lexicalThisEnvironment = lexicalThisEnvironment;
            _isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict;
            _isAsync = function.IsAsync;
            _isGenerator = function.IsGenerator;
            _allowIdentifierCache = AllowsIdentifierCaching(function);

            var planCache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
            if (!planCache.Succeeded || planCache.Plan is null)
            {
                var reason = planCache.FailureReason ?? "Generator contains unsupported construct for IR.";
                throw new NotSupportedException($"Generator IR not implemented for this function: {reason}");
            }

            _plan = planCache.Plan;
            _programCounter = _plan.EntryPoint;
        }

        /// <summary>
        /// Private constructor for script execution mode.
        /// Used by RunScript() to create a minimal runner without function context.
        /// </summary>
        private ExecutionPlanRunner(
            ExecutionPlan plan,
            JsEnvironment environment,
            EvaluationContext context)
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
            _isStrict = environment.IsStrict;
            _isAsync = false; // Scripts run via RunScript are synchronous
            _isGenerator = false; // Scripts are not generators
            _allowIdentifierCache = context.AllowIdentifierCache;
            _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
            _isScriptMode = true;
        }

        // Lazy accessors
        private AsyncState AsyncStateRef => field ??= new AsyncState();
        private YieldState YieldStateRef => field ??= new YieldState();
        private IteratorState IteratorStateRef => field ??= new IteratorState();
        private TryCatchState TryCatchStateRef => _tryCatchState ??= new TryCatchState();
        private BreakableState BreakableStateRef => field ??= new BreakableState();
        private WithState WithStateRef => field ??= new WithState();

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
            var runner = new ExecutionPlanRunner(plan, environment, context);
            return runner.RunScriptInternal();
        }

        /// <summary>
        /// Internal method to run script without generator overhead.
        /// Returns the raw completion value, not an iterator result.
        /// </summary>
        private JsValue RunScriptInternal()
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

        /// <summary>
        /// Gets the final completion value, converting Unit sentinel to undefined.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            // Run the plan - for sync functions this completes immediately
            var result = ExecutePlan(ResumeMode.Next, JsValue.Undefined);

            // ExecutePlan returns an iterator result {value, done} for generators.
            // For sync execution, extract the raw value.
            // Handle both IteratorResultObject (lightweight) and JsObject (full) cases.
            if (result.TryGetObject<IteratorResultObject>(out var iteratorResult))
            {
                iteratorResult.TryGetProperty("value", out var value);
                return value;
            }

            if (result.TryGetObject<JsObject>(out var jsObject) &&
                jsObject.TryGetProperty("value", out var jsValue))
            {
                return jsValue;
            }

            // If no iterator result (shouldn't happen), return as-is
            return result;
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

                if (AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _))
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

        private JsEnvironment CreateExecutionEnvironment()
        {
            var description = _function.Name is { } name
                ? $"function* {name.Name}"
                : "generator function";

            var hasParameterExpressions = _function.HasParameterExpressions();
            var hoistPlan = ((IAstCacheable<HoistPlan>)_function.Body).GetOrCreateCache();
            var lexicalNamesRaw = hoistPlan.LexicalNames;
            var lexicalNames = lexicalNamesRaw.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(lexicalNamesRaw, ReferenceEqualityComparer<Symbol>.Instance);
            var catchParameterNamesRaw = hoistPlan.CatchParameterNames;
            var catchParameterNames = catchParameterNamesRaw.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(catchParameterNamesRaw, ReferenceEqualityComparer<Symbol>.Instance);
            var simpleCatchParameterNamesRaw = hoistPlan.SimpleCatchParameterNames;
            var simpleCatchParameterNames = simpleCatchParameterNamesRaw.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(simpleCatchParameterNamesRaw, ReferenceEqualityComparer<Symbol>.Instance);
            var bodyLexicalNames = lexicalNames.Count == 0
                ? lexicalNames
                : new HashSet<Symbol>(lexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            bodyLexicalNames.ExceptWith(simpleCatchParameterNames);

            var parameterNames = ((IAstCacheable<FunctionParameterNamesPlan>)_function).GetOrCreateCache()
                .ParameterNames;
            var blockedFunctionVarNames = bodyLexicalNames.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(bodyLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            foreach (var parameterName in parameterNames)
            {
                blockedFunctionVarNames.Add(parameterName);
            }

            JsEnvironment parameterEnvironment;
            JsEnvironment varEnvironment;
            var functionEnvironment = new JsEnvironment(_closure, true, _isStrict, _function.Source,
                description);
            if (hasParameterExpressions)
            {
                parameterEnvironment = new JsEnvironment(functionEnvironment, false, _isStrict, _function.Source,
                    description, isParameterEnvironment: true);

                varEnvironment = new JsEnvironment(parameterEnvironment, true, _isStrict, _function.Source,
                    description);
            }
            else
            {
                parameterEnvironment = functionEnvironment;
                varEnvironment = functionEnvironment;
            }

            var executionEnvironment = new JsEnvironment(varEnvironment, false, _isStrict,
                _function.Source, description, isBodyEnvironment: true);
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);

            // Initialize slots for generator-internal variables (iterator states, values, etc.) FIRST.
            // This must happen BEFORE hoisting lexical bindings because the IR uses 0-based slot indices.
            // Plan slots get indices 0, 1, 2... and hoisted lexical bindings get subsequent indices.
            // This enables O(1) slot-based access instead of dictionary lookups.
            // Use the plan's RootScopeId for all execution plan slots.
            if (_plan is { SlotCount: > 0, SlotSymbols.IsDefaultOrEmpty: false })
            {
                // Ensure we allocate enough slots to cover:
                // - Internal plan slots (SlotSymbols.Length)
                // - Root slot map entries (indices can be sparse)
                // - Explicit RootSlotCount from analysis (if present)
                var rootSlotMap = _plan.SafeRootSlotMap;
                var mapMax = rootSlotMap.Count > 0 ? rootSlotMap.Values.Max() + 1 : 0;
                var requiredSlots = Math.Max(Math.Max(_plan.RootSlotCount, _plan.SlotSymbols.Length), mapMax);
                if (requiredSlots == 0)
                {
                    requiredSlots = _plan.SlotCount;
                }

                var scopeLexicals = _plan.SafeScopeLexicalBindings;
                var rootLexicals = _plan.SafeRootLexicalBindings;
                if (rootLexicals.Count == 0 && scopeLexicals.TryGetValue(_plan.RootScopeId, out var fromRoot))
                {
                    rootLexicals = fromRoot;
                }

                executionEnvironment.ResetSlotLayoutForPlan(
                    requiredSlots,
                    rootSlotMap,
                    rootLexicals,
                    _plan.SlotSymbols,
                    _plan.LayoutId,
                    _plan.RootScopeId);
            }

            // ES2024 9.2.12 FunctionDeclarationInstantiation step 34-35:
            // Create TDZ bindings for lexical declarations (let/const) in the function environment.
            // This must happen BEFORE the body is evaluated so that closures that reference these
            // variables will find them in TDZ state and throw ReferenceError if accessed before initialization.
            // NOTE: We use TopLevelLexicalNames which excludes for-loop/for-of initializer variables
            // (those create their own per-iteration environments and should NOT be in function TDZ).
            // These bindings are added AFTER plan slots so they don't conflict with 0-based IR indices.
            var topLevelLexicalNames = hoistPlan.TopLevelLexicalNames;
            var lexicalDeclarationKinds = hoistPlan.LexicalDeclarationKinds;
            foreach (var lexicalName in topLevelLexicalNames)
            {
                if (!executionEnvironment.HasBinding(lexicalName))
                {
                    var isConst = lexicalDeclarationKinds.TryGetValue(lexicalName, out var c) && c;
                    executionEnvironment.DefineJsValue(lexicalName, JsValue.Uninitialized, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true, isConst: isConst);
                }
            }

            // Store YieldResumeContext reference in the environment for yield expressions
            var yieldState = YieldStateRef;

            var generatorContext = _realmState.CreateContext(
                ScopeKind.Function,
                DetermineGeneratorScopeMode());

            var boundThis = _thisValue;
            if (!_isStrict)
            {
                if (boundThis.IsNullish)
                {
                    boundThis = _realmState.Engine?.GlobalObject is { } go ? new JsValue(go) : JsValue.Undefined;
                }

                if (boundThis.IsNull)
                {
                    boundThis = new JsValue(new JsObject { RealmState = _realmState });
                }
                else if (!boundThis.TryGetObject<IJsPropertyAccessor>(out _) &&
                         !boundThis.IsNullish &&
                         !boundThis.TryGetObject<IIsHtmlDda>(out _))
                {
                    boundThis = JsValue.FromObjectUnsafe(ToObjectForDestructuringJsValue(boundThis, generatorContext));
                }
            }

            functionEnvironment.DefineJsValue(Symbol.This, boundThis);

            // For arrow functions with captured lexical this environment, define LexicalThisEnvironment
            // so super() calls can update the correct this binding in the original constructor
            if (_function.IsArrow && _lexicalThisEnvironment is not null)
            {
                functionEnvironment.DefineJsValue(Symbol.LexicalThisEnvironment,
                    JsValue.FromObjectUnsafe(_lexicalThisEnvironment));
            }

            // Define new.target for non-arrow functions so inner arrow functions can access it lexically
            if (!_function.IsArrow)
            {
                var newTargetValue = _newTarget.IsUndefined ? JsValue.Undefined : _newTarget;
                functionEnvironment.DefineJsValue(Symbol.NewTarget, newTargetValue, true, isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
            }

            functionEnvironment.DefineJsValue(Symbol.YieldResumeContextSymbol,
                JsValue.FromObjectUnsafe(yieldState.ResumeContext));
            functionEnvironment.DefineJsValue(Symbol.GeneratorInstanceSymbol, JsValue.FromObjectUnsafe(this));

            var superPrototype = _homeObject?.Prototype;
            if (superPrototype is null && boundThis.TryGetObject<JsObject>(out var thisObj))
            {
                superPrototype = thisObj.Prototype;
            }

            if (superPrototype is not null)
            {
                var superBinding = new SuperBinding(null, superPrototype, boundThis, true);
                functionEnvironment.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(superBinding));
            }

            var argumentsObject = _function.CreateArgumentsObject(_arguments, executionEnvironment, _realmState,
                _callable,
                _isStrict);
            parameterEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                isLexicalBinding: false);
            if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
            {
                functionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                    isLexicalBinding: false);
            }

            if (_function.Name is { } functionName && !_hasFunctionNameEnvironment)
            {
                parameterEnvironment.DefineJsValue(functionName, JsValue.FromObjectUnsafe(_callable), true,
                    isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            // ES spec order: bind parameters FIRST, then hoist function declarations
            // Function declarations should override parameter bindings with the same name
            _function.BindFunctionParameters(_arguments, parameterEnvironment, generatorContext);
            if (generatorContext.IsThrow)
            {
                var thrown = generatorContext.FlowValue;
                generatorContext.Clear();
                throw new ThrowSignal(thrown);
            }

            SyncParameterSlotsToPlan(executionEnvironment, parameterEnvironment, parameterNames);

            _function.Body.HoistVarDeclarations(executionEnvironment, generatorContext,
                lexicalNames: lexicalNames,
                catchParameterNames: catchParameterNames,
                simpleCatchParameterNames: simpleCatchParameterNames);

            if (generatorContext.IsThrow)
            {
                var thrown = generatorContext.FlowValue;
                generatorContext.Clear();
                throw new ThrowSignal(thrown);
            }

            if (generatorContext.IsReturn)
            {
                generatorContext.ClearReturn();
            }

            return executionEnvironment;
        }

        private static void SyncParameterSlotsToPlan(
            JsEnvironment executionEnvironment,
            JsEnvironment parameterEnvironment,
            ImmutableArray<Symbol> parameterNames)
        {
            if (parameterNames.IsDefaultOrEmpty || executionEnvironment._slots is null)
            {
                return;
            }

            foreach (var name in parameterNames)
            {
                if (!executionEnvironment.TryGetSlotIndex(name, out var slotIndex))
                {
                    continue;
                }

                var value = parameterEnvironment.GetJsValue(name);
                executionEnvironment.SetSlotDirect(slotIndex, value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private JsValue ExecutePlan(ResumeMode mode, JsValue resumeValue)
        {
            if (_plan is null)
            {
                throw new InvalidOperationException("No generator plan available.");
            }

            JsEnvironment environment;
            EvaluationContext context;

            // Fast path for non-generator, non-async functions - skip all generator/async machinery
            if (!_isGenerator && !_isAsync)
            {
                environment = EnsureExecutionEnvironment();
                context = EnsureEvaluationContext();
            }
            else
            {
                // Full generator/async path with state machine support
                if (_state == GeneratorState.Executing)
                {
                    _state = GeneratorState.Completed;
                    _done = true;
                    _programCounter = -1;
                    TryCatchStateRef.TryStack.Clear();
                    YieldStateRef.ResumeContext.Clear();
                    var throwContext = _context ??= _realmState.CreateContext(
                        ScopeKind.Function,
                        DetermineGeneratorScopeMode());
                    throw StandardLibrary.ThrowTypeError("Generator is already executing", throwContext, _realmState);
                }

                var wasStart = _state == GeneratorState.Start;
                if (_done || _state == GeneratorState.Completed)
                {
                    _done = true;
                    return FinishExternalCompletion(mode, resumeValue);
                }

                if (mode is ResumeMode.Throw or ResumeMode.Return && wasStart)
                {
                    _state = GeneratorState.Completed;
                    _done = true;
                    return FinishExternalCompletion(mode, resumeValue);
                }

                _state = GeneratorState.Executing;
                PreparePendingResumeValue(mode, resumeValue, wasStart);

                environment = EnsureExecutionEnvironment();

                // Track the environment we resumed with (if resuming from suspend).
                // This prevents returning it to the pool while we're still using it.
                IteratorStateRef.ResumedWithEnvironment = wasStart ? null : environment;
                context = EnsureEvaluationContext();

                // If we're resuming from a yield that happened during AST evaluation
                // (via StatementInstruction), handle based on the resume mode.
                _realmState.Logger?.LogInformation(
                    "ExecutePlan resume check: wasStart={WasStart} mode={Mode} YieldStateRef.LastYieldSourceStart={Start}",
                    wasStart, mode, YieldStateRef.LastYieldSourceStart);

                if (!wasStart && YieldStateRef.LastYieldSourceStart >= 0)
                {
                    switch (mode)
                    {
                        case ResumeMode.Next:
                            // For next(), set up resume state so the yield expression returns the resume value
                            SetYieldResumeValue(environment, resumeValue, YieldStateRef.LastYieldSourceStart,
                                YieldStateRef.LastYieldSourceEnd);
                            break;
                        case ResumeMode.Return:
                            // For return(), close any active iterators and complete the generator.
                            // Don't re-evaluate the statement - just close and return.
                            _realmState.Logger?.LogInformation("ExecutePlan: early CompleteReturn for Return mode");
                            YieldStateRef.LastYieldSourceStart = -1;
                            YieldStateRef.LastYieldSourceEnd = -1;
                            return CompleteReturn(resumeValue);
                    }
                    // For Throw mode, we'll let the normal flow handle it via AsyncStateRef.PendingResumeKind

                    YieldStateRef.LastYieldSourceStart = -1;
                    YieldStateRef.LastYieldSourceEnd = -1;
                }

                // Restore active with-scopes when resuming
                // The _activeWithScopes stack contains the slots in reverse order (bottom to top)
                // We need to restore environments from bottom to top
                if (WithStateRef.ActiveWithScopes.Count > 0)
                {
                    var scopesToRestore = WithStateRef.ActiveWithScopes.ToArray();
                    // The array is in stack order (top first), so reverse to get bottom-to-top order
                    for (var i = scopesToRestore.Length - 1; i >= 0; i--)
                    {
                        var slot = scopesToRestore[i];
                        if (TryGetSymbolValueJsValue(environment, slot, out var storedEnvValue) &&
                            storedEnvValue.TryGetObject<JsEnvironment>(out var storedWithEnv))
                        {
                            environment = storedWithEnv;
                        }
                    }
                }

                // If we are resuming after a pending await, thread the resolved
                // value into the per-site await state so subsequent evaluations
                // of the AwaitExpression see the fulfilled value instead of the
                // original promise object.
                if (_isAsync && AsyncStateRef.PendingAwaitKey is { } awaitKey)
                {
                    var (kind, value) = ConsumeResumeValue();
                    var isThrow = kind == ResumePayloadKind.Throw;

                    // Store the resolved value (or thrown error) in AwaitState so
                    // EvaluateAwaitInGenerator can retrieve it when re-evaluated.
                    if (kind == ResumePayloadKind.Value || isThrow)
                    {
                        if (environment.TryGetObject<AwaitState>(awaitKey, out var state))
                        {
                            state.HasResult = true;
                            state.IsThrow = isThrow;
                            state.Result = value;
                            environment.AssignJsValue(awaitKey, JsValue.FromObjectUnsafe(state));
                        }
                        else
                        {
                            var newState = new AwaitState { HasResult = true, IsThrow = isThrow, Result = value };
                            if (environment.HasBinding(awaitKey))
                            {
                                environment.AssignJsValue(awaitKey, JsValue.FromObjectUnsafe(newState));
                            }
                            else
                            {
                                environment.DefineJsValue(awaitKey, JsValue.FromObjectUnsafe(newState));
                            }
                        }
                    }

                    AsyncStateRef.PendingAwaitKey = null;
                }
            }

            // Cache debug mode check outside the hot loop - avoid virtual property access per iteration
            var debugMode = _realmState.Options.DebugMode;
            var instructions = _plan.Instructions;
            var instructionsLength = instructions.Length;

            // Get underlying array from ImmutableArray and reference to start - enables bounds-check-free access
            var instructionsArray = ImmutableCollectionsMarshal.AsArray(instructions)!;
            ref var instructionsRef = ref MemoryMarshal.GetArrayDataReference(instructionsArray);

            // Cache try-catch state check - avoid repeated null checks in hot loop
            var hasTryCatchState = _tryCatchState is not null;

            bool continueAfterCatch;
            do
            {
                continueAfterCatch = false;
                try
                {
                    while ((uint)_programCounter < (uint)instructionsLength)
                    {
                        // Check if HandleAbruptCompletion restored the environment (e.g., jumping to catch handler)
                        // This ensures block-scoped bindings from inside the try are no longer visible.
                        // Only check when TryCatchState has been allocated.
                        if (hasTryCatchState && _tryCatchState!.RestoredEnvironmentFromTry is { } restored)
                        {
                            environment = restored;
                            _tryCatchState.RestoredEnvironmentFromTry = null;
                        }

                        _currentInstructionIndex = _programCounter;
                        // Use profiling wrapper to measure instruction fetch cost
                        var instruction = ProfileFetchInstruction(ref instructionsRef, _programCounter);
                        var instructionKind = instruction.Kind;

                        // Trace instruction execution when debug logging is enabled
                        if (debugMode)
                        {
                            _realmState.Logger?.LogTrace(
                                "[IR:{PC,3}] {Instruction}",
                                _programCounter,
                                ExecutionPlanPrinter.FormatInstruction(instruction));
                        }

                        // Detailed IR execution trace with environment depth
#pragma warning disable CS0162 // Unreachable code detected (TraceIrExecution is compile-time constant)
                        if (JsEngineConstants.TraceIrExecution && _realmState.Logger is not null)
                        {
                            ExecutionPlanPrinter.TraceInstruction(
                                _realmState.Logger,
                                _programCounter,
                                instruction,
                                environment.Depth,
                                environment.ScopeId,
                                environment.GetHashCode()
                            );
                        }
#pragma warning restore CS0162

                        // ═══════════════════════════════════════════════════════════════════════════
                        // FAST PATH: Inline the hottest instructions to avoid switch dispatch overhead
                        // For a 1M iteration loop, this saves millions of switch table lookups
                        // ═══════════════════════════════════════════════════════════════════════════

                        // Jump is the simplest - just update program counter
                        if (instructionKind == InstructionKind.Jump)
                        {
                            _programCounter = ProfileHandleJump(Unsafe.As<JumpInstruction>(instruction));
                            continue;
                        }

                        // Branch is hot - handle before switch dispatch
                        if (instructionKind == InstructionKind.Branch)
                        {
                            var result = HandleBranchFastPath(Unsafe.As<BranchInstruction>(instruction), environment, context, out var returnValue);
                            if (result == InstructionResult.Return) return returnValue;
                            continue;
                        }

                        switch (instructionKind)
                        {
                            case InstructionKind.Statement:
                            {
                                var result = HandleStatement(Unsafe.As<StatementInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.Throw:
                            {
                                var result = HandleThrow(Unsafe.As<ThrowInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.EvaluateAndDiscard:
                            {
                                var result = HandleEvaluateAndDiscard(Unsafe.As<EvaluateAndDiscardInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.BinaryOp:
                            {
                                var result = HandleBinaryOp(Unsafe.As<BinaryOpInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.IncrementSlot:
                            {
                                var result = HandleIncrementSlot(Unsafe.As<IncrementSlotInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.CompoundAssignmentSlot:
                            {
                                var result = HandleCompoundAssignmentSlot(Unsafe.As<CompoundAssignmentSlotInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.FunctionDeclaration:
                            {
                                HandleFunctionDeclaration(Unsafe.As<FunctionDeclarationInstruction>(instruction), out _);
                                continue;
                            }

                            case InstructionKind.ClassDeclaration:
                            {
                                var result = HandleClassDeclaration(Unsafe.As<ClassDeclarationInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.SimpleVariableDeclaration:
                            {
                                var result = HandleSimpleVariableDeclaration(Unsafe.As<SimpleVariableDeclarationInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.PushEnvironment:
                            {
                                HandlePushEnvironment(Unsafe.As<PushEnvironmentInstruction>(instruction), ref environment, out _);
                                continue;
                            }

                            case InstructionKind.PopEnvironment:
                            {
                                HandlePopEnvironment(Unsafe.As<PopEnvironmentInstruction>(instruction), ref environment, out _);
                                continue;
                            }

                            case InstructionKind.Yield:
                            {
                                var result = HandleYield(Unsafe.As<YieldInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.YieldStar:
                            {
                                var result = HandleYieldStar(Unsafe.As<YieldStarInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.StoreResumeValue:
                            {
                                var result = HandleStoreResumeValue(Unsafe.As<StoreResumeValueInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.EnterTry:
                            {
                                HandleEnterTry(Unsafe.As<EnterTryInstruction>(instruction), environment, out _);
                                continue;
                            }

                            case InstructionKind.EnterCatch:
                            {
                                HandleEnterCatch(Unsafe.As<EnterCatchInstruction>(instruction), ref environment, out _);
                                continue;
                            }

                            case InstructionKind.EnterCatchWithDestructuring:
                            {
                                var result = HandleEnterCatchWithDestructuring(Unsafe.As<EnterCatchWithDestructuringInstruction>(instruction), ref environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.LeaveTry:
                            {
                                HandleLeaveTry(Unsafe.As<LeaveTryInstruction>(instruction), out _);
                                continue;
                            }

                            case InstructionKind.BreakableEnter:
                            {
                                HandleBreakableEnter(Unsafe.As<BreakableEnterInstruction>(instruction), out _);
                                continue;
                            }

                            case InstructionKind.BreakableExit:
                            {
                                HandleBreakableExit(Unsafe.As<BreakableExitInstruction>(instruction), out _);
                                continue;
                            }

                            case InstructionKind.EndFinally:
                            {
                                var result = HandleEndFinally(Unsafe.As<EndFinallyInstruction>(instruction), ref environment, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.IteratorInit:
                            {
                                var result = HandleIteratorInit(Unsafe.As<IteratorInitInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.IteratorMoveNext:
                            {
                                var result = HandleIteratorMoveNext(Unsafe.As<IteratorMoveNextInstruction>(instruction), ref environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.Jump:
                            {
                                HandleJumpSwitch(Unsafe.As<JumpInstruction>(instruction), out _);
                                continue;
                            }

                            case InstructionKind.Branch:
                            {
                                var result = HandleBranchSwitch(Unsafe.As<BranchInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.Break:
                            {
                                HandleBreak(Unsafe.As<BreakInstruction>(instruction), ref environment, out _);
                                continue;
                            }

                            case InstructionKind.Continue:
                            {
                                HandleContinue(Unsafe.As<ContinueInstruction>(instruction), ref environment, out _);
                                continue;
                            }

                            case InstructionKind.Return:
                            {
                                var result = HandleReturn(Unsafe.As<ReturnInstruction>(instruction), environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.EnterWith:
                            {
                                var result = HandleEnterWith(Unsafe.As<EnterWithInstruction>(instruction), ref environment, context, out var returnValue);
                                if (result == InstructionResult.Return) return returnValue;
                                continue;
                            }

                            case InstructionKind.LeaveWith:
                            {
                                HandleLeaveWith(Unsafe.As<LeaveWithInstruction>(instruction), ref environment, out _);
                                continue;
                            }

                            case InstructionKind.IteratorClose:
                            {
                                HandleIteratorClose(Unsafe.As<IteratorCloseInstruction>(instruction), environment, out _);
                                continue;
                            }

                            case InstructionKind.SetCompletionValue:
                            {
                                HandleSetCompletionValue(Unsafe.As<SetCompletionValueInstruction>(instruction), out _);
                                continue;
                            }

                            default:
                                throw new InvalidOperationException(
                                    $"Unsupported generator instruction kind {instruction.Kind}");
                        }
                    }
                }
                catch (ThrowSignal signal)
                {
                    // A ThrowSignal was thrown from code evaluation (e.g., from EvaluateAwaitInGenerator
                    // when resuming after a rejected promise). Route it through HandleAbruptCompletion
                    // to check if there's a JS catch block that can handle it.

                    // Clear any stale throw state from context before handling - this ensures
                    // finally blocks don't see the stale throw state
                    if (context.IsThrow)
                    {
                        context.Clear();
                    }

                    if (HandleAbruptCompletion(AbruptKind.Throw, signal.ThrownValue, environment))
                    {
                        // A catch block will handle this - continue execution from the catch handler
                        if (_programCounter == _currentInstructionIndex)
                        {
                            // When already inside a finally block, ensure forward progress
                            // instead of re-executing the same instruction repeatedly.
                            _programCounter = _currentInstructionIndex + 1;
                        }

                        continueAfterCatch = true;
                        continue;
                    }

                    // No catch block - mark as completed and re-throw
                    _state = GeneratorState.Completed;
                    _done = true;
                    _programCounter = -1;
                    TryCatchStateRef.TryStack.Clear();
                    YieldStateRef.ResumeContext.Clear();
                    throw;
                }
                catch
                {
                    _state = GeneratorState.Completed;
                    _done = true;
                    _programCounter = -1;
                    TryCatchStateRef.TryStack.Clear();
                    YieldStateRef.ResumeContext.Clear();
                    throw;
                }
            } while (continueAfterCatch);

            _state = GeneratorState.Completed;
            _done = true;
            TryCatchStateRef.TryStack.Clear();
            return CreateIteratorResult(JsValue.Undefined, true);
        }

        private JsEnvironment EnsureExecutionEnvironment()
        {
            if (_executionEnvironment is null)
            {
                _executionEnvironment = CreateExecutionEnvironment();
                LogRootScopeIdOnce();
            }

            return _executionEnvironment;
        }

        private void LogRootScopeIdOnce()
        {
            if (_rootScopeLogged || _realmState.Logger is null || _plan is null)
            {
                return;
            }

            _realmState.Logger.LogInformation(
                "ExecutionPlanRunner scopeId={RootScopeId}",
                JsEnvironment.FormatScopeIdForLog(_plan.RootScopeId));
            _rootScopeLogged = true;
        }

        private EvaluationContext EnsureEvaluationContext()
        {
            if (_context is null)
            {
                _context = _realmState.CreateContext(
                    ScopeKind.Function,
                    DetermineGeneratorScopeMode());
            }
            else
            {
                _context.Clear();
            }

            _context.AllowIdentifierCache = _allowIdentifierCache;
            ApplyPrivateNameScopes();

            return _context;
        }

        private void ApplyPrivateNameScopes()
        {
            if (_privateScopesApplied || _context is null)
            {
                return;
            }

            if (!_capturedPrivateNameScopes.IsDefaultOrEmpty)
            {
                _context.EnterPrivateNameScopes(_capturedPrivateNameScopes);
            }

            if (_privateNameScope is not null)
            {
                _context.EnterPrivateNameScope(_privateNameScope);
            }

            _privateScopesApplied = true;
        }

        private ScopeMode DetermineGeneratorScopeMode()
        {
            return _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
        }

        internal JsValue EvaluateAwaitInGenerator(AwaitExpression expression, JsEnvironment environment,
            EvaluationContext context)
        {
            // When not executing under async-aware stepping, fall back to the
            // legacy blocking helper so synchronous generators remain usable.
            if (!AsyncStateRef.AsyncStepMode)
            {
                // Keep as JsValue to avoid boxing round trips
                var awaitedValueSync = expression.Expression.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return awaitedValueSync;
                }

                // awaitedValueSync is already JsValue
                if (!TryAwaitPromise(awaitedValueSync, context, out var resolvedSync))
                {
                    return resolvedSync;
                }

                return resolvedSync;
            }

            // Async-aware mode: use per-site await state so we don't re-run
            // side-effecting expressions after the promise has resolved.
            var awaitKey = expression.GetAwaitStateKey();
            if (awaitKey is not null &&
                environment.TryGetObject<AwaitState>(awaitKey, out var state) &&
                state.HasResult)
            {
                // Await has already completed; reuse the resolved value once
                // for this resume, then clear the flag so future iterations
                // (e.g. in loops) see a fresh await.
                var result = state.Result;
                var isThrow = state.IsThrow;
                environment.AssignJsValue(awaitKey, JsValue.FromObjectUnsafe(new AwaitState()));
                AsyncStateRef.PendingAwaitKey = null;

                // If the await was rejected, throw at this point so the
                // generator's try-catch can handle it.
                if (isThrow)
                {
                    throw new ThrowSignal(result);
                }

                return result;
            }

            // Keep as JsValue to avoid boxing round trips
            var awaitedValue = expression.Expression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return awaitedValue;
            }

            if (awaitKey is not null)
            {
                var existingState = JsValue.FromObjectUnsafe(new AwaitState());

                if (environment.HasBinding(awaitKey))
                {
                    environment.AssignJsValue(awaitKey, existingState);
                }
                else
                {
                    environment.DefineJsValue(awaitKey, existingState);
                }
            }

            // Async-aware mode: surface promise-like values as pending steps
            // so AsyncGeneratorInvoker can resume via the event queue.
            // awaitedValue is already JsValue
            if (TryResolvePromiseOrYield(awaitedValue, context, out var resolved))
            {
                return resolved;
            }

            if (!AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _) || awaitKey is null)
            {
                return resolved;
            }

            // Remember which await site is pending so we can stash the
            // resolved value on resume.
            AsyncStateRef.PendingAwaitKey = awaitKey;
            _state = GeneratorState.Suspended;
            _programCounter = _currentInstructionIndex;
            context.SetPendingAwait();
            return JsValue.Undefined;

            // If TryResolvePromiseOrYield reported an error via the context,
            // let the caller observe the pending throw/return.
        }

        private bool TryResolvePromiseOrYield(JsValue candidate, EvaluationContext context, out JsValue resolvedValue)
        {
            var pendingPromise = AsyncStateRef.PendingPromise;
            var result = AwaitScheduler.TryResolvePromiseOrYield(candidate, AsyncStateRef.AsyncStepMode,
                ref pendingPromise,
                context, out var resolvedObj);
            AsyncStateRef.PendingPromise = pendingPromise;
            // resolvedObj is already JsValue from the scheduler
            resolvedValue = resolvedObj;
            return result;
        }

        private bool TryHandlePendingAwait(EvaluationContext context, out JsValue result,
            JsEnvironment? currentEnvironment = null)
        {
            if (!context.IsPendingAwait)
            {
                result = JsValue.Undefined;
                return false;
            }

            context.Clear();
            _state = GeneratorState.Suspended;

            // Save the current environment so that when the async function resumes after await,
            // it uses the correct per-iteration environment (for loops with let bindings).
            // This is critical for `continue` to work correctly in async loops.
            if (currentEnvironment != null)
            {
                _executionEnvironment = currentEnvironment;
            }

            // In async-step mode, surface the pending promise directly to the
            // caller without allocating an iterator result object.
            result = AsyncStateRef.AsyncStepMode
                ? JsValue.Undefined
                : CreateIteratorResult(JsValue.Undefined, false);
            return true;
        }

        /// <summary>
        /// Handles throw state from context by attempting abrupt completion handling.
        /// Returns true if the throw was handled and the caller should continue the loop.
        /// Throws ThrowSignal if the throw could not be handled.
        /// </summary>
        private bool TryHandleContextThrow(EvaluationContext context, JsEnvironment environment)
        {
            if (!context.IsThrow) return false;

            var thrownValue = context.FlowValue;
            context.Clear();
            if (HandleAbruptCompletion(AbruptKind.Throw, thrownValue, environment))
            {
                return true;
            }

            TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(thrownValue);
        }

        /// <summary>
        /// Result of HandleContextSignals indicating what action the caller should take.
        /// </summary>
        private enum SignalAction { None, Continue, Return }

        /// <summary>
        /// Handles async await, throw, return, and yield signals from context.
        /// Returns the action the caller should take and any result value.
        /// For Return action, the result should be returned from the caller.
        /// For Continue action, the caller should continue the loop.
        /// For None action, the caller should fall through to normal processing.
        /// May throw ThrowSignal if a throw cannot be handled.
        /// </summary>
        private (SignalAction action, JsValue result) HandleContextSignals(
            EvaluationContext context,
            JsEnvironment environment,
            int nextInstructionIndex)
        {
            if (_isAsync && TryHandlePendingAwait(context, out var pendingResult, environment))
            {
                return (SignalAction.Return, pendingResult);
            }

            if (context.IsThrow)
            {
                var thrown = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                {
                    if (_programCounter == _currentInstructionIndex)
                    {
                        _programCounter = nextInstructionIndex;
                    }

                    return (SignalAction.Continue, default);
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrown);
            }

            if (context.IsReturn)
            {
                var returnSignalValue = context.FlowValue;
                context.ClearReturn();
                if (!HandleAbruptCompletion(AbruptKind.Return, returnSignalValue, environment))
                {
                    return (SignalAction.Return, CompleteReturn(returnSignalValue));
                }

                if (_programCounter == _currentInstructionIndex)
                {
                    _programCounter = nextInstructionIndex;
                }

                return (SignalAction.Continue, default);
            }

            if (context.IsYield)
            {
                var yieldedSignalValue = context.FlowValue;
                var iteratorResultObject = (context.CurrentSignal as YieldCompletionSignal)
                    ?.IteratorResultObject;
                RecordYield(context, environment);
                context.Clear();
                _state = GeneratorState.Suspended;
                var result = iteratorResultObject is not null
                    ? new JsValue(JsValueKind.Object, 0.0, iteratorResultObject)
                    : CreateIteratorResult(yieldedSignalValue, false);
                return (SignalAction.Return, result);
            }

            return (SignalAction.None, default);
        }

        /// <summary>
        /// Handles the common logic when TryResolvePromiseOrYield returns false for iterator values.
        /// Manages async step mode suspension, throw handling, and environment restoration.
        /// Returns true if the caller should return the suspension result; false if caller should continue loop.
        /// </summary>
        private bool TryHandleAwaitSuspension(
            IteratorDriverState driverState,
            JsVariable iterVar,
            IteratorMoveNextInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context,
            int iteratorIndex,
            out JsValue suspendResult)
        {
            if (AsyncStateRef.AsyncStepMode &&
                AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _))
            {
                driverState.AwaitingValue = true;
                var iterState = driverState.AsJsValue;
                if (iterVar.IsValid)
                {
                    iterVar.Write(iterState);
                }
                else
                {
                    StoreValueBySlot(environment, instruction.IteratorSlot,
                        instruction.IteratorSlotIndex, iterState);
                }

                _executionEnvironment = environment;
                _state = GeneratorState.Suspended;
                _programCounter = iteratorIndex;
                suspendResult = CreateIteratorResult(JsValue.Undefined, false);
                return true;
            }

            if (context.IsThrow)
            {
                var thrownValue = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, thrownValue, environment))
                {
                    suspendResult = JsValue.Undefined;
                    return false;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrownValue);
            }

            // Restore environment to enclosing scope
            if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv)
            {
                environment = enclosingEnv;
            }

            IteratorStateRef.CurrentDriverState = null;
            _programCounter = instruction.BreakIndex;
            suspendResult = JsValue.Undefined;
            return false;
        }

        /// <summary>
        /// Result of an instruction handler for control flow.
        /// </summary>
        private enum InstructionResult
        {
            /// <summary>Continue to next instruction (normal flow).</summary>
            Continue,
            /// <summary>Return from ExecutePlan with a value.</summary>
            Return,
            /// <summary>An exception was thrown (already handled).</summary>
            Throw
        }


    }
}
