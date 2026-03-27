#region

using System.Buffers;
using System.Collections.Immutable;
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
            var rentedFlags = ArrayPool<bool>.Shared.Rent(stackSize);
            var stackFlags = rentedFlags.AsSpan(0, stackSize);
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
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case LoadRegexLiteralExpressionOp loadRegex:
                            stack[stackIndex++] = new JsValue(
                                StdLib.RegExpHelper.CreateRegExpLiteral(
                                    loadRegex.Pattern,
                                    loadRegex.Flags,
                                    context.RealmState));
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case LoadFunctionLiteralExpressionOp loadFunction:
                            stack[stackIndex++] = JsValue.FromObjectUnsafe(
                                loadFunction.Function.CreateFunctionValue(
                                    environment,
                                    context,
                                    loadFunction.IsConstructorFunction));
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case LoadClassLiteralExpressionOp loadClass:
                            stack[stackIndex++] = loadClass.Class.Definition.CreateClassValue(
                                environment,
                                context,
                                loadClass.Class.Name ?? context.CurrentFunctionNameHint);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case LoadTemplateObjectExpressionOp loadTemplateObject:
                            stack[stackIndex++] = JsValue.FromJsArray(
                                GetOrCreateProgramTemplateObject(loadTemplateObject.Descriptor, context));
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case LoadIdentifierExpressionOp loadIdentifier:
                            stack[stackIndex++] = EvaluateProgramIdentifier(loadIdentifier, environment, context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case StoreIdentifierExpressionOp storeIdentifier:
                            ApplyProgramIdentifierAssignment(storeIdentifier, stack[stackIndex - 1], environment, context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case ApplyBindingTargetExpressionOp applyBindingTarget:
                            stackIndex--;
                            ApplyBindingTargetProgram(
                                applyBindingTarget.TargetProgram,
                                stack[stackIndex],
                                environment,
                                context,
                                BindingMode.Assign,
                                allowNameInference: false);
                            if (context.ShouldStopEvaluation)
                            {
                                return JsValue.Undefined;
                            }

                            programCounter++;
                            break;

                        case DuplicateTopExpressionOp:
                            stack[stackIndex] = stack[stackIndex - 1];
                            stackFlags[stackIndex] = stackFlags[stackIndex - 1];
                            stackIndex++;
                            programCounter++;
                            break;

                        case DuplicateTopTwoExpressionOp:
                            stack[stackIndex] = stack[stackIndex - 2];
                            stack[stackIndex + 1] = stack[stackIndex - 1];
                            stackFlags[stackIndex] = stackFlags[stackIndex - 2];
                            stackFlags[stackIndex + 1] = stackFlags[stackIndex - 1];
                            stackIndex += 2;
                            programCounter++;
                            break;

                        case SwapTopTwoExpressionOp:
                            (stack[stackIndex - 1], stack[stackIndex - 2]) =
                                (stack[stackIndex - 2], stack[stackIndex - 1]);
                            (stackFlags[stackIndex - 1], stackFlags[stackIndex - 2]) =
                                (stackFlags[stackIndex - 2], stackFlags[stackIndex - 1]);
                            programCounter++;
                            break;

                        case RotateTopThreeRightExpressionOp:
                            (stack[stackIndex - 1], stack[stackIndex - 2], stack[stackIndex - 3]) =
                                (stack[stackIndex - 2], stack[stackIndex - 3], stack[stackIndex - 1]);
                            (stackFlags[stackIndex - 1], stackFlags[stackIndex - 2], stackFlags[stackIndex - 3]) =
                                (stackFlags[stackIndex - 2], stackFlags[stackIndex - 3], stackFlags[stackIndex - 1]);
                            programCounter++;
                            break;

                        case LoadThisExpressionOp:
                            stack[stackIndex++] = ResolveThisValue(environment, context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case LoadNewTargetExpressionOp:
                            stack[stackIndex++] = _newTarget.IsUndefined ? JsValue.Undefined : _newTarget;
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case LoadNamedCallTargetExpressionOp namedCallTarget:
                            {
                                var target = stack[stackIndex - 1];
                                var callee = GetProgramNamedPropertyValue(
                                    target,
                                    stackFlags[stackIndex - 1],
                                    new GetNamedPropertyExpressionOp(namedCallTarget.PropertyName),
                                    context,
                                    out var calleeWasShortCircuited);
                                stack[stackIndex++] = callee;
                                stackFlags[stackIndex - 1] = calleeWasShortCircuited;
                                programCounter++;
                                break;
                            }

                        case LoadComputedCallTargetExpressionOp:
                            {
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                var callee = GetProgramComputedPropertyValue(
                                    target,
                                    stackFlags[stackIndex - 1],
                                    propertyKey,
                                    new GetComputedPropertyExpressionOp(),
                                    context,
                                    out var calleeWasShortCircuited);
                                stack[stackIndex++] = callee;
                                stackFlags[stackIndex - 1] = calleeWasShortCircuited;
                                programCounter++;
                                break;
                            }

                        case LoadNamedSuperCallTargetExpressionOp superCallTarget:
                            {
                                LoadProgramNamedSuperCallTarget(
                                    superCallTarget.PropertyName,
                                    environment,
                                    context,
                                    out var receiver,
                                    out var callee);
                                stack[stackIndex++] = receiver;
                                stackFlags[stackIndex - 1] = false;
                                stack[stackIndex++] = callee;
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case LoadComputedSuperCallTargetExpressionOp:
                            {
                                var propertyKey = stack[--stackIndex];
                                LoadProgramComputedSuperCallTarget(
                                    propertyKey,
                                    environment,
                                    context,
                                    out var receiver,
                                    out var callee);
                                stack[stackIndex++] = receiver;
                                stackFlags[stackIndex - 1] = false;
                                stack[stackIndex++] = callee;
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case EnsureSuperReferenceExpressionOp:
                            EnsureProgramSuperReference(environment, context);
                            programCounter++;
                            break;

                        case CreateArrayExpressionOp:
                            stack[stackIndex++] = JsValue.FromJsArray(new JsArray(context.RealmState));
                            stackFlags[stackIndex - 1] = false;
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
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case RequireObjectCoercibleExpressionOp requireCoercible:
                            {
                                var checkIndex = stackIndex - 1 - requireCoercible.Depth;
                                if (stack[checkIndex].IsNullOrUndefined)
                                {
                                    throw StandardLibrary.ThrowTypeError(
                                        "Cannot read properties of null or undefined",
                                        context,
                                        context.RealmState);
                                }

                                programCounter++;
                                break;
                            }

                        case ResolvePropertyKeyExpressionOp:
                            stack[stackIndex - 1] = ResolveProgramPropertyKey(stack[stackIndex - 1], context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

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

                        case DefineComputedObjectPropertyExpressionOp defineComputedProperty:
                            {
                                var propertyValue = stack[--stackIndex];
                                var propertyKey = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Computed object property expression op requires an object receiver.");
                                }

                                DefineComputedObjectLiteralProperty(
                                    targetObject,
                                    propertyKey,
                                    defineComputedProperty,
                                    propertyValue,
                                    context);
                                programCounter++;
                                break;
                            }

                        case DefineObjectMethodExpressionOp defineMethod:
                            {
                                var methodValue = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Object method expression op requires an object receiver.");
                                }

                                DefineObjectLiteralMethod(targetObject, defineMethod.PropertyName, methodValue);
                                programCounter++;
                                break;
                            }

                        case DefineComputedObjectMethodExpressionOp:
                            {
                                var methodValue = stack[--stackIndex];
                                var propertyKey = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Computed object method expression op requires an object receiver.");
                                }

                                DefineComputedObjectLiteralMethod(targetObject, propertyKey, methodValue, context);
                                programCounter++;
                                break;
                            }

                        case DefineObjectAccessorExpressionOp defineAccessor:
                            {
                                var accessorValue = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Object accessor expression op requires an object receiver.");
                                }

                                DefineObjectLiteralAccessor(
                                    targetObject,
                                    defineAccessor.PropertyName,
                                    defineAccessor.AccessorKind,
                                    accessorValue);
                                programCounter++;
                                break;
                            }

                        case DefineComputedObjectAccessorExpressionOp defineComputedAccessor:
                            {
                                var accessorValue = stack[--stackIndex];
                                var propertyKey = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Computed object accessor expression op requires an object receiver.");
                                }

                                DefineComputedObjectLiteralAccessor(
                                    targetObject,
                                    propertyKey,
                                    defineComputedAccessor.AccessorKind,
                                    accessorValue,
                                    context);
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
                                stackFlags[stackIndex - 1],
                                namedProperty,
                                context,
                                out stackFlags[stackIndex - 1]);
                            programCounter++;
                            break;

                        case GetComputedPropertyExpressionOp computedProperty:
                            {
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                stack[stackIndex - 1] = GetProgramComputedPropertyValue(
                                    target,
                                    stackFlags[stackIndex - 1],
                                    propertyKey,
                                    computedProperty,
                                    context,
                                    out stackFlags[stackIndex - 1]);
                                programCounter++;
                                break;
                            }

                        case GetNamedSuperPropertyExpressionOp namedSuperProperty:
                            stack[stackIndex++] = GetProgramNamedSuperPropertyValue(
                                namedSuperProperty.PropertyName,
                                environment,
                                context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case GetComputedSuperPropertyExpressionOp:
                            {
                                var propertyKey = stack[--stackIndex];
                                stack[stackIndex++] = GetProgramComputedSuperPropertyValue(
                                    propertyKey,
                                    environment,
                                    context);
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case SetNamedPropertyExpressionOp namedAssignment:
                            {
                                var propertyValue = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                ApplyProgramNamedPropertyAssignment(target, namedAssignment, propertyValue, context);
                                stack[stackIndex - 1] = propertyValue;
                                stackFlags[stackIndex - 1] = false;
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
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case SetNamedSuperPropertyExpressionOp namedSuperAssignment:
                            {
                                var propertyValue = stack[stackIndex - 1];
                                stack[stackIndex - 1] = ApplyProgramNamedSuperPropertyAssignment(
                                    namedSuperAssignment,
                                    propertyValue,
                                    environment,
                                    context);
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case SetComputedSuperPropertyExpressionOp computedSuperAssignment:
                            {
                                var propertyValue = stack[--stackIndex];
                                var propertyKey = stack[--stackIndex];
                                stack[stackIndex++] = ApplyProgramComputedSuperPropertyAssignment(
                                    propertyKey,
                                    computedSuperAssignment,
                                    propertyValue,
                                    environment,
                                    context);
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case UpdateIdentifierExpressionOp updateIdentifier:
                            stack[stackIndex++] = ExecuteProgramIdentifierUpdate(
                                updateIdentifier,
                                environment,
                                context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case UpdateNamedPropertyExpressionOp updateNamedProperty:
                            stack[stackIndex - 1] = ExecuteProgramNamedPropertyUpdate(
                                stack[stackIndex - 1],
                                updateNamedProperty,
                                context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case UpdateComputedPropertyExpressionOp updateComputedProperty:
                            {
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                stack[stackIndex - 1] = ExecuteProgramComputedPropertyUpdate(
                                    target,
                                    propertyKey,
                                    updateComputedProperty,
                                    context);
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case UpdateNamedSuperPropertyExpressionOp updateNamedSuperProperty:
                            stack[stackIndex++] = ExecuteProgramNamedSuperPropertyUpdate(
                                updateNamedSuperProperty,
                                environment,
                                context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case UpdateComputedSuperPropertyExpressionOp updateComputedSuperProperty:
                            {
                                var propertyKey = stack[--stackIndex];
                                stack[stackIndex++] = ExecuteProgramComputedSuperPropertyUpdate(
                                    propertyKey,
                                    updateComputedSuperProperty,
                                    environment,
                                    context);
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case TypeOfExpressionOp:
                            stack[stackIndex - 1] = new JsValue(GetTypeofStringValue(stack[stackIndex - 1]));
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case TypeOfIdentifierExpressionOp typeofIdentifier:
                            stack[stackIndex++] = ExecuteProgramTypeOfIdentifier(
                                typeofIdentifier,
                                environment,
                                context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case DeleteIdentifierExpressionOp deleteIdentifier:
                            stack[stackIndex++] = ExecuteProgramDeleteIdentifier(
                                deleteIdentifier,
                                environment,
                                context)
                                ? JsValue.True
                                : JsValue.False;
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case DeleteNamedPropertyExpressionOp deleteNamedProperty:
                            stack[stackIndex - 1] = ExecuteProgramDeleteNamedProperty(
                                stack[stackIndex - 1],
                                deleteNamedProperty,
                                context)
                                ? JsValue.True
                                : JsValue.False;
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case DeleteComputedPropertyExpressionOp:
                            {
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                stack[stackIndex - 1] = ExecuteProgramDeleteComputedProperty(
                                    target,
                                    propertyKey,
                                    context)
                                    ? JsValue.True
                                    : JsValue.False;
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case UnaryPlusExpressionOp:
                            {
                                var operand = stack[stackIndex - 1];
                                stack[stackIndex - 1] = operand.IsBigInt
                                    ? throw StandardLibrary.ThrowTypeError(
                                        "Cannot convert a BigInt value to a number",
                                        context)
                                    : new JsValue(ToNumberValue(operand, context));
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case UnaryMinusExpressionOp:
                            stack[stackIndex - 1] = NegateValue(stack[stackIndex - 1], context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case UnaryBitwiseNotExpressionOp:
                            stack[stackIndex - 1] = BitwiseNotValue(stack[stackIndex - 1], context);
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case UnaryVoidExpressionOp:
                            stack[stackIndex - 1] = JsValue.Undefined;
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case ToStringExpressionOp:
                            stack[stackIndex - 1] = new JsValue(JsOps.ToJsString(stack[stackIndex - 1], context));
                            stackFlags[stackIndex - 1] = false;
                            programCounter++;
                            break;

                        case UnaryLogicalNotExpressionOp:
                            stack[stackIndex - 1] = stack[stackIndex - 1].IsTruthy ? JsValue.False : JsValue.True;
                            stackFlags[stackIndex - 1] = false;
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
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case PrivateFieldInExpressionOp privateFieldIn:
                            {
                                var target = stack[stackIndex - 1];
                                if (target.Kind != JsValueKind.Object ||
                                    target.ObjectValue is not JsObject jsObj)
                                {
                                    context.SetThrow(StandardLibrary.CreateTypeError(
                                        "Cannot use 'in' operator to search for a private field in a non-object",
                                        context,
                                        context.RealmState));
                                    return JsValue.Undefined;
                                }

                                var lexeme = $"#{privateFieldIn.PrivateName}";
                                var resolvedKey = context.ResolvePrivateNameKey(lexeme);
                                var found = false;
                                if (resolvedKey is not null)
                                {
                                    found = jsObj.HasPrivateField(resolvedKey);
                                    if (!found &&
                                        PrivateNameScope.TryResolveScope(
                                            context.RealmState, resolvedKey, out var scope) &&
                                        scope is not null)
                                    {
                                        found = jsObj.HasPrivateBrand(scope.BrandToken);
                                    }
                                }

                                stack[stackIndex - 1] = found ? JsValue.True : JsValue.False;
                                stackFlags[stackIndex - 1] = false;
                                programCounter++;
                                break;
                            }

                        case ThrowReferenceErrorExpressionOp throwRefError:
                            throw StandardLibrary.ThrowReferenceError(
                                throwRefError.Message, context, context.RealmState);

                        case PopExpressionOp:
                            stackIndex--;
                            programCounter++;
                            break;

                        case JumpExpressionOp jump:
                            programCounter = jump.Target;
                            break;

                        case JumpIfNullishExpressionOp jumpIfNullish:
                            if (stackFlags[stackIndex - 1] || stack[stackIndex - 1].IsNullish)
                            {
                                if (jumpIfNullish.ReplaceWithUndefined)
                                {
                                    stack[stackIndex - 1] = JsValue.Undefined;
                                    stackFlags[stackIndex - 1] = true;
                                }

                                programCounter = jumpIfNullish.Target;
                            }
                            else
                            {
                                programCounter++;
                            }
                            break;

                        case JumpIfShortCircuitedExpressionOp jumpIfShortCircuited:
                            programCounter = stackFlags[stackIndex - 1]
                                ? jumpIfShortCircuited.Target
                                : programCounter + 1;
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

                        case SuperConstructExpressionOp superConstruct:
                            stackIndex = ExecuteProgramSuperConstruct(
                                superConstruct,
                                stack,
                                stackFlags,
                                stackIndex,
                                environment,
                                context);
                            programCounter++;
                            break;

                        case CallExpressionOp call:
                            stackIndex = ExecuteProgramCall(
                                call,
                                stack,
                                stackFlags,
                                stackIndex,
                                environment,
                                context);
                            programCounter++;
                            break;

                        case ConstructExpressionOp construct:
                            stackIndex = ExecuteProgramConstruct(
                                construct,
                                stack,
                                stackFlags,
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
                        return stackIndex > 0
                            ? stackFlags[stackIndex - 1] ? JsValue.Undefined : stack[stackIndex - 1]
                            : JsValue.Undefined;
                    }
                }

                return stackIndex > 0
                    ? stackFlags[stackIndex - 1] ? JsValue.Undefined : stack[stackIndex - 1]
                    : JsValue.Undefined;
            }
            finally
            {
                ArrayPool<JsValue>.Shared.Return(rentedStack, clearArray: true);
                ArrayPool<bool>.Shared.Return(rentedFlags, clearArray: true);
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

        private void ApplyProgramIdentifierAssignment(
            StoreIdentifierExpressionOp identifier,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (identifier.AllowNameInference &&
                value is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(identifier.Name.Name);
            }

            var variable = FlatSlotAccessor.Create(this, identifier.FlatSlotId);
            if (variable.UseFlatSlot)
            {
                variable.EnsureAssignable(identifier.Name, _realmState);
                variable.Variable.Write(value);
                return;
            }

            if (identifier.ScopeId >= 0 && identifier.SlotIndex >= 0)
            {
                var targetIdentifier = new IdentifierExpression(
                    Source: null,
                    identifier.Name,
                    SlotIndex: identifier.SlotIndex,
                    ScopeId: identifier.ScopeId,
                    FlatSlotId: identifier.FlatSlotId);
                environment.TryWriteIdentifierWithSlot(targetIdentifier, value, context);
                return;
            }

            environment.SetIdentifierJsValue(identifier.Name, value, context);
        }

        private JsValue ExecuteProgramIdentifierUpdate(
            UpdateIdentifierExpressionOp update,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var loadIdentifier = new LoadIdentifierExpressionOp(
                update.Name,
                update.ScopeId,
                update.SlotIndex,
                update.FlatSlotId,
                update.IsArguments);
            var currentValue = EvaluateProgramIdentifier(loadIdentifier, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            GetUpdatedNumericValue(currentValue, update.IsIncrement, context, out var oldNumericValue, out var newValue);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            ApplyProgramIdentifierAssignment(
                new StoreIdentifierExpressionOp(
                    update.Name,
                    update.ScopeId,
                    update.SlotIndex,
                    update.FlatSlotId,
                    AllowNameInference: false),
                newValue,
                environment,
                context);

            return update.IsPrefix ? newValue : oldNumericValue;
        }

        private static JsValue ExecuteProgramNamedPropertyUpdate(
            JsValue target,
            UpdateNamedPropertyExpressionOp update,
            EvaluationContext context)
        {
            var currentValue = GetProgramNamedPropertyValue(
                target,
                targetWasShortCircuited: false,
                new GetNamedPropertyExpressionOp(update.PropertyName),
                context,
                out _);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            GetUpdatedNumericValue(currentValue, update.IsIncrement, context, out var oldNumericValue, out var newValue);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            ApplyProgramNamedPropertyAssignment(
                target,
                new SetNamedPropertyExpressionOp(update.PropertyName, AllowNameInference: false),
                newValue,
                context);

            return update.IsPrefix ? newValue : oldNumericValue;
        }

        private static JsValue ExecuteProgramComputedPropertyUpdate(
            JsValue target,
            JsValue propertyKey,
            UpdateComputedPropertyExpressionOp update,
            EvaluationContext context)
        {
            var currentValue = GetProgramComputedPropertyValue(
                target,
                targetWasShortCircuited: false,
                propertyKey,
                new GetComputedPropertyExpressionOp(),
                context,
                out _);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            GetUpdatedNumericValue(currentValue, update.IsIncrement, context, out var oldNumericValue, out var newValue);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            ApplyProgramComputedPropertyAssignment(
                target,
                propertyKey,
                new SetComputedPropertyExpressionOp(AllowNameInference: false),
                newValue,
                context);

            return update.IsPrefix ? newValue : oldNumericValue;
        }

        private JsValue ExecuteProgramNamedSuperPropertyUpdate(
            UpdateNamedSuperPropertyExpressionOp update,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var currentValue = GetProgramNamedSuperPropertyValue(update.PropertyName, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            GetUpdatedNumericValue(currentValue, update.IsIncrement, context, out var oldNumericValue, out var newValue);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            ApplyProgramNamedSuperPropertyAssignment(
                new SetNamedSuperPropertyExpressionOp(update.PropertyName, AllowNameInference: false),
                newValue,
                environment,
                context);

            return update.IsPrefix ? newValue : oldNumericValue;
        }

        private JsValue ExecuteProgramComputedSuperPropertyUpdate(
            JsValue propertyKey,
            UpdateComputedSuperPropertyExpressionOp update,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var currentValue = GetProgramComputedSuperPropertyValue(propertyKey, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            GetUpdatedNumericValue(currentValue, update.IsIncrement, context, out var oldNumericValue, out var newValue);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            ApplyProgramComputedSuperPropertyAssignment(
                propertyKey,
                new SetComputedSuperPropertyExpressionOp(AllowNameInference: false),
                newValue,
                environment,
                context);

            return update.IsPrefix ? newValue : oldNumericValue;
        }

        private JsValue ExecuteProgramTypeOfIdentifier(
            TypeOfIdentifierExpressionOp identifier,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var hasBinding = environment.HasBinding(identifier.Name);
            var operandValue = EvaluateProgramIdentifier(
                new LoadIdentifierExpressionOp(
                    identifier.Name,
                    identifier.ScopeId,
                    identifier.SlotIndex,
                    identifier.FlatSlotId,
                    identifier.IsArguments),
                environment,
                context);

            if (context.IsThrow && !hasBinding)
            {
                context.Clear();
                return new JsValue("undefined");
            }

            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return new JsValue(GetTypeofStringValue(operandValue));
        }

        private static bool ExecuteProgramDeleteIdentifier(
            DeleteIdentifierExpressionOp identifier,
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (context.CurrentScope.IsStrict)
            {
                throw StandardLibrary.ThrowSyntaxError(
                    "Delete of an unqualified identifier is not allowed in strict mode.",
                    context,
                    context.RealmState);
            }

            var outcome = environment.DeleteBinding(identifier.Name);
            return outcome is DeleteBindingResult.Deleted or DeleteBindingResult.NotFound;
        }

        private static bool ExecuteProgramDeleteNamedProperty(
            JsValue target,
            DeleteNamedPropertyExpressionOp propertyOp,
            EvaluationContext context)
        {
            var handle = PropertyHandle.Resolve(
                target,
                propertyOp.PropertyName,
                context,
                context.CurrentScope.IsStrict);
            return handle.Delete();
        }

        private static bool ExecuteProgramDeleteComputedProperty(
            JsValue target,
            JsValue propertyKey,
            EvaluationContext context)
        {
            var handle = PropertyHandle.Resolve(
                target,
                propertyKey,
                context,
                context.CurrentScope.IsStrict,
                allowPrivate: false);
            return handle.Delete();
        }

        private static void GetUpdatedNumericValue(
            JsValue currentValue,
            bool isIncrement,
            EvaluationContext context,
            out JsValue oldNumericValue,
            out JsValue newValue)
        {
            if (currentValue.Kind == JsValueKind.Number)
            {
                oldNumericValue = currentValue;
                newValue = JsValueCache.GetNumberJsValue(
                    isIncrement
                        ? currentValue.NumberValue + 1.0
                        : currentValue.NumberValue - 1.0);
                return;
            }

            var numericValue = currentValue.IsBigInt
                ? currentValue
                : ToNumericValue(currentValue, context);
            if (context.ShouldStopEvaluation)
            {
                oldNumericValue = JsValue.Undefined;
                newValue = JsValue.Undefined;
                return;
            }

            oldNumericValue = numericValue;
            newValue = isIncrement
                ? IncrementValue(numericValue, context)
                : DecrementValue(numericValue, context);
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

            if (defineProperty.AllowNameInference)
            {
                ApplyObjectLiteralAnonymousFunctionName(propertyValue, defineProperty.PropertyName);
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
            DefineComputedObjectPropertyExpressionOp defineProperty,
            JsValue propertyValue,
            EvaluationContext context)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            if (defineProperty.AllowNameInference)
            {
                ApplyObjectLiteralAnonymousFunctionName(propertyValue, propertyName);
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

        private static void DefineObjectLiteralMethod(
            JsObject targetObject,
            string propertyName,
            JsValue methodValue)
        {
            ConfigureObjectLiteralCallable(targetObject, propertyName, methodValue, accessorKind: null);
            targetObject.DefineProperty(propertyName,
                new PropertyDescriptor
                {
                    JsValue = methodValue,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                });
        }

        private static void DefineComputedObjectLiteralMethod(
            JsObject targetObject,
            JsValue propertyKey,
            JsValue methodValue,
            EvaluationContext context)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            DefineObjectLiteralMethod(targetObject, propertyName, methodValue);
        }

        private static void DefineObjectLiteralAccessor(
            JsObject targetObject,
            string propertyName,
            ObjectAccessorKind accessorKind,
            JsValue accessorValue)
        {
            var callable = ConfigureObjectLiteralCallable(
                targetObject,
                propertyName,
                accessorValue,
                accessorKind);

            targetObject.DefineAccessorProperty(
                propertyName,
                accessorKind == ObjectAccessorKind.Getter ? callable : null,
                accessorKind == ObjectAccessorKind.Setter ? callable : null);
        }

        private static void DefineComputedObjectLiteralAccessor(
            JsObject targetObject,
            JsValue propertyKey,
            ObjectAccessorKind accessorKind,
            JsValue accessorValue,
            EvaluationContext context)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            DefineObjectLiteralAccessor(targetObject, propertyName, accessorKind, accessorValue);
        }

        private static IJsCallable ConfigureObjectLiteralCallable(
            JsObject targetObject,
            string propertyName,
            JsValue callableValue,
            ObjectAccessorKind? accessorKind)
        {
            if (!callableValue.TryGetObject<IJsCallable>(out var callable))
            {
                throw new InvalidOperationException("Object literal function members require a callable value.");
            }

            switch (callable)
            {
                case SyncFunctionInvoker typed:
                    typed.SetHomeObject(targetObject);
                    typed.DisableConstruction();
                    break;
                case SyncGeneratorInvoker generatorFactory:
                    generatorFactory.SetHomeObject(targetObject);
                    generatorFactory.DisableConstruction();
                    break;
                case AsyncGeneratorFunctionInvoker asyncGeneratorFactory:
                    asyncGeneratorFactory.SetHomeObject(targetObject);
                    asyncGeneratorFactory.DisableConstruction();
                    break;
            }

            if (callable is IFunctionNameTarget nameTarget)
            {
                var displayName = accessorKind switch
                {
                    ObjectAccessorKind.Getter => $"get {propertyName.BuildFunctionNameDisplay()}",
                    ObjectAccessorKind.Setter => $"set {propertyName.BuildFunctionNameDisplay()}",
                    _ => propertyName.BuildFunctionNameDisplay()
                };
                nameTarget.EnsureHasName(displayName);
            }

            return callable;
        }

        private static JsValue ResolveProgramPropertyKey(JsValue propertyKey, EvaluationContext context)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            return context.ShouldStopEvaluation ? JsValue.Undefined : new JsValue(propertyName);
        }

        private static void ApplyObjectLiteralAnonymousFunctionName(JsValue propertyValue, string propertyName)
        {
            if (propertyValue.ObjectValue is not IFunctionNameTarget nameTarget)
            {
                return;
            }

            nameTarget.EnsureHasName(BuildFunctionNameDisplay(propertyName));
        }

        private static string BuildFunctionNameDisplay(string propertyName)
        {
            if (JsSymbol.TryGetByInternalKey(propertyName, out var symbol))
            {
                return symbol!.Description is null ? string.Empty : $"[{symbol.Description}]";
            }

            return propertyName;
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

        private static JsArray GetOrCreateProgramTemplateObject(
            TaggedTemplateDescriptor descriptor,
            EvaluationContext context)
        {
            if (context.RealmState.TemplateObjectCache.TryGetValue(descriptor, out var cachedTemplate))
            {
                return (JsArray)cachedTemplate;
            }

            var stringsArray = new JsArray(descriptor.CookedStrings, context.RealmState);
            var rawStringsArray = new JsArray(descriptor.RawStrings, context.RealmState);
            var templateObject = (JsArray)stringsArray.CreateTemplateObject(rawStringsArray);
            context.RealmState.TemplateObjectCache[descriptor] = templateObject;
            return templateObject;
        }

        private static SuperBinding GetSuperBindingForProgramRead(
            JsEnvironment environment,
            EvaluationContext context)
        {
            EnsureProgramSuperReference(environment, context);

            var binding = environment.ExpectSuperBinding(context);
            if (binding.Prototype is null)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Cannot read properties of null (reading from super)",
                    context,
                    context.RealmState);
                context.SetThrow(error);
            }

            return binding;
        }

        private static void EnsureProgramSuperReference(
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (!environment.IsThisInitializationKnownTrue(context))
            {
                throw environment.CreateSuperReferenceError(context);
            }
        }

        private static void LoadProgramNamedSuperCallTarget(
            string propertyName,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue receiver,
            out JsValue callee)
        {
            var binding = GetSuperBindingForProgramRead(environment, context);
            receiver = binding.ThisValue;
            callee = context.ShouldStopEvaluation
                ? JsValue.Undefined
                : binding.TryGetProperty(propertyName, out var value)
                    ? value
                    : JsValue.Undefined;
        }

        private static void LoadProgramComputedSuperCallTarget(
            JsValue propertyKey,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue receiver,
            out JsValue callee)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                receiver = JsValue.Undefined;
                callee = JsValue.Undefined;
                return;
            }

            LoadProgramNamedSuperCallTarget(propertyName, environment, context, out receiver, out callee);
        }

        private static JsValue GetProgramNamedPropertyValue(
            JsValue target,
            bool targetWasShortCircuited,
            GetNamedPropertyExpressionOp propertyOp,
            EvaluationContext context,
            out bool resultWasShortCircuited)
        {
            if (targetWasShortCircuited)
            {
                resultWasShortCircuited = true;
                return JsValue.Undefined;
            }

            if (propertyOp.IsOptional && target.IsNullOrUndefined)
            {
                resultWasShortCircuited = true;
                return JsValue.Undefined;
            }

            if (target.IsNullOrUndefined)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Cannot read properties of null or undefined",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                resultWasShortCircuited = false;
                return JsValue.Undefined;
            }

            resultWasShortCircuited = false;
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
            bool targetWasShortCircuited,
            JsValue propertyKey,
            GetComputedPropertyExpressionOp propertyOp,
            EvaluationContext context,
            out bool resultWasShortCircuited)
        {
            if (targetWasShortCircuited)
            {
                resultWasShortCircuited = true;
                return JsValue.Undefined;
            }

            if (target.IsNullOrUndefined)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Cannot read properties of null or undefined",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                resultWasShortCircuited = false;
                return JsValue.Undefined;
            }

            resultWasShortCircuited = false;
            return JsOps.TryGetPropertyValueJsValue(target, propertyKey, out var directValue, context)
                ? directValue
                : JsValue.Undefined;
        }

        private static JsValue GetProgramNamedSuperPropertyValue(
            string propertyName,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var binding = GetSuperBindingForProgramRead(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return binding.TryGetProperty(propertyName, out var value)
                ? value
                : JsValue.Undefined;
        }

        private static JsValue GetProgramComputedSuperPropertyValue(
            JsValue propertyKey,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return GetProgramNamedSuperPropertyValue(propertyName, environment, context);
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

            var handle = PropertyHandle.Resolve(
                target,
                propertyName,
                context,
                context.CurrentScope.IsStrict,
                allowPrivate: false);
            handle.SetValue(value);
        }

        private static JsValue ApplyProgramNamedSuperPropertyAssignment(
            SetNamedSuperPropertyExpressionOp propertyOp,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (propertyOp.AllowNameInference &&
                value is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(propertyOp.PropertyName);
            }

            return AssignToSuperBinding(environment, context, propertyOp.PropertyName, value, "property");
        }

        private static JsValue ApplyProgramComputedSuperPropertyAssignment(
            JsValue propertyKey,
            SetComputedSuperPropertyExpressionOp propertyOp,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            if (propertyOp.AllowNameInference &&
                value is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(propertyName);
            }

            return AssignToSuperBinding(environment, context, propertyName, value, "index");
        }

        private int ExecuteProgramCall(
            CallExpressionOp call,
            Span<JsValue> stack,
            Span<bool> stackFlags,
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
                stackFlags[baseIndex] = false;
                return baseIndex + 1;
            }

            if (callable is SyncFunctionInvoker { IsClassConstructor: true } classConstructor)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Class constructor cannot be invoked without 'new'",
                    context,
                    classConstructor.RealmState);
                context.SetThrow(error);
                stack[baseIndex] = JsValue.Undefined;
                stackFlags[baseIndex] = false;
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
                if (callable is global::Asynkron.JsEngine.EvalHostFunction evalHostFunction)
                {
                    evalHost = evalHostFunction;
                    evalHost.IsDirectCall = call.IsDirectEval;
                    evalHost.InClassFieldInitializer = context.InClassFieldInitializer;
                }

                if (callable is DebugAwareHostFunction debugAware)
                {
                    debugFunction = debugAware;
                    debugFunction.CurrentJsEnvironment = environment;
                    debugFunction.CurrentContext = context;
                }

                IReadOnlyList<JsValue> arguments;
                arguments = MaterializeProgramArguments(
                    call.ArgumentCount,
                    call.SpreadMask,
                    stack,
                    calleeIndex + 1,
                    context,
                    out pooledArguments);

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
            stackFlags[baseIndex] = false;
            return baseIndex + 1;
        }

        private int ExecuteProgramConstruct(
            ConstructExpressionOp construct,
            Span<JsValue> stack,
            Span<bool> stackFlags,
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
                stackFlags[constructorIndex] = false;
                return constructorIndex + 1;
            }

            JsValue[]? pooledArguments = null;

            try
            {
                var arguments = MaterializeProgramArguments(
                    construct.ArgumentCount,
                    construct.SpreadMask,
                    stack,
                    constructorIndex + 1,
                    context,
                    out pooledArguments);

                stack[constructorIndex] = global::Asynkron.JsEngine.StdLib.ReflectHelper.Construct(
                    callable,
                    arguments,
                    callable,
                    context.RealmState);
                stackFlags[constructorIndex] = false;
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
                stack[constructorIndex] = signal.ThrownValue;
                stackFlags[constructorIndex] = false;
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

        private int ExecuteProgramSuperConstruct(
            SuperConstructExpressionOp superConstruct,
            Span<JsValue> stack,
            Span<bool> stackFlags,
            int stackIndex,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var baseIndex = stackIndex - superConstruct.ArgumentCount;
            JsValue[]? pooledArguments = null;
            var callDepthIncremented = false;

            try
            {
                var superBindingForCall = environment.ExpectSuperBinding(context);
                var dynamicSuperConstructor = environment.ResolveSuperConstructorForCall(superBindingForCall);

                if (dynamicSuperConstructor is null)
                {
                    throw new InvalidOperationException(
                        $"Super constructor is not available in this context.{context.GetSourceInfo()}");
                }

                var constructorValue = JsValue.FromObjectUnsafe(dynamicSuperConstructor);

                JsEnvironment? thisInitializationEnvironment = null;
                var thisInitializationValue = JsValue.Undefined;
                if (environment.TryFindBindingJsValue(Symbol.LexicalThisEnvironment, true, out _, out var lexicalEnvValue) &&
                    lexicalEnvValue.TryGetObject<JsEnvironment>(out var lexicalThisEnv))
                {
                    thisInitializationEnvironment = lexicalThisEnv;
                    if (lexicalThisEnv.TryGetJsValue(Symbol.ThisInitialized, out var lexicalInitValue))
                    {
                        thisInitializationValue = lexicalInitValue;
                    }
                }
                else if (environment.TryFindBindingJsValue(Symbol.This, true, out var thisEnv, out _))
                {
                    thisInitializationEnvironment = thisEnv;
                    if (thisEnv.TryGetJsValue(Symbol.ThisInitialized, out var initValue))
                    {
                        thisInitializationValue = initValue;
                    }
                }

                if (thisInitializationEnvironment is null &&
                    environment.TryFindBindingJsValue(Symbol.ThisInitialized, true, out var foundEnv, out var foundValue))
                {
                    thisInitializationEnvironment = foundEnv;
                    thisInitializationValue = foundValue;
                }

                if (++context.CallDepth > context.MaxCallDepth)
                {
                    context.CallDepth--;
                    throw new InvalidOperationException(
                        $"Exceeded maximum call depth of {context.MaxCallDepth}.");
                }

                callDepthIncremented = true;

                var arguments = MaterializeProgramArguments(
                    superConstruct.ArgumentCount,
                    superConstruct.SpreadMask,
                    stack,
                    baseIndex,
                    context,
                    out pooledArguments);

                if (!JsOps.IsConstructor(constructorValue) ||
                    !constructorValue.TryGetObject<IJsCallable>(out var callable))
                {
                    var error = StandardLibrary.CreateTypeError(
                        "Super constructor is not a constructor",
                        context,
                        context.RealmState);
                    context.SetThrow(error);
                    stack[baseIndex] = JsValue.Undefined;
                    stackFlags[baseIndex] = false;
                    return baseIndex + 1;
                }

                var newTargetValue = environment.TryGetJsValue(Symbol.NewTarget, out var inheritedNewTarget)
                    ? inheritedNewTarget
                    : JsValue.Undefined;
                var newTargetCallable = newTargetValue.TryGetObject<IJsCallable>(out var nt)
                    ? nt
                    : callable;

                var result = ReflectHelper.Construct(callable, arguments, newTargetCallable, context.RealmState);

                var callResultObject = result.Kind == JsValueKind.Object ? result.ObjectValue : null;
                object? thisAfterSuper = callResultObject;
                if (callResultObject is not JsObject && callResultObject is not IJsObjectLike)
                {
                    thisAfterSuper = superBindingForCall.ThisValue.Kind == JsValueKind.Object
                        ? superBindingForCall.ThisValue.ObjectValue
                        : null;
                }

                if (thisInitializationEnvironment is not null)
                {
                    var alreadyInitialized = thisInitializationValue.IsUndefined
                        ? thisInitializationEnvironment.TryGetJsValue(Symbol.ThisInitialized, out var initValue)
                            ? initValue
                            : JsValue.Undefined
                        : thisInitializationValue;

                    if (!alreadyInitialized.IsUndefined && JsOps.ToBoolean(alreadyInitialized))
                    {
                        throw StandardLibrary.ThrowReferenceError(
                            "Super constructor may only be called once.", context, context.RealmState);
                    }
                }

                var targetEnvironment = thisInitializationEnvironment ?? environment;
                var initializedThis = thisAfterSuper is null
                    ? JsValue.Undefined
                    : JsValue.FromObjectUnsafe(thisAfterSuper);
                targetEnvironment.AssignJsValue(Symbol.This, initializedThis);

                if (targetEnvironment.TryGetObject<SuperBinding>(Symbol.Super, out var binding))
                {
                    var constructorForSuper = superBindingForCall.Constructor ?? binding.Constructor;
                    var prototypeForSuper = superBindingForCall.Prototype ?? binding.Prototype;
                    targetEnvironment.AssignJsValue(Symbol.Super,
                        JsValue.FromObjectUnsafe(new SuperBinding(
                            constructorForSuper,
                            prototypeForSuper,
                            initializedThis,
                            true)));
                }

                context.MarkThisInitialized();
                targetEnvironment.SetThisInitializationStatus(true);

                if (thisAfterSuper is IJsObjectLike objectLike &&
                    context.TryPopClassFieldInitializer(out var pendingInitializer) &&
                    pendingInitializer.Constructor is SyncFunctionInvoker pendingConstructor)
                {
                    pendingConstructor.InitializeInstance(
                        objectLike,
                        pendingInitializer.Environment,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        stack[baseIndex] = context.FlowValue;
                        stackFlags[baseIndex] = false;
                        return baseIndex + 1;
                    }
                }

                stack[baseIndex] = result;
                stackFlags[baseIndex] = false;
                return baseIndex + 1;
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
                stack[baseIndex] = signal.ThrownValue;
                stackFlags[baseIndex] = false;
                return baseIndex + 1;
            }
            finally
            {
                if (pooledArguments is not null)
                {
                    global::Asynkron.JsEngine.JsValueCache.ReturnJsValueArray(pooledArguments);
                }

                if (callDepthIncremented)
                {
                    context.CallDepth--;
                }
            }
        }

        private IReadOnlyList<JsValue> MaterializeProgramArguments(
            int argumentCount,
            ImmutableArray<bool> spreadMask,
            Span<JsValue> stack,
            int firstArgumentIndex,
            EvaluationContext context,
            out JsValue[]? pooledArguments)
        {
            pooledArguments = null;

            if (argumentCount == 0)
            {
                return [];
            }

            if (!spreadMask.IsDefaultOrEmpty)
            {
                var spreadArguments = ImmutableArray.CreateBuilder<JsValue>(argumentCount);
                for (var i = 0; i < argumentCount; i++)
                {
                    var argumentValue = stack[firstArgumentIndex + i];
                    if (spreadMask[i])
                    {
                        spreadArguments.AddRange(EnumerateSpread(argumentValue, context));
                    }
                    else
                    {
                        spreadArguments.Add(argumentValue);
                    }
                }

                return spreadArguments.ToImmutable();
            }

            var argumentArray = argumentCount <= 4
                ? global::Asynkron.JsEngine.JsValueCache.RentJsValueArray(argumentCount)
                : new JsValue[argumentCount];

            if (argumentCount <= 4)
            {
                pooledArguments = argumentArray;
            }

            for (var i = 0; i < argumentCount; i++)
            {
                argumentArray[i] = stack[firstArgumentIndex + i];
            }

            return argumentArray;
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
