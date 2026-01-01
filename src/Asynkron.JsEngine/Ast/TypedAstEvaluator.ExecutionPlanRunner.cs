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
        private readonly bool _isStrict;
        private readonly ExecutionPlan? _plan;
        private readonly PrivateNameScope? _privateNameScope;
        private readonly RealmState _realmState;
        private readonly JsValue _thisValue;
        private readonly JsValue _newTarget;
        private readonly JsEnvironment? _lexicalThisEnvironment;
        private readonly bool _isScriptMode;
        private EvaluationContext? _context;
        private int _currentInstructionIndex;
        private bool _done;
        private JsEnvironment? _executionEnvironment;
        private bool _privateScopesApplied;
        private int _programCounter;
        private GeneratorState _state = GeneratorState.Start;
        private JsValue _scriptCompletionValue = JsValue.Unit;

        // Lazy state objects - only allocated when needed
        // TryCatchState needs explicit backing field for hot-path null check without allocation
        private TryCatchState? _tryCatchState;

        // Lazy accessors
        private AsyncState AsyncStateRef => field ??= new AsyncState();
        private YieldState YieldStateRef => field ??= new YieldState();
        private IteratorState IteratorStateRef => field ??= new IteratorState();
        private TryCatchState TryCatchStateRef => _tryCatchState ??= new TryCatchState();
        private BreakableState BreakableStateRef => field ??= new BreakableState();
        private WithState WithStateRef => field ??= new WithState();

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
            _arguments = Array.Empty<JsValue>();
            _callable = null!;
            _function = null!;
            _thisValue = context.RealmState.Engine?.GlobalObject is { } go
                ? new JsValue(go)
                : JsValue.Undefined;
            _isStrict = environment.IsStrict;
            _allowIdentifierCache = context.AllowIdentifierCache;
            _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
            _isScriptMode = true;
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
            functionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
            if (hasParameterExpressions)
            {
                parameterEnvironment = new JsEnvironment(functionEnvironment, false, _isStrict, _function.Source,
                    description, isParameterEnvironment: true);
                parameterEnvironment.SetBodyLexicalNames(bodyLexicalNames);

                varEnvironment = new JsEnvironment(parameterEnvironment, true, _isStrict, _function.Source,
                    description);
                varEnvironment.SetBodyLexicalNames(bodyLexicalNames);
            }
            else
            {
                parameterEnvironment = functionEnvironment;
                varEnvironment = functionEnvironment;
            }

            var executionEnvironment = new JsEnvironment(varEnvironment, false, _isStrict,
                _function.Source, description, isBodyEnvironment: true);
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);

            // ES2024 9.2.12 FunctionDeclarationInstantiation step 34-35:
            // Create TDZ bindings for lexical declarations (let/const) in the function environment.
            // This must happen BEFORE the body is evaluated so that closures that reference these
            // variables will find them in TDZ state and throw ReferenceError if accessed before initialization.
            // NOTE: We use TopLevelLexicalNames which excludes for-loop/for-of initializer variables
            // (those create their own per-iteration environments and should NOT be in function TDZ).
            var topLevelLexicalNames = hoistPlan.TopLevelLexicalNames;
            var lexicalDeclarationKinds = hoistPlan.LexicalDeclarationKinds;
            foreach (var lexicalName in topLevelLexicalNames)
            {
                if (!executionEnvironment.HasBinding(lexicalName))
                {
                    var isConst = lexicalDeclarationKinds.TryGetValue(lexicalName, out var c) && c;
                    executionEnvironment.DefineJsValue(lexicalName, JsValue.Uninitialized, isLexical: true,
                        blocksFunctionScopeOverride: true, isConst: isConst);
                }
            }

            // Store YieldResumeContext reference in the environment for yield expressions
            var yieldState = YieldStateRef;

            // Initialize slots for generator-internal variables (iterator states, values, etc.)
            // This enables O(1) slot-based access instead of dictionary lookups
            // ScopeId = 0 is used for execution plan slots (matches stamped IdentifierExpressions)
            if (_plan is { SlotCount: > 0, SlotSymbols.IsDefaultOrEmpty: false })
            {
                executionEnvironment.InitializeSlots(_plan.SlotCount, scopeId: 0);
                var slotMap = ImmutableDictionary.CreateBuilder<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
                for (var i = 0; i < _plan.SlotSymbols.Length; i++)
                {
                    slotMap[_plan.SlotSymbols[i]] = i;
                }
                executionEnvironment.SetSlotMap(slotMap.ToImmutable());
            }

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
                functionEnvironment.DefineJsValue(Symbol.NewTarget, newTargetValue, true, isLexical: true,
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

            var argumentsObject = _function.CreateArgumentsObject(_arguments, parameterEnvironment, _realmState,
                _callable,
                _isStrict);
            parameterEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                isLexical: false);
            if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
            {
                functionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                    isLexical: false);
            }

            if (_function.Name is { } functionName && !_hasFunctionNameEnvironment)
            {
                parameterEnvironment.DefineJsValue(functionName, JsValue.FromObjectUnsafe(_callable), true,
                    isLexical: true, blocksFunctionScopeOverride: true);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue ExecutePlan(ResumeMode mode, JsValue resumeValue)
        {
            if (_plan is null)
            {
                throw new InvalidOperationException("No generator plan available.");
            }

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

            var environment = EnsureExecutionEnvironment();

            // Track the environment we resumed with (if resuming from suspend).
            // This prevents returning it to the pool while we're still using it.
            IteratorStateRef.ResumedWithEnvironment = wasStart ? null : environment;
            var context = EnsureEvaluationContext();

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
                        SetYieldResumeValue(environment, resumeValue, YieldStateRef.LastYieldSourceStart, YieldStateRef.LastYieldSourceEnd);
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
            if (AsyncStateRef.PendingAwaitKey is { } awaitKey)
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

            // Cache debug mode check outside the hot loop - avoid virtual property access per iteration
            var debugMode = _realmState.Options.DebugMode;
            var instructions = _plan.Instructions;
            var instructionsLength = instructions.Length;

            bool continueAfterCatch;
            do
            {
                continueAfterCatch = false;
                try
                {
                    while (_programCounter >= 0 && _programCounter < instructionsLength)
                    {
                        // Check if HandleAbruptCompletion restored the environment (e.g., jumping to catch handler)
                        // This ensures block-scoped bindings from inside the try are no longer visible.
                        // Only check when TryCatchState has been allocated (_tryCatchState is not null).
                        if (_tryCatchState is { RestoredEnvironmentFromTry: { } restored })
                        {
                            environment = restored;
                            _tryCatchState.RestoredEnvironmentFromTry = null;
                        }

                        _currentInstructionIndex = _programCounter;
                        var instruction = instructions[_programCounter];
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
                            _programCounter = Unsafe.As<JumpInstruction>(instruction).TargetIndex;
                            continue;
                        }

                        // Branch is hot - inline the entire handler to avoid switch dispatch
                        if (instructionKind == InstructionKind.Branch)
                        {
                            var branchInstruction = Unsafe.As<BranchInstruction>(instruction);
                            var testValue = branchInstruction.Condition.EvaluateExpression(environment, context);

                            // Check for pending await (async code)
                            if (TryHandlePendingAwait(context, out var pendingBranchResult, environment))
                            {
                                return pendingBranchResult;
                            }

                            // Check for throw
                            if (context.IsThrow)
                            {
                                var thrownBranch = context.FlowValue;
                                context.Clear();
                                if (HandleAbruptCompletion(AbruptKind.Throw, thrownBranch, environment))
                                {
                                    continue;
                                }

                                TryCatchStateRef.TryStack.Clear();
                                throw new ThrowSignal(thrownBranch);
                            }

                            // Normal path: branch based on condition
                            _programCounter = testValue.IsTruthy
                                ? branchInstruction.ConsequentIndex
                                : branchInstruction.AlternateIndex;
                            continue;
                        }

                        switch (instructionKind)
                        {
                            case InstructionKind.Statement:
                            {
                                var statementInstruction = Unsafe.As<StatementInstruction>(instruction);
                                var stmtResult = statementInstruction.Statement.EvaluateStatementJsValue(environment, context);
                                // In script mode, track the completion value (per ES spec, block completion is last statement value)
                                // Per UpdateEmpty semantics: if result is Unit (empty), do NOT update completion value.
                                // Empty completions preserve the previous completion value.
                                // Only update when the statement actually produces a value (non-Unit).
                                if (_isScriptMode && !stmtResult.IsUnit)
                                {
                                    _scriptCompletionValue = stmtResult;
                                }
                                if (TryHandlePendingAwait(context, out var pendingResult, environment))
                                {
                                    return pendingResult;
                                }

                                if (context.IsThrow)
                                {
                                    var thrown = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                                    {
                                        if (_programCounter == _currentInstructionIndex)
                                        {
                                            _programCounter = statementInstruction.Next;
                                        }

                                        continue;
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
                                        return CompleteReturn(returnSignalValue);
                                    }

                                    if (_programCounter == _currentInstructionIndex)
                                    {
                                        _programCounter = statementInstruction.Next;
                                    }

                                    continue;
                                }

                                if (context.IsYield)
                                {
                                    var yieldedSignalValue = context.FlowValue;
                                    // Check if the yield signal includes an original iterator result object (from yield*)
                                    var iteratorResultObject = (context.CurrentSignal as YieldCompletionSignal)
                                        ?.IteratorResultObject;
                                    RecordYield(context, environment);
                                    context.Clear();
                                    _state = GeneratorState.Suspended;
                                    // If we have an original iterator result object, return it to preserve done property
                                    return iteratorResultObject is not null
                                        ? new JsValue(JsValueKind.Object, 0.0, iteratorResultObject)
                                        : CreateIteratorResult(yieldedSignalValue, false);
                                }

                                // Handle break/continue signals from AST-evaluated code inside loops
                                if (context.IsBreak || context.IsContinue)
                                {
                                    var isBreak = context.IsBreak;
                                    var label = (context.CurrentSignal as BreakCompletionSignal)?.Label
                                             ?? (context.CurrentSignal as ContinueCompletionSignal)?.Label;
                                    context.Clear();

                                    var target = FindBreakableTarget(label, isBreak);
                                    if (target >= 0)
                                    {
                                        _programCounter = target;
                                        continue;
                                    }
                                    // If no target found, the signal should propagate (shouldn't happen if loop stack is correct)
                                    throw new InvalidOperationException($"No loop target found for {(isBreak ? "break" : "continue")}{(label is not null ? $" {label.Name}" : "")}");
                                }

                                _programCounter = statementInstruction.Next;
                                continue;
                            }

                            case InstructionKind.Throw:
                            {
                                var throwInstruction = Unsafe.As<ThrowInstruction>(instruction);
                                // Evaluate the throw expression and throw it
                                var throwValue = throwInstruction.Expression.EvaluateExpression(environment, context);
                                if (TryHandlePendingAwait(context, out var pendingThrowResult, environment))
                                {
                                    return pendingThrowResult;
                                }

                                // If evaluating the expression already threw, handle that
                                if (context.IsThrow)
                                {
                                    var existingThrown = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, existingThrown, environment))
                                    {
                                        // If PC changed (jumped to catch/finally), continue
                                        if (_programCounter != _currentInstructionIndex)
                                        {
                                            continue;
                                        }

                                        // PC didn't change - we're inside a finally and updated pending.
                                        // The finally ends abruptly, pop frame and re-propagate.
                                        if (TryCatchStateRef.TryStack.Count > 0)
                                        {
                                            TryCatchStateRef.TryStack.Pop();
                                            if (HandleAbruptCompletion(AbruptKind.Throw, existingThrown, environment))
                                            {
                                                continue;
                                            }
                                        }
                                    }

                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(existingThrown);
                                }

                                // Now throw the evaluated value
                                if (HandleAbruptCompletion(AbruptKind.Throw, throwValue, environment))
                                {
                                    // If PC changed (jumped to catch/finally), continue
                                    if (_programCounter != _currentInstructionIndex)
                                    {
                                        continue;
                                    }

                                    // PC didn't change - we're inside a finally and updated pending.
                                    // The finally ends abruptly, pop frame and re-propagate.
                                    if (TryCatchStateRef.TryStack.Count > 0)
                                    {
                                        TryCatchStateRef.TryStack.Pop();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, throwValue, environment))
                                        {
                                            continue;
                                        }
                                    }
                                }

                                TryCatchStateRef.TryStack.Clear();
                                throw new ThrowSignal(throwValue);
                            }

                            case InstructionKind.EvaluateAndDiscard:
                            {
                                var evaluateInstruction = Unsafe.As<EvaluateAndDiscardInstruction>(instruction);
                                // Evaluate the expression
                                var evaluatedValue = evaluateInstruction.Expression.EvaluateExpression(environment, context);
                                // In script mode, track the completion value (per ES spec, script completion is last expression value)
                                // SuppressCompletionValue is true for loop update expressions (per ES spec, update expressions don't contribute)
                                if (_isScriptMode && !evaluateInstruction.SuppressCompletionValue)
                                {
                                    _scriptCompletionValue = evaluatedValue;
                                }
                                if (TryHandlePendingAwait(context, out var pendingEvalResult, environment))
                                {
                                    return pendingEvalResult;
                                }

                                if (context.IsThrow)
                                {
                                    var evalThrown = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, evalThrown, environment))
                                    {
                                        if (_programCounter == _currentInstructionIndex)
                                        {
                                            _programCounter = evaluateInstruction.Next;
                                        }

                                        continue;
                                    }

                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(evalThrown);
                                }

                                if (context.IsReturn)
                                {
                                    var returnSignalValue = context.FlowValue;
                                    context.ClearReturn();
                                    if (!HandleAbruptCompletion(AbruptKind.Return, returnSignalValue, environment))
                                    {
                                        return CompleteReturn(returnSignalValue);
                                    }

                                    if (_programCounter == _currentInstructionIndex)
                                    {
                                        _programCounter = evaluateInstruction.Next;
                                    }

                                    continue;
                                }

                                if (context.IsYield)
                                {
                                    var yieldedSignalValue = context.FlowValue;
                                    // Check if the yield signal includes an original iterator result object (from yield*)
                                    var iteratorResultObject = (context.CurrentSignal as YieldCompletionSignal)
                                        ?.IteratorResultObject;
                                    RecordYield(context, environment);
                                    context.Clear();
                                    _state = GeneratorState.Suspended;
                                    // If we have an original iterator result object, return it to preserve done property
                                    return iteratorResultObject is not null
                                        ? new JsValue(JsValueKind.Object, 0.0, iteratorResultObject)
                                        : CreateIteratorResult(yieldedSignalValue, false);
                                }

                                _programCounter = evaluateInstruction.Next;
                                continue;
                            }

                            case InstructionKind.BinaryOp:
                            {
                                var binaryOpInstruction = Unsafe.As<BinaryOpInstruction>(instruction);
                                // Fast path for binary operations - evaluate left and right, apply operator
                                var binLeft = binaryOpInstruction.Left.EvaluateExpression(environment, context);
                                if (context.ShouldStopEvaluation)
                                {
                                    if (TryHandlePendingAwait(context, out var pendingBinLeftResult, environment))
                                    {
                                        return pendingBinLeftResult;
                                    }
                                    if (context.IsThrow)
                                    {
                                        var binThrown = context.FlowValue;
                                        context.Clear();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, binThrown, environment))
                                        {
                                            continue;
                                        }
                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(binThrown);
                                    }
                                }
                                var binRight = binaryOpInstruction.Right.EvaluateExpression(environment, context);
                                if (context.ShouldStopEvaluation)
                                {
                                    if (TryHandlePendingAwait(context, out var pendingBinRightResult, environment))
                                    {
                                        return pendingBinRightResult;
                                    }
                                    if (context.IsThrow)
                                    {
                                        var binThrown = context.FlowValue;
                                        context.Clear();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, binThrown, environment))
                                        {
                                            continue;
                                        }
                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(binThrown);
                                    }
                                }

                                // Apply the operator using fast-path methods
                                var binResult = binaryOpInstruction.Operator switch
                                {
                                    BinaryOperator.Add => AddValue(binLeft, binRight, context),
                                    BinaryOperator.Subtract => SubtractValue(binLeft, binRight, context),
                                    BinaryOperator.Multiply => MultiplyValue(binLeft, binRight, context),
                                    BinaryOperator.Divide => DivideValue(binLeft, binRight, context),
                                    BinaryOperator.Modulo => ModuloValue(binLeft, binRight, context),
                                    BinaryOperator.Power => PowerValue(binLeft, binRight, context),
                                    BinaryOperator.LessThan => LessThanValue(binLeft, binRight, context),
                                    BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(binLeft, binRight, context),
                                    BinaryOperator.GreaterThan => GreaterThanValue(binLeft, binRight, context),
                                    BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(binLeft, binRight, context),
                                    BinaryOperator.StrictEqual => StrictEqualsValue(binLeft, binRight) ? JsValue.True : JsValue.False,
                                    BinaryOperator.StrictNotEqual => StrictEqualsValue(binLeft, binRight) ? JsValue.False : JsValue.True,
                                    BinaryOperator.Equal => LooseEqualsValue(binLeft, binRight, context) ? JsValue.True : JsValue.False,
                                    BinaryOperator.NotEqual => LooseEqualsValue(binLeft, binRight, context) ? JsValue.False : JsValue.True,
                                    BinaryOperator.BitwiseAnd => BitwiseAndValue(binLeft, binRight, context),
                                    BinaryOperator.BitwiseOr => BitwiseOrValue(binLeft, binRight, context),
                                    BinaryOperator.BitwiseXor => BitwiseXorValue(binLeft, binRight, context),
                                    BinaryOperator.LeftShift => LeftShiftValue(binLeft, binRight, context),
                                    BinaryOperator.RightShift => RightShiftValue(binLeft, binRight, context),
                                    BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(binLeft, binRight, context),
                                    _ => throw new NotSupportedException($"Binary operator '{binaryOpInstruction.Operator}' not supported in BinaryOpInstruction.")
                                };

                                // Store result in slot if specified
                                if (binaryOpInstruction.ResultSlot is not null)
                                {
                                    // Use AssignJsValue to walk up scope chain and find existing binding
                                    environment.AssignJsValue(binaryOpInstruction.ResultSlot, binResult);
                                }

                                _programCounter = binaryOpInstruction.Next;
                                continue;
                            }

                            case InstructionKind.IncrementSlot:
                            {
                                var incrementInstruction = Unsafe.As<IncrementSlotInstruction>(instruction);
                                // Fast path for ++/-- on identifiers
                                var incCurrentValue = environment.GetIdentifierJsValueDirect(incrementInstruction.TargetSymbol, context);
                                if (context.IsThrow)
                                {
                                    var incThrown = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, incThrown, environment))
                                    {
                                        continue;
                                    }
                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(incThrown);
                                }

                                JsValue incNewJsValue;
                                JsValue incOldNumericValue; // For postfix: the numeric value before incrementing
                                // Handle BigInt separately - cannot use ToNumber()
                                if (incCurrentValue.IsBigInt)
                                {
                                    var bigInt = (JsBigInt)incCurrentValue.ObjectValue!;
                                    incOldNumericValue = incCurrentValue; // BigInt is already numeric
                                    var incNewBigInt = incrementInstruction.IsIncrement ? bigInt.Value + 1 : bigInt.Value - 1;
                                    incNewJsValue = new JsBigInt(incNewBigInt);
                                }
                                else
                                {
                                    // Convert to numeric using ToNumericValue which properly calls ToPrimitive
                                    // for objects (invoking valueOf/toString methods as per ES spec)
                                    var numericJsValue = ToNumericValue(incCurrentValue, context);
                                    if (context.ShouldStopEvaluation)
                                    {
                                        var incFlowThrown = context.FlowValue;
                                        context.Clear();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, incFlowThrown, environment))
                                        {
                                            continue;
                                        }
                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(incFlowThrown);
                                    }

                                    // Check if we got a BigInt from ToNumericValue (e.g., valueOf returned a BigInt)
                                    if (numericJsValue.IsBigInt)
                                    {
                                        var bigInt = (JsBigInt)numericJsValue.ObjectValue!;
                                        incOldNumericValue = numericJsValue;
                                        var incNewBigInt = incrementInstruction.IsIncrement ? bigInt.Value + 1 : bigInt.Value - 1;
                                        incNewJsValue = new JsBigInt(incNewBigInt);
                                    }
                                    else
                                    {
                                        var incNumValue = numericJsValue.NumberValue;
                                        incOldNumericValue = JsValueCache.GetNumberJsValue(incNumValue);

                                        // Apply increment or decrement
                                        var incNewValue = incrementInstruction.IsIncrement ? incNumValue + 1.0 : incNumValue - 1.0;
                                        incNewJsValue = JsValueCache.GetNumberJsValue(incNewValue);
                                    }
                                }

                                // Update the binding - use AssignJsValue to walk up scope chain
                                environment.AssignJsValue(incrementInstruction.TargetSymbol, incNewJsValue);

                                // Track completion value for scripts (prefix returns new, postfix returns old numeric value)
                                // SuppressCompletionValue is true for loop update expressions (per ES spec, update expressions don't contribute)
                                if (_isScriptMode && !incrementInstruction.SuppressCompletionValue)
                                {
                                    _scriptCompletionValue = incrementInstruction.IsPrefix ? incNewJsValue : incOldNumericValue;
                                }

                                _programCounter = incrementInstruction.Next;
                                continue;
                            }

                            case InstructionKind.CompoundAssignmentSlot:
                            {
                                var compoundInstruction = Unsafe.As<CompoundAssignmentSlotInstruction>(instruction);
                                // Fast path for compound assignment on identifiers (e.g., s += i)
                                // Read current value from target
                                var compCurrentValue = environment.GetIdentifierJsValueDirect(compoundInstruction.TargetSymbol, context);
                                if (context.IsThrow)
                                {
                                    var compThrown = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, compThrown, environment))
                                    {
                                        continue;
                                    }
                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(compThrown);
                                }

                                // Evaluate RHS expression
                                var compRhsValue = compoundInstruction.RhsExpression.EvaluateExpression(environment, context);
                                if (context.ShouldStopEvaluation)
                                {
                                    if (TryHandlePendingAwait(context, out var pendingCompResult, environment))
                                    {
                                        return pendingCompResult;
                                    }
                                    if (context.IsThrow)
                                    {
                                        var compRhsThrown = context.FlowValue;
                                        context.Clear();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, compRhsThrown, environment))
                                        {
                                            continue;
                                        }
                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(compRhsThrown);
                                    }
                                }

                                // Apply the operator using fast-path methods
                                var compResult = compoundInstruction.Operator switch
                                {
                                    BinaryOperator.Add => AddValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.Subtract => SubtractValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.Multiply => MultiplyValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.Divide => DivideValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.Modulo => ModuloValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.Power => PowerValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.BitwiseAnd => BitwiseAndValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.BitwiseOr => BitwiseOrValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.BitwiseXor => BitwiseXorValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.LeftShift => LeftShiftValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.RightShift => RightShiftValue(compCurrentValue, compRhsValue, context),
                                    BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(compCurrentValue, compRhsValue, context),
                                    _ => throw new NotSupportedException($"Compound assignment operator '{compoundInstruction.Operator}' not supported in CompoundAssignmentSlotInstruction.")
                                };

                                // Update the binding
                                environment.AssignJsValue(compoundInstruction.TargetSymbol, compResult);

                                // Track completion value for scripts
                                if (_isScriptMode && !compoundInstruction.SuppressCompletionValue)
                                {
                                    _scriptCompletionValue = compResult;
                                }

                                _programCounter = compoundInstruction.Next;
                                continue;
                            }

                            case InstructionKind.FunctionDeclaration:
                            {
                                var functionDeclInstruction = Unsafe.As<FunctionDeclarationInstruction>(instruction);
                                // Function declarations are hoisted - this is a no-op at runtime
                                _programCounter = functionDeclInstruction.Next;
                                continue;
                            }

                            case InstructionKind.ClassDeclaration:
                            {
                                var classDeclInstruction = Unsafe.As<ClassDeclarationInstruction>(instruction);
                                // Create the class value and bind it to the class name
                                var classValue = classDeclInstruction.Declaration.Definition.CreateClassValue(
                                    environment, context, classDeclInstruction.Declaration.Name);

                                if (TryHandlePendingAwait(context, out var pendingClassResult, environment))
                                {
                                    return pendingClassResult;
                                }

                                if (context.IsThrow)
                                {
                                    var classThrown = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, classThrown, environment))
                                    {
                                        continue;
                                    }

                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(classThrown);
                                }

                                // Bind the class name in the environment
                                environment.DefineJsValue(classDeclInstruction.Declaration.Name, classValue,
                                    isLexical: true, blocksFunctionScopeOverride: true);

                                _programCounter = classDeclInstruction.Next;
                                continue;
                            }

                            case InstructionKind.SimpleVariableDeclaration:
                            {
                                var varDeclInstruction = Unsafe.As<SimpleVariableDeclarationInstruction>(instruction);

                                // Per ES spec 13.3.1.4: If IsAnonymousFunctionDefinition(Initializer) is true,
                                // then perform SetFunctionName(value, bindingId).
                                var isAnonymousFunctionDefinition = varDeclInstruction.Initializer is not null &&
                                    ExpressionNode.IsAnonymousFunctionDefinitionNode(varDeclInstruction.Initializer);

                                using var functionNameHint = isAnonymousFunctionDefinition
                                    ? context.EnterFunctionNameHint(varDeclInstruction.TargetSymbol)
                                    : null;

                                // Evaluate initializer if present
                                var varValue = varDeclInstruction.Initializer?.EvaluateExpression(environment, context) ?? JsValue.Undefined;

                                if (TryHandlePendingAwait(context, out var pendingVarResult, environment))
                                {
                                    return pendingVarResult;
                                }

                                if (context.IsThrow)
                                {
                                    var varThrown = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, varThrown, environment))
                                    {
                                        if (_programCounter == _currentInstructionIndex)
                                        {
                                            _programCounter = varDeclInstruction.Next;
                                        }

                                        continue;
                                    }

                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(varThrown);
                                }

                                if (context.IsReturn)
                                {
                                    var varReturnValue = context.FlowValue;
                                    context.ClearReturn();
                                    if (!HandleAbruptCompletion(AbruptKind.Return, varReturnValue, environment))
                                    {
                                        return CompleteReturn(varReturnValue);
                                    }

                                    if (_programCounter == _currentInstructionIndex)
                                    {
                                        _programCounter = varDeclInstruction.Next;
                                    }

                                    continue;
                                }

                                if (context.IsYield)
                                {
                                    var varYieldedValue = context.FlowValue;
                                    var varIteratorResultObject = (context.CurrentSignal as YieldCompletionSignal)
                                        ?.IteratorResultObject;
                                    RecordYield(context, environment);
                                    context.Clear();
                                    _state = GeneratorState.Suspended;
                                    return varIteratorResultObject is not null
                                        ? JsValue.FromObjectUnsafe(varIteratorResultObject)
                                        : CreateIteratorResult(varYieldedValue, false);
                                }

                                // For var declarations, ensure the binding exists in function scope
                                // Only assign if there's an initializer - var without initializer preserves hoisted value
                                if (varDeclInstruction.VarKind == VariableKind.Var)
                                {
                                    environment.EnsureFunctionScopedVarBinding(varDeclInstruction.TargetSymbol, context);
                                    // Only assign if there's an initializer
                                    // Per ES spec, `var x;` should not override hoisted function declarations
                                    if (varDeclInstruction.Initializer is not null)
                                    {
                                        // Try to assign to a blocked binding first (shadowed let/const in same scope)
                                        if (!environment.TryAssignBlockedBindingJsValue(varDeclInstruction.TargetSymbol, varValue))
                                        {
                                            if (varDeclInstruction.IsScriptLevel)
                                            {
                                                // Script-level var: use AssignJsValue to update global object too
                                                environment.AssignJsValue(varDeclInstruction.TargetSymbol, varValue);
                                            }
                                            else
                                            {
                                                // Function-level var: local binding only
                                                environment.DefineOrAssignJsValue(varDeclInstruction.TargetSymbol, varValue);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    // let/const - define as lexical binding with blocksFunctionScopeOverride
                                    // to match AST evaluator behavior (see IdentifierBindingExtensions.cs)
                                    var isConst = varDeclInstruction.VarKind == VariableKind.Const;
#pragma warning disable CS0162 // Unreachable code detected (TraceIrExecution is compile-time constant)
                                    if (JsEngineConstants.TraceIrExecution && _realmState.Logger is not null)
                                    {
                                        ExecutionPlanPrinter.TraceDefine(
                                            _realmState.Logger,
                                            varDeclInstruction.VarKind.ToString(),
                                            varDeclInstruction.TargetSymbol.Name,
                                            varValue.ToString() ?? "?",
                                            environment.Depth,
                                            environment.ScopeId,
                                            environment.GetHashCode());
                                    }
#pragma warning restore CS0162
                                    environment.DefineJsValue(varDeclInstruction.TargetSymbol, varValue,
                                        isConst: isConst, isLexical: true, blocksFunctionScopeOverride: true);
                                }

                                _programCounter = varDeclInstruction.Next;
                                continue;
                            }

                            case InstructionKind.PushEnvironment:
                            {
                                var pushEnvInstruction = Unsafe.As<PushEnvironmentInstruction>(instruction);
                                // Stack-based environment model for per-iteration bindings:
                                // - If current env has same ScopeId as instruction → we're in a previous iteration
                                //   Parent is current.Enclosing (the loop scope), current is previous iter env
                                // - Otherwise → first iteration, current IS the loop scope
                                //
                                // Per-iteration envs are SIBLINGS (same parent), values are COPIED between them.
                                // This ensures closures capture separate values per iteration.

                                // Detect if we're in a subsequent iteration:
                                // 1. If ScopeId is valid, use ScopeId matching
                                // 2. If ScopeId is -1 (scope analysis not run), check if:
                                //    - This is a loop iteration scope (has PerIterationBindings), AND
                                //    - Current environment is an iteration env we created (description="scope")
                                // The PerIterationBindings check is CRITICAL: it distinguishes loop iteration
                                // scopes from nested block scopes within the loop body. Without it, nested
                                // blocks would incorrectly become siblings of their parent iteration scope.
                                var isSubsequentIteration =
                                    (pushEnvInstruction.ScopeId >= 0 && environment.ScopeId == pushEnvInstruction.ScopeId) ||
                                    (pushEnvInstruction.ScopeId < 0 &&
                                     !pushEnvInstruction.PerIterationBindings.IsDefaultOrEmpty &&
                                     environment.Description == "scope" && environment.Enclosing != null);

                                // FAST PATH: For subsequent iterations with pooling enabled (no closures),
                                // reuse the same environment in-place. The increment (i++) already updated
                                // the binding values, so no copy or rent/return is needed.
                                // This eliminates all pool overhead for simple loops without closures.
                                //
                                // Why this is safe:
                                // 1. AllowPooling = true means no closures capture iteration variables
                                // 2. Without closures, we don't need separate environments per iteration
                                // 3. The loop update (i++) modifies the binding in-place
                                // 4. The "new" iteration environment would have the same values anyway
                                if (isSubsequentIteration &&
                                    pushEnvInstruction.AllowPooling &&
                                    !pushEnvInstruction.PerIterationBindings.IsDefaultOrEmpty)
                                {
                                    // Just continue using the same environment - bindings already have correct values
                                    _programCounter = pushEnvInstruction.Next;
                                    continue;
                                }

                                JsEnvironment loopScope;
                                JsEnvironment? previousIterEnv = null;

                                if (isSubsequentIteration)
                                {
                                    // In a previous iteration - parent is Enclosing, current is previous iter
                                    previousIterEnv = environment;
                                    loopScope = environment.Enclosing!;
                                }
                                else
                                {
                                    // First iteration - current IS the loop scope
                                    loopScope = environment;
                                }

                                var allowPooling = pushEnvInstruction.AllowPooling;
                                // Use different description for loop scope (empty bindings) vs per-iteration scope
                                // This allows the subsequent iteration heuristic to correctly distinguish them
                                var description = pushEnvInstruction.PerIterationBindings.IsDefaultOrEmpty ? "loop-scope" : "scope";
                                var newIterationEnv = allowPooling
                                    ? JsEnvironmentPool.Rent(loopScope, false, false, null, description, logger: _realmState.Logger)
                                    : new JsEnvironment(loopScope, false, false, null, description);

                                // Initialize slots for O(1) identifier lookups
                                if (pushEnvInstruction is { SlotCount: > 0, ScopeId: >= 0 })
                                {
                                    newIterationEnv.InitializeSlots(pushEnvInstruction.SlotCount,
                                        pushEnvInstruction.ScopeId);
                                    if (!pushEnvInstruction.SlotMap.IsEmpty)
                                    {
                                        newIterationEnv.SetSlotMap(pushEnvInstruction.SlotMap);
                                    }
                                }

                                // Copy per-iteration bindings from appropriate source (for loop iterations)
                                if (previousIterEnv != null && !pushEnvInstruction.PerIterationBindings.IsDefaultOrEmpty)
                                {
                                    // Copy from previous iteration (fast slot path if available)
                                    var useSlotCopy = newIterationEnv.HasSlots &&
                                                      previousIterEnv.HasSlots &&
                                                      !pushEnvInstruction.SlotMap.IsEmpty;

                                    if (useSlotCopy)
                                    {
                                        foreach (var binding in pushEnvInstruction.PerIterationBindings)
                                        {
                                            if (pushEnvInstruction.SlotMap.TryGetValue(binding, out var slotIndex))
                                            {
                                                var value = previousIterEnv.GetSlotRef(slotIndex);
                                                newIterationEnv.SetSlotDirect(slotIndex, value);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Preserve const flag to ensure TypeError is thrown on reassignment
                                        foreach (var binding in pushEnvInstruction.PerIterationBindings)
                                        {
                                            if (previousIterEnv.TryGetJsValueWithConst(binding, out var value, out var isConst))
                                            {
                                                newIterationEnv.DefineJsValue(binding, value, isConst: isConst, isLexical: true);
                                            }
                                        }
                                    }

                                    // Return previous iteration env to pool (if pooled and not resumed-with)
                                    if (allowPooling && !ReferenceEquals(previousIterEnv, IteratorStateRef.ResumedWithEnvironment))
                                    {
                                        JsEnvironmentPool.Return(previousIterEnv, _realmState.Logger);
                                    }
                                }
                                else if (!pushEnvInstruction.PerIterationBindings.IsDefaultOrEmpty)
                                {
                                    // First iteration - copy from loopScope where binding was defined
                                    // Preserve const flag to ensure TypeError is thrown on reassignment
                                    foreach (var binding in pushEnvInstruction.PerIterationBindings)
                                    {
                                        if (loopScope.TryGetJsValueWithConst(binding, out var value, out var isConst))
                                        {
                                            newIterationEnv.DefineJsValue(binding, value, isConst: isConst, isLexical: true);
                                        }
                                    }
                                }

                                IteratorStateRef.ResumedWithEnvironment = null;

                                // Update environment to new env (push onto stack)
                                _realmState.Logger?.LogInformation(
                                    "PushEnv: old.ScopeId={OldScope} new.ScopeId={NewScope} loopScope.ScopeId={LoopScope} parent={Parent}",
                                    environment.ScopeId,
                                    newIterationEnv.ScopeId,
                                    loopScope.ScopeId,
                                    newIterationEnv.Enclosing?.ScopeId);
                                environment = newIterationEnv;
                                _programCounter = pushEnvInstruction.Next;
                                continue;
                            }

                            case InstructionKind.PopEnvironment:
                            {
                                var popEnvInstruction = Unsafe.As<PopEnvironmentInstruction>(instruction);
                                // Pop the iteration environment when exiting a loop.
                                // If current env matches ScopeId, pop (set to Enclosing).
                                // If not (loop ran 0 times), this is a no-op.
                                //
                                // When ScopeId is -1 (scope analysis not run), use heuristic:
                                // Pop if current env has Description="scope" or "loop-scope" and has Enclosing.
                                // This matches environments created by PushEnvironment for loops.
                                var shouldPop = popEnvInstruction.ScopeId >= 0
                                    ? environment.ScopeId == popEnvInstruction.ScopeId
                                    : (environment.Description is "scope" or "loop-scope") && environment.Enclosing != null;

                                if (shouldPop)
                                {
                                    var envToPop = environment;
                                    environment = environment.Enclosing!;

                                    // Return to pool if allowed
                                    if (popEnvInstruction.AllowPooling)
                                    {
                                        JsEnvironmentPool.Return(envToPop, _realmState.Logger);
                                    }
                                }

                                _programCounter = popEnvInstruction.Next;
                                continue;
                            }

                            case InstructionKind.Yield:
                            {
                                var yieldInstruction = Unsafe.As<YieldInstruction>(instruction);
                                var yieldedValue = JsValue.Undefined;
                                if (yieldInstruction.YieldExpression is not null)
                                {
                                    yieldedValue = yieldInstruction.YieldExpression.EvaluateExpression(environment,
                                        context);
                                    if (TryHandlePendingAwait(context, out var pendingYieldResult, environment))
                                    {
                                        return pendingYieldResult;
                                    }

                                    if (context.IsThrow)
                                    {
                                        var thrown = context.FlowValue;
                                        context.Clear();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                                        {
                                            continue;
                                        }

                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(thrown);
                                    }

                                    if (context.IsYield)
                                    {
                                        yieldedValue = context.FlowValue;
                                        // Check if the yield signal includes an original iterator result object (from yield* in operand)
                                        var nestedIteratorResult = (context.CurrentSignal as YieldCompletionSignal)
                                            ?.IteratorResultObject;
                                        context.Clear();
                                        _programCounter = _currentInstructionIndex;
                                        RecordYield(context, environment);
                                        _state = GeneratorState.Suspended;
                                        return nestedIteratorResult is not null
                                            ? JsValue.FromObjectUnsafe(nestedIteratorResult)
                                            : CreateIteratorResult(yieldedValue, false);
                                    }
                                }

                                _programCounter = yieldInstruction.Next;
                                RecordYield(context, environment);
                                _state = GeneratorState.Suspended;
                                return CreateIteratorResult(yieldedValue, false);
                            }

                            case InstructionKind.YieldStar:
                            {
                                var yieldStarInstruction = Unsafe.As<YieldStarInstruction>(instruction);
                                var currentIndex = _programCounter;
                                if (!TryGetSymbolValueJsValue(environment, yieldStarInstruction.StateSlotSymbol,
                                        out var stateValue) ||
                                    !stateValue.TryGetObject<YieldStarState>(out var yieldStarState))
                                {
                                    yieldStarState = new YieldStarState();
                                    StoreSymbolValue(environment, yieldStarInstruction.StateSlotSymbol, yieldStarState);
                                }

                                if (yieldStarState.PendingAbrupt != AbruptKind.None &&
                                    AsyncStateRef.PendingResumeKind is not ResumePayloadKind.Throw and not ResumePayloadKind.Return)
                                {
                                    var pendingKind = yieldStarState.PendingAbrupt;
                                    // PendingValue is now JsValue, no boxing/unboxing needed
                                    var pendingValue = yieldStarState.PendingValue;
                                    yieldStarState.PendingAbrupt = AbruptKind.None;
                                    yieldStarState.PendingValue = JsValue.Undefined;
                                    yieldStarState.State = null;
                                    yieldStarState.AwaitingResume = false;
                                    environment.AssignJsValue(yieldStarInstruction.StateSlotSymbol, JsValue.Null);

                                    switch (pendingKind)
                                    {
                                        case AbruptKind.Throw
                                            when HandleAbruptCompletion(AbruptKind.Throw, pendingValue, environment):
                                            continue;
                                        case AbruptKind.Throw:
                                            TryCatchStateRef.TryStack.Clear();
                                            // pendingValue is already JsValue
                                            throw new ThrowSignal(pendingValue);
                                        case AbruptKind.Return when HandleAbruptCompletion(AbruptKind.Return,
                                            pendingValue, environment):
                                            continue;
                                        // pendingValue is already JsValue
                                        case AbruptKind.Return:
                                            return CompleteReturn(pendingValue);
                                    }
                                }

                                // Track if this is the first entry to this yield* (State is null means first entry)
                                var isFirstYieldStarEntry = yieldStarState.State is null;

                                if (yieldStarState.State is null)
                                {
                                    _realmState.Logger?.LogInformation("YieldStar: Creating new DelegatedState");
                                    var yieldStarIterableValue =
                                        yieldStarInstruction.IterableExpression
                                            .EvaluateExpression(environment, context);
                                    if (TryHandlePendingAwait(context, out var pendingYieldStarResult, environment))
                                    {
                                        return pendingYieldStarResult;
                                    }

                                    if (context.IsThrow)
                                    {
                                        var thrown = context.FlowValue;
                                        context.Clear();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                                        {
                                            continue;
                                        }

                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(thrown);
                                    }

                                    yieldStarState.State = CreateDelegatedState(yieldStarIterableValue, context);

                                    // Check if CreateDelegatedState resulted in a throw (e.g., from calling @@iterator)
                                    if (context.IsThrow)
                                    {
                                        var thrown = context.FlowValue;
                                        context.Clear();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                                        {
                                            continue;
                                        }

                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(thrown);
                                    }

                                    yieldStarState.AwaitingResume = false;
                                }
                                else
                                {
                                    _realmState.Logger?.LogInformation(
                                        "YieldStar: Reusing existing DelegatedState, AwaitingResume={Awaiting}",
                                        yieldStarState.AwaitingResume);
                                }

                                while (true)
                                {
                                    var sendValue = JsValue.Undefined;
                                    var propagateThrow = false;
                                    var propagateReturn = false;

                                    // Per ES spec (14.4.14): On first entry to yield*, call iteratorRecord.[[NextMethod]]
                                    // with iteratorRecord.[[Iterator]] as this and no arguments (undefined).
                                    // Node.js V8 confirms: args.length=1, args[0]=undefined
                                    if (isFirstYieldStarEntry)
                                    {
                                        // On first entry to yield*, we pass undefined as the argument
                                        // (the outer generator's first next() argument is ignored per spec)
                                        sendValue = JsValue.Undefined;
                                        // Mark that we're no longer on first entry for subsequent iterations
                                        isFirstYieldStarEntry = false;
                                    }
                                    else if (yieldStarState.AwaitingResume)
                                    {
                                        var (delegatedResumeKind, delegatedResumePayload) = ConsumeResumeValue();
                                        switch (delegatedResumeKind)
                                        {
                                            case ResumePayloadKind.Throw:
                                                propagateThrow = true;
                                                sendValue = delegatedResumePayload;
                                                break;
                                            case ResumePayloadKind.Return:
                                                propagateReturn = true;
                                                sendValue = delegatedResumePayload;
                                                break;
                                            default:
                                                sendValue = delegatedResumePayload;
                                                break;
                                        }
                                    }

                                    var iteratorResult = yieldStarState.State!.MoveNext(
                                        sendValue,
                                        propagateThrow,
                                        propagateReturn,
                                        context,
                                        out _);

                                    // Check if MoveNext resulted in a throw (e.g., from calling iterator.next())
                                    if (context.IsThrow)
                                    {
                                        var thrown = context.FlowValue;
                                        context.Clear();
                                        yieldStarState.State = null;
                                        yieldStarState.AwaitingResume = false;
                                        environment.AssignJsValue(yieldStarInstruction.StateSlotSymbol, JsValue.Null);
                                        if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                                        {
                                            break;
                                        }

                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(thrown);
                                    }

                                    if (iteratorResult.IsDelegatedCompletion)
                                    {
                                        // Check PropagateThrow from the result - this is true when MoveNext itself threw
                                        // (e.g., iterator.next() returned non-object), not just when we called throw()
                                        var isThrowCompletion = propagateThrow || iteratorResult.PropagateThrow;
                                        var pendingKind = isThrowCompletion ? AbruptKind.Throw : AbruptKind.Return;
                                        // The thrown/returned value is in iteratorResult.Value
                                        var abruptValue = iteratorResult.Value;

                                        if (!iteratorResult.Done)
                                        {
                                            yieldStarState.PendingAbrupt = pendingKind;
                                            // sendValue is already JsValue, no boxing needed
                                            yieldStarState.PendingValue = sendValue;
                                            yieldStarState.AwaitingResume = true;
                                            _programCounter = currentIndex;
                                            RecordYield(context, environment);
                                            _state = GeneratorState.Suspended;
                                            // Use original iterator result object to preserve done/value properties
                                            return iteratorResult.IteratorResultObject is not null
                                                ? JsValue.FromObjectUnsafe(iteratorResult.IteratorResultObject)
                                                : CreateIteratorResult(iteratorResult.Value, false);
                                        }

                                        yieldStarState.State = null;
                                        yieldStarState.AwaitingResume = false;
                                        environment.AssignJsValue(yieldStarInstruction.StateSlotSymbol, JsValue.Null);

                                        if (pendingKind == AbruptKind.Throw)
                                        {
                                            if (HandleAbruptCompletion(AbruptKind.Throw, abruptValue, environment))
                                            {
                                                break;
                                            }

                                            TryCatchStateRef.TryStack.Clear();
                                            throw new ThrowSignal(abruptValue);
                                        }

                                        if (HandleAbruptCompletion(AbruptKind.Return, abruptValue, environment))
                                        {
                                            break;
                                        }

                                        return CompleteReturn(abruptValue);
                                    }

                                    // If the delegated iterator's throw method completed (done=true),
                                    // the yield* expression completes normally with that value (no further delegation).
                                    if (propagateThrow && iteratorResult.Done)
                                    {
                                        yieldStarState.State = null;
                                        yieldStarState.AwaitingResume = false;
                                        environment.AssignJsValue(yieldStarInstruction.StateSlotSymbol, JsValue.Null);
                                        if (yieldStarInstruction.ResultSlotSymbol is { } throwResultSlot)
                                        {
                                            StoreSymbolValue(environment, throwResultSlot, iteratorResult.Value);
                                        }

                                        _programCounter = yieldStarInstruction.Next;
                                        break;
                                    }

                                    if (iteratorResult.Done && !propagateThrow && !propagateReturn)
                                    {
                                        yieldStarState.State = null;
                                        yieldStarState.AwaitingResume = false;
                                        environment.AssignJsValue(yieldStarInstruction.StateSlotSymbol, JsValue.Null);
                                        if (yieldStarInstruction.ResultSlotSymbol is { } resultSlot)
                                        {
                                            StoreSymbolValue(environment, resultSlot, iteratorResult.Value);
                                        }

                                        _programCounter = yieldStarInstruction.Next;
                                        break;
                                    }

                                    yieldStarState.AwaitingResume = true;
                                    _programCounter = currentIndex;
                                    RecordYield(context, environment);
                                    _state = GeneratorState.Suspended;
                                    // Use original iterator result object to preserve done/value properties
                                    if (iteratorResult.IteratorResultObject is { } originalResult)
                                    {
                                        return JsValue.FromObjectUnsafe(originalResult);
                                    }

                                    var resultDone = propagateReturn && iteratorResult.Done;
                                    return CreateIteratorResult(iteratorResult.Value, resultDone);
                                }

                                continue;
                            }

                            case InstructionKind.StoreResumeValue:
                            {
                                var storeResumeValueInstruction = Unsafe.As<StoreResumeValueInstruction>(instruction);
                                var (resumeKind, resumePayload) = ConsumeResumeValue();
                                if (resumeKind == ResumePayloadKind.Throw)
                                {
                                    context.SetThrow(resumePayload);
                                }
                                else if (resumeKind == ResumePayloadKind.Return)
                                {
                                    context.SetReturn(resumePayload);
                                }
                                else if (storeResumeValueInstruction.TargetSymbol is { } resumeSymbol)
                                {
                                    StoreSymbolValueJsValue(environment, resumeSymbol, resumePayload);
                                }

                                if (context.IsThrow)
                                {
                                    var thrownPayload = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, thrownPayload, environment))
                                    {
                                        if (_programCounter == _currentInstructionIndex)
                                        {
                                            _programCounter = storeResumeValueInstruction.Next;
                                        }

                                        continue;
                                    }

                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(thrownPayload);
                                }

                                if (context.IsReturn)
                                {
                                    var resumeReturnValue = context.FlowValue;
                                    context.ClearReturn();
                                    if (HandleAbruptCompletion(AbruptKind.Return, resumeReturnValue, environment))
                                    {
                                        if (_programCounter == _currentInstructionIndex)
                                        {
                                            _programCounter = storeResumeValueInstruction.Next;
                                        }

                                        continue;
                                    }

                                    // resumeReturnValue is already a JsValue from context.FlowValue
                                    return CompleteReturn(resumeReturnValue);
                                }

                                _programCounter = storeResumeValueInstruction.Next;
                                continue;
                            }

                            case InstructionKind.EnterTry:
                            {
                                var enterTryInstruction = Unsafe.As<EnterTryInstruction>(instruction);
                                ResetCompletionValue();
                                PushTryFrame(enterTryInstruction, environment);
                                _programCounter = enterTryInstruction.Next;
                                continue;
                            }

                            case InstructionKind.EnterCatch:
                            {
                                var enterCatch = Unsafe.As<EnterCatchInstruction>(instruction);
                                ResetCompletionValue();

                                // Read the thrown value from the try frame
                                var thrownValue = JsValue.Undefined;
                                if (TryCatchStateRef.TryStack.Count > 0)
                                {
                                    thrownValue = TryCatchStateRef.TryStack.Peek().ThrownValue;
                                }

                                // Create a new lexical environment for the catch block
                                var catchEnv = new JsEnvironment(
                                    enclosing: environment,
                                    isFunctionScope: false,
                                    isStrict: environment.IsStrict,
                                    creatingSource: null,
                                    description: "catch");

                                // Initialize slots if needed
                                if (enterCatch.SlotCount > 0)
                                {
                                    catchEnv.InitializeSlots(enterCatch.SlotCount, enterCatch.ScopeId);
                                }

                                // Bind the catch parameter directly to the thrown value
                                if (enterCatch.CatchParameterSymbol is { } param)
                                {
                                    catchEnv.DefineJsValue(param, thrownValue, isConst: false, isLexical: true);
                                }

                                environment = catchEnv;
                                _programCounter = enterCatch.Next;
                                continue;
                            }

                            case InstructionKind.EnterCatchWithDestructuring:
                            {
                                var enterCatchDestructure = Unsafe.As<EnterCatchWithDestructuringInstruction>(instruction);
                                ResetCompletionValue();

                                // Read the thrown value from the try frame
                                var thrownValue = JsValue.Undefined;
                                if (TryCatchStateRef.TryStack.Count > 0)
                                {
                                    thrownValue = TryCatchStateRef.TryStack.Peek().ThrownValue;
                                }

                                // Create a new lexical environment for the catch block
                                var catchEnv = new JsEnvironment(
                                    enclosing: environment,
                                    isFunctionScope: false,
                                    isStrict: environment.IsStrict,
                                    creatingSource: null,
                                    description: "catch");

                                // Initialize slots if needed
                                if (enterCatchDestructure.SlotCount > 0)
                                {
                                    catchEnv.InitializeSlots(enterCatchDestructure.SlotCount, enterCatchDestructure.ScopeId);
                                }

                                // Apply the destructuring pattern to bind the thrown value
                                enterCatchDestructure.BindingPattern.DefineBindingTarget(
                                    thrownValue, catchEnv, context, isConst: false);

                                // Check for errors during destructuring (e.g., TypeError)
                                if (context.ShouldStopEvaluation)
                                {
                                    if (context.IsThrow)
                                    {
                                        var exception = context.FlowValue;
                                        context.Clear();
                                        // Re-throw - the outer try/catch may handle it
                                        if (HandleAbruptCompletion(AbruptKind.Throw, exception, environment))
                                        {
                                            continue;
                                        }
                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(exception);
                                    }
                                    // Other stop conditions (return, break, continue) shouldn't happen here
                                    // but handle gracefully by continuing execution
                                }

                                environment = catchEnv;
                                _programCounter = enterCatchDestructure.Next;
                                continue;
                            }

                            case InstructionKind.LeaveTry:
                            {
                                var leaveTryInstruction = Unsafe.As<LeaveTryInstruction>(instruction);
                                CompleteTryNormally(leaveTryInstruction.Next);
                                continue;
                            }

                            case InstructionKind.BreakableEnter:
                            {
                                var enterInstruction = Unsafe.As<BreakableEnterInstruction>(instruction);
                                // ResetsCompletionValue: loops and labeled non-loops need runtime to reset
                                // HandlesCompletionInternally: switch handles it with explicit undefined statement
                                if (enterInstruction.ConstructKind == BreakableKind.ResetsCompletionValue)
                                {
                                    ResetCompletionValue();
                                }
                                BreakableStateRef.BreakableStack.Push(new BreakableFrame(
                                    enterInstruction.Label,
                                    enterInstruction.BreakTarget,
                                    enterInstruction.ContinueTarget));
                                _programCounter = enterInstruction.Next;
                                continue;
                            }

                            case InstructionKind.BreakableExit:
                            {
                                var exitInstruction = Unsafe.As<BreakableExitInstruction>(instruction);
                                if (BreakableStateRef.BreakableStack.Count > 0)
                                {
                                    BreakableStateRef.BreakableStack.Pop();
                                }
                                FinalizeCompletionValue();
                                _programCounter = exitInstruction.Next;
                                continue;
                            }

                            case InstructionKind.EndFinally:
                            {
                                var endFinallyInstruction = Unsafe.As<EndFinallyInstruction>(instruction);
                                if (TryCatchStateRef.TryStack.Count == 0)
                                {
                                    _programCounter = endFinallyInstruction.Next;
                                    continue;
                                }

                                var completedFrame = TryCatchStateRef.TryStack.Pop();
                                var pending = completedFrame.PendingCompletion;

                                // Normal completion - restore saved completion value (finally's value is discarded)
                                if (pending.Kind == AbruptKind.None)
                                {
                                    RestoreCompletionValueFromFinally(completedFrame);
                                    var target = pending.ResumeTarget >= 0
                                        ? pending.ResumeTarget
                                        : endFinallyInstruction.Next;
                                    _programCounter = target;
                                    continue;
                                }

                                if (pending.Kind == AbruptKind.Return)
                                {
                                    if (HandleAbruptCompletion(AbruptKind.Return, pending.Value, environment))
                                    {
                                        continue;
                                    }

                                    // Handle case where pending.Value is already a boxed JsValue
                                    var pendingJs = pending.Value is JsValue pjs
                                        ? pjs
                                        : JsValue.FromObjectUnsafe(pending.Value);
                                    return CompleteReturn(pendingJs);
                                }

                                if (pending.Kind == AbruptKind.Break || pending.Kind == AbruptKind.Continue)
                                {
                                    // Break/continue - restore saved completion value
                                    RestoreCompletionValueFromFinally(completedFrame);
                                    if (HandleAbruptCompletion(pending.Kind, pending.Value, environment))
                                    {
                                        continue;
                                    }

                                    _programCounter = pending.Value is int idx ? idx : endFinallyInstruction.Next;
                                    continue;
                                }

                                if (HandleAbruptCompletion(AbruptKind.Throw, pending.Value, environment))
                                {
                                    continue;
                                }

                                TryCatchStateRef.TryStack.Clear();
                                // Handle case where pending.Value is already a boxed JsValue
                                var throwJs = pending.Value is JsValue tjs
                                    ? tjs
                                    : JsValue.FromObjectUnsafe(pending.Value);
                                throw new ThrowSignal(throwJs);
                            }

                            case InstructionKind.IteratorInit:
                            {
                                var iteratorInitInstruction = Unsafe.As<IteratorInitInstruction>(instruction);
                                // For let/const declarations, create TDZ environment before evaluating iterable.
                                // This ensures `for (const x of [x])` throws ReferenceError for accessing x before initialization.
                                var iterableEnv = environment;
                                if (!iteratorInitInstruction.TdzBindings.IsDefaultOrEmpty)
                                {
                                    iterableEnv = new JsEnvironment(environment, false, false, iteratorInitInstruction.IterableExpression.Source, "for-of-head-tdz");
                                    foreach (var tdzSymbol in iteratorInitInstruction.TdzBindings)
                                    {
                                        iterableEnv.DefineJsValue(tdzSymbol, JsValue.Uninitialized, iteratorInitInstruction.TdzIsConst, isLexical: true, blocksFunctionScopeOverride: true);
                                    }
                                }
                                var iterableValue =
                                    iteratorInitInstruction.IterableExpression.EvaluateExpression(iterableEnv, context);
                                if (TryHandlePendingAwait(context, out var pendingIteratorResult, environment))
                                {
                                    return pendingIteratorResult;
                                }

                                if (context.IsThrow)
                                {
                                    var initThrown = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, initThrown, environment))
                                    {
                                        continue;
                                    }

                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(initThrown);
                                }

                                var iteratorState =
                                    CreateIteratorDriverState(iterableValue, iteratorInitInstruction.IteratorKind, context);

                                // Find the correct environment for storing iterator state.
                                // When a for-await-of loop is nested inside another loop with
                                // per-iteration bindings, `environment` might be a child environment
                                // with different slots. The iterator slot was allocated in a parent
                                // scope, so we need to walk up the chain to find it.
                                //
                                // IMPORTANT: Skip per-iteration environments (ScopeId >= 0) to avoid
                                // slot collisions with loop variables like `i`, `j`. Iterator temps
                                // should be stored in function-level or module-level scopes.
                                var iteratorEnv = environment;
                                var walkCount = 0;
                                if (iteratorInitInstruction.IteratorSlotIndex >= 0)
                                {
                                    while (iteratorEnv is not null &&
                                           (iteratorEnv.ScopeId >= 0 || // Skip per-iteration envs
                                            !iteratorEnv.HasSlots ||
                                            iteratorEnv._slots!.Length <= iteratorInitInstruction.IteratorSlotIndex))
                                    {
                                        iteratorEnv = iteratorEnv.Enclosing;
                                        walkCount++;
                                        if (walkCount > 1000)
                                        {
                                            break;
                                        }
                                    }
                                    iteratorEnv ??= environment; // Fallback to current environment
                                }

                                // Store JsVariable directly on state object for O(1) access
                                // This avoids dictionary lookups on every iteration
                                if (iteratorInitInstruction.IteratorSlotIndex >= 0 && iteratorEnv.HasSlots)
                                {
                                    iteratorState.IteratorVariable = new JsVariable(iteratorEnv, iteratorInitInstruction.IteratorSlotIndex);
                                }

                                // Save the loop scope environment for nested loop support.
                                // When async resume resets environment to function scope, CreateIterationEnv
                                // needs this to create properly parented iteration environments.
                                iteratorState.LoopScopeEnvironment = environment;

                                // Cache driver state for scope-correct access from child scopes
                                IteratorStateRef.CurrentDriverState = iteratorState;

                                // Use slot-based storage for O(1) access
                                StoreValueBySlot(iteratorEnv, iteratorInitInstruction.IteratorSlot,
                                    iteratorInitInstruction.IteratorSlotIndex,
                                    JsValue.FromObjectUnsafe(iteratorState));

                                _programCounter = iteratorInitInstruction.Next;
                                continue;
                            }

                            case InstructionKind.IteratorMoveNext:
                            {
                                var iteratorMoveNextInstruction = Unsafe.As<IteratorMoveNextInstruction>(instruction);
                                var iteratorIndex = _programCounter;

                                // Use cached driver state for scope-correct access from child scopes
                                // (The iterator slot is in the loop scope, but we may be in a per-iteration child scope)
                                var driverState = IteratorStateRef.CurrentDriverState;

                                if (driverState is null)
                                {
                                    // Fallback: try to get iterator state from the correct scope.
                                    // The iterator slot is stored in function/module scope (not per-iteration envs),
                                    // so we need to walk up the chain to find it, similar to IteratorInit.
                                    var slotEnv = environment;
                                    var slotIdx = iteratorMoveNextInstruction.IteratorSlotIndex;

                                    // Walk up to find the scope with the right slots
                                    // Skip per-iteration envs (ScopeId >= 0) since iterator temps are stored
                                    // in function scope to avoid slot collisions with loop variables
                                    if (slotIdx >= 0)
                                    {
                                        var slotWalkCount = 0;
                                        while (slotEnv != null &&
                                               (slotEnv.ScopeId >= 0 || // Skip per-iteration envs
                                                !slotEnv.HasSlots ||
                                                slotEnv._slots!.Length <= slotIdx))
                                        {
                                            slotEnv = slotEnv.Enclosing;
                                            slotWalkCount++;
                                            if (slotWalkCount > 100) break;
                                        }
                                        slotEnv ??= environment;
                                    }

                                    if (slotEnv is null || !TryGetValueBySlot(slotEnv, iteratorMoveNextInstruction.IteratorSlot,
                                             slotIdx, out var iteratorStateValue))
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    if (!iteratorStateValue.TryGetObject(out driverState))
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    IteratorStateRef.CurrentDriverState = driverState;
                                }

                                // Get JsVariables directly from driverState (O(1) access, no dictionary lookup)
                                var iterVar = driverState.IteratorVariable;
                                var valueVar = driverState.ValueVariable;

                                // Capture value JsVariable on first execution (while still in loop scope)
                                // IMPORTANT: Use the loop scope environment (from iterVar) rather than the current
                                // environment, which may be a stale per-iteration environment from a previous outer
                                // loop iteration. The value slot is allocated in the same scope as the iterator.
                                if (!valueVar.IsValid && iteratorMoveNextInstruction.ValueSlotIndex >= 0)
                                {
                                    // Use the iterator's environment since value slot is in the same scope
                                    var loopScopeEnv = iterVar.IsValid ? iterVar.Environment : environment;
                                    if (loopScopeEnv.HasSlots && loopScopeEnv._slots!.Length > iteratorMoveNextInstruction.ValueSlotIndex)
                                    {
                                        valueVar = new JsVariable(loopScopeEnv, iteratorMoveNextInstruction.ValueSlotIndex);
                                        driverState.ValueVariable = valueVar;
                                    }
                                }

                                if (!driverState.IsAsyncIterator)
                                {
                                    JsValue currentValue;
                                    if (driverState.IteratorObject is { } iteratorObj)
                                    {
                                        driverState.NextMethod ??= iteratorObj.GetIteratorNextCallable(context);
                                        var nextResult = iteratorObj.InvokeIteratorNext(
                                            driverState.NextMethod,
                                            context: context,
                                            callingEnvironment: environment);
                                        // Handle case where nextResult is already a boxed JsValue
                                        if (!nextResult.TryGetObject<IJsPropertyAccessor>(out var resultObj))
                                        {
                                            // Per ES spec 7.4.2: if result is not an object, throw TypeError
                                            var typeError = StandardLibrary.CreateTypeError("Iterator result is not an object",
                                                context, context.RealmState);
                                            if (HandleAbruptCompletion(AbruptKind.Throw, typeError, environment))
                                            {
                                                continue;
                                            }

                                            TryCatchStateRef.TryStack.Clear();
                                            throw new ThrowSignal(typeError);
                                        }

                                        var done = resultObj.TryGetProperty("done", out var doneValue) &&
                                                   JsOps.ToBoolean(doneValue);
                                        if (done)
                                        {
                                            // When breaking out of iterator, restore environment to enclosing scope.
                                            // This is critical for nested loops: after async resume, environment was
                                            // reset to function scope, and we need to restore it to the loop scope
                                            // so that variable lookups (like loop counter increments) work correctly.
                                            if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv)
                                            {
                                                environment = enclosingEnv;
                                            }
                                            // Clear driver state to prevent outer loop's CreateIterationEnv from
                                            // incorrectly updating this driver's CurrentIterationEnvironment.
                                            IteratorStateRef.CurrentDriverState = null;
                                            _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                            continue;
                                        }

                                        // yielded is already a JsValue from TryGetProperty
                                        currentValue = resultObj.TryGetProperty("value", out var yielded)
                                            ? yielded
                                            : JsValue.Undefined;

                                        // Mark that we've successfully entered the loop (next() succeeded).
                                        // Per ES spec 13.6.4.13 step 5.d, IteratorClose should only be called
                                        // if we've entered the loop body, not if next() itself throws.
                                        driverState.HasEnteredLoop = true;
                                    }
                                    else if (driverState.Enumerator is { } enumerator)
                                    {
                                        if (!enumerator.MoveNext())
                                        {
                                            // Restore environment to enclosing scope when iterator exhausted
                                            if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv2)
                                            {
                                                environment = enclosingEnv2;
                                            }
                                            IteratorStateRef.CurrentDriverState = null;
                                            _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                            continue;
                                        }

                                        currentValue = enumerator.Current;

                                        // Mark that we've successfully entered the loop (enumerator succeeded).
                                        driverState.HasEnteredLoop = true;
                                    }
                                    else
                                    {
                                        // Restore environment to enclosing scope when no iterator
                                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv3)
                                        {
                                            environment = enclosingEnv3;
                                        }
                                        IteratorStateRef.CurrentDriverState = null;
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    // Use JsVariable for scope-correct access (value slot is in loop scope)
                                    _realmState.Logger?.LogInformation(
                                        "SyncIterator StoreValue: valueVar.IsValid={Valid} currentEnv.ScopeId={CurScope} slot={Slot} value={Value}",
                                        valueVar.IsValid,
                                        environment.ScopeId,
                                        iteratorMoveNextInstruction.ValueSlot.Name,
                                        currentValue.Kind);
                                    if (valueVar.IsValid)
                                    {
                                        valueVar.Write(currentValue);
                                        // Also create binding for symbol-based identifier lookup in loop body
                                        valueVar.Environment.DefineOrAssignJsValue(
                                            iteratorMoveNextInstruction.ValueSlot, currentValue);
                                        _realmState.Logger?.LogInformation(
                                            "SyncIterator StoreValue: wrote to valueVar.Environment.ScopeId={Scope}",
                                            valueVar.Environment.ScopeId);
                                    }
                                    else
                                    {
                                        StoreValueBySlot(environment, iteratorMoveNextInstruction.ValueSlot,
                                            iteratorMoveNextInstruction.ValueSlotIndex, currentValue);
                                        _realmState.Logger?.LogInformation(
                                            "SyncIterator StoreValue: wrote via StoreValueBySlot to env.ScopeId={Scope}",
                                            environment.ScopeId);
                                    }
                                    _programCounter = iteratorMoveNextInstruction.Next;
                                    continue;
                                }

                                var awaitedValue = JsValue.Undefined;
                                var awaitedNextResult = JsValue.Undefined;
                                var hasAwaitedNextResult = false;

                                // If we're resuming after a pending await from this
                                // iterator site, consume the resume payload and treat
                                // it as the awaited result instead of calling into the
                                // iterator again.
                                if (driverState.AwaitingNextResult || driverState.AwaitingValue)
                                {
                                    var awaitingValue = driverState.AwaitingValue;
                                    driverState.AwaitingNextResult = false;
                                    driverState.AwaitingValue = false;
                                    var (forAwaitResumeKind, forAwaitResumePayload) = ConsumeResumeValue();
                                    // Use JsVariable for scope-correct access (iterator slot is in loop scope)
                                    var iterStateValue = driverState.AsJsValue;
                                    if (iterVar.IsValid)
                                    {
                                        iterVar.Write(iterStateValue);
                                    }
                                    else
                                    {
                                        StoreValueBySlot(environment, iteratorMoveNextInstruction.IteratorSlot,
                                            iteratorMoveNextInstruction.IteratorSlotIndex, iterStateValue);
                                    }

                                    if (forAwaitResumeKind == ResumePayloadKind.Throw)
                                    {
                                        // forAwaitResumePayload is already JsValue, no need to box with .ToObject()
                                        if (HandleAbruptCompletion(AbruptKind.Throw, forAwaitResumePayload,
                                                environment))
                                        {
                                            continue;
                                        }

                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(forAwaitResumePayload);
                                    }

                                    if (forAwaitResumeKind == ResumePayloadKind.Return)
                                    {
                                        // forAwaitResumePayload is already JsValue, no need to box with .ToObject()
                                        if (HandleAbruptCompletion(AbruptKind.Return, forAwaitResumePayload,
                                                environment))
                                        {
                                            continue;
                                        }

                                        return CompleteReturn(forAwaitResumePayload);
                                    }

                                    if (awaitingValue)
                                    {
                                        awaitedValue = forAwaitResumePayload;
                                        goto StoreIteratorValue;
                                    }

                                    awaitedNextResult = forAwaitResumePayload;
                                    hasAwaitedNextResult = true;
                                }

                                if (driverState.IteratorObject is { } awaitIteratorObj)
                                {
                                    if (!hasAwaitedNextResult)
                                    {
                                        driverState.NextMethod ??= awaitIteratorObj.GetIteratorNextCallable(context);
                                        var nextResult = awaitIteratorObj.InvokeIteratorNext(
                                            driverState.NextMethod,
                                            context: context,
                                            callingEnvironment: environment);
                                        if (!TryResolvePromiseOrYield(nextResult, context, out var awaitedNext))
                                        {
                                            if (AsyncStateRef.AsyncStepMode && AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _))
                                            {
                                                driverState.AwaitingNextResult = true;
                                                // Use JsVariable for scope-correct access
                                                var iterState = driverState.AsJsValue;
                                                if (iterVar.IsValid)
                                                {
                                                    iterVar.Write(iterState);
                                                }
                                                else
                                                {
                                                    StoreValueBySlot(environment, iteratorMoveNextInstruction.IteratorSlot,
                                                        iteratorMoveNextInstruction.IteratorSlotIndex, iterState);
                                                }
                                                // Save environment before suspending so we restore it on resume
                                                _executionEnvironment = environment;
                                                _state = GeneratorState.Suspended;
                                                _programCounter = iteratorIndex;
                                                return CreateIteratorResult(JsValue.Undefined, false);
                                            }

                                            if (context.IsThrow)
                                            {
                                                var thrownAwait = context.FlowValue;
                                                context.Clear();
                                                if (HandleAbruptCompletion(AbruptKind.Throw, thrownAwait, environment))
                                                {
                                                    continue;
                                                }

                                                TryCatchStateRef.TryStack.Clear();
                                                throw new ThrowSignal(thrownAwait);
                                            }

                                            // Restore environment to enclosing scope when breaking
                                            if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv4)
                                            {
                                                environment = enclosingEnv4;
                                            }
                                            IteratorStateRef.CurrentDriverState = null;
                                            _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                            continue;
                                        }

                                        awaitedNextResult = awaitedNext;
                                    }

                                    if (!awaitedNextResult.TryGetObject<IJsPropertyAccessor>(out var awaitResultObj))
                                    {
                                        // Per ES spec 7.4.2: if result is not an object, throw TypeError
                                        var typeError = StandardLibrary.CreateTypeError("Iterator result is not an object", context,
                                            context.RealmState);
                                        if (HandleAbruptCompletion(AbruptKind.Throw, typeError, environment))
                                        {
                                            continue;
                                        }

                                        TryCatchStateRef.TryStack.Clear();
                                        throw new ThrowSignal(typeError);
                                    }

                                    var doneAwait = awaitResultObj.TryGetProperty("done", out var awaitDoneValue) &&
                                                    JsOps.ToBoolean(awaitDoneValue);
                                    if (doneAwait)
                                    {
                                        // Restore environment to enclosing scope when async iterator exhausted
                                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv5)
                                        {
                                            environment = enclosingEnv5;
                                        }
                                        IteratorStateRef.CurrentDriverState = null;
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    var rawValue = awaitResultObj.TryGetProperty("value", out var yieldedAwait)
                                        ? yieldedAwait
                                        : JsValue.Undefined;
                                    if (!TryResolvePromiseOrYield(rawValue, context, out var fullyAwaitedValue))
                                    {
                                        if (AsyncStateRef.AsyncStepMode && AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _))
                                        {
                                            driverState.AwaitingValue = true;
                                            // Use JsVariable for scope-correct access
                                            var iterState = driverState.AsJsValue;
                                            if (iterVar.IsValid)
                                            {
                                                iterVar.Write(iterState);
                                            }
                                            else
                                            {
                                                StoreValueBySlot(environment, iteratorMoveNextInstruction.IteratorSlot,
                                                    iteratorMoveNextInstruction.IteratorSlotIndex, iterState);
                                            }
                                            // Save environment before suspending so we restore it on resume
                                            _executionEnvironment = environment;
                                            _state = GeneratorState.Suspended;
                                            _programCounter = iteratorIndex;
                                            return CreateIteratorResult(JsValue.Undefined, false);
                                        }

                                        if (context.IsThrow)
                                        {
                                            var thrownAwaitValue = context.FlowValue;
                                            context.Clear();
                                            if (HandleAbruptCompletion(AbruptKind.Throw, thrownAwaitValue, environment))
                                            {
                                                continue;
                                            }

                                            TryCatchStateRef.TryStack.Clear();
                                            throw new ThrowSignal(thrownAwaitValue);
                                        }

                                        // Restore environment to enclosing scope
                                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv6)
                                        {
                                            environment = enclosingEnv6;
                                        }
                                        IteratorStateRef.CurrentDriverState = null;
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    awaitedValue = fullyAwaitedValue;
                                }
                                else if (driverState.Enumerator is { } awaitEnumerator)
                                {
                                    if (!awaitEnumerator.MoveNext())
                                    {
                                        // Restore environment to enclosing scope when enumerator exhausted
                                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv7)
                                        {
                                            environment = enclosingEnv7;
                                        }

                                        // Clear the driver state since this iterator loop is done.
                                        // This prevents outer loop's CreateIterationEnv from incorrectly
                                        // updating this driver's CurrentIterationEnvironment.
                                        IteratorStateRef.CurrentDriverState = null;
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    // enumerated is already JsValue from IEnumerator<JsValue>.Current
                                    var enumerated = awaitEnumerator.Current;
                                    if (!TryResolvePromiseOrYield(enumerated, context, out var awaitedEnumerated))
                                    {
                                        if (AsyncStateRef.AsyncStepMode && AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _))
                                        {
                                            driverState.AwaitingValue = true;
                                            // Use JsVariable for scope-correct access
                                            var iterState = driverState.AsJsValue;
                                            if (iterVar.IsValid)
                                            {
                                                iterVar.Write(iterState);
                                            }
                                            else
                                            {
                                                StoreValueBySlot(environment, iteratorMoveNextInstruction.IteratorSlot,
                                                    iteratorMoveNextInstruction.IteratorSlotIndex, iterState);
                                            }
                                            // Save environment before suspending so we restore it on resume
                                            _executionEnvironment = environment;
                                            _state = GeneratorState.Suspended;
                                            _programCounter = iteratorIndex;
                                            return CreateIteratorResult(JsValue.Undefined, false);
                                        }

                                        if (context.IsThrow)
                                        {
                                            var thrownAwaitEnum = context.FlowValue;
                                            context.Clear();
                                            if (HandleAbruptCompletion(AbruptKind.Throw, thrownAwaitEnum, environment))
                                            {
                                                continue;
                                            }

                                            TryCatchStateRef.TryStack.Clear();
                                            throw new ThrowSignal(thrownAwaitEnum);
                                        }

                                        // Restore environment to enclosing scope
                                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv8)
                                        {
                                            environment = enclosingEnv8;
                                        }
                                        IteratorStateRef.CurrentDriverState = null;
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    awaitedValue = awaitedEnumerated;
                                }
                                else
                                {
                                    // Restore environment to enclosing scope
                                    if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv9)
                                    {
                                        environment = enclosingEnv9;
                                    }
                                    IteratorStateRef.CurrentDriverState = null;
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                StoreIteratorValue:
                                // Mark that we've successfully entered the loop (next() succeeded for async iterator).
                                // Per ES spec 13.6.4.13 step 5.d, IteratorClose should only be called
                                // if we've entered the loop body, not if next() itself throws.
                                driverState.HasEnteredLoop = true;

                                // Use JsVariable for scope-correct access (value slot is in loop scope)
                                _realmState.Logger?.LogInformation(
                                    "StoreIteratorValue: valueVar.IsValid={Valid} slot={Slot} value={Value} envHash={Env}",
                                    valueVar.IsValid,
                                    iteratorMoveNextInstruction.ValueSlot.Name,
                                    awaitedValue.Kind,
                                    environment.GetHashCode());
                                if (valueVar.IsValid)
                                {
                                    valueVar.Write(awaitedValue);
                                    // Also create binding for symbol-based identifier lookup in loop body
                                    valueVar.Environment.DefineOrAssignJsValue(
                                        iteratorMoveNextInstruction.ValueSlot, awaitedValue);
                                    _realmState.Logger?.LogInformation(
                                        "StoreIteratorValue: wrote to valueVar.Environment={Env}",
                                        valueVar.Environment.GetHashCode());
                                }
                                else
                                {
                                    StoreValueBySlot(environment, iteratorMoveNextInstruction.ValueSlot,
                                        iteratorMoveNextInstruction.ValueSlotIndex, awaitedValue);
                                    _realmState.Logger?.LogInformation(
                                        "StoreIteratorValue: wrote via StoreValueBySlot to env={Env}",
                                        environment.GetHashCode());
                                }
                                _programCounter = iteratorMoveNextInstruction.Next;
                                continue;
                            }

                            case InstructionKind.Jump:
                            {
                                var jumpInstruction = Unsafe.As<JumpInstruction>(instruction);
                                _programCounter = jumpInstruction.TargetIndex;
                                continue;
                            }

                            case InstructionKind.Branch:
                            {
                                var branchInstruction = Unsafe.As<BranchInstruction>(instruction);
                                var testValue = branchInstruction.Condition.EvaluateExpression(environment, context);
                                if (TryHandlePendingAwait(context, out var pendingBranchResult, environment))
                                {
                                    return pendingBranchResult;
                                }

                                if (context.IsThrow)
                                {
                                    var thrownBranch = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, thrownBranch, environment))
                                    {
                                        continue;
                                    }

                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(thrownBranch);
                                }

                                _programCounter = testValue.IsTruthy
                                    ? branchInstruction.ConsequentIndex
                                    : branchInstruction.AlternateIndex;
                                continue;
                            }

                            case InstructionKind.Break:
                            {
                                var breakInstruction = Unsafe.As<BreakInstruction>(instruction);

                                if (HandleAbruptCompletion(AbruptKind.Break, breakInstruction.TargetIndex, environment))
                                {
                                    // If inside scheduled finally, HandleAbruptCompletion stored the pending
                                    // completion but didn't advance _programCounter. We need to jump
                                    // to EndFinally so it can process the pending break.
                                    if (_programCounter == _currentInstructionIndex &&
                                        TryCatchStateRef.TryStack.Count > 0)
                                    {
                                        var frame = TryCatchStateRef.TryStack.Peek();
                                        if (frame.EndFinallyIndex >= 0)
                                        {
                                            _programCounter = frame.EndFinallyIndex;
                                        }
                                    }

                                    continue;
                                }

                                // Pop environments until we reach the target scope
                                if (breakInstruction.TargetScopeId >= 0)
                                {
                                    while (environment.ScopeId != breakInstruction.TargetScopeId &&
                                           environment.Enclosing != null)
                                    {
                                        environment = environment.Enclosing;
                                        // Note: we don't return to pool here as we don't track pooling per-env
                                    }
                                }

                                _programCounter = breakInstruction.TargetIndex;
                                continue;
                            }

                            case InstructionKind.Continue:
                            {
                                var continueInstruction = Unsafe.As<ContinueInstruction>(instruction);

                                if (HandleAbruptCompletion(AbruptKind.Continue, continueInstruction.TargetIndex,
                                        environment))
                                {
                                    // If inside scheduled finally, HandleAbruptCompletion stored the pending
                                    // completion but didn't advance _programCounter. We need to jump
                                    // to EndFinally so it can process the pending continue.
                                    if (_programCounter == _currentInstructionIndex &&
                                        TryCatchStateRef.TryStack.Count > 0)
                                    {
                                        var frame = TryCatchStateRef.TryStack.Peek();
                                        if (frame.EndFinallyIndex >= 0)
                                        {
                                            _programCounter = frame.EndFinallyIndex;
                                        }
                                    }

                                    continue;
                                }

                                // Pop environments until we reach the target scope
                                if (continueInstruction.TargetScopeId >= 0)
                                {
                                    while (environment.ScopeId != continueInstruction.TargetScopeId &&
                                           environment.Enclosing != null)
                                    {
                                        environment = environment.Enclosing;
                                        // Note: we don't return to pool here as we don't track pooling per-env
                                    }
                                }

                                _programCounter = continueInstruction.TargetIndex;
                                continue;
                            }

                            case InstructionKind.Return:
                            {
                                var returnInstruction = Unsafe.As<ReturnInstruction>(instruction);
                                var returnValue = returnInstruction.ReturnExpression?.EvaluateExpression(environment, context) ?? JsValue.Undefined;
                                if (TryHandlePendingAwait(context, out var pendingReturnResult, environment))
                                {
                                    return pendingReturnResult;
                                }

                                if (context.IsThrow)
                                {
                                    var pendingThrow = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, pendingThrow, environment))
                                    {
                                        if (_programCounter == _currentInstructionIndex)
                                        {
                                            _programCounter = returnInstruction.Next;
                                        }

                                        continue;
                                    }

                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(pendingThrow);
                                }

                                if (context.IsReturn)
                                {
                                    var pendingReturn = context.FlowValue;
                                    context.ClearReturn();
                                    returnValue = pendingReturn;
                                }

                                // Check if we're inside a scheduled finally BEFORE calling HandleAbruptCompletion.
                                // If so, the return statement should terminate the finally block immediately,
                                // not execute code after the return (which is unreachable code).
                                var wasInsideScheduledFinally = IsInsideScheduledFinally();

                                if (HandleAbruptCompletionJsValue(AbruptKind.Return, returnValue, environment))
                                {
                                    // If we were inside a scheduled finally, the pending completion was updated.
                                    // Now complete the function with that value instead of advancing to unreachable code.
                                    if (wasInsideScheduledFinally)
                                    {
                                        return CompleteReturn(returnValue);
                                    }

                                    if (_programCounter == _currentInstructionIndex)
                                    {
                                        _programCounter = returnInstruction.Next;
                                    }

                                    continue;
                                }

                                return CompleteReturn(returnValue);
                            }

                            case InstructionKind.EnterWith:
                            {
                                var enterWithInstruction = Unsafe.As<EnterWithInstruction>(instruction);
                                var objValueJs =
                                    enterWithInstruction.ObjectExpression.EvaluateExpression(environment, context);
                                if (TryHandlePendingAwait(context, out var pendingWithResult, environment))
                                {
                                    return pendingWithResult;
                                }

                                if (context.IsThrow)
                                {
                                    var thrownWith = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, thrownWith, environment))
                                    {
                                        continue;
                                    }

                                    TryCatchStateRef.TryStack.Clear();
                                    throw new ThrowSignal(thrownWith);
                                }

                                // Create the with-environment and store it in the slot
                                // TryConvertToWithBindingObject will handle wrapping primitives and throwing for null/undefined.
                                if (TryConvertToWithBindingObject(objValueJs, context, out var withObject))
                                {
                                    var withEnv = new JsEnvironment(environment, false, context.CurrentScope.IsStrict,
                                        enterWithInstruction.ObjectExpression.Source, "with", withObject);
                                    // Store the with-environment in the root environment slot so it persists across yields
                                    StoreSymbolValue(_executionEnvironment!, enterWithInstruction.WithScopeSlot,
                                        withEnv);
                                    // Track this with-scope as active
                                    WithStateRef.ActiveWithScopes.Push(enterWithInstruction.WithScopeSlot);
                                    // Update the local environment reference to use the with-environment
                                    environment = withEnv;
                                }
                                // If we couldn't create a with-environment, just continue with the same environment

                                _programCounter = enterWithInstruction.Next;
                                continue;
                            }

                            case InstructionKind.LeaveWith:
                            {
                                var leaveWithInstruction = Unsafe.As<LeaveWithInstruction>(instruction);
                                // Remove this with-scope from active tracking
                                if (WithStateRef.ActiveWithScopes.Count > 0 &&
                                    ReferenceEquals(WithStateRef.ActiveWithScopes.Peek(), leaveWithInstruction.WithScopeSlot))
                                {
                                    WithStateRef.ActiveWithScopes.Pop();
                                }

                                // Restore the previous environment by getting it from the enclosing scope of the stored with-env
                                if (TryGetSymbolValueJsValue(_executionEnvironment!, leaveWithInstruction.WithScopeSlot,
                                        out var storedEnvValue) &&
                                    storedEnvValue.TryGetObject<JsEnvironment>(out var storedWithEnv))
                                {
                                    // The with-environment's Enclosing is the original environment
                                    environment = storedWithEnv.Enclosing ?? environment;
                                }

                                _programCounter = leaveWithInstruction.Next;
                                continue;
                            }

                            case InstructionKind.IteratorClose:
                            {
                                var iteratorCloseInstruction = Unsafe.As<IteratorCloseInstruction>(instruction);
                                // Get the iterator state from the slot
                                if (TryGetSymbolValueJsValue(environment, iteratorCloseInstruction.IteratorSlot,
                                        out var iterStateValue) &&
                                    iterStateValue.TryGetObject<IteratorDriverState>(out var iterState) &&
                                    iterState.IteratorObject is { } iteratorObj)
                                {
                                    // Per ES spec 13.6.4.13 step 5.d: If IteratorStep (calling next()) returns
                                    // an abrupt completion, we return that completion WITHOUT calling IteratorClose.
                                    // Only call IteratorClose if we've successfully entered the loop body
                                    // (i.e., at least one successful next() call completed).
                                    if (!iterState.HasEnteredLoop)
                                    {
                                        // Mark as closed anyway to prevent any future close attempts
                                        iterState.MarkIteratorClosed();
                                        _programCounter = iteratorCloseInstruction.Next;
                                        continue;
                                    }

                                    // Mark as closed before attempting close to prevent double-close
                                    iterState.MarkIteratorClosed();

                                    // Check if we're closing due to a throw completion.
                                    // Per ES spec 7.4.7 IteratorClose steps 6-7:
                                    // - Step 6: If completion.[[Type]] is throw, return Completion(completion).
                                    // - Step 7: If innerResult.[[Type]] is throw, return Completion(innerResult).
                                    // This means if the original completion was a throw, we PRESERVE IT regardless
                                    // of what happens during IteratorClose (GetMethod or Call errors are suppressed).
                                    var hasPendingThrow = false;
                                    if (TryCatchStateRef.TryStack.Count > 0)
                                    {
                                        var topFrame = TryCatchStateRef.TryStack.Peek();
                                        hasPendingThrow = topFrame.PendingCompletion.Kind == AbruptKind.Throw;
                                    }

                                    try
                                    {
                                        // Call IteratorClose with preserveExistingThrow if we have a pending throw.
                                        // This ensures the original throw is preserved per ES spec 7.4.7 step 6.
                                        iteratorObj.IteratorClose(context, preserveExistingThrow: hasPendingThrow);
                                    }
                                    catch (ThrowSignal closeThrown)
                                    {
                                        // IteratorClose threw - per ES spec 7.4.7:
                                        // If the original completion was a throw, we already returned it (step 6).
                                        // If not, we return the IteratorClose throw (step 7).
                                        if (hasPendingThrow)
                                        {
                                            // Original throw preserved - continue to EndFinally which will re-throw it
                                            _programCounter = iteratorCloseInstruction.Next;
                                            continue;
                                        }

                                        // No pending throw - propagate the IteratorClose error
                                        if (HandleAbruptCompletion(AbruptKind.Throw, closeThrown.ThrownValue,
                                                environment))
                                        {
                                            // HandleAbruptCompletion updated the pending completion in the try frame.
                                            // Continue to the next instruction in the finally block.
                                            _programCounter = iteratorCloseInstruction.Next;
                                            continue;
                                        }

                                        TryCatchStateRef.TryStack.Clear();
                                        throw;
                                    }
                                }

                                _programCounter = iteratorCloseInstruction.Next;
                                continue;
                            }

                            case InstructionKind.SetCompletionValue:
                            {
                                var setCompletionInstruction = Unsafe.As<SetCompletionValueInstruction>(instruction);
                                if (_isScriptMode)
                                {
                                    _scriptCompletionValue = JsValue.Undefined;
                                }
                                _programCounter = setCompletionInstruction.Next;
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
            return _executionEnvironment ??= CreateExecutionEnvironment();
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
            var result = AwaitScheduler.TryResolvePromiseOrYield(candidate, AsyncStateRef.AsyncStepMode, ref pendingPromise,
                context, out var resolvedObj);
            AsyncStateRef.PendingPromise = pendingPromise;
            // resolvedObj is already JsValue from the scheduler
            resolvedValue = resolvedObj;
            return result;
        }

        private bool TryHandlePendingAwait(EvaluationContext context, out JsValue result, JsEnvironment? currentEnvironment = null)
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

    }
}
