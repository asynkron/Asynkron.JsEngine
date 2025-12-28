#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class ExecutionPlanRunner
    {
        // Track active with-scope slots for restoration after yield/resume
        private readonly Stack<Symbol> _activeWithScopes = new();
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
        private readonly YieldResumeContext _resumeContext = new();
        private readonly JsValue _thisValue;
        private readonly JsValue _newTarget;
        private readonly Stack<TryFrame> _tryStack = new();
        private readonly Stack<LoopFrame> _loopStack = new();
        private bool _asyncStepMode;
        private EvaluationContext? _context;
        private int _currentInstructionIndex;
        private bool _done;
        private JsEnvironment? _executionEnvironment;
        private int _lastYieldIndex = -1;
        private int _lastYieldSourceStart = -1;
        private int _lastYieldSourceEnd = -1;

        private Symbol? _pendingAwaitKey;
        private JsValue _pendingPromise;
        private ResumePayloadKind _pendingResumeKind;
        private JsValue _pendingResumeValue = JsValue.Undefined;
        private bool _privateScopesApplied;
        private int _programCounter;
        private GeneratorState _state = GeneratorState.Start;

        // Caches the current iterator driver state for scope-correct access in CreateIterationEnvironmentInstruction.
        // The driverState.IteratorVariable holds the loop scope environment reference.
        private IteratorDriverState? _currentDriverState;

        // Tracks the specific environment we resumed with after a suspend (await/yield).
        // We should NOT return this specific environment to the pool because
        // it might still be in progress (we suspended mid-iteration).
        // Other environments (from completed iterations) can still be returned.
        private JsEnvironment? _resumedWithEnvironment;

        // When HandleAbruptCompletion jumps to a catch/finally handler, this field
        // is set to the environment that was active when entering the try block.
        // The caller should check this after HandleAbruptCompletion returns true
        // and restore the environment if set.
        private JsEnvironment? _restoredEnvironmentFromTry;

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
            JsValue newTarget = default)
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
            var previousAsyncStepMode = _asyncStepMode;
            _asyncStepMode = true;
            _pendingPromise = JsValue.Undefined;

            try
            {
                var result = ExecutePlan(mode, resumeValue);

                if (_pendingPromise.TryGetPropertyAccessor(out _))
                {
                    return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, JsValue.Undefined, false,
                        _pendingPromise);
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
                _asyncStepMode = previousAsyncStepMode;
                _pendingPromise = JsValue.Undefined;
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

            // Define new.target for non-arrow functions so inner arrow functions can access it lexically
            if (!_function.IsArrow)
            {
                var newTargetValue = _newTarget.IsUndefined ? JsValue.Undefined : _newTarget;
                functionEnvironment.DefineJsValue(Symbol.NewTarget, newTargetValue, true, isLexical: true,
                    blocksFunctionScopeOverride: true);
            }

            functionEnvironment.DefineJsValue(Symbol.YieldResumeContextSymbol,
                JsValue.FromObjectUnsafe(_resumeContext));
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

            _function.Body.HoistVarDeclarations(executionEnvironment, generatorContext,
                lexicalNames: lexicalNames,
                catchParameterNames: catchParameterNames,
                simpleCatchParameterNames: simpleCatchParameterNames);

            _function.BindFunctionParameters(_arguments, parameterEnvironment, generatorContext);
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

        private static JsValue CreateIteratorResult(JsValue value, bool done)
        {
            // Use singleton for the common done case with undefined value
            if (done && value.IsUndefined)
            {
                return IteratorResultObject.DoneUndefined.AsJsValue;
            }
            return new IteratorResultObject(value, done).AsJsValue;
        }

        private static IteratorDriverState CreateIteratorDriverState(
            JsValue iterable,
            IteratorDriverKind kind,
            EvaluationContext context)
        {
            // FAST PATH: Use IEnumerator<JsValue> for arrays to avoid iterator object allocation.
            // This bypasses creating iterator objects with next() methods for JsArray.
            var fastEnumerator = TryGetFastEnumeratorForIteration(iterable);
            if (fastEnumerator is not null)
            {
                return new IteratorDriverState
                {
                    IteratorObject = null,
                    Enumerator = fastEnumerator,
                    IsAsyncIterator = kind == IteratorDriverKind.Await,
                    NextMethod = null
                };
            }

            // SLOW PATH: Full iterator protocol for custom iterables
            var iteratorTarget = NormalizeIterableTarget(iterable, context);

            if (!TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) || iterator is null)
            {
                throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
            }

            var nextMethod = iterator.GetIteratorNextCallable(context);
            return new IteratorDriverState
            {
                IteratorObject = iterator,
                Enumerator = null,
                IsAsyncIterator = kind == IteratorDriverKind.Await,
                NextMethod = nextMethod
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StoreSymbolValue(JsEnvironment environment, Symbol symbol, object? /* intentional */ value)
        {
            // Handle case where value is already a boxed JsValue
            var jsVal = value is JsValue jv ? jv : JsValue.FromObjectUnsafe(value);
            StoreSymbolValueJsValue(environment, symbol, jsVal);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StoreSymbolValueJsValue(JsEnvironment environment, Symbol symbol, JsValue value)
        {
            // DefineOrAssignJsValue is O(1) on the current environment -
            // it only looks at environment.Values, no scope chain walk.
            // This is optimal for generator symbols defined in the execution environment.
            environment.DefineOrAssignJsValue(symbol, value);
        }

        /// <summary>
        /// Stores a value using pre-resolved slot index for O(1) access.
        /// Falls back to dictionary-based storage if slot index is invalid.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StoreValueBySlot(JsEnvironment environment, Symbol symbol, int slotIndex, JsValue value)
        {
            if (slotIndex >= 0 && environment.HasSlots)
            {
                environment.GetSlotRef(slotIndex) = value;
                // Also update dictionary for symbol-based lookups elsewhere
                environment.DefineOrAssignJsValue(symbol, value);
            }
            else
            {
                environment.DefineOrAssignJsValue(symbol, value);
            }
        }

        /// <summary>
        /// Reads a value using pre-resolved slot index for O(1) access.
        /// Falls back to dictionary-based lookup if slot index is invalid.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetValueBySlot(JsEnvironment environment, Symbol symbol, int slotIndex, out JsValue value)
        {
            if (slotIndex >= 0 && environment.HasSlots)
            {
                value = environment.GetSlotRef(slotIndex);
                return true;
            }
            return TryGetSymbolValueJsValue(environment, symbol, out value);
        }

        private static bool TryGetSymbolValueJsValue(JsEnvironment environment, Symbol symbol, out JsValue value)
        {
            if (environment.TryGetJsValue(symbol, out value))
            {
                return true;
            }

            value = JsValue.Undefined;
            return false;
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
                _tryStack.Clear();
                _resumeContext.Clear();
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
            _resumedWithEnvironment = wasStart ? null : environment;
            var context = EnsureEvaluationContext();

            // If we're resuming from a yield that happened during AST evaluation
            // (via StatementInstruction), handle based on the resume mode.
            _realmState.Logger?.LogInformation(
                "ExecutePlan resume check: wasStart={WasStart} mode={Mode} _lastYieldSourceStart={Start}",
                wasStart, mode, _lastYieldSourceStart);

            if (!wasStart && _lastYieldSourceStart >= 0)
            {
                switch (mode)
                {
                    case ResumeMode.Next:
                        // For next(), set up resume state so the yield expression returns the resume value
                        SetYieldResumeValue(environment, resumeValue, _lastYieldSourceStart, _lastYieldSourceEnd);
                        break;
                    case ResumeMode.Return:
                        // For return(), close any active iterators and complete the generator.
                        // Don't re-evaluate the statement - just close and return.
                        _realmState.Logger?.LogInformation("ExecutePlan: early CompleteReturn for Return mode");
                        _lastYieldSourceStart = -1;
                        _lastYieldSourceEnd = -1;
                        return CompleteReturn(resumeValue);
                }
                // For Throw mode, we'll let the normal flow handle it via _pendingResumeKind

                _lastYieldSourceStart = -1;
                _lastYieldSourceEnd = -1;
            }

            // Restore active with-scopes when resuming
            // The _activeWithScopes stack contains the slots in reverse order (bottom to top)
            // We need to restore environments from bottom to top
            if (_activeWithScopes.Count > 0)
            {
                var scopesToRestore = _activeWithScopes.ToArray();
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
            if (_pendingAwaitKey is { } awaitKey)
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

                _pendingAwaitKey = null;
            }

            bool continueAfterCatch;
            do
            {
                continueAfterCatch = false;
                try
                {
                    while (_programCounter >= 0 && _programCounter < _plan.Instructions.Length)
                    {
                        // Check if HandleAbruptCompletion restored the environment (e.g., jumping to catch handler)
                        // This ensures block-scoped bindings from inside the try are no longer visible.
                        if (_restoredEnvironmentFromTry is { } restored)
                        {
                            environment = restored;
                            _restoredEnvironmentFromTry = null;
                        }

                        _currentInstructionIndex = _programCounter;
                        var instruction = _plan.Instructions[_programCounter];

                        // Trace instruction execution when debug logging is enabled
                        if (_realmState.Options.DebugMode)
                        {
                            _realmState.Logger?.LogTrace(
                                "[IR:{PC,3}] {Instruction}",
                                _programCounter,
                                ExecutionPlanPrinter.FormatInstruction(instruction));
                        }

                        // Detailed IR execution trace with environment depth
                        if (JsEngineConstants.TraceIrExecution)
                        {
                            ExecutionPlanPrinter.TraceInstruction(
                                _programCounter,
                                instruction,
                                environment.Depth,
                                environment.ScopeId,
                                environment.GetHashCode());
                        }

                        switch (instruction)
                        {
                            case StatementInstruction statementInstruction:
                                _ = statementInstruction.Statement.EvaluateStatementJsValue(environment, context);
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

                                    _tryStack.Clear();
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

                                    var target = FindLoopTarget(label, isBreak);
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

                            case ThrowInstruction throwInstruction:
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
                                        if (_tryStack.Count > 0)
                                        {
                                            _tryStack.Pop();
                                            if (HandleAbruptCompletion(AbruptKind.Throw, existingThrown, environment))
                                            {
                                                continue;
                                            }
                                        }
                                    }

                                    _tryStack.Clear();
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
                                    if (_tryStack.Count > 0)
                                    {
                                        _tryStack.Pop();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, throwValue, environment))
                                        {
                                            continue;
                                        }
                                    }
                                }

                                _tryStack.Clear();
                                throw new ThrowSignal(throwValue);

                            case EvaluateAndDiscardInstruction evaluateInstruction:
                                // Evaluate the expression and discard the result
                                _ = evaluateInstruction.Expression.EvaluateExpression(environment, context);
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

                                    _tryStack.Clear();
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

                            case BinaryOpInstruction binaryOpInstruction:
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
                                        _tryStack.Clear();
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
                                        _tryStack.Clear();
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

                            case IncrementSlotInstruction incrementInstruction:
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
                                    _tryStack.Clear();
                                    throw new ThrowSignal(incThrown);
                                }

                                // Convert to number if needed (fast path for already-number values)
                                double incNumValue;
                                if (incCurrentValue.IsNumber)
                                {
                                    incNumValue = incCurrentValue.NumberValue;
                                }
                                else
                                {
                                    incNumValue = incCurrentValue.ToNumber();
                                }

                                // Apply increment or decrement
                                var incNewValue = incrementInstruction.IsIncrement ? incNumValue + 1.0 : incNumValue - 1.0;
                                var incNewJsValue = JsValueCache.GetNumberJsValue(incNewValue);

                                // Update the binding - use AssignJsValue to walk up scope chain
                                environment.AssignJsValue(incrementInstruction.TargetSymbol, incNewJsValue);

                                _programCounter = incrementInstruction.Next;
                                continue;

                            case FunctionDeclarationInstruction functionDeclInstruction:
                                // Function declarations are hoisted - this is a no-op at runtime
                                _programCounter = functionDeclInstruction.Next;
                                continue;

                            case ClassDeclarationInstruction classDeclInstruction:
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

                                    _tryStack.Clear();
                                    throw new ThrowSignal(classThrown);
                                }

                                // Bind the class name in the environment
                                environment.DefineJsValue(classDeclInstruction.Declaration.Name, classValue,
                                    isLexical: true, blocksFunctionScopeOverride: true);

                                _programCounter = classDeclInstruction.Next;
                                continue;

                            case SimpleVariableDeclarationInstruction varDeclInstruction:
                                // Evaluate initializer if present
                                var varValue = varDeclInstruction.Initializer is null
                                    ? JsValue.Undefined
                                    : varDeclInstruction.Initializer.EvaluateExpression(environment, context);

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

                                    _tryStack.Clear();
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

                                // For var declarations, ensure the binding exists in function scope and assign
                                if (varDeclInstruction.Kind == VariableKind.Var)
                                {
                                    environment.EnsureFunctionScopedVarBinding(varDeclInstruction.TargetSymbol, context);
                                    // Try to assign to a blocked binding first (shadowed let/const in same scope)
                                    if (!environment.TryAssignBlockedBindingJsValue(varDeclInstruction.TargetSymbol, varValue))
                                    {
                                        environment.DefineOrAssignJsValue(varDeclInstruction.TargetSymbol, varValue);
                                    }
                                }
                                else
                                {
                                    // let/const - define as lexical binding with blocksFunctionScopeOverride
                                    // to match AST evaluator behavior (see IdentifierBindingExtensions.cs)
                                    var isConst = varDeclInstruction.Kind == VariableKind.Const;
                                    if (JsEngineConstants.TraceIrExecution)
                                    {
                                        ExecutionPlanPrinter.TraceDefine(
                                            varDeclInstruction.Kind.ToString(),
                                            varDeclInstruction.TargetSymbol.Name,
                                            varValue.ToString() ?? "?",
                                            environment.Depth,
                                            environment.ScopeId,
                                            environment.GetHashCode());
                                    }
                                    environment.DefineJsValue(varDeclInstruction.TargetSymbol, varValue,
                                        isConst: isConst, isLexical: true, blocksFunctionScopeOverride: true);
                                }

                                _programCounter = varDeclInstruction.Next;
                                continue;

                            case PushEnvironmentInstruction pushEnvInstruction:
                                // Stack-based environment model for per-iteration bindings:
                                // - If current env has same ScopeId as instruction → we're in a previous iteration
                                //   Parent is current.Enclosing (the loop scope), current is previous iter env
                                // - Otherwise → first iteration, current IS the loop scope
                                //
                                // Per-iteration envs are SIBLINGS (same parent), values are COPIED between them.
                                // This ensures closures capture separate values per iteration.

                                JsEnvironment loopScope;
                                JsEnvironment? previousIterEnv = null;

                                if (pushEnvInstruction.ScopeId >= 0 &&
                                    environment.ScopeId == pushEnvInstruction.ScopeId)
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
                                var newIterationEnv = allowPooling
                                    ? JsEnvironmentPool.Rent(loopScope, false, false, null, "scope", logger: _realmState.Logger)
                                    : new JsEnvironment(loopScope, false, false, null, "scope");

                                // Initialize slots for O(1) identifier lookups
                                if (pushEnvInstruction.SlotCount > 0 && pushEnvInstruction.ScopeId >= 0)
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
                                        foreach (var binding in pushEnvInstruction.PerIterationBindings)
                                        {
                                            if (previousIterEnv.TryGetJsValue(binding, out var value))
                                            {
                                                newIterationEnv.DefineJsValue(binding, value, isConst: false, isLexical: true);
                                            }
                                        }
                                    }

                                    // Return previous iteration env to pool (if pooled and not resumed-with)
                                    if (allowPooling && !ReferenceEquals(previousIterEnv, _resumedWithEnvironment))
                                    {
                                        JsEnvironmentPool.Return(previousIterEnv, _realmState.Logger);
                                    }
                                }
                                else if (!pushEnvInstruction.PerIterationBindings.IsDefaultOrEmpty)
                                {
                                    // First iteration - copy from loopScope where binding was defined
                                    foreach (var binding in pushEnvInstruction.PerIterationBindings)
                                    {
                                        if (loopScope.TryGetJsValue(binding, out var value))
                                        {
                                            newIterationEnv.DefineJsValue(binding, value, isConst: false, isLexical: true);
                                        }
                                    }
                                }

                                _resumedWithEnvironment = null;

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

                            case PopEnvironmentInstruction popEnvInstruction:
                                // Pop the iteration environment when exiting a loop.
                                // If current env matches ScopeId, pop (set to Enclosing).
                                // If not (loop ran 0 times), this is a no-op.
                                // CRITICAL: Only pop if ScopeId is valid (>= 0). When ScopeAnalyzer
                                // hasn't run, both ScopeIds are -1, and we'd incorrectly pop.
                                if (popEnvInstruction.ScopeId >= 0 && environment.ScopeId == popEnvInstruction.ScopeId)
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

                            case YieldInstruction yieldInstruction:
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

                                        _tryStack.Clear();
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

                            case YieldStarInstruction yieldStarInstruction:
                            {
                                var currentIndex = _programCounter;
                                if (!TryGetSymbolValueJsValue(environment, yieldStarInstruction.StateSlotSymbol,
                                        out var stateValue) ||
                                    !stateValue.TryGetObject<YieldStarState>(out var yieldStarState))
                                {
                                    yieldStarState = new YieldStarState();
                                    StoreSymbolValue(environment, yieldStarInstruction.StateSlotSymbol, yieldStarState);
                                }

                                if (yieldStarState.PendingAbrupt != AbruptKind.None &&
                                    _pendingResumeKind is not ResumePayloadKind.Throw and not ResumePayloadKind.Return)
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
                                            _tryStack.Clear();
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

                                        _tryStack.Clear();
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

                                        _tryStack.Clear();
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

                                        _tryStack.Clear();
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

                                            _tryStack.Clear();
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

                            case StoreResumeValueInstruction storeResumeValueInstruction:
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

                                    _tryStack.Clear();
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

                            case EnterTryInstruction enterTryInstruction:
                                PushTryFrame(enterTryInstruction, environment);
                                _programCounter = enterTryInstruction.Next;
                                continue;

                            case LeaveTryInstruction leaveTryInstruction:
                                CompleteTryNormally(leaveTryInstruction.Next);
                                continue;

                            case LoopEnterInstruction loopEnterInstruction:
                                _loopStack.Push(new LoopFrame(
                                    loopEnterInstruction.Label,
                                    loopEnterInstruction.BreakTarget,
                                    loopEnterInstruction.ContinueTarget));
                                _programCounter = loopEnterInstruction.Next;
                                continue;

                            case LoopExitInstruction loopExitInstruction:
                                if (_loopStack.Count > 0)
                                {
                                    _loopStack.Pop();
                                }
                                _programCounter = loopExitInstruction.Next;
                                continue;

                            case EndFinallyInstruction endFinallyInstruction:
                                if (_tryStack.Count == 0)
                                {
                                    _programCounter = endFinallyInstruction.Next;
                                    continue;
                                }

                                var completedFrame = _tryStack.Pop();
                                var pending = completedFrame.PendingCompletion;
                                if (pending.Kind == AbruptKind.None)
                                {
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

                                _tryStack.Clear();
                                // Handle case where pending.Value is already a boxed JsValue
                                var throwJs = pending.Value is JsValue tjs
                                    ? tjs
                                    : JsValue.FromObjectUnsafe(pending.Value);
                                throw new ThrowSignal(throwJs);

                            case IteratorInitInstruction iteratorInitInstruction:
                                var iterableValue =
                                    iteratorInitInstruction.IterableExpression.EvaluateExpression(environment, context);
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

                                    _tryStack.Clear();
                                    throw new ThrowSignal(initThrown);
                                }

                                var iteratorState =
                                    CreateIteratorDriverState(iterableValue, iteratorInitInstruction.Kind, context);

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
                                _currentDriverState = iteratorState;

                                // Use slot-based storage for O(1) access
                                StoreValueBySlot(iteratorEnv, iteratorInitInstruction.IteratorSlot,
                                    iteratorInitInstruction.IteratorSlotIndex,
                                    JsValue.FromObjectUnsafe(iteratorState));

                                _programCounter = iteratorInitInstruction.Next;
                                continue;

                            case IteratorMoveNextInstruction iteratorMoveNextInstruction:
                                var iteratorIndex = _programCounter;

                                // Use cached driver state for scope-correct access from child scopes
                                // (The iterator slot is in the loop scope, but we may be in a per-iteration child scope)
                                var driverState = _currentDriverState;

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

                                    if (!iteratorStateValue.TryGetObject<IteratorDriverState>(out driverState))
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    _currentDriverState = driverState;
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
                                    if (driverState.IteratorObject is JsObject iteratorObj)
                                    {
                                        driverState.NextMethod ??= iteratorObj.GetIteratorNextCallable(context);
                                        var nextResult = iteratorObj.InvokeIteratorNext(
                                            driverState.NextMethod!,
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

                                            _tryStack.Clear();
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
                                            _currentDriverState = null;
                                            _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                            continue;
                                        }

                                        // yielded is already a JsValue from TryGetProperty
                                        currentValue = resultObj.TryGetProperty("value", out var yielded)
                                            ? yielded
                                            : JsValue.Undefined;
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
                                            _currentDriverState = null;
                                            _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                            continue;
                                        }

                                        currentValue = enumerator.Current;
                                    }
                                    else
                                    {
                                        // Restore environment to enclosing scope when no iterator
                                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv3)
                                        {
                                            environment = enclosingEnv3;
                                        }
                                        _currentDriverState = null;
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

                                        _tryStack.Clear();
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

                                if (driverState.IteratorObject is JsObject awaitIteratorObj)
                                {
                                    if (!hasAwaitedNextResult)
                                    {
                                        driverState.NextMethod ??= awaitIteratorObj.GetIteratorNextCallable(context);
                                        var nextResult = awaitIteratorObj.InvokeIteratorNext(
                                            driverState.NextMethod!,
                                            context: context,
                                            callingEnvironment: environment);
                                        if (!TryResolvePromiseOrYield(nextResult, context, out var awaitedNext))
                                        {
                                            if (_asyncStepMode && _pendingPromise.TryGetPropertyAccessor(out _))
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

                                                _tryStack.Clear();
                                                throw new ThrowSignal(thrownAwait);
                                            }

                                            // Restore environment to enclosing scope when breaking
                                            if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv4)
                                            {
                                                environment = enclosingEnv4;
                                            }
                                            _currentDriverState = null;
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

                                        _tryStack.Clear();
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
                                        _currentDriverState = null;
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    var rawValue = awaitResultObj.TryGetProperty("value", out var yieldedAwait)
                                        ? yieldedAwait
                                        : JsValue.Undefined;
                                    if (!TryResolvePromiseOrYield(rawValue, context, out var fullyAwaitedValue))
                                    {
                                        if (_asyncStepMode && _pendingPromise.TryGetPropertyAccessor(out _))
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

                                            _tryStack.Clear();
                                            throw new ThrowSignal(thrownAwaitValue);
                                        }

                                        // Restore environment to enclosing scope
                                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv6)
                                        {
                                            environment = enclosingEnv6;
                                        }
                                        _currentDriverState = null;
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
                                        _currentDriverState = null;
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    // enumerated is already JsValue from IEnumerator<JsValue>.Current
                                    var enumerated = awaitEnumerator.Current;
                                    if (!TryResolvePromiseOrYield(enumerated, context, out var awaitedEnumerated))
                                    {
                                        if (_asyncStepMode && _pendingPromise.TryGetPropertyAccessor(out _))
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

                                            _tryStack.Clear();
                                            throw new ThrowSignal(thrownAwaitEnum);
                                        }

                                        // Restore environment to enclosing scope
                                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv8)
                                        {
                                            environment = enclosingEnv8;
                                        }
                                        _currentDriverState = null;
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
                                    _currentDriverState = null;
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                StoreIteratorValue:
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

                            case JumpInstruction jumpInstruction:
                                _programCounter = jumpInstruction.TargetIndex;
                                continue;

                            case BranchInstruction branchInstruction:
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

                                    _tryStack.Clear();
                                    throw new ThrowSignal(thrownBranch);
                                }

                                _programCounter = testValue.IsTruthy
                                    ? branchInstruction.ConsequentIndex
                                    : branchInstruction.AlternateIndex;
                                continue;

                            case BreakInstruction breakInstruction:
                                if (HandleAbruptCompletion(AbruptKind.Break, breakInstruction.TargetIndex, environment))
                                {
                                    continue;
                                }

                                // Pop environments until we reach the target scope
                                if (breakInstruction.TargetScopeId >= 0)
                                {
                                    while (environment.ScopeId != breakInstruction.TargetScopeId &&
                                           environment.Enclosing != null)
                                    {
                                        var popped = environment;
                                        environment = environment.Enclosing;
                                        // Note: we don't return to pool here as we don't track pooling per-env
                                    }
                                }

                                _programCounter = breakInstruction.TargetIndex;
                                continue;

                            case ContinueInstruction continueInstruction:
                                if (HandleAbruptCompletion(AbruptKind.Continue, continueInstruction.TargetIndex,
                                        environment))
                                {
                                    continue;
                                }

                                // Pop environments until we reach the target scope
                                if (continueInstruction.TargetScopeId >= 0)
                                {
                                    while (environment.ScopeId != continueInstruction.TargetScopeId &&
                                           environment.Enclosing != null)
                                    {
                                        var popped = environment;
                                        environment = environment.Enclosing;
                                        // Note: we don't return to pool here as we don't track pooling per-env
                                    }
                                }

                                _programCounter = continueInstruction.TargetIndex;
                                continue;

                            case ReturnInstruction returnInstruction:
                                var returnValue = returnInstruction.ReturnExpression is null
                                    ? JsValue.Undefined
                                    : returnInstruction.ReturnExpression.EvaluateExpression(environment, context);
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

                                    _tryStack.Clear();
                                    throw new ThrowSignal(pendingThrow);
                                }

                                if (context.IsReturn)
                                {
                                    var pendingReturn = context.FlowValue;
                                    context.ClearReturn();
                                    returnValue = pendingReturn;
                                }

                                if (HandleAbruptCompletionJsValue(AbruptKind.Return, returnValue, environment))
                                {
                                    if (_programCounter == _currentInstructionIndex)
                                    {
                                        _programCounter = returnInstruction.Next;
                                    }

                                    continue;
                                }

                                _programCounter = -1;
                                _state = GeneratorState.Completed;
                                _done = true;
                                _tryStack.Clear();
                                return CreateIteratorResult(returnValue, true);

                            case EnterWithInstruction enterWithInstruction:
                            {
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

                                    _tryStack.Clear();
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
                                    _activeWithScopes.Push(enterWithInstruction.WithScopeSlot);
                                    // Update the local environment reference to use the with-environment
                                    environment = withEnv;
                                }
                                // If we couldn't create a with-environment, just continue with the same environment

                                _programCounter = enterWithInstruction.Next;
                                continue;
                            }

                            case LeaveWithInstruction leaveWithInstruction:
                            {
                                // Remove this with-scope from active tracking
                                if (_activeWithScopes.Count > 0 &&
                                    ReferenceEquals(_activeWithScopes.Peek(), leaveWithInstruction.WithScopeSlot))
                                {
                                    _activeWithScopes.Pop();
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

                            case IteratorCloseInstruction iteratorCloseInstruction:
                            {
                                // Get the iterator state from the slot
                                if (TryGetSymbolValueJsValue(environment, iteratorCloseInstruction.IteratorSlot,
                                        out var iterStateValue) &&
                                    iterStateValue.TryGetObject<IteratorDriverState>(out var iterState) &&
                                    iterState.IteratorObject is JsObject iteratorObj)
                                {
                                    // Mark as closed before attempting close to prevent double-close
                                    iterState.MarkIteratorClosed();

                                    try
                                    {
                                        // Call IteratorClose - we don't preserve existing throws because
                                        // if IteratorClose throws, that error should replace any pending completion
                                        iteratorObj.IteratorClose(context);
                                    }
                                    catch (ThrowSignal closeThrown)
                                    {
                                        // IteratorClose threw - this should replace any pending return/throw
                                        // per ES spec: if IteratorClose throws, return that throw completion
                                        if (HandleAbruptCompletion(AbruptKind.Throw, closeThrown.ThrownValue,
                                                environment))
                                        {
                                            // HandleAbruptCompletion updated the pending completion in the try frame.
                                            // Continue to the next instruction in the finally block.
                                            _programCounter = iteratorCloseInstruction.Next;
                                            continue;
                                        }

                                        _tryStack.Clear();
                                        throw;
                                    }
                                }

                                _programCounter = iteratorCloseInstruction.Next;
                                continue;
                            }

                            default:
                                throw new InvalidOperationException(
                                    $"Unsupported generator instruction {instruction.GetType().Name}");
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
                    _tryStack.Clear();
                    _resumeContext.Clear();
                    throw;
                }
                catch
                {
                    _state = GeneratorState.Completed;
                    _done = true;
                    _programCounter = -1;
                    _tryStack.Clear();
                    _resumeContext.Clear();
                    throw;
                }
            } while (continueAfterCatch);

            _state = GeneratorState.Completed;
            _done = true;
            _tryStack.Clear();
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

        private static JsValue FinishExternalCompletion(ResumeMode mode, JsValue value)
        {
            return mode switch
            {
                ResumeMode.Throw => throw new ThrowSignal(value),
                _ => CreateIteratorResult(value, true)
            };
        }

        internal JsValue EvaluateAwaitInGenerator(AwaitExpression expression, JsEnvironment environment,
            EvaluationContext context)
        {
            // When not executing under async-aware stepping, fall back to the
            // legacy blocking helper so synchronous generators remain usable.
            if (!_asyncStepMode)
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
                _pendingAwaitKey = null;

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

            if (!_pendingPromise.TryGetPropertyAccessor(out _) || awaitKey is null)
            {
                return resolved;
            }

            // Remember which await site is pending so we can stash the
            // resolved value on resume.
            _pendingAwaitKey = awaitKey;
            _state = GeneratorState.Suspended;
            _programCounter = _currentInstructionIndex;
            context.SetPendingAwait();
            return JsValue.Undefined;

            // If TryResolvePromiseOrYield reported an error via the context,
            // let the caller observe the pending throw/return.
        }

        private bool TryResolvePromiseOrYield(JsValue candidate, EvaluationContext context, out JsValue resolvedValue)
        {
            var pendingPromise = _pendingPromise;
            var result = AwaitScheduler.TryResolvePromiseOrYield(candidate, _asyncStepMode, ref pendingPromise,
                context, out var resolvedObj);
            _pendingPromise = pendingPromise;
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
            result = _asyncStepMode
                ? JsValue.Undefined
                : CreateIteratorResult(JsValue.Undefined, false);
            return true;
        }

        private void RecordYield(EvaluationContext context, JsEnvironment? currentEnvironment = null)
        {
            // Remember the active yield slot so the next resume value is applied to the
            // right YieldExpression (ECMA-262 GeneratorResume, step threading of sent values).
            _lastYieldIndex = context.LastYieldIndex;

            // Also save source positions for yields from StatementInstruction (AST-evaluated yields).
            // These are used to set up resume state so the yield expression returns the resume value.
            _lastYieldSourceStart = context.LastYieldSourceStart;
            _lastYieldSourceEnd = context.LastYieldSourceEnd;

            _realmState.Logger?.LogInformation(
                "RecordYield: yieldIndex={YieldIndex} sourceStart={Start} sourceEnd={End}",
                _lastYieldIndex, _lastYieldSourceStart, _lastYieldSourceEnd);

            // Save the current environment so that when the generator resumes, it uses
            // the correct per-iteration environment (for loops with let bindings).
            // This is critical for `continue` to work correctly in generators.
            if (currentEnvironment != null)
            {
                _executionEnvironment = currentEnvironment;
            }

            // Clear the context's source positions for the next yield
            context.LastYieldSourceStart = -1;
            context.LastYieldSourceEnd = -1;
        }


        private void PreparePendingResumeValue(ResumeMode mode, JsValue resumeValue, bool wasStart)
        {
            if (wasStart)
            {
                // Per ES spec: The first next() argument is ignored when starting a generator.
                // This applies to both regular yield and yield* - the first call to inner iterator's
                // next() receives undefined, not the outer generator's first next() argument.
                _pendingResumeKind = ResumePayloadKind.None;
                _pendingResumeValue = JsValue.Undefined;
                return;
            }

            _pendingResumeKind = mode switch
            {
                ResumeMode.Throw => ResumePayloadKind.Throw,
                ResumeMode.Return => ResumePayloadKind.Return,
                _ => ResumePayloadKind.Value
            };

            _pendingResumeValue = resumeValue;

            if (_realmState.Logger?.IsEnabled(LogLevel.Information) == true)
            {
                var resumeType = resumeValue.ObjectValue?.GetType().Name ?? resumeValue.Kind.ToString();
                _realmState.Logger.LogInformation(
                    "PrepareResume yieldIndex={YieldIndex} kind={Kind} valueType={Type}",
                    _lastYieldIndex,
                    _pendingResumeKind,
                    resumeType);
            }

            if (_lastYieldIndex < 0)
            {
                return;
            }

            var resumeSlotIndex = _lastYieldIndex;
            switch (_pendingResumeKind)
            {
                case ResumePayloadKind.Throw:
                    _resumeContext.SetException(resumeSlotIndex, resumeValue);
                    break;
                case ResumePayloadKind.Return:
                    _resumeContext.SetReturn(resumeSlotIndex, resumeValue);
                    break;
                default:
                    _resumeContext.SetValue(resumeSlotIndex, resumeValue);
                    break;
            }
        }

        private (ResumePayloadKind Kind, JsValue Value) ConsumeResumeValue()
        {
            var kind = _pendingResumeKind;
            var value = _pendingResumeValue;
            _pendingResumeKind = ResumePayloadKind.None;
            _pendingResumeValue = JsValue.Undefined;

            if (kind == ResumePayloadKind.None)
            {
                return (ResumePayloadKind.Value, JsValue.Undefined);
            }

            return (kind, value);
        }

        private void PushTryFrame(EnterTryInstruction instruction, JsEnvironment environment)
        {
            var frame = new TryFrame(instruction.HandlerIndex, instruction.CatchSlotSymbol, instruction.FinallyIndex, environment);
            if (instruction.CatchSlotSymbol is { } slot && !environment.HasBinding(slot))
            {
                environment.DefineJsValue(slot, JsValue.Undefined);
            }

            _tryStack.Push(frame);
        }

        /// <summary>
        /// Finds the jump target for a break or continue signal.
        /// For unlabeled: returns the innermost loop's target.
        /// For labeled: searches the stack for a matching label.
        /// </summary>
        private int FindLoopTarget(Symbol? label, bool isBreak)
        {
            if (_loopStack.Count == 0)
            {
                return -1;
            }

            if (label is null)
            {
                // Unlabeled: use innermost loop
                var frame = _loopStack.Peek();
                return isBreak ? frame.BreakTarget : frame.ContinueTarget;
            }

            // Labeled: search for matching label
            foreach (var frame in _loopStack)
            {
                if (ReferenceEquals(frame.Label, label))
                {
                    return isBreak ? frame.BreakTarget : frame.ContinueTarget;
                }
            }

            return -1;
        }

        private void CompleteTryNormally(int resumeTarget)
        {
            if (_tryStack.Count == 0)
            {
                _programCounter = resumeTarget;
                return;
            }

            var frame = _tryStack.Peek();
            if (frame is { FinallyIndex: >= 0, FinallyScheduled: false })
            {
                frame.FinallyScheduled = true;
                frame.PendingCompletion = PendingCompletion.FromNormal(resumeTarget);
                _programCounter = frame.FinallyIndex;
                return;
            }

            _tryStack.Pop();
            _programCounter = resumeTarget;
        }

        private bool HandleAbruptCompletion(AbruptKind kind, object? /* intentional */ value, JsEnvironment environment)
        {
            // Clear any previous restored environment
            _restoredEnvironmentFromTry = null;

            while (_tryStack.Count > 0)
            {
                var frame = _tryStack.Peek();
                if (kind == AbruptKind.Throw && frame is { HandlerIndex: >= 0, CatchUsed: false })
                {
                    frame.CatchUsed = true;

                    // Restore to the environment that was active when entering the try block.
                    // This ensures that block-scoped bindings inside the try are no longer visible.
                    var targetEnv = frame.EntryEnvironment;
                    _restoredEnvironmentFromTry = targetEnv;

                    if (frame.CatchSlotSymbol is { } slot)
                    {
                        // Handle case where value is already a boxed JsValue
                        var valueJs = value is JsValue js ? js : JsValue.FromObjectUnsafe(value);
                        // Use the entry environment for the catch slot, not the current (nested) environment
                        if (targetEnv.HasBinding(slot))
                        {
                            targetEnv.AssignJsValue(slot, valueJs);
                        }
                        else
                        {
                            targetEnv.DefineJsValue(slot, valueJs);
                        }
                    }

                    _programCounter = frame.HandlerIndex;
                    return true;
                }

                if (frame.FinallyIndex >= 0)
                {
                    if (!frame.FinallyScheduled)
                    {
                        frame.FinallyScheduled = true;
                        frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);

                        // Restore to the environment that was active when entering the try block.
                        // This ensures that block-scoped bindings inside the try are no longer visible in finally.
                        _restoredEnvironmentFromTry = frame.EntryEnvironment;

                        _programCounter = frame.FinallyIndex;
                        return true;
                    }

                    // Per ES spec: when an abrupt completion occurs inside a finally block,
                    // the new completion replaces the pending one. For most callers
                    // (like StoreResumeValueInstruction for generator resumption),
                    // we update the pending and let them advance PC. For throw/return
                    // statements that end the finally abruptly, the caller handles it.
                    frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);
                    return true;
                }

                _tryStack.Pop();
            }

            return false;
        }

        /// <summary>
        /// JsValue overload - boxes the JsValue which is better than ToObject() as it preserves type info.
        /// </summary>
        private bool HandleAbruptCompletionJsValue(AbruptKind kind, JsValue value, JsEnvironment environment)
        {
            // Boxing JsValue is preferred over ToObject() because:
            // 1. It preserves the JsValue type information
            // 2. Downstream code can detect "is JsValue" and unbox efficiently
            return HandleAbruptCompletion(kind, value, environment);
        }

        private JsValue CompleteReturn(JsValue value)
        {
            // Close any active iterators before completing (array patterns and for-of loops)
            CloseActiveIterators();

            _programCounter = -1;
            _state = GeneratorState.Completed;
            _done = true;
            _tryStack.Clear();
            return CreateIteratorResult(value, true);
        }

        /// <summary>
        /// Closes all active iterators stored in the environment chain.
        /// This handles both array pattern iterators (from destructuring) and
        /// for-of loop iterators via the unified IActiveIteratorState interface.
        /// </summary>
        private void CloseActiveIterators()
        {
            if (_executionEnvironment is null)
            {
                return;
            }

            // Create a fresh context to avoid state interference from the existing context
            var context = _realmState.CreateContext(ScopeKind.Function, DetermineGeneratorScopeMode());

            // Scan the environment for all IActiveIteratorState objects and close their iterators
            var statesToClose = new List<(IActiveIteratorState State, IJsObjectLike Iterator)>();
            ScanEnvironmentForActiveIterators(_executionEnvironment, statesToClose);

            foreach (var (state, iterator) in statesToClose)
            {
                // Mark as closed first (before potential exception)
                state.MarkIteratorClosed();

                // Close the iterator - if it throws, that error replaces the return completion
                // Let the exception propagate directly without catching
                iterator.IteratorClose(context);
            }
        }

        /// <summary>
        /// Scans the environment chain for all IActiveIteratorState objects and collects
        /// those with active iterators that need closing.
        /// </summary>
        private static void ScanEnvironmentForActiveIterators(JsEnvironment env,
            List<(IActiveIteratorState, IJsObjectLike)> results)
        {
            while (true)
            {
                // Scan symbol bindings in this environment (using safe method to skip uninitialized TDZ bindings)
                foreach (var symbol in env.GetBindingSymbols())
                {
                    if (env.TryGetJsValueLocalSafe(symbol, out var jsValue) &&
                        !jsValue.IsNullOrUndefined &&
                        jsValue.TryGetObject<IActiveIteratorState>(out var state) &&
                        state.TryGetActiveIterator(out var iterator))
                    {
                        results.Add((state, iterator));
                    }
                }

                // Also scan slots for IteratorDriverState (for-of loop state)
                if (env.HasSlots && env._slots is { } slots)
                {
                    foreach (var slot in slots)
                    {
                        if (!slot.IsNullOrUndefined &&
                            slot.TryGetObject<IActiveIteratorState>(out var state) &&
                            state.TryGetActiveIterator(out var iterator))
                        {
                            results.Add((state, iterator));
                        }
                    }
                }

                // Also scan parent environments
                if (env.Enclosing is { } parent)
                {
                    env = parent;
                    continue;
                }

                break;
            }
        }

        private sealed class AwaitState
        {
            public bool HasResult { get; set; }
            public bool IsThrow { get; set; }
            public JsValue Result { get; set; } = JsValue.Undefined;
        }

        // Lightweight step result used by async-generator wrappers so they can
        // drive the same IR plan without duplicating the interpreter. This
        // supports yield/completion/throw, and has room for a future "Pending"
        // state that surfaces promise-like values without blocking.
        [StructLayout(LayoutKind.Auto)]
        internal readonly record struct AsyncGeneratorStepResult(
            AsyncGeneratorStepKind Kind,
            JsValue Value,
            bool Done,
            JsValue PendingPromise);

        internal enum AsyncGeneratorStepKind
        {
            Yield,
            Completed,
            Throw,
            Pending
        }

        internal enum ResumeMode
        {
            Next,
            Throw,
            Return
        }

        private enum GeneratorState
        {
            Start,
            Suspended,
            Executing,
            Completed
        }

        private enum ResumePayloadKind
        {
            None,
            Value,
            Throw,
            Return
        }

        private enum AbruptKind
        {
            None,
            Return,
            Throw,
            Break,
            Continue
        }

        private sealed class TryFrame(int handlerIndex, Symbol? catchSlotSymbol, int finallyIndex, JsEnvironment entryEnvironment)
        {
            public int HandlerIndex { get; } = handlerIndex;
            public Symbol? CatchSlotSymbol { get; } = catchSlotSymbol;
            public int FinallyIndex { get; } = finallyIndex;
            /// <summary>
            /// The environment that was active when entering the try block.
            /// Used to restore scope when jumping to catch/finally handlers.
            /// </summary>
            public JsEnvironment EntryEnvironment { get; } = entryEnvironment;
            public bool CatchUsed { get; set; }
            public bool FinallyScheduled { get; set; }
            public PendingCompletion PendingCompletion { get; set; } = PendingCompletion.None;
        }

        private readonly record struct PendingCompletion(AbruptKind Kind, object? Value, int ResumeTarget)
        {
            public static PendingCompletion None { get; } = new(AbruptKind.None, null, -1);

            public static PendingCompletion FromNormal(int resumeTarget)
            {
                return new PendingCompletion(AbruptKind.None, null, resumeTarget);
            }

            public static PendingCompletion FromAbrupt(AbruptKind kind, object? value)
            {
                return new PendingCompletion(kind, value, -1);
            }
        }

        /// <summary>
        /// Tracks loop context at runtime so break/continue from AST-evaluated code
        /// (via StatementInstruction) can resolve their jump targets.
        /// </summary>
        private readonly record struct LoopFrame(Symbol? Label, int BreakTarget, int ContinueTarget);

        private sealed class YieldStarState
        {
            public DelegatedYieldState? State { get; set; }
            public bool AwaitingResume { get; set; }

            public AbruptKind PendingAbrupt { get; set; }

            // Use JsValue instead of object? to avoid boxing
            public JsValue PendingValue { get; set; }
        }
    }
}
