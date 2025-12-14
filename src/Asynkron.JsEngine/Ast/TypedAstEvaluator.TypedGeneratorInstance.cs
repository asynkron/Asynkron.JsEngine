using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class TypedGeneratorInstance
    {
        private readonly IReadOnlyList<JsValue> _arguments;
        private readonly IJsCallable _callable;
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly GeneratorPlan? _plan;
        private readonly bool _hasFunctionNameEnvironment;
        private readonly bool _isStrict;
        private readonly bool _allowIdentifierCache;
        private readonly ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes;
        private readonly RealmState _realmState;
        private readonly YieldResumeContext _resumeContext = new();
        private readonly IJsObjectLike? _homeObject;
        private readonly PrivateNameScope? _privateNameScope;
        // Track yield slots that have already produced a value so re-running the body after a
        // nested suspension skips only those slots (per the generator resumption rules).
        private readonly HashSet<int> _consumedYieldIndices = new();
        private readonly JsValue _thisValue;
        private readonly Stack<TryFrame> _tryStack = new();
        // Track active with-scope slots for restoration after yield/resume
        private readonly Stack<Symbol> _activeWithScopes = new();
        private bool _asyncStepMode;
        private EvaluationContext? _context;
        private int _currentInstructionIndex;
        private bool _done;
        private JsEnvironment? _executionEnvironment;
        private int _lastYieldIndex = -1;

        private Symbol? _pendingAwaitKey;
        private JsValue _pendingPromise;
        private ResumePayloadKind _pendingResumeKind;
        private JsValue _pendingResumeValue = JsValue.Undefined;
        private int _programCounter;
        private bool _privateScopesApplied;
        private GeneratorState _state = GeneratorState.Start;

        public TypedGeneratorInstance(
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
            ImmutableArray<PrivateNameScope> capturedPrivateNameScopes)
        {
            _function = function;
            _closure = closure;
            _arguments = arguments;
            _thisValue = thisValue;
            _callable = callable;
            _realmState = realmState;
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
            _homeObject = homeObject;
            _privateNameScope = privateNameScope;
            _capturedPrivateNameScopes = capturedPrivateNameScopes;
            _isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict;
            _allowIdentifierCache = AllowsIdentifierCaching(function);

            if (!GeneratorIrBuilder.TryBuild(function, out var plan, out var failureReason))
            {
                var reason = failureReason ?? "Generator contains unsupported construct for IR.";
                throw new NotSupportedException($"Generator IR not implemented for this function: {reason}");
            }

            _plan = plan;
            _programCounter = plan.EntryPoint;
        }

        public JsObject CreateGeneratorObject()
        {
            var prototype = ResolveGeneratorPrototype();
            var iterator = CreateGeneratorIteratorObject(
                args => Next(args.GetArgument(0)),
                args => Return(args.Count > 0 ? args[0] : JsValue.Undefined),
                args => Throw(args.Count > 0 ? args[0] : JsValue.Undefined),
                prototype);
            iterator.SetProperty(IteratorSymbolPropertyName, new JsValue(new HostFunction((_, _) => new JsValue(iterator))));
            iterator.SetProperty(GeneratorBrandPropertyName, new JsValue(GeneratorBrandMarker));
            return iterator;
        }

        public void Initialize()
        {
            EnsureExecutionEnvironment();
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
                accessor.TryGetProperty("prototype", out var protoValue) &&
                protoValue.TryGetObject<JsObject>(out var prototypeObject))
            {
                return prototypeObject;
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

                if (_pendingPromise.TryGetObject<JsObject>(out var pending))
                {
                    return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, JsValue.Undefined, false,
                        new JsValue(pending));
                }

                if (result.TryGetObject<JsObject>(out var obj) &&
                    obj.TryGetProperty("done", out var doneRaw) &&
                    doneRaw.TryGetObject<bool>(out var done) &&
                    obj.TryGetProperty("value", out var value))
                {
                    return done
                        ? new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, value, true, JsValue.Undefined)
                        : new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Yield, value, false, JsValue.Undefined);
                }

                // If the plan completed without producing a well-formed iterator
                // result, treat it as a completed step with undefined.
                return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, JsValue.Undefined, true, JsValue.Undefined);
            }
            catch (PendingAwaitException)
            {
                if (_pendingPromise.TryGetObject<JsObject>(out var pending))
                {
                    return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, JsValue.Undefined, false,
                        new JsValue(pending));
                }

                throw new InvalidOperationException("Async generator awaited a non-promise value.");
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

            var hasParameterExpressions = HasParameterExpressions(_function);
            var lexicalNamesRaw = CollectLexicalNames(_function.Body);
            var lexicalNames = lexicalNamesRaw.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(lexicalNamesRaw, ReferenceEqualityComparer<Symbol>.Instance);
            var catchParameterNamesRaw = CollectCatchParameterNames(_function.Body);
            var catchParameterNames = catchParameterNamesRaw.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(catchParameterNamesRaw, ReferenceEqualityComparer<Symbol>.Instance);
            var simpleCatchParameterNamesRaw = CollectSimpleCatchParameterNames(_function.Body);
            var simpleCatchParameterNames = simpleCatchParameterNamesRaw.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(simpleCatchParameterNamesRaw, ReferenceEqualityComparer<Symbol>.Instance);
            var bodyLexicalNames = lexicalNames.Count == 0
                ? lexicalNames
                : new HashSet<Symbol>(lexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            bodyLexicalNames.ExceptWith(simpleCatchParameterNames);

            var parameterNames = new List<Symbol>();
            CollectParameterNamesFromFunction(_function, parameterNames);
            var blockedFunctionVarNames = bodyLexicalNames.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(bodyLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            foreach (var parameterName in parameterNames)
            {
                blockedFunctionVarNames.Add(parameterName);
            }

            JsEnvironment parameterEnvironment;
            JsEnvironment functionEnvironment;
            JsEnvironment varEnvironment;
            if (hasParameterExpressions)
            {
                functionEnvironment = new JsEnvironment(_closure, true, _isStrict, _function.Source,
                    description);
                functionEnvironment.SetBodyLexicalNames(bodyLexicalNames);

                parameterEnvironment = new JsEnvironment(functionEnvironment, false, _isStrict, _function.Source,
                    description, isParameterEnvironment: true);
                parameterEnvironment.SetBodyLexicalNames(bodyLexicalNames);

                varEnvironment = new JsEnvironment(parameterEnvironment, true, _isStrict, _function.Source,
                    description);
                varEnvironment.SetBodyLexicalNames(bodyLexicalNames);
            }
            else
            {
                functionEnvironment = new JsEnvironment(_closure, true, _isStrict, _function.Source,
                    description);
                functionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
                parameterEnvironment = functionEnvironment;
                varEnvironment = functionEnvironment;
            }

            var executionEnvironment = new JsEnvironment(varEnvironment, false, _isStrict,
                _function.Source, description, isBodyEnvironment: true);
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);

            var generatorContext = _realmState.CreateContext(
                ScopeKind.Function,
                DetermineGeneratorScopeMode());

            JsValue boundThis = _thisValue;
            if (!_isStrict)
            {
                if (boundThis.IsNullish)
                {
                    boundThis = _realmState.Engine?.GlobalObject is { } go ? new JsValue(go) : JsValue.Undefined;
                }

                if (boundThis.IsNull)
                {
                    boundThis = new JsValue(new JsObject
                    {
                        RealmState = _realmState
                    });
                }
                else if (!boundThis.TryGetObject<IJsPropertyAccessor>(out _) &&
                         !boundThis.IsNullish &&
                         !boundThis.TryGetObject<IIsHtmlDda>(out _))
                {
                    boundThis = new JsValue(ToObjectForDestructuring(boundThis.ToObject(), generatorContext));
                }
            }

            functionEnvironment.Define(Symbol.This, boundThis.ToObject());
            functionEnvironment.Define(Symbol.YieldResumeContextSymbol, _resumeContext);
            functionEnvironment.Define(Symbol.GeneratorInstanceSymbol, this);

            var superPrototype = _homeObject?.Prototype;
            if (superPrototype is null && boundThis.TryGetObject<JsObject>(out var thisObj))
            {
                superPrototype = thisObj.Prototype;
            }

            if (superPrototype is not null)
            {
                var superBinding = new SuperBinding(null, superPrototype, boundThis.ToObject(), true);
                functionEnvironment.Define(Symbol.Super, superBinding);
            }

            var argumentsObject =
                CreateArgumentsObject(_function, _arguments, parameterEnvironment, _realmState, _callable,
                    _isStrict);
            parameterEnvironment.Define(Symbol.Arguments, argumentsObject, isLexical: false);
            if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
            {
                functionEnvironment.Define(Symbol.Arguments, argumentsObject, isLexical: false);
            }

            if (_function.Name is { } functionName && !_hasFunctionNameEnvironment)
            {
                parameterEnvironment.Define(functionName, _callable, isConst: true, isLexical: true, blocksFunctionScopeOverride: true);
            }

            HoistVarDeclarations(_function.Body, executionEnvironment, generatorContext,
                lexicalNames: lexicalNames,
                catchParameterNames: catchParameterNames,
                simpleCatchParameterNames: simpleCatchParameterNames);

            BindFunctionParameters(_function, _arguments, parameterEnvironment, generatorContext);
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

        private static JsObject CreateIteratorResult(JsValue value, bool done)
        {
            var result = new JsObject();
            result.SetProperty("value", value);
            result.SetProperty("done", new JsValue(done));
            return result;
        }

        private static IteratorDriverState CreateIteratorDriverState(
            JsValue iterable,
            IteratorDriverKind kind,
            EvaluationContext context)
        {
            var iteratorTarget = NormalizeIterableTarget(iterable.ToObject(), context);

            if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
            {
                var nextMethod = iterator.GetIteratorNextCallable(context);
                return new IteratorDriverState
                {
                    IteratorObject = iterator,
                    Enumerator = null,
                    IsAsyncIterator = kind == IteratorDriverKind.Await,
                    NextMethod = nextMethod
                };
            }

            throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
        }

        private static void StoreSymbolValue(JsEnvironment environment, Symbol symbol, object? value)
        {
            if (environment.TryGet(symbol, out _))
            {
                environment.Assign(symbol, value);
            }
            else
            {
                environment.Define(symbol, value);
            }
        }

        private static bool TryGetSymbolValue(JsEnvironment environment, Symbol symbol, out object? value)
        {
            if (environment.TryGet(symbol, out var existing))
            {
                value = existing;
                return true;
            }

            value = null;
            return false;
        }

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

            if ((mode == ResumeMode.Throw || mode == ResumeMode.Return) && wasStart)
            {
                _state = GeneratorState.Completed;
                _done = true;
                return FinishExternalCompletion(mode, resumeValue);
            }

            _state = GeneratorState.Executing;
            PreparePendingResumeValue(mode, resumeValue, wasStart);

            var environment = EnsureExecutionEnvironment();
            var context = EnsureEvaluationContext();
            StoreSymbolValue(environment, Symbol.YieldTrackerSymbol, new YieldTracker(_consumedYieldIndices));

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
                    if (TryGetSymbolValue(environment, slot, out var storedEnvObj) &&
                        storedEnvObj is JsEnvironment storedWithEnv)
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
                if (kind == ResumePayloadKind.Value)
                {
                    if (environment.TryGet(awaitKey, out var stateObj) && stateObj is AwaitState state)
                    {
                        state.HasResult = true;
                        state.Result = value;
                        environment.Assign(awaitKey, state);
                    }
                    else
                    {
                        var newState = new AwaitState { HasResult = true, Result = value };
                        if (environment.TryGet(awaitKey, out _))
                        {
                            environment.Assign(awaitKey, newState);
                        }
                        else
                        {
                            environment.Define(awaitKey, newState);
                        }
                    }
                }

                _pendingAwaitKey = null;
            }

            try
            {
                while (_programCounter >= 0 && _programCounter < _plan.Instructions.Length)
                {
                    _currentInstructionIndex = _programCounter;
                    var instruction = _plan.Instructions[_programCounter];
                    switch (instruction)
                    {
                        case StatementInstruction statementInstruction:
                            EvaluateStatement(statementInstruction.Statement, environment, context);
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

                            if (context.IsReturn)
                            {
                                var returnSignalValue = context.FlowValue;
                                context.ClearReturn();
                                if (HandleAbruptCompletion(AbruptKind.Return, returnSignalValue, environment))
                                {
                                    continue;
                                }

                                return CompleteReturn(returnSignalValue);
                            }

                            if (context.IsYield)
                            {
                                var yieldedSignalValue = context.FlowValue;
                                // Check if the yield signal includes an original iterator result object (from yield*)
                                var iteratorResultObject = (context.CurrentSignal as YieldCompletionSignal)?.IteratorResultObject;
                                RecordYield(context);
                                context.Clear();
                                _state = GeneratorState.Suspended;
                                // If we have an original iterator result object, return it to preserve done property
                                return iteratorResultObject ?? CreateIteratorResult(yieldedSignalValue, false);
                            }

                            _programCounter = statementInstruction.Next;
                            continue;

                        case YieldInstruction yieldInstruction:
                            JsValue yieldedValue = JsValue.Undefined;
                            var yieldedDuringOperand = false;
                            if (yieldInstruction.YieldExpression is not null)
                            {
                                yieldedValue = EvaluateExpression(yieldInstruction.YieldExpression, environment,
                                    context);
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
                                    yieldedValue = new JsValue(context.FlowValue);
                                    // Check if the yield signal includes an original iterator result object (from yield* in operand)
                                    var nestedIteratorResult = (context.CurrentSignal as YieldCompletionSignal)?.IteratorResultObject;
                                    context.Clear();
                                    yieldedDuringOperand = true;
                                    _programCounter = _currentInstructionIndex;
                                    RecordYield(context);
                                    _state = GeneratorState.Suspended;
                                    return new JsValue(nestedIteratorResult ?? CreateIteratorResult(yieldedValue, false));
                                }
                            }

                            _programCounter = yieldInstruction.Next;
                            RecordYield(context);
                            _state = GeneratorState.Suspended;
                            return new JsValue(CreateIteratorResult(yieldedValue, false));

                        case YieldStarInstruction yieldStarInstruction:
                        {
                            var currentIndex = _programCounter;
                            if (!TryGetSymbolValue(environment, yieldStarInstruction.StateSlotSymbol,
                                    out var stateValue) ||
                                stateValue is not YieldStarState yieldStarState)
                            {
                                yieldStarState = new YieldStarState();
                                StoreSymbolValue(environment, yieldStarInstruction.StateSlotSymbol, yieldStarState);
                            }

                            if (yieldStarState.PendingAbrupt != AbruptKind.None &&
                                _pendingResumeKind is not ResumePayloadKind.Throw and not ResumePayloadKind.Return)
                            {
                                var pendingKind = yieldStarState.PendingAbrupt;
                                var pendingValue = yieldStarState.PendingValue;
                                yieldStarState.PendingAbrupt = AbruptKind.None;
                                yieldStarState.PendingValue = null;
                                yieldStarState.State = null;
                                yieldStarState.AwaitingResume = false;
                                environment.Assign(yieldStarInstruction.StateSlotSymbol, null);

                                if (pendingKind == AbruptKind.Throw)
                                {
                                    if (HandleAbruptCompletion(AbruptKind.Throw, pendingValue, environment))
                                    {
                                        continue;
                                    }

                                    _tryStack.Clear();
                                    throw new ThrowSignal(pendingValue);
                                }

                                if (pendingKind == AbruptKind.Return)
                                {
                                    if (HandleAbruptCompletion(AbruptKind.Return, pendingValue, environment))
                                    {
                                        continue;
                                    }

                                    return CompleteReturn(pendingValue);
                                }
                            }

                            // Track if this is the first entry to this yield* (State is null means first entry)
                            var isFirstYieldStarEntry = yieldStarState.State is null;

                            if (yieldStarState.State is null)
                            {
                                _realmState.Logger?.LogInformation("YieldStar: Creating new DelegatedState");
                                var yieldStarIterable =
                                    EvaluateExpression(yieldStarInstruction.IterableExpression, environment, context).ToObject();
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

                                yieldStarState.State = CreateDelegatedState(yieldStarIterable, context);

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
                                _realmState.Logger?.LogInformation("YieldStar: Reusing existing DelegatedState, AwaitingResume={Awaiting}",
                                    yieldStarState.AwaitingResume);
                            }

                            while (true)
                            {
                                JsValue sendValue = JsValue.Undefined;
                                var hasSendValue = false;
                                var propagateThrow = false;
                                var propagateReturn = false;

                                // Per ES spec (14.4.14): On first entry to yield*, call iteratorRecord.[[NextMethod]]
                                // with iteratorRecord.[[Iterator]] as this and no arguments (undefined).
                                // Node.js V8 confirms: args.length=1, args[0]=undefined
                                if (isFirstYieldStarEntry)
                                {
                                    // On first entry to yield*, we pass undefined as the argument
                                    // (the outer generator's first next() argument is ignored per spec)
                                    hasSendValue = true;
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
                                            hasSendValue = true;
                                            sendValue = delegatedResumePayload;
                                            break;
                                        case ResumePayloadKind.Return:
                                            propagateReturn = true;
                                            hasSendValue = true;
                                            sendValue = delegatedResumePayload;
                                            break;
                                        default:
                                            hasSendValue = true;
                                            sendValue = delegatedResumePayload;
                                            break;
                                    }
                                }

                                var iteratorResult = yieldStarState.State!.MoveNext(
                                    sendValue.ToObject(),
                                    hasSendValue,
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
                                    environment.Assign(yieldStarInstruction.StateSlotSymbol, null);
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
                                        yieldStarState.PendingValue = sendValue.ToObject();
                                        yieldStarState.AwaitingResume = true;
                                        _programCounter = currentIndex;
                                        _state = GeneratorState.Suspended;
                                        // Use original iterator result object to preserve done/value properties
                                        return new JsValue(iteratorResult.IteratorResultObject ?? CreateIteratorResult(new JsValue(iteratorResult.Value), false));
                                    }

                                    yieldStarState.State = null;
                                    yieldStarState.AwaitingResume = false;
                                    environment.Assign(yieldStarInstruction.StateSlotSymbol, null);

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
                                    environment.Assign(yieldStarInstruction.StateSlotSymbol, null);
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
                                    environment.Assign(yieldStarInstruction.StateSlotSymbol, null);
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
                                    return new JsValue(originalResult);
                                }
                                var resultDone = propagateReturn ? iteratorResult.Done : false;
                                return new JsValue(CreateIteratorResult(new JsValue(iteratorResult.Value), resultDone));
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
                                if (environment.TryGet(resumeSymbol, out _))
                                {
                                    environment.Assign(resumeSymbol, resumePayload);
                                }
                                else
                                {
                                    environment.Define(resumeSymbol, resumePayload);
                                }
                            }

                            if (context.IsThrow)
                            {
                                var thrownPayload = context.FlowValue;
                                context.Clear();
                                if (HandleAbruptCompletion(AbruptKind.Throw, thrownPayload, environment))
                                {
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
                                    continue;
                                }

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

                        case EndFinallyInstruction endFinallyInstruction:
                            if (_tryStack.Count == 0)
                            {
                                _programCounter = endFinallyInstruction.Next;
                                continue;
                            }

                            var completedFrame = _tryStack.Pop();
                            var pending = completedFrame.PendingCompletion;
                            // Console.WriteLine($"[IR] EndFinally: pending={pending.Kind}, value={pending.Value}, resume={pending.ResumeTarget}, stack={_tryStack.Count}");
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

                                return CompleteReturn(pending.Value);
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
                            throw new ThrowSignal(pending.Value);

                        case IteratorInitInstruction iteratorInitInstruction:
                            var iterableValue = EvaluateExpression(iteratorInitInstruction.IterableExpression,
                                environment, context);
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
                            StoreSymbolValue(environment, iteratorInitInstruction.IteratorSlot, iteratorState);
                            _programCounter = iteratorInitInstruction.Next;
                            continue;

                        case IteratorMoveNextInstruction iteratorMoveNextInstruction:
                            var iteratorIndex = _programCounter;
                            if (!TryGetSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                    out var iteratorStateValue) ||
                                iteratorStateValue is not IteratorDriverState driverState)
                            {
                                _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                continue;
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
                                    if (nextResult is not JsObject resultObj)
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    var done = resultObj.TryGetProperty("done", out var doneValue) &&
                                               JsOps.ToBoolean(doneValue);
                                    if (done)
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    currentValue = resultObj.TryGetProperty("value", out var yielded)
                                        ? yielded
                                        : JsValue.Undefined;
                                }
                                else if (driverState.Enumerator is IEnumerator<object?> enumerator)
                                {
                                    if (!enumerator.MoveNext())
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    currentValue = new JsValue(enumerator.Current);
                                }
                                else
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                StoreSymbolValue(environment, iteratorMoveNextInstruction.ValueSlot, currentValue.ToObject());
                                _programCounter = iteratorMoveNextInstruction.Next;
                                continue;
                            }

                            JsValue awaitedValue = JsValue.Undefined;
                            JsValue awaitedNextResult = JsValue.Undefined;

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
                                StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot, driverState);

                                if (forAwaitResumeKind == ResumePayloadKind.Throw)
                                {
                                    if (HandleAbruptCompletion(AbruptKind.Throw, forAwaitResumePayload.ToObject(), environment))
                                    {
                                        continue;
                                    }

                                    _tryStack.Clear();
                                    throw new ThrowSignal(forAwaitResumePayload.ToObject());
                                }

                                if (forAwaitResumeKind == ResumePayloadKind.Return)
                                {
                                    if (HandleAbruptCompletion(AbruptKind.Return, forAwaitResumePayload.ToObject(), environment))
                                    {
                                        continue;
                                    }

                                    return CompleteReturn(forAwaitResumePayload.ToObject());
                                }

                                if (awaitingValue)
                                {
                                    awaitedValue = forAwaitResumePayload;
                                    goto StoreIteratorValue;
                                }

                                awaitedNextResult = forAwaitResumePayload;
                            }

                            if (driverState.IteratorObject is JsObject awaitIteratorObj)
                            {
                                if (awaitedNextResult.IsUndefined)
                                {
                                    driverState.NextMethod ??= awaitIteratorObj.GetIteratorNextCallable(context);
                                    var nextResult = awaitIteratorObj.InvokeIteratorNext(
                                        driverState.NextMethod!,
                                        context: context,
                                        callingEnvironment: environment);
                                    if (!TryAwaitPromiseOrSchedule(new JsValue(nextResult), context, out var awaitedNext))
                                    {
                                        if (_asyncStepMode && _pendingPromise.TryGetObject<JsObject>(out _))
                                        {
                                            driverState.AwaitingNextResult = true;
                                            StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                                driverState);
                                            _state = GeneratorState.Suspended;
                                            _programCounter = iteratorIndex;
                                            return new JsValue(CreateIteratorResult(JsValue.Undefined, false));
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

                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    awaitedNextResult = new JsValue(awaitedNext);
                                }

                                if (!awaitedNextResult.TryGetObject<JsObject>(out var awaitResultObj))
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                var doneAwait = awaitResultObj.TryGetProperty("done", out var awaitDoneValue) &&
                                                awaitDoneValue.TryGetObject<bool>(out var doneVal) && doneVal;
                                if (doneAwait)
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                var rawValue = awaitResultObj.TryGetProperty("value", out var yieldedAwait)
                                    ? yieldedAwait
                                    : JsValue.Undefined;
                                if (!TryAwaitPromiseOrSchedule(rawValue, context, out var fullyAwaitedValue))
                                {
                                    if (_asyncStepMode && _pendingPromise.TryGetObject<JsObject>(out _))
                                    {
                                        driverState.AwaitingValue = true;
                                        StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                            driverState);
                                        _state = GeneratorState.Suspended;
                                        _programCounter = iteratorIndex;
                                        return new JsValue(CreateIteratorResult(JsValue.Undefined, false));
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

                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                awaitedValue = new JsValue(fullyAwaitedValue);
                            }
                            else if (driverState.Enumerator is IEnumerator<object?> awaitEnumerator)
                            {
                                if (!awaitEnumerator.MoveNext())
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                var enumerated = awaitEnumerator.Current;
                                if (!TryAwaitPromiseOrSchedule(new JsValue(enumerated), context, out var awaitedEnumerated))
                                {
                                    if (_asyncStepMode && _pendingPromise.TryGetObject<JsObject>(out _))
                                    {
                                        driverState.AwaitingValue = true;
                                        StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                            driverState);
                                        _state = GeneratorState.Suspended;
                                        _programCounter = iteratorIndex;
                                        return new JsValue(CreateIteratorResult(JsValue.Undefined, false));
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

                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                awaitedValue = new JsValue(awaitedEnumerated);
                            }
                            else
                            {
                                _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                continue;
                            }

                            StoreIteratorValue:
                            StoreSymbolValue(environment, iteratorMoveNextInstruction.ValueSlot, awaitedValue.ToObject());
                            _programCounter = iteratorMoveNextInstruction.Next;
                            continue;

                        case JumpInstruction jumpInstruction:
                            _programCounter = jumpInstruction.TargetIndex;
                            continue;

                        case BranchInstruction branchInstruction:
                            var testValue = EvaluateExpression(branchInstruction.Condition, environment, context);
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

                            _programCounter = breakInstruction.TargetIndex;
                            continue;

                        case ContinueInstruction continueInstruction:
                            if (HandleAbruptCompletion(AbruptKind.Continue, continueInstruction.TargetIndex,
                                    environment))
                            {
                                continue;
                            }

                            _programCounter = continueInstruction.TargetIndex;
                            continue;

                        case ReturnInstruction returnInstruction:
                            var returnValue = returnInstruction.ReturnExpression is null
                                ? JsValue.Undefined
                                : EvaluateExpression(returnInstruction.ReturnExpression, environment, context);
                            if (context.IsThrow)
                            {
                                var pendingThrow = context.FlowValue;
                                context.Clear();
                                if (HandleAbruptCompletion(AbruptKind.Throw, pendingThrow, environment))
                                {
                                    continue;
                                }

                                _tryStack.Clear();
                                throw new ThrowSignal(pendingThrow);
                            }

                            if (context.IsReturn)
                            {
                                var pendingReturn = context.FlowValue;
                                context.ClearReturn();
                                returnValue = new JsValue(pendingReturn);
                            }

                            if (HandleAbruptCompletion(AbruptKind.Return, returnValue.ToObject(), environment))
                            {
                                continue;
                            }

                            _programCounter = -1;
                            _state = GeneratorState.Completed;
                            _done = true;
                            _tryStack.Clear();
                            return new JsValue(CreateIteratorResult(returnValue, true));

                        case EnterWithInstruction enterWithInstruction:
                        {
                            var objValue = EvaluateExpression(enterWithInstruction.ObjectExpression, environment, context).ToObject();
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
                            if (TryConvertToWithBindingObject(objValue, context, out var withObject))
                            {
                                var withEnv = new JsEnvironment(environment, false, context.CurrentScope.IsStrict,
                                    enterWithInstruction.ObjectExpression.Source, "with", withObject);
                                // Store the with-environment in the root environment slot so it persists across yields
                                StoreSymbolValue(_executionEnvironment!, enterWithInstruction.WithScopeSlot, withEnv);
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
                            if (TryGetSymbolValue(_executionEnvironment!, leaveWithInstruction.WithScopeSlot, out var storedEnvObj) &&
                                storedEnvObj is JsEnvironment storedWithEnv)
                            {
                                // The with-environment's Enclosing is the original environment
                                environment = storedWithEnv.Enclosing ?? environment;
                            }

                            _programCounter = leaveWithInstruction.Next;
                            continue;
                        }

                        default:
                            throw new InvalidOperationException(
                                $"Unsupported generator instruction {instruction.GetType().Name}");
                    }
                }
            }
            catch (PendingAwaitException)
            {
                // A pending await surfaced from within the generator body.
                // Async-aware callers translate this into a Pending step so
                // the generator can resume once the promise settles.
                if (_asyncStepMode)
                {
                    throw;
                }

                return new JsValue(CreateIteratorResult(JsValue.Undefined, false));
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

            _state = GeneratorState.Completed;
            _done = true;
            _tryStack.Clear();
            return new JsValue(CreateIteratorResult(JsValue.Undefined, true));
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

        private object? ResumeGenerator(ResumeMode mode, object? value)
        {
            var completed = _done || _state == GeneratorState.Completed;
            if (completed)
            {
                _state = GeneratorState.Completed;
                _done = true;
                _resumeContext.Clear();
                return FinishExternalCompletion(mode, value);
            }

            var wasStart = _state == GeneratorState.Start;
            if ((mode == ResumeMode.Throw || mode == ResumeMode.Return) && wasStart)
            {
                _state = GeneratorState.Completed;
                _done = true;
                _resumeContext.Clear();
                return FinishExternalCompletion(mode, value);
            }

            try
            {
                _state = GeneratorState.Executing;

                _executionEnvironment ??= CreateExecutionEnvironment();

                if (!wasStart && _lastYieldIndex >= 0)
                {
                    switch (mode)
                    {
                        case ResumeMode.Throw:
                            _resumeContext.SetException(_lastYieldIndex, value);
                            break;
                        case ResumeMode.Return:
                            _resumeContext.SetReturn(_lastYieldIndex, value);
                            break;
                        default:
                            _resumeContext.SetValue(_lastYieldIndex, value);
                            break;
                    }
                }

                var context = _realmState.CreateContext(
                    ScopeKind.Function,
                    DetermineGeneratorScopeMode());
                _executionEnvironment.Define(Symbol.YieldTrackerSymbol, new YieldTracker(_consumedYieldIndices));

                var result = EvaluateBlock(
                    _function.Body,
                    _executionEnvironment,
                    context);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    _state = GeneratorState.Completed;
                    _done = true;
                    _resumeContext.Clear();
                    throw new ThrowSignal(thrown);
                }

                if (context.IsYield)
                {
                    var yielded = context.FlowValue;
                    // Check if the yield signal includes an original iterator result object (from yield*)
                    var iteratorResultObject = (context.CurrentSignal as YieldCompletionSignal)?.IteratorResultObject;
                    RecordYield(context);
                    context.Clear();
                    _state = GeneratorState.Suspended;
                    // If we have an original iterator result object, return it to preserve done property
                    return iteratorResultObject ?? CreateIteratorResult(yielded, false);
                }

                if (context.IsReturn)
                {
                    var returnValue = context.FlowValue;
                    context.ClearReturn();
                    _state = GeneratorState.Completed;
                    _done = true;
                    _resumeContext.Clear();
                    return CreateIteratorResult(returnValue, true);
                }

                _state = GeneratorState.Completed;
                _done = true;
                _resumeContext.Clear();
                return CreateIteratorResult(result, true);
            }
            catch
            {
                _state = GeneratorState.Completed;
                _done = true;
                _resumeContext.Clear();
                throw;
            }
        }

        private static JsValue FinishExternalCompletion(ResumeMode mode, JsValue value)
        {
            return mode switch
            {
                ResumeMode.Throw => throw new ThrowSignal(value.ToObject()),
                _ => new JsValue(CreateIteratorResult(value, true))
            };
        }

        internal object? EvaluateAwaitInGenerator(AwaitExpression expression, JsEnvironment environment,
            EvaluationContext context)
        {
            // When not executing under async-aware stepping, fall back to the
            // legacy blocking helper so synchronous generators remain usable.
            if (!_asyncStepMode)
            {
                var awaitedValueSync = EvaluateExpression(expression.Expression, environment, context).ToObject();
                if (context.ShouldStopEvaluation)
                {
                    return awaitedValueSync;
                }

                if (!TryAwaitPromise(awaitedValueSync, context, out var resolvedSync))
                {
                    return resolvedSync;
                }

                return resolvedSync;
            }

            // Async-aware mode: use per-site await state so we don't re-run
            // side-effecting expressions after the promise has resolved.
            var awaitKey = GetAwaitStateKey(expression);
            if (awaitKey is not null &&
                environment.TryGet(awaitKey, out var stateObj) &&
                stateObj is AwaitState { HasResult: true } state)
            {
                // Await has already completed; reuse the resolved value once
                // for this resume, then clear the flag so future iterations
                // (e.g. in loops) see a fresh await.
                var result = state.Result;
                environment.Assign(awaitKey, new AwaitState());
                _pendingAwaitKey = null;
                return result;
            }

            var awaitedValue = EvaluateExpression(expression.Expression, environment, context).ToObject();
            if (context.ShouldStopEvaluation)
            {
                return awaitedValue;
            }

            if (awaitKey is not null)
            {
                var existingState = new AwaitState();

                if (environment.TryGet(awaitKey, out _))
                {
                    environment.Assign(awaitKey, existingState);
                }
                else
                {
                    environment.Define(awaitKey, existingState);
                }
            }

            // Async-aware mode: surface promise-like values as pending steps
            // so AsyncGeneratorInstance can resume via the event queue.
            if (TryAwaitPromiseOrSchedule(awaitedValue, context, out var resolved))
            {
                return resolved;
            }

            if (_pendingPromise is not JsObject || awaitKey is null)
            {
                return resolved;
            }

            // Remember which await site is pending so we can stash the
            // resolved value on resume.
            _pendingAwaitKey = awaitKey;
            _state = GeneratorState.Suspended;
            _programCounter = _currentInstructionIndex;
            throw new PendingAwaitException();

            // If TryAwaitPromiseOrSchedule reported an error via the context,
            // let the caller observe the pending throw/return.
        }

        private bool TryAwaitPromiseOrSchedule(JsValue candidate, EvaluationContext context, out object? resolvedValue)
        {
            var pendingPromise = _pendingPromise;
            var result = AwaitScheduler.TryAwaitPromiseOrSchedule(candidate, _asyncStepMode, ref pendingPromise,
                context, out resolvedValue);
            _pendingPromise = pendingPromise;
            return result;
        }

        private void RecordYield(EvaluationContext context)
        {
            // Remember the active yield slot so the next resume value is applied to the
            // right YieldExpression (ECMA-262 GeneratorResume, step threading of sent values).
            _lastYieldIndex = context.LastYieldIndex;
            if (_lastYieldIndex >= 0)
            {
                _consumedYieldIndices.Add(_lastYieldIndex);
            }
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

            switch (mode)
            {
                case ResumeMode.Throw:
                    _pendingResumeKind = ResumePayloadKind.Throw;
                    break;
                case ResumeMode.Return:
                    _pendingResumeKind = ResumePayloadKind.Return;
                    break;
                default:
                    _pendingResumeKind = ResumePayloadKind.Value;
                    break;
            }

            _pendingResumeValue = resumeValue;

            _realmState.Logger?.LogInformation(
                "PrepareResume yieldIndex={YieldIndex} kind={Kind} valueType={Type}",
                _lastYieldIndex,
                _pendingResumeKind,
                resumeValue.ToObject()?.GetType().Name ?? "null");

            if (_lastYieldIndex < 0)
            {
                return;
            }

            var resumeSlotIndex = _lastYieldIndex;
            switch (_pendingResumeKind)
            {
                case ResumePayloadKind.Throw:
                    _resumeContext.SetException(resumeSlotIndex, resumeValue.ToObject());
                    break;
                case ResumePayloadKind.Return:
                    _resumeContext.SetReturn(resumeSlotIndex, resumeValue.ToObject());
                    break;
                default:
                    _resumeContext.SetValue(resumeSlotIndex, resumeValue.ToObject());
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
            var frame = new TryFrame(instruction.HandlerIndex, instruction.CatchSlotSymbol, instruction.FinallyIndex);
            if (instruction.CatchSlotSymbol is { } slot && !environment.TryGet(slot, out _))
            {
                environment.Define(slot, Symbol.Undefined);
            }

            _tryStack.Push(frame);
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

        private bool HandleAbruptCompletion(AbruptKind kind, object? value, JsEnvironment environment)
        {
            // Console.WriteLine($"[IR] HandleAbruptCompletion kind={kind}, value={value}, stack={_tryStack.Count}");
            while (_tryStack.Count > 0)
            {
                var frame = _tryStack.Peek();
                if (kind == AbruptKind.Throw && frame is { HandlerIndex: >= 0, CatchUsed: false })
                {
                    frame.CatchUsed = true;
                    if (frame.CatchSlotSymbol is { } slot)
                    {
                        if (environment.TryGet(slot, out _))
                        {
                            environment.Assign(slot, value);
                        }
                        else
                        {
                            environment.Define(slot, value);
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
                        _programCounter = frame.FinallyIndex;
                        return true;
                    }

                    frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);
                    return true;
                }

                _tryStack.Pop();
            }

            return false;
        }

        private JsValue CompleteReturn(object? value)
        {
            _programCounter = -1;
            _state = GeneratorState.Completed;
            _done = true;
            _tryStack.Clear();
            return new JsValue(CreateIteratorResult(new JsValue(value), true));
        }

        private sealed class PendingAwaitException : Exception
        {
        }


        private sealed class AwaitState
        {
            public bool HasResult { get; set; }
            public object? Result { get; set; }
        }

        // Lightweight step result used by async-generator wrappers so they can
        // drive the same IR plan without duplicating the interpreter. This
        // supports yield/completion/throw, and has room for a future "Pending"
        // state that surfaces promise-like values without blocking.
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

        private sealed class TryFrame(int handlerIndex, Symbol? catchSlotSymbol, int finallyIndex)
        {
            public int HandlerIndex { get; } = handlerIndex;
            public Symbol? CatchSlotSymbol { get; } = catchSlotSymbol;
            public int FinallyIndex { get; } = finallyIndex;
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

        private sealed class YieldStarState
        {
            public DelegatedYieldState? State { get; set; }
            public bool AwaitingResume { get; set; }
            public AbruptKind PendingAbrupt { get; set; }
            public object? PendingValue { get; set; }
        }
    }
}
