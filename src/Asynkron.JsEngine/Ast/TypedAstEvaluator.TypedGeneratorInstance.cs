using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class TypedGeneratorInstance
    {
        private readonly IReadOnlyList<object?> _arguments;
        private readonly IJsCallable _callable;
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly GeneratorPlan? _plan;
        private readonly RealmState _realmState;
        private readonly YieldResumeContext _resumeContext = new();
        private readonly object? _thisValue;
        private readonly Stack<TryFrame> _tryStack = new();
        private bool _asyncStepMode;
        private EvaluationContext? _context;
        private int _currentInstructionIndex;
        private int _currentYieldIndex;
        private bool _done;
        private JsEnvironment? _executionEnvironment;

        private Symbol? _pendingAwaitKey;
        private object? _pendingPromise;
        private ResumePayloadKind _pendingResumeKind;
        private object? _pendingResumeValue = Symbol.Undefined;
        private int _programCounter;
        private GeneratorState _state = GeneratorState.Start;

        public TypedGeneratorInstance(FunctionExpression function, JsEnvironment closure,
            IReadOnlyList<object?> arguments, object? thisValue, IJsCallable callable, RealmState realmState)
        {
            _function = function;
            _closure = closure;
            _arguments = arguments;
            _thisValue = thisValue;
            _callable = callable;
            _realmState = realmState;

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
            var iterator = CreateGeneratorIteratorObject(
                args => Next(args.Count > 0 ? args[0] : Symbol.Undefined),
                args => Return(args.Count > 0 ? args[0] : null),
                args => Throw(args.Count > 0 ? args[0] : null));
            iterator.SetProperty(IteratorSymbolPropertyName, new HostFunction((_, _) => iterator));
            iterator.SetProperty(GeneratorBrandPropertyName, GeneratorBrandMarker);
            return iterator;
        }

        public void Initialize()
        {
            EnsureExecutionEnvironment();
        }

        private object? Next(object? value)
        {
            return ExecutePlan(ResumeMode.Next, value);
        }

        private object? Return(object? value)
        {
            return ExecutePlan(ResumeMode.Return, value);
        }

        private object? Throw(object? error)
        {
            return ExecutePlan(ResumeMode.Throw, error);
        }

        internal AsyncGeneratorStepResult ExecuteAsyncStep(ResumeMode mode, object? resumeValue)
        {
            // Reuse the existing ExecutePlan logic but translate its iterator
            // result / exceptions into a structured step result that async
            // generators can consume without throwing. This entrypoint also
            // marks the executor as async-aware so future steps can surface
            // pending Promises instead of blocking.
            var previousAsyncStepMode = _asyncStepMode;
            _asyncStepMode = true;
            _pendingPromise = null;

            try
            {
                var result = ExecutePlan(mode, resumeValue);

                if (_pendingPromise is JsObject pending)
                {
                    return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, Symbol.Undefined, false,
                        pending);
                }

                if (result is JsObject obj &&
                    obj.TryGetProperty("done", out var doneRaw) &&
                    doneRaw is bool done &&
                    obj.TryGetProperty("value", out var value))
                {
                    return done
                        ? new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, value, true, null)
                        : new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Yield, value, false, null);
                }

                // If the plan completed without producing a well-formed iterator
                // result, treat it as a completed step with undefined.
                return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, Symbol.Undefined, true, null);
            }
            catch (PendingAwaitException)
            {
                if (_pendingPromise is JsObject pending)
                {
                    return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, Symbol.Undefined, false,
                        pending);
                }

                throw new InvalidOperationException("Async generator awaited a non-promise value.");
            }
            finally
            {
                _asyncStepMode = previousAsyncStepMode;
                _pendingPromise = null;
            }
        }

        private JsEnvironment CreateExecutionEnvironment()
        {
            var description = _function.Name is { } name
                ? $"function* {name.Name}"
                : "generator function";
            var environment = new JsEnvironment(_closure, true, _function.Body.IsStrict, _function.Source, description);
            environment.Define(Symbol.This, _thisValue ?? new JsObject());
            environment.Define(Symbol.YieldResumeContextSymbol, _resumeContext);
            environment.Define(Symbol.GeneratorInstanceSymbol, this);

            if (_function.Name is { } functionName)
            {
                environment.Define(functionName, _callable);
            }

            var generatorContext = _realmState.CreateContext(
                ScopeKind.Function,
                DetermineGeneratorScopeMode(),
                true);
            HoistVarDeclarations(_function.Body, environment, generatorContext);

            BindFunctionParameters(_function, _arguments, environment, generatorContext);
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

            // Define `arguments` inside generator functions so generator bodies
            // can observe the values they were invoked with (including mappings).
            var argumentsObject = CreateArgumentsObject(_function, _arguments, environment, _realmState, _callable);
            environment.Define(Symbol.Arguments, argumentsObject, isLexical: false);

            return environment;
        }

        private static JsObject CreateIteratorResult(object? value, bool done)
        {
            var result = new JsObject();
            result.SetProperty("value", value);
            result.SetProperty("done", done);
            return result;
        }

        private static IteratorDriverState CreateIteratorDriverState(object? iterable, IteratorDriverKind kind)
        {
            if (TryGetIteratorFromProtocols(iterable, out var iterator) && iterator is not null)
            {
                return new IteratorDriverState
                {
                    IteratorObject = iterator, Enumerator = null, IsAsyncIterator = kind == IteratorDriverKind.Await
                };
            }

            var enumerable = EnumerateValues(iterable);
            return new IteratorDriverState
            {
                IteratorObject = null,
                Enumerator = enumerable.GetEnumerator(),
                IsAsyncIterator = kind == IteratorDriverKind.Await
            };
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

        private object? ExecutePlan(ResumeMode mode, object? resumeValue)
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
                    DetermineGeneratorScopeMode(),
                    true);
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
            StoreSymbolValue(environment, Symbol.YieldTrackerSymbol, new YieldTracker(_currentYieldIndex));

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
                                context.Clear();
                                _state = GeneratorState.Suspended;
                                _currentYieldIndex++;
                                return CreateIteratorResult(yieldedSignalValue, false);
                            }

                            _programCounter = statementInstruction.Next;
                            continue;

                        case YieldInstruction yieldInstruction:
                            object? yieldedValue = Symbol.Undefined;
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
                            }

                            _programCounter = yieldInstruction.Next;
                            _state = GeneratorState.Suspended;
                            return CreateIteratorResult(yieldedValue, false);

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

                            if (yieldStarState.State is null)
                            {
                                var yieldStarIterable =
                                    EvaluateExpression(yieldStarInstruction.IterableExpression, environment, context);
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

                                yieldStarState.State = CreateDelegatedState(yieldStarIterable);
                                yieldStarState.AwaitingResume = false;
                            }

                            while (true)
                            {
                                object? sendValue = Symbol.Undefined;
                                var hasSendValue = false;
                                var propagateThrow = false;
                                var propagateReturn = false;

                                if (yieldStarState.AwaitingResume)
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
                                    sendValue,
                                    hasSendValue,
                                    propagateThrow,
                                    propagateReturn,
                                    context,
                                    out _);

                                if (iteratorResult.IsDelegatedCompletion)
                                {
                                    var pendingKind = propagateThrow ? AbruptKind.Throw : AbruptKind.Return;
                                    object? abruptValue;
                                    if (pendingKind == AbruptKind.Throw && context.IsThrow)
                                    {
                                        abruptValue = context.FlowValue;
                                        context.Clear();
                                    }
                                    else
                                    {
                                        abruptValue = pendingKind == AbruptKind.Throw
                                            ? sendValue
                                            : iteratorResult.Value;
                                    }

                                    if (!iteratorResult.Done)
                                    {
                                        yieldStarState.PendingAbrupt = pendingKind;
                                        yieldStarState.PendingValue = sendValue;
                                        yieldStarState.AwaitingResume = true;
                                        _programCounter = currentIndex;
                                        _state = GeneratorState.Suspended;
                                        return CreateIteratorResult(iteratorResult.Value, false);
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
                                var resultDone = propagateReturn ? iteratorResult.Done : false;
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

                            var iteratorState = CreateIteratorDriverState(iterableValue, iteratorInitInstruction.Kind);
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
                                object? currentValue;
                                if (driverState.IteratorObject is JsObject iteratorObj)
                                {
                                    var nextResult = InvokeIteratorNext(iteratorObj);
                                    if (nextResult is not JsObject resultObj)
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    var done = resultObj.TryGetProperty("done", out var doneValue) &&
                                               doneValue is bool and true;
                                    if (done)
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    currentValue = resultObj.TryGetProperty("value", out var yielded)
                                        ? yielded
                                        : Symbol.Undefined;
                                }
                                else if (driverState.Enumerator is IEnumerator<object?> enumerator)
                                {
                                    if (!enumerator.MoveNext())
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    currentValue = enumerator.Current;
                                }
                                else
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                StoreSymbolValue(environment, iteratorMoveNextInstruction.ValueSlot, currentValue);
                                _programCounter = iteratorMoveNextInstruction.Next;
                                continue;
                            }

                            object? awaitedValue = null;
                            object? awaitedNextResult = null;

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
                                    if (HandleAbruptCompletion(AbruptKind.Throw, forAwaitResumePayload, environment))
                                    {
                                        continue;
                                    }

                                    _tryStack.Clear();
                                    throw new ThrowSignal(forAwaitResumePayload);
                                }

                                if (forAwaitResumeKind == ResumePayloadKind.Return)
                                {
                                    if (HandleAbruptCompletion(AbruptKind.Return, forAwaitResumePayload, environment))
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
                            }

                            if (driverState.IteratorObject is JsObject awaitIteratorObj)
                            {
                                if (awaitedNextResult is null)
                                {
                                    var nextResult = InvokeIteratorNext(awaitIteratorObj);
                                    if (!TryAwaitPromiseOrSchedule(nextResult, context, out var awaitedNext))
                                    {
                                        if (_asyncStepMode && _pendingPromise is JsObject)
                                        {
                                            driverState.AwaitingNextResult = true;
                                            StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                                driverState);
                                            _state = GeneratorState.Suspended;
                                            _programCounter = iteratorIndex;
                                            return CreateIteratorResult(Symbol.Undefined, false);
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

                                    awaitedNextResult = awaitedNext;
                                }

                                if (awaitedNextResult is not JsObject awaitResultObj)
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                var doneAwait = awaitResultObj.TryGetProperty("done", out var awaitDoneValue) &&
                                                awaitDoneValue is bool and true;
                                if (doneAwait)
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                var rawValue = awaitResultObj.TryGetProperty("value", out var yieldedAwait)
                                    ? yieldedAwait
                                    : Symbol.Undefined;
                                if (!TryAwaitPromiseOrSchedule(rawValue, context, out var fullyAwaitedValue))
                                {
                                    if (_asyncStepMode && _pendingPromise is JsObject)
                                    {
                                        driverState.AwaitingValue = true;
                                        StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                            driverState);
                                        _state = GeneratorState.Suspended;
                                        _programCounter = iteratorIndex;
                                        return CreateIteratorResult(Symbol.Undefined, false);
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

                                awaitedValue = fullyAwaitedValue;
                            }
                            else if (driverState.Enumerator is IEnumerator<object?> awaitEnumerator)
                            {
                                if (!awaitEnumerator.MoveNext())
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                var enumerated = awaitEnumerator.Current;
                                if (!TryAwaitPromiseOrSchedule(enumerated, context, out var awaitedEnumerated))
                                {
                                    if (_asyncStepMode && _pendingPromise is JsObject)
                                    {
                                        driverState.AwaitingValue = true;
                                        StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                            driverState);
                                        _state = GeneratorState.Suspended;
                                        _programCounter = iteratorIndex;
                                        return CreateIteratorResult(Symbol.Undefined, false);
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

                                awaitedValue = awaitedEnumerated;
                            }
                            else
                            {
                                _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                continue;
                            }

                            StoreIteratorValue:
                            StoreSymbolValue(environment, iteratorMoveNextInstruction.ValueSlot, awaitedValue);
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

                            _programCounter = IsTruthy(testValue)
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
                                ? Symbol.Undefined
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
                                returnValue = pendingReturn;
                            }

                            if (HandleAbruptCompletion(AbruptKind.Return, returnValue, environment))
                            {
                                continue;
                            }

                            _programCounter = -1;
                            _state = GeneratorState.Completed;
                            _done = true;
                            _tryStack.Clear();
                            return CreateIteratorResult(returnValue, true);

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

                return CreateIteratorResult(Symbol.Undefined, false);
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
            return CreateIteratorResult(Symbol.Undefined, true);
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
                    DetermineGeneratorScopeMode(),
                    true);
            }
            else
            {
                _context.Clear();
            }

            return _context;
        }

        private ScopeMode DetermineGeneratorScopeMode()
        {
            if (_function.Body.IsStrict)
            {
                return ScopeMode.Strict;
            }

            return _realmState.Options.EnableAnnexBFunctionExtensions ? ScopeMode.SloppyAnnexB : ScopeMode.Sloppy;
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

                if (!wasStart && _currentYieldIndex > 0)
                {
                    switch (mode)
                    {
                        case ResumeMode.Throw:
                            _resumeContext.SetException(_currentYieldIndex - 1, value);
                            break;
                        case ResumeMode.Return:
                            _resumeContext.SetReturn(_currentYieldIndex - 1, value);
                            break;
                        default:
                            _resumeContext.SetValue(_currentYieldIndex - 1, value);
                            break;
                    }
                }

                var context = _realmState.CreateContext(
                    ScopeKind.Function,
                    DetermineGeneratorScopeMode(),
                    true);
                _executionEnvironment.Define(Symbol.YieldTrackerSymbol, new YieldTracker(_currentYieldIndex));

                var result = EvaluateBlock(
                    _function.Body,
                    _executionEnvironment,
                    context,
                    true);

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
                    context.Clear();
                    _state = GeneratorState.Suspended;
                    _currentYieldIndex++;
                    return CreateIteratorResult(yielded, false);
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

        private static object? FinishExternalCompletion(ResumeMode mode, object? value)
        {
            return mode switch
            {
                ResumeMode.Throw => throw new ThrowSignal(value),
                _ => CreateIteratorResult(value, true)
            };
        }

        internal object? EvaluateAwaitInGenerator(AwaitExpression expression, JsEnvironment environment,
            EvaluationContext context)
        {
            // When not executing under async-aware stepping, fall back to the
            // legacy blocking helper so synchronous generators remain usable.
            if (!_asyncStepMode)
            {
                var awaitedValueSync = EvaluateExpression(expression.Expression, environment, context);
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

            var awaitedValue = EvaluateExpression(expression.Expression, environment, context);
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

        private bool TryAwaitPromiseOrSchedule(object? candidate, EvaluationContext context, out object? resolvedValue)
        {
            var pendingPromise = _pendingPromise;
            var result = AwaitScheduler.TryAwaitPromiseOrSchedule(candidate, _asyncStepMode, ref pendingPromise,
                context, out resolvedValue);
            _pendingPromise = pendingPromise;
            return result;
        }


        private void PreparePendingResumeValue(ResumeMode mode, object? resumeValue, bool wasStart)
        {
            if (wasStart)
            {
                _pendingResumeKind = ResumePayloadKind.None;
                _pendingResumeValue = Symbol.Undefined;
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

            if (_currentYieldIndex <= 0)
            {
                return;
            }

            var resumeSlotIndex = _currentYieldIndex - 1;
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

        private (ResumePayloadKind Kind, object? Value) ConsumeResumeValue()
        {
            var kind = _pendingResumeKind;
            var value = _pendingResumeValue;
            _pendingResumeKind = ResumePayloadKind.None;
            _pendingResumeValue = Symbol.Undefined;

            if (kind == ResumePayloadKind.None)
            {
                return (ResumePayloadKind.Value, Symbol.Undefined);
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

        private object? CompleteReturn(object? value)
        {
            _programCounter = -1;
            _state = GeneratorState.Completed;
            _done = true;
            _tryStack.Clear();
            return CreateIteratorResult(value, true);
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
            object? Value,
            bool Done,
            object? PendingPromise);

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
