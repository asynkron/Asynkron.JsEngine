#region

using System.Buffers;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private static JsValue CreateIteratorResult(JsValue value, bool done)
        {
            // Use singleton for the common done case with undefined value
            if (done && value.IsUndefined)
            {
                return IteratorResultObject.DoneUndefined.AsJsValue;
            }

            return IteratorResultObjectPool.Rent(value, done).AsJsValue;
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

        [MethodImpl(JsEngineConstants.Inlining)]
        private static void StoreSymbolValue(JsEnvironment environment, Symbol symbol, object? /* intentional */ value)
        {
            // Handle case where value is already a boxed JsValue
            var jsVal = value is JsValue jv ? jv : JsValue.FromObjectUnsafe(value);
            StoreSymbolValueJsValue(environment, symbol, jsVal);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static void StoreSymbolValueJsValue(JsEnvironment environment, Symbol symbol, JsValue value)
        {
            // DefineOrAssignJsValue is O(1) on the current environment -
            // it only looks at environment.Values, no scope chain walk.
            // This is optimal for generator symbols defined in the execution environment.
            environment.DefineOrAssignJsValue(symbol, value);
        }

        /// <summary>
        /// Gets the actual slot index, applying offset for GlobalEnvironment access in script mode.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private int GetActualSlotIndex(JsEnvironment environment, int slotIndex)
        {
            // Apply offset only when accessing the GlobalEnvironment (stored in _closure for scripts).
            // Child environments created during execution have their own fresh slots.
            var isClosure = ReferenceEquals(environment, _closure);
            if (_slotOffset > 0 && !isClosure)
            {
                _realmState.Logger?.LogWarning(
                    "[DEBUG] GetActualSlotIndex: _slotOffset={Offset} but env != _closure. env.ScopeId={EnvScope}, _closure?.ScopeId={ClosureScope}, sameRef={Same}",
                    _slotOffset, environment.ScopeId, _closure?.ScopeId, isClosure);
            }
            return _slotOffset > 0 && isClosure
                ? slotIndex + _slotOffset
                : slotIndex;
        }

        /// <summary>
        /// Stores a value using pre-resolved slot index for O(1) access.
        /// Falls back to dictionary-based storage if slot index is invalid.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private void StoreValueBySlot(JsEnvironment environment, Symbol symbol, int slotIndex, JsValue value)
        {
            if (slotIndex >= 0 && environment.HasSlots)
            {
                var actualSlotIndex = GetActualSlotIndex(environment, slotIndex);
                environment.SetSlotDirect(actualSlotIndex, value);
                // Also update dictionary for symbol-based lookups elsewhere
            }

            environment.DefineOrAssignJsValue(symbol, value);
        }

        /// <summary>
        /// Reads a value using pre-resolved slot index for O(1) access.
        /// Falls back to dictionary-based lookup if slot index is invalid.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryGetValueBySlot(JsEnvironment environment, Symbol symbol, int slotIndex,
            out JsValue value)
        {
            if (slotIndex >= 0 && environment.HasSlots)
            {
                var actualSlotIndex = GetActualSlotIndex(environment, slotIndex);
                value = environment.GetSlotRef(actualSlotIndex);
                return true;
            }

            return TryGetSymbolValueJsValue(environment, symbol, out value);
        }

        /// <summary>
        /// Creates a JsVariable for slot-based access, applying offset for GlobalEnvironment.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsVariable CreateSlotVariable(JsEnvironment environment, int slotIndex)
        {
            var actualSlotIndex = GetActualSlotIndex(environment, slotIndex);
            return new JsVariable(environment, actualSlotIndex);
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

        private static JsValue FinishExternalCompletion(ResumeMode mode, JsValue value)
        {
            return mode switch
            {
                ResumeMode.Throw => throw new ThrowSignal(value),
                _ => CreateIteratorResult(value, true)
            };
        }

        private JsValue EvaluateExpressionProgram(
            ExpressionProgram program,
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (program.IsEmpty)
            {
                return JsValue.Undefined;
            }

            var operationCount = program.Operations.Length;
            var stackSize = Math.Max(16, operationCount);
            var rentedStack = ArrayPool<JsValue>.Shared.Rent(stackSize);
            var stack = rentedStack.AsSpan(0, stackSize);
            var stackIndex = 0;
            var programCounter = 0;

            try
            {
                while ((uint)programCounter < (uint)operationCount)
                {
                    switch (program.Operations[programCounter])
                    {
                        case LoadLiteralExpressionOp loadLiteral:
                            stack[stackIndex++] = loadLiteral.Value;
                            programCounter++;
                            break;

                        case LoadIdentifierExpressionOp loadIdentifier:
                            stack[stackIndex++] = EvaluateProgramIdentifier(loadIdentifier, environment, context);
                            programCounter++;
                            break;

                        case LoadThisExpressionOp:
                            stack[stackIndex++] = _thisValue;
                            programCounter++;
                            break;

                        case LoadNewTargetExpressionOp:
                            stack[stackIndex++] = _newTarget.IsUndefined ? JsValue.Undefined : _newTarget;
                            programCounter++;
                            break;

                        case LoadNamedCallTargetExpressionOp namedCallTarget:
                            {
                                var target = stack[stackIndex - 1];
                                var callee = GetProgramNamedPropertyValue(
                                    target,
                                    new GetNamedPropertyExpressionOp(namedCallTarget.PropertyName),
                                    context);
                                stack[stackIndex++] = callee;
                                programCounter++;
                                break;
                            }

                        case LoadComputedCallTargetExpressionOp:
                            {
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                var callee = GetProgramComputedPropertyValue(
                                    target,
                                    propertyKey,
                                    new GetComputedPropertyExpressionOp(),
                                    context);
                                stack[stackIndex++] = callee;
                                programCounter++;
                                break;
                            }

                        case CreateArrayExpressionOp:
                            stack[stackIndex++] = JsValue.FromJsArray(new JsArray(context.RealmState));
                            programCounter++;
                            break;

                        case ArrayPushExpressionOp:
                            {
                                var elementValue = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetArray(out var targetArray))
                                {
                                    throw new InvalidOperationException("Array push expression op requires an array receiver.");
                                }

                                targetArray.Push(elementValue);
                                programCounter++;
                                break;
                            }

                        case ArrayPushHoleExpressionOp:
                            {
                                if (!stack[stackIndex - 1].TryGetArray(out var targetArray))
                                {
                                    throw new InvalidOperationException("Array hole expression op requires an array receiver.");
                                }

                                targetArray.PushHole();
                                programCounter++;
                                break;
                            }

                        case ArraySpreadExpressionOp:
                            {
                                var spreadValue = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetArray(out var targetArray))
                                {
                                    throw new InvalidOperationException("Array spread expression op requires an array receiver.");
                                }

                                foreach (var item in EnumerateSpread(spreadValue, context))
                                {
                                    targetArray.Push(item);
                                }

                                programCounter++;
                                break;
                            }

                        case CreateObjectExpressionOp:
                            {
                                var targetObject = new JsObject
                                {
                                    RealmState = context.RealmState
                                };
                                if (context.RealmState.ObjectPrototype is { } objectPrototype)
                                {
                                    targetObject.SetPrototype(objectPrototype);
                                }

                                stack[stackIndex++] = JsValue.FromJsObject(targetObject);
                                programCounter++;
                                break;
                            }

                        case DefineObjectPropertyExpressionOp defineProperty:
                            {
                                var propertyValue = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Object property expression op requires an object receiver.");
                                }

                                DefineObjectLiteralProperty(targetObject, defineProperty, propertyValue);
                                programCounter++;
                                break;
                            }

                        case DefineComputedObjectPropertyExpressionOp:
                            {
                                var propertyValue = stack[--stackIndex];
                                var propertyKey = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Computed object property expression op requires an object receiver.");
                                }

                                DefineComputedObjectLiteralProperty(targetObject, propertyKey, propertyValue, context);
                                programCounter++;
                                break;
                            }

                        case ObjectSpreadExpressionOp:
                            {
                                var spreadValue = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Object spread expression op requires an object receiver.");
                                }

                                ApplyObjectLiteralSpread(targetObject, spreadValue, context);
                                programCounter++;
                                break;
                            }

                        case GetNamedPropertyExpressionOp namedProperty:
                            stack[stackIndex - 1] = GetProgramNamedPropertyValue(
                                stack[stackIndex - 1],
                                namedProperty,
                                context);
                            programCounter++;
                            break;

                        case GetComputedPropertyExpressionOp computedProperty:
                            {
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                stack[stackIndex - 1] = GetProgramComputedPropertyValue(
                                    target,
                                    propertyKey,
                                    computedProperty,
                                    context);
                                programCounter++;
                                break;
                            }

                        case SetNamedPropertyExpressionOp namedAssignment:
                            {
                                var propertyValue = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                ApplyProgramNamedPropertyAssignment(target, namedAssignment, propertyValue, context);
                                stack[stackIndex - 1] = propertyValue;
                                programCounter++;
                                break;
                            }

                        case SetComputedPropertyExpressionOp computedAssignment:
                            {
                                var propertyValue = stack[--stackIndex];
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                ApplyProgramComputedPropertyAssignment(
                                    target,
                                    propertyKey,
                                    computedAssignment,
                                    propertyValue,
                                    context);
                                stack[stackIndex - 1] = propertyValue;
                                programCounter++;
                                break;
                            }

                        case ToStringExpressionOp:
                            stack[stackIndex - 1] = new JsValue(JsOps.ToJsString(stack[stackIndex - 1], context));
                            programCounter++;
                            break;

                        case UnaryLogicalNotExpressionOp:
                            stack[stackIndex - 1] = stack[stackIndex - 1].IsTruthy ? JsValue.False : JsValue.True;
                            programCounter++;
                            break;

                        case BinaryExpressionOp binary:
                            {
                                var right = stack[--stackIndex];
                                var left = stack[stackIndex - 1];
                                stack[stackIndex - 1] =
                                    binary.Operator switch
                                    {
                                        BinaryOperator.LessThan or
                                        BinaryOperator.LessThanOrEqual or
                                        BinaryOperator.GreaterThan or
                                        BinaryOperator.GreaterThanOrEqual =>
                                            ProfileBranchCompare(binary.Operator, left, right, context),
                                        _ => ProfileApplyBinaryOperator(binary.Operator, left, right, context)
                                    };
                                programCounter++;
                                break;
                            }

                        case PopExpressionOp:
                            stackIndex--;
                            programCounter++;
                            break;

                        case JumpExpressionOp jump:
                            programCounter = jump.Target;
                            break;

                        case JumpIfNullishExpressionOp jumpIfNullish:
                            if (stack[stackIndex - 1].IsNullish)
                            {
                                if (jumpIfNullish.ReplaceWithUndefined)
                                {
                                    stack[stackIndex - 1] = JsValue.Undefined;
                                }

                                programCounter = jumpIfNullish.Target;
                            }
                            else
                            {
                                programCounter++;
                            }
                            break;

                        case JumpIfTrueExpressionOp jumpIfTrue:
                            programCounter = stack[stackIndex - 1].IsTruthy
                                ? jumpIfTrue.Target
                                : programCounter + 1;
                            break;

                        case JumpIfFalseExpressionOp jumpIfFalse:
                            programCounter = !stack[stackIndex - 1].IsTruthy
                                ? jumpIfFalse.Target
                                : programCounter + 1;
                            break;

                        case JumpIfNotNullishExpressionOp jumpIfNotNullish:
                            programCounter = !stack[stackIndex - 1].IsNullish
                                ? jumpIfNotNullish.Target
                                : programCounter + 1;
                            break;

                        case CallExpressionOp call:
                            stackIndex = ExecuteProgramCall(
                                call,
                                stack,
                                stackIndex,
                                environment,
                                context);
                            programCounter++;
                            break;

                        case ConstructExpressionOp construct:
                            stackIndex = ExecuteProgramConstruct(
                                construct,
                                stack,
                                stackIndex,
                                context);
                            programCounter++;
                            break;

                        default:
                            throw new NotSupportedException(
                                $"Unsupported expression op '{program.Operations[programCounter].GetType().Name}'.");
                    }

                    if (context.ShouldStopEvaluation)
                    {
                        return stackIndex > 0 ? stack[stackIndex - 1] : JsValue.Undefined;
                    }
                }

                return stackIndex > 0 ? stack[stackIndex - 1] : JsValue.Undefined;
            }
            finally
            {
                ArrayPool<JsValue>.Shared.Return(rentedStack, clearArray: true);
            }
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue EvaluateProgramIdentifier(
            LoadIdentifierExpressionOp identifier,
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (identifier.IsArguments)
            {
                return environment.TryGetIdentifierJsValue(identifier.Name, context, out var argumentsValue)
                    ? argumentsValue
                    : HandleIdentifierNotFound(identifier.Name, context);
            }

            if (!context.AllowIdentifierCache)
            {
                return environment.TryGetIdentifierJsValue(identifier.Name, context, out var resolvedValue)
                    ? resolvedValue
                    : HandleIdentifierNotFound(identifier.Name, context);
            }

            if (identifier.ScopeId >= 0 && identifier.SlotIndex >= 0)
            {
                var identifierExpression = new IdentifierExpression(
                    Source: null,
                    identifier.Name,
                    SlotIndex: identifier.SlotIndex,
                    ScopeId: identifier.ScopeId,
                    FlatSlotId: identifier.FlatSlotId);
                if (environment.TryReadIdentifierWithSlot(identifierExpression, context, out var slotValue))
                {
                    return slotValue;
                }
            }

            return environment.TryGetIdentifierJsValue(identifier.Name, context, out var value)
                ? value
                : HandleIdentifierNotFound(identifier.Name, context);
        }

        private static void DefineObjectLiteralProperty(
            JsObject targetObject,
            DefineObjectPropertyExpressionOp defineProperty,
            JsValue propertyValue)
        {
            if (defineProperty.IsPrototypeMutation)
            {
                if (propertyValue.IsNull)
                {
                    targetObject.SetPrototype(null);
                }
                else if (propertyValue.TryGetObject<IJsPropertyAccessor>(out var prototypeAccessor))
                {
                    targetObject.SetPrototype(prototypeAccessor);
                }

                return;
            }

            targetObject.DefineProperty(defineProperty.PropertyName,
                new PropertyDescriptor
                {
                    JsValue = propertyValue,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                });
        }

        private static void DefineComputedObjectLiteralProperty(
            JsObject targetObject,
            JsValue propertyKey,
            JsValue propertyValue,
            EvaluationContext context)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            targetObject.DefineProperty(propertyName,
                new PropertyDescriptor
                {
                    JsValue = propertyValue,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                });
        }

        private static void ApplyObjectLiteralSpread(
            JsObject targetObject,
            JsValue spreadValue,
            EvaluationContext context)
        {
            if (spreadValue.IsNullOrUndefined)
            {
                return;
            }

            if (spreadValue.ObjectValue is IIsHtmlDda)
            {
                return;
            }

            if (spreadValue.ObjectValue is IDictionary<string, object?> dictionary and not JsObject)
            {
                foreach (var (key, value) in dictionary)
                {
                    targetObject.DefineProperty(key,
                        new PropertyDescriptor
                        {
                            Value = value,
                            Writable = true,
                            Enumerable = true,
                            Configurable = true
                        });
                }

                return;
            }

            var accessor = spreadValue.ObjectValue is IJsPropertyAccessor propertyAccessor
                ? propertyAccessor
                : ToObjectForDestructuringJsValue(spreadValue, context);

            foreach (var key in accessor.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
            {
                var descriptor = accessor.GetOwnPropertyDescriptor(key);
                if (descriptor is not { Enumerable: true })
                {
                    continue;
                }

                var spreadPropertyValue = accessor.TryGetProperty(key, out var value)
                    ? value
                    : JsValue.Undefined;
                targetObject.DefineProperty(key,
                    new PropertyDescriptor
                    {
                        JsValue = spreadPropertyValue,
                        Writable = true,
                        Enumerable = true,
                        Configurable = true
                    });
            }
        }

        private static JsValue GetProgramNamedPropertyValue(
            JsValue target,
            GetNamedPropertyExpressionOp propertyOp,
            EvaluationContext context)
        {
            if (propertyOp.IsOptional && target.IsNullOrUndefined)
            {
                return JsValue.Undefined;
            }

            if (propertyOp.ShortCircuitOnNullishTarget && target.IsNullOrUndefined)
            {
                return JsValue.Undefined;
            }

            if (target.IsNullOrUndefined)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Cannot read properties of null or undefined",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                return JsValue.Undefined;
            }

            if (!propertyOp.PropertyName.IsPrivateName())
            {
                return JsOps.TryGetPropertyValue(target, propertyOp.PropertyName, out var directValue, context)
                    ? directValue
                    : JsValue.Undefined;
            }

            var handle = PropertyHandle.Resolve(
                target,
                propertyOp.PropertyName,
                context,
                context.CurrentScope.IsStrict,
                allowPrivate: true);
            return handle.GetJsValue();
        }

        private static JsValue GetProgramComputedPropertyValue(
            JsValue target,
            JsValue propertyKey,
            GetComputedPropertyExpressionOp propertyOp,
            EvaluationContext context)
        {
            if (propertyOp.ShortCircuitOnNullishTarget && target.IsNullOrUndefined)
            {
                return JsValue.Undefined;
            }

            if (target.IsNullOrUndefined)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Cannot read properties of null or undefined",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                return JsValue.Undefined;
            }

            return JsOps.TryGetPropertyValueJsValue(target, propertyKey, out var directValue, context)
                ? directValue
                : JsValue.Undefined;
        }

        private static void ApplyProgramNamedPropertyAssignment(
            JsValue target,
            SetNamedPropertyExpressionOp propertyOp,
            JsValue value,
            EvaluationContext context)
        {
            if (propertyOp.AllowNameInference &&
                value is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(propertyOp.PropertyName);
            }

            var handle = PropertyHandle.Resolve(target, propertyOp.PropertyName, context, context.CurrentScope.IsStrict);
            handle.SetValue(value);
        }

        private static void ApplyProgramComputedPropertyAssignment(
            JsValue target,
            JsValue propertyKey,
            SetComputedPropertyExpressionOp propertyOp,
            JsValue value,
            EvaluationContext context)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            if (propertyOp.AllowNameInference &&
                value is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(propertyName);
            }

            var handle = PropertyHandle.Resolve(target, propertyName, context, context.CurrentScope.IsStrict);
            handle.SetValue(value);
        }

        private int ExecuteProgramCall(
            CallExpressionOp call,
            Span<JsValue> stack,
            int stackIndex,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var calleeIndex = stackIndex - call.ArgumentCount - 1;
            var receiverIndex = call.HasExplicitThis ? calleeIndex - 1 : -1;
            var baseIndex = call.HasExplicitThis ? receiverIndex : calleeIndex;
            var calleeValue = stack[calleeIndex];
            var thisValue = call.HasExplicitThis ? stack[receiverIndex] : JsValue.Undefined;

            if (!calleeValue.TryGetObject<IJsCallable>(out var callable))
            {
                var error = StandardLibrary.CreateTypeError(
                    "Attempted to call a non-callable value.",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                stack[baseIndex] = JsValue.Undefined;
                return baseIndex + 1;
            }

            if (callable is SyncFunctionInvoker { IsClassConstructor: true })
            {
                var error = StandardLibrary.CreateTypeError(
                    "Class constructor cannot be invoked without 'new'",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                stack[baseIndex] = JsValue.Undefined;
                return baseIndex + 1;
            }

            if (++context.CallDepth > context.MaxCallDepth)
            {
                context.CallDepth--;
                throw new InvalidOperationException(
                    $"Exceeded maximum call depth of {context.MaxCallDepth}.");
            }

            var isAsyncCallable = callable is SyncFunctionInvoker { IsAsyncLike: true };
            global::Asynkron.JsEngine.EvalHostFunction? evalHost = null;
            DebugAwareHostFunction? debugFunction = null;
            JsValue result = JsValue.Undefined;
            JsValue[]? pooledArguments = null;

            try
            {
                if (call.IsDirectEval && callable is global::Asynkron.JsEngine.EvalHostFunction evalHostFunction)
                {
                    evalHost = evalHostFunction;
                    evalHost.IsDirectCall = true;
                    evalHost.InClassFieldInitializer = context.InClassFieldInitializer;
                }

                if (callable is DebugAwareHostFunction debugAware)
                {
                    debugFunction = debugAware;
                    debugFunction.CurrentJsEnvironment = environment;
                    debugFunction.CurrentContext = context;
                }

                IReadOnlyList<JsValue> arguments;
                if (call.ArgumentCount == 0)
                {
                    arguments = [];
                }
                else
                {
                    var argumentArray = call.ArgumentCount <= 4
                        ? global::Asynkron.JsEngine.JsValueCache.RentJsValueArray(call.ArgumentCount)
                        : new JsValue[call.ArgumentCount];

                    if (call.ArgumentCount <= 4)
                    {
                        pooledArguments = argumentArray;
                    }

                    for (var i = 0; i < call.ArgumentCount; i++)
                    {
                        argumentArray[i] = stack[calleeIndex + 1 + i];
                    }

                    arguments = argumentArray;
                }

                result = InvokeCallableJsValue(callable, arguments, thisValue, context, environment);
            }
            catch (ThrowSignal signal)
            {
                if (isAsyncCallable)
                {
                    context.Clear();
                    result = CreateRejectedPromise(signal.ThrownValue, environment);
                }
                else
                {
                    context.SetThrow(signal.ThrownValue);
                    result = signal.ThrownValue;
                }
            }
            catch (Exception ex) when (isAsyncCallable)
            {
                context.Clear();
                result = CreateRejectedPromise(JsValue.FromObjectUnsafe(ex), environment);
            }
            finally
            {
                if (evalHost is not null)
                {
                    evalHost.IsDirectCall = false;
                    evalHost.InClassFieldInitializer = false;
                }

                if (debugFunction is not null)
                {
                    debugFunction.CurrentJsEnvironment = null;
                    debugFunction.CurrentContext = null;
                }

                if (pooledArguments is not null)
                {
                    global::Asynkron.JsEngine.JsValueCache.ReturnJsValueArray(pooledArguments);
                }

                context.CallDepth--;
            }

            if (isAsyncCallable && context.IsThrow)
            {
                var reason = context.FlowValue;
                context.Clear();
                result = CreateRejectedPromise(reason, environment);
            }

            stack[baseIndex] = result;
            return baseIndex + 1;
        }

        private int ExecuteProgramConstruct(
            ConstructExpressionOp construct,
            Span<JsValue> stack,
            int stackIndex,
            EvaluationContext context)
        {
            var constructorIndex = stackIndex - construct.ArgumentCount - 1;
            var constructorValue = stack[constructorIndex];

            if (!JsOps.IsConstructor(constructorValue) ||
                !constructorValue.TryGetObject<IJsCallable>(out var callable))
            {
                var error = StandardLibrary.CreateTypeError(
                    "Target is not a constructor",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                stack[constructorIndex] = JsValue.Undefined;
                return constructorIndex + 1;
            }

            JsValue[]? pooledArguments = null;

            try
            {
                IReadOnlyList<JsValue> arguments;
                if (construct.ArgumentCount == 0)
                {
                    arguments = [];
                }
                else
                {
                    var argumentArray = construct.ArgumentCount <= 4
                        ? global::Asynkron.JsEngine.JsValueCache.RentJsValueArray(construct.ArgumentCount)
                        : new JsValue[construct.ArgumentCount];

                    if (construct.ArgumentCount <= 4)
                    {
                        pooledArguments = argumentArray;
                    }

                    for (var i = 0; i < construct.ArgumentCount; i++)
                    {
                        argumentArray[i] = stack[constructorIndex + 1 + i];
                    }

                    arguments = argumentArray;
                }

                stack[constructorIndex] = global::Asynkron.JsEngine.StdLib.ReflectHelper.Construct(
                    callable,
                    arguments,
                    callable,
                    context.RealmState);
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
                stack[constructorIndex] = signal.ThrownValue;
            }
            finally
            {
                if (pooledArguments is not null)
                {
                    global::Asynkron.JsEngine.JsValueCache.ReturnJsValueArray(pooledArguments);
                }
            }

            return constructorIndex + 1;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryEvaluateSimpleExpression(
            ExpressionNode expression,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue value)
        {
            switch (expression)
            {
                case LiteralExpression { Value: var literalValue }:
                    value = literalValue;
                    return true;

                case IdentifierExpression identifier:
                    value = EvaluateSimpleIdentifier(identifier, environment, context);
                    return true;

                case UnaryExpression { Operator: UnaryOperator.LogicalNot } unary:
                    if (!TryEvaluateSimpleExpression(unary.Operand, environment, context, out var operandValue))
                    {
                        value = default;
                        return false;
                    }

                    value = operandValue.IsTruthy ? JsValue.False : JsValue.True;
                    return true;

                case BinaryExpression binary:
                    return TryEvaluateSimpleBinaryExpression(binary, environment, context, out value);

                default:
                    value = default;
                    return false;
            }
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue EvaluateSimpleIdentifier(
            IdentifierExpression identifier,
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (ReferenceEquals(identifier.Name, Symbol.Arguments))
            {
                return environment.TryGetIdentifierJsValue(identifier.Name, context, out var argumentsValue)
                    ? argumentsValue
                    : HandleIdentifierNotFound(identifier.Name, context);
            }

            if (!context.AllowIdentifierCache)
            {
                return environment.TryGetIdentifierJsValue(identifier.Name, context, out var resolvedValue)
                    ? resolvedValue
                    : HandleIdentifierNotFound(identifier.Name, context);
            }

            if (environment.TryReadIdentifierWithSlot(identifier, context, out var slotValue))
            {
                return slotValue;
            }

            return HandleIdentifierNotFound(identifier.Name, context);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryEvaluateSimpleBinaryExpression(
            BinaryExpression expression,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue value)
        {
            if (!TryEvaluateSimpleExpression(expression.Left, environment, context, out var leftValue))
            {
                value = default;
                return false;
            }

            if (context.ShouldStopEvaluation)
            {
                value = leftValue;
                return true;
            }

            switch (expression.Operator)
            {
                case BinaryOperator.LogicalAnd when !leftValue.IsTruthy:
                case BinaryOperator.LogicalOr when leftValue.IsTruthy:
                case BinaryOperator.NullishCoalescing when !leftValue.IsNullish:
                    value = leftValue;
                    return true;
            }

            if (!TryEvaluateSimpleExpression(expression.Right, environment, context, out var rightValue))
            {
                value = default;
                return false;
            }

            if (context.ShouldStopEvaluation)
            {
                value = rightValue;
                return true;
            }

            if (expression.Operator == BinaryOperator.Add)
            {
                var fastAdd = ProfileCompoundAdd(leftValue, rightValue);
                value = !fastAdd.IsUndefined
                    ? fastAdd
                    : ProfileApplyBinaryOperator(expression.Operator, leftValue, rightValue, context);
                return true;
            }

            value = expression.Operator switch
            {
                BinaryOperator.LessThan or
                BinaryOperator.LessThanOrEqual or
                BinaryOperator.GreaterThan or
                BinaryOperator.GreaterThanOrEqual =>
                    ProfileBranchCompare(expression.Operator, leftValue, rightValue, context),
                _ => ProfileApplyBinaryOperator(expression.Operator, leftValue, rightValue, context)
            };

            return true;
        }
    }
}
