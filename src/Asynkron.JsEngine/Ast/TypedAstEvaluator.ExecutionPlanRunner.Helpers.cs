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

        private static int GetExpressionFlagWordCount(int stackSize)
        {
            return (stackSize + 63) >> 6;
        }

        private ref struct ExpressionFlagStack(Span<ulong> words)
        {
            private Span<ulong> _words = words;

            [MethodImpl(JsEngineConstants.Inlining)]
            public bool Get(int index)
            {
                var wordIndex = index >> 6;
                var bit = 1UL << (index & 63);
                return (_words[wordIndex] & bit) != 0;
            }

            [MethodImpl(JsEngineConstants.Inlining)]
            public void Set(int index, bool value)
            {
                var wordIndex = index >> 6;
                var bit = 1UL << (index & 63);
                ref var word = ref _words[wordIndex];
                if (value)
                {
                    word |= bit;
                }
                else
                {
                    word &= ~bit;
                }
            }

            [MethodImpl(JsEngineConstants.Inlining)]
            public void Copy(int sourceIndex, int destinationIndex)
            {
                Set(destinationIndex, Get(sourceIndex));
            }

            [MethodImpl(JsEngineConstants.Inlining)]
            public void Swap(int leftIndex, int rightIndex)
            {
                var left = Get(leftIndex);
                Set(leftIndex, Get(rightIndex));
                Set(rightIndex, left);
            }

            [MethodImpl(JsEngineConstants.Inlining)]
            public void RotateRight(int firstIndex, int secondIndex, int thirdIndex)
            {
                var third = Get(thirdIndex);
                Set(thirdIndex, Get(secondIndex));
                Set(secondIndex, Get(firstIndex));
                Set(firstIndex, third);
            }
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

            var operations = program.Operations.AsSpan();
            var literalConstants = program.LiteralConstants.AsSpan();
            var stringConstants = program.StringConstants.AsSpan();
            var objectConstants = program.ObjectConstants.AsSpan();
            var identifierConstants = program.IdentifierConstants.AsSpan();
            var spreadMaskConstants = program.SpreadMaskConstants.AsSpan();
            var operationCount = operations.Length;
            var stackSize = Math.Max(program.MaxStackDepth, 1);
            AcquireExpressionBuffers(
                stackSize,
                out var stackBuffer,
                out var flagBuffer,
                out var rentedFromPool);
            Span<JsValue> stack = stackBuffer.AsSpan(0, stackSize);
            var stackFlags = new ExpressionFlagStack(flagBuffer.AsSpan(0, GetExpressionFlagWordCount(stackSize)));
            var stackIndex = 0;
            var programCounter = 0;
            AssignmentReference[]? assignmentReferenceBuffer = null;
            var assignmentReferenceCount = 0;
            var assignmentReferenceHighWaterMark = 0;

            try
            {
                while ((uint)programCounter < (uint)operationCount)
                {
                    var operation = operations[programCounter];
                    switch (operation.Kind)
                    {
                        case ExpressionOpKind.LoadLiteral:
                            {
                                stack[stackIndex++] = operation.GetLiteral(literalConstants);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadRegexLiteral:
                            {
                                stack[stackIndex++] = new JsValue(
                                    StdLib.RegExpHelper.CreateRegExpLiteral(
                                        operation.GetString(stringConstants),
                                        operation.EncodedRegexFlags,
                                        context.RealmState));
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadFunctionLiteral:
                            {
                                var descriptor = operation.GetObject<FunctionLiteralDescriptor>(objectConstants);
                                stack[stackIndex++] = JsValue.FromObjectUnsafe(
                                    descriptor.Function.CreateFunctionValue(
                                        environment,
                                        context,
                                        operation.IsConstructorFunction,
                                        planSeed: descriptor.PlanSeed));
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadClassLiteral:
                            {
                                var classExpression = operation.GetObject<ClassExpression>(objectConstants);
                                stack[stackIndex++] = classExpression.Definition.CreateClassValue(
                                    environment,
                                    context,
                                    classExpression.Name ?? context.CurrentFunctionNameHint);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadTemplateObject:
                            {
                                var templateDescriptor = operation.GetObject<TaggedTemplateDescriptor>(objectConstants);
                                stack[stackIndex++] = JsValue.FromJsArray(
                                    GetOrCreateProgramTemplateObject(templateDescriptor, context));
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadIdentifier:
                            {
                                var identifier = operation.GetIdentifier(identifierConstants);
                                stack[stackIndex++] = EvaluateProgramIdentifier(
                                    identifier.Name,
                                    identifier.ScopeId,
                                    identifier.SlotIndex,
                                    operation.IsArguments,
                                    environment,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadIdentifierCallTarget:
                            {
                                var identifier = operation.GetIdentifier(identifierConstants);
                                LoadProgramIdentifierCallTarget(
                                    identifier,
                                    operation.IsArguments,
                                    environment,
                                    context,
                                    out var receiver,
                                    out var callee);
                                stack[stackIndex++] = receiver;
                                stackFlags.Set(stackIndex - 1, false);
                                stack[stackIndex++] = callee;
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.ResolveIdentifierReference:
                            {
                                assignmentReferenceBuffer ??= ArrayPool<AssignmentReference>.Shared.Rent(stackSize);
                                assignmentReferenceBuffer[assignmentReferenceCount++] =
                                    environment.ResolveIdentifierAssignmentReference(
                                        operation.GetIdentifier(identifierConstants).Name,
                                        context);
                                assignmentReferenceHighWaterMark = Math.Max(
                                    assignmentReferenceHighWaterMark,
                                    assignmentReferenceCount);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadResolvedIdentifierValue:
                            {
                                if (assignmentReferenceCount == 0 || assignmentReferenceBuffer is null)
                                {
                                    throw new InvalidOperationException(
                                        "Expression bytecode attempted to load a missing identifier reference.");
                                }

                                stack[stackIndex++] =
                                    assignmentReferenceBuffer[assignmentReferenceCount - 1].GetJsValue();
                                stackFlags.Set(stackIndex - 1, false);
                                if (context.ShouldStopEvaluation)
                                {
                                    return JsValue.Undefined;
                                }

                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.PopResolvedIdentifierReference:
                            {
                                if (assignmentReferenceCount == 0 || assignmentReferenceBuffer is null)
                                {
                                    throw new InvalidOperationException(
                                        "Expression bytecode attempted to pop a missing identifier reference.");
                                }

                                assignmentReferenceBuffer[--assignmentReferenceCount] = default;
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.StoreResolvedIdentifier:
                            {
                                if (assignmentReferenceCount == 0 || assignmentReferenceBuffer is null)
                                {
                                    throw new InvalidOperationException(
                                        "Expression bytecode attempted to store through a missing identifier reference.");
                                }

                                var identifier = operation.GetIdentifier(identifierConstants);
                                var assignedValue = stack[stackIndex - 1];
                                if (operation.AllowNameInference &&
                                    assignedValue is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
                                {
                                    nameTarget.EnsureHasName(identifier.Name.Name);
                                }

                                assignmentReferenceBuffer[--assignmentReferenceCount].SetValue(assignedValue);
                                assignmentReferenceBuffer[assignmentReferenceCount] = default;
                                stackFlags.Set(stackIndex - 1, false);
                                if (context.ShouldStopEvaluation)
                                {
                                    return JsValue.Undefined;
                                }

                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.StoreIdentifier:
                            {
                                var identifier = operation.GetIdentifier(identifierConstants);
                                ApplyProgramIdentifierAssignment(
                                    identifier.Name,
                                    identifier.ScopeId,
                                    identifier.SlotIndex,
                                    identifier.FlatSlotId,
                                    operation.AllowNameInference,
                                    stack[stackIndex - 1],
                                    environment,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.ApplyBindingTarget:
                            {
                                stackIndex--;
                                var targetProgram = operation.GetObject<BindingTargetProgram>(objectConstants);
                                ApplyBindingTargetProgram(
                                    targetProgram,
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
                            }

                        case ExpressionOpKind.DuplicateTop:
                            stack[stackIndex] = stack[stackIndex - 1];
                            stackFlags.Copy(stackIndex - 1, stackIndex);
                            stackIndex++;
                            programCounter++;
                            break;

                        case ExpressionOpKind.DuplicateTopTwo:
                            stack[stackIndex] = stack[stackIndex - 2];
                            stack[stackIndex + 1] = stack[stackIndex - 1];
                            stackFlags.Copy(stackIndex - 2, stackIndex);
                            stackFlags.Copy(stackIndex - 1, stackIndex + 1);
                            stackIndex += 2;
                            programCounter++;
                            break;

                        case ExpressionOpKind.SwapTopTwo:
                            (stack[stackIndex - 1], stack[stackIndex - 2]) =
                                (stack[stackIndex - 2], stack[stackIndex - 1]);
                            stackFlags.Swap(stackIndex - 1, stackIndex - 2);
                            programCounter++;
                            break;

                        case ExpressionOpKind.RotateTopThreeRight:
                            (stack[stackIndex - 1], stack[stackIndex - 2], stack[stackIndex - 3]) =
                                (stack[stackIndex - 2], stack[stackIndex - 3], stack[stackIndex - 1]);
                            stackFlags.RotateRight(stackIndex - 3, stackIndex - 2, stackIndex - 1);
                            programCounter++;
                            break;

                        case ExpressionOpKind.LoadThis:
                            stack[stackIndex++] = ResolveThisValue(environment, context);
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.LoadNewTarget:
                            var effectiveNewTarget = _newTarget;
                            if (effectiveNewTarget.IsUndefined &&
                                environment.TryGetJsValue(Symbol.NewTarget, out var inheritedNewTarget))
                            {
                                effectiveNewTarget = inheritedNewTarget;
                            }

                            stack[stackIndex++] = effectiveNewTarget;
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.LoadImportMeta:
                            stack[stackIndex++] = EvaluateImportMeta(environment, context);
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.LoadNamedCallTarget:
                            {
                                var target = stack[stackIndex - 1];
                                var callee = GetProgramNamedCallTargetValue(
                                    target,
                                    stackFlags.Get(stackIndex - 1),
                                    operation.GetString(stringConstants),
                                    context,
                                    out var calleeWasShortCircuited);
                                stack[stackIndex++] = callee;
                                stackFlags.Set(stackIndex - 1, calleeWasShortCircuited);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadComputedCallTarget:
                            {
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                var callee = GetProgramComputedCallTargetValue(
                                    target,
                                    stackFlags.Get(stackIndex - 1),
                                    propertyKey,
                                    context,
                                    out var calleeWasShortCircuited);
                                stack[stackIndex++] = callee;
                                stackFlags.Set(stackIndex - 1, calleeWasShortCircuited);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadNamedSuperCallTarget:
                            {
                                LoadProgramNamedSuperCallTarget(
                                    operation.GetString(stringConstants),
                                    environment,
                                    context,
                                    out var receiver,
                                    out var callee);
                                stack[stackIndex++] = receiver;
                                stackFlags.Set(stackIndex - 1, false);
                                stack[stackIndex++] = callee;
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadComputedSuperCallTarget:
                            {
                                var propertyKey = stack[--stackIndex];
                                LoadProgramComputedSuperCallTarget(
                                    propertyKey,
                                    environment,
                                    context,
                                    out var receiver,
                                    out var callee);
                                stack[stackIndex++] = receiver;
                                stackFlags.Set(stackIndex - 1, false);
                                stack[stackIndex++] = callee;
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.EnsureSuperReference:
                            EnsureProgramSuperReference(environment, context);
                            programCounter++;
                            break;

                        case ExpressionOpKind.CreateArray:
                            stack[stackIndex++] = JsValue.FromJsArray(new JsArray(context.RealmState));
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.ArrayPush:
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

                        case ExpressionOpKind.ArrayPushHole:
                            {
                                if (!stack[stackIndex - 1].TryGetArray(out var targetArray))
                                {
                                    throw new InvalidOperationException("Array hole expression op requires an array receiver.");
                                }

                                targetArray.PushHole();
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.ArraySpread:
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

                        case ExpressionOpKind.CreateObject:
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
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.RequireObjectCoercible:
                            {
                                var checkIndex = stackIndex - 1 - operation.Depth;
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

                        case ExpressionOpKind.ResolvePropertyKey:
                            stack[stackIndex - 1] = ResolveProgramPropertyKey(stack[stackIndex - 1], context);
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.DefineObjectProperty:
                            {
                                var propertyValue = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Object property expression op requires an object receiver.");
                                }

                                DefineObjectLiteralProperty(
                                    targetObject,
                                    operation.GetString(stringConstants),
                                    operation,
                                    propertyValue);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.DefineComputedObjectProperty:
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
                                    operation,
                                    propertyValue,
                                    context);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.DefineObjectMethod:
                            {
                                var methodValue = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Object method expression op requires an object receiver.");
                                }

                                DefineObjectLiteralMethod(
                                    targetObject,
                                    operation.GetString(stringConstants),
                                    methodValue);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.DefineComputedObjectMethod:
                            {
                                var methodValue = stack[--stackIndex];
                                var propertyKey = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Computed object method expression op requires an object receiver.");
                                }

                                DefineComputedObjectLiteralMethod(
                                    targetObject,
                                    propertyKey,
                                    methodValue,
                                    context);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.DefineObjectAccessor:
                            {
                                var accessorValue = stack[--stackIndex];
                                if (!stack[stackIndex - 1].TryGetObject<JsObject>(out var targetObject))
                                {
                                    throw new InvalidOperationException(
                                        "Object accessor expression op requires an object receiver.");
                                }

                                DefineObjectLiteralAccessor(
                                    targetObject,
                                    operation.GetString(stringConstants),
                                    operation.AccessorKind,
                                    accessorValue);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.DefineComputedObjectAccessor:
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
                                    operation.AccessorKind,
                                    accessorValue,
                                    context);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.ObjectSpread:
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

                        case ExpressionOpKind.GetNamedProperty:
                            {
                                var targetWasShortCircuited = stackFlags.Get(stackIndex - 1);
                                stack[stackIndex - 1] = GetProgramNamedPropertyValue(
                                    stack[stackIndex - 1],
                                    targetWasShortCircuited,
                                    operation.GetString(stringConstants),
                                    operation.IsOptional,
                                    context,
                                    out var resultWasShortCircuited);
                                stackFlags.Set(stackIndex - 1, resultWasShortCircuited);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.GetComputedProperty:
                            {
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                var targetWasShortCircuited = stackFlags.Get(stackIndex - 1);
                                stack[stackIndex - 1] = GetProgramComputedPropertyValue(
                                    target,
                                    targetWasShortCircuited,
                                    propertyKey,
                                    context,
                                    out var resultWasShortCircuited);
                                stackFlags.Set(stackIndex - 1, resultWasShortCircuited);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.GetNamedSuperProperty:
                            {
                                stack[stackIndex++] = GetProgramNamedSuperPropertyValue(
                                    operation.GetString(stringConstants),
                                    environment,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.GetComputedSuperProperty:
                            {
                                var propertyKey = stack[--stackIndex];
                                stack[stackIndex++] = GetProgramComputedSuperPropertyValue(
                                    propertyKey,
                                    environment,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.SetNamedProperty:
                            {
                                var propertyValue = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                ApplyProgramNamedPropertyAssignment(
                                    target,
                                    operation.GetString(stringConstants),
                                    operation.AllowNameInference,
                                    propertyValue,
                                    context);
                                stack[stackIndex - 1] = propertyValue;
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.SetComputedProperty:
                            {
                                var propertyValue = stack[--stackIndex];
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                ApplyProgramComputedPropertyAssignment(
                                    target,
                                    propertyKey,
                                    operation.AllowNameInference,
                                    propertyValue,
                                    context);
                                stack[stackIndex - 1] = propertyValue;
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.SetNamedSuperProperty:
                            {
                                var propertyValue = stack[stackIndex - 1];
                                stack[stackIndex - 1] = ApplyProgramNamedSuperPropertyAssignment(
                                    operation.GetString(stringConstants),
                                    operation.AllowNameInference,
                                    propertyValue,
                                    environment,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.SetComputedSuperProperty:
                            {
                                var propertyValue = stack[--stackIndex];
                                var propertyKey = stack[--stackIndex];
                                stack[stackIndex++] = ApplyProgramComputedSuperPropertyAssignment(
                                    propertyKey,
                                    operation.AllowNameInference,
                                    propertyValue,
                                    environment,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.UpdateIdentifier:
                            {
                                stack[stackIndex++] = ExecuteProgramIdentifierUpdate(
                                    operation,
                                    identifierConstants,
                                    environment,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.UpdateNamedProperty:
                            {
                                stack[stackIndex - 1] = ExecuteProgramNamedPropertyUpdate(
                                    stack[stackIndex - 1],
                                    operation.GetString(stringConstants),
                                    operation,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.UpdateComputedProperty:
                            {
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                stack[stackIndex - 1] = ExecuteProgramComputedPropertyUpdate(
                                    target,
                                    propertyKey,
                                    operation,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.UpdateNamedSuperProperty:
                            {
                                stack[stackIndex++] = ExecuteProgramNamedSuperPropertyUpdate(
                                    operation.GetString(stringConstants),
                                    operation,
                                    environment,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.UpdateComputedSuperProperty:
                            {
                                var propertyKey = stack[--stackIndex];
                                stack[stackIndex++] = ExecuteProgramComputedSuperPropertyUpdate(
                                    propertyKey,
                                    operation,
                                    environment,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.TypeOf:
                            stack[stackIndex - 1] = new JsValue(GetTypeofStringValue(stack[stackIndex - 1]));
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.TypeOfIdentifier:
                            {
                                stack[stackIndex++] = ExecuteProgramTypeOfIdentifier(
                                    operation,
                                    identifierConstants,
                                    environment,
                                    context);
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.DeleteIdentifier:
                            {
                                stack[stackIndex++] = ExecuteProgramDeleteIdentifier(
                                    operation,
                                    identifierConstants,
                                    environment,
                                    context)
                                    ? JsValue.True
                                    : JsValue.False;
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.DeleteNamedProperty:
                            {
                                stack[stackIndex - 1] = ExecuteProgramDeleteNamedProperty(
                                    stack[stackIndex - 1],
                                    operation.GetString(stringConstants),
                                    context)
                                    ? JsValue.True
                                    : JsValue.False;
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.DeleteComputedProperty:
                            {
                                var propertyKey = stack[--stackIndex];
                                var target = stack[stackIndex - 1];
                                stack[stackIndex - 1] = ExecuteProgramDeleteComputedProperty(
                                    target,
                                    propertyKey,
                                    context)
                                    ? JsValue.True
                                    : JsValue.False;
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.UnaryPlus:
                            {
                                var operand = stack[stackIndex - 1];
                                stack[stackIndex - 1] = operand.IsBigInt
                                    ? throw StandardLibrary.ThrowTypeError(
                                        "Cannot convert a BigInt value to a number",
                                        context)
                                    : new JsValue(ToNumberValue(operand, context));
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.UnaryMinus:
                            stack[stackIndex - 1] = NegateValue(stack[stackIndex - 1], context);
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.UnaryBitwiseNot:
                            stack[stackIndex - 1] = BitwiseNotValue(stack[stackIndex - 1], context);
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.UnaryVoid:
                            stack[stackIndex - 1] = JsValue.Undefined;
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.ToString:
                            stack[stackIndex - 1] = new JsValue(JsOps.ToJsString(stack[stackIndex - 1], context));
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.UnaryLogicalNot:
                            stack[stackIndex - 1] = stack[stackIndex - 1].IsTruthy ? JsValue.False : JsValue.True;
                            stackFlags.Set(stackIndex - 1, false);
                            programCounter++;
                            break;

                        case ExpressionOpKind.Binary:
                            {
                                var right = stack[--stackIndex];
                                var left = stack[stackIndex - 1];
                                stack[stackIndex - 1] =
                                    operation.Operator switch
                                    {
                                        BinaryOperator.LessThan or
                                        BinaryOperator.LessThanOrEqual or
                                        BinaryOperator.GreaterThan or
                                        BinaryOperator.GreaterThanOrEqual =>
                                            ProfileBranchCompare(operation.Operator, left, right, context),
                                        _ => ProfileApplyBinaryOperator(operation.Operator, left, right, context)
                                    };
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.PrivateFieldIn:
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

                                var lexeme = $"#{operation.GetString(stringConstants)}";
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
                                stackFlags.Set(stackIndex - 1, false);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.ThrowReferenceError:
                            {
                                throw StandardLibrary.ThrowReferenceError(
                                    operation.GetString(stringConstants), context, context.RealmState);
                            }

                        case ExpressionOpKind.Pop:
                            stackIndex--;
                            programCounter++;
                            break;

                        case ExpressionOpKind.Jump:
                            {
                                programCounter = operation.Target;
                                break;
                            }

                        case ExpressionOpKind.JumpIfNullish:
                            {
                                if (stackFlags.Get(stackIndex - 1) || stack[stackIndex - 1].IsNullish)
                                {
                                    if (operation.ReplaceWithUndefined)
                                    {
                                        stack[stackIndex - 1] = JsValue.Undefined;
                                        stackFlags.Set(stackIndex - 1, true);
                                    }

                                    programCounter = operation.Target;
                                }
                                else
                                {
                                    programCounter++;
                                }
                                break;
                            }

                        case ExpressionOpKind.JumpIfShortCircuited:
                            {
                                programCounter = stackFlags.Get(stackIndex - 1)
                                    ? operation.Target
                                    : programCounter + 1;
                                break;
                            }

                        case ExpressionOpKind.JumpIfTrue:
                            {
                                programCounter = stack[stackIndex - 1].IsTruthy
                                    ? operation.Target
                                    : programCounter + 1;
                                break;
                            }

                        case ExpressionOpKind.JumpIfFalse:
                            {
                                programCounter = !stack[stackIndex - 1].IsTruthy
                                    ? operation.Target
                                    : programCounter + 1;
                                break;
                            }

                        case ExpressionOpKind.JumpIfNotNullish:
                            {
                                programCounter = !stack[stackIndex - 1].IsNullish
                                    ? operation.Target
                                    : programCounter + 1;
                                break;
                            }

                        case ExpressionOpKind.SuperConstruct:
                            {
                                stackIndex = ExecuteProgramSuperConstruct(
                                    operation,
                                    stack,
                                    ref stackFlags,
                                    stackIndex,
                                    spreadMaskConstants,
                                    environment,
                                    context);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.Call:
                            {
                                stackIndex = ExecuteProgramCall(
                                    operation,
                                    stack,
                                    ref stackFlags,
                                    stackIndex,
                                    spreadMaskConstants,
                                    environment,
                                    context);
                                programCounter++;
                                break;
                            }

                        case ExpressionOpKind.Construct:
                            {
                                stackIndex = ExecuteProgramConstruct(
                                    operation,
                                    stack,
                                    ref stackFlags,
                                    stackIndex,
                                    spreadMaskConstants,
                                    context);
                                programCounter++;
                                break;
                            }

                        default:
                            throw new NotSupportedException(
                                $"Unsupported expression op '{operation.Kind}'.");
                    }

                    if (context.ShouldStopEvaluation)
                    {
                        return stackIndex > 0
                            ? stackFlags.Get(stackIndex - 1) ? JsValue.Undefined : stack[stackIndex - 1]
                            : JsValue.Undefined;
                    }
                }

                return stackIndex > 0
                    ? stackFlags.Get(stackIndex - 1) ? JsValue.Undefined : stack[stackIndex - 1]
                    : JsValue.Undefined;
            }
            finally
            {
                if (assignmentReferenceBuffer is not null)
                {
                    assignmentReferenceBuffer.AsSpan(0, assignmentReferenceHighWaterMark).Clear();
                    ArrayPool<AssignmentReference>.Shared.Return(assignmentReferenceBuffer, clearArray: false);
                }

                ReleaseExpressionBuffers(stackBuffer, flagBuffer, stackIndex, rentedFromPool);
            }
        }

        private void AcquireExpressionBuffers(
            int stackSize,
            out JsValue[] stackBuffer,
            out ulong[] flagBuffer,
            out bool rentedFromPool)
        {
            var flagWordCount = GetExpressionFlagWordCount(stackSize);
            if (_expressionBufferLeaseCount == 0)
            {
                EnsureCachedExpressionBufferCapacity(stackSize);
                stackBuffer = _expressionStackBuffer!;
                flagBuffer = _expressionFlagBuffer!;
                rentedFromPool = false;
            }
            else
            {
                stackBuffer = ArrayPool<JsValue>.Shared.Rent(stackSize);
                flagBuffer = ArrayPool<ulong>.Shared.Rent(flagWordCount);
                rentedFromPool = true;
            }

            _expressionBufferLeaseCount++;
        }

        private void EnsureCachedExpressionBufferCapacity(int stackSize)
        {
            if (_expressionStackBuffer is null || _expressionStackBuffer.Length < stackSize)
            {
                if (_expressionStackBuffer is not null)
                {
                    ArrayPool<JsValue>.Shared.Return(_expressionStackBuffer, clearArray: false);
                }

                _expressionStackBuffer = ArrayPool<JsValue>.Shared.Rent(stackSize);
            }

            var flagWordCount = GetExpressionFlagWordCount(stackSize);
            if (_expressionFlagBuffer is null || _expressionFlagBuffer.Length < flagWordCount)
            {
                if (_expressionFlagBuffer is not null)
                {
                    ArrayPool<ulong>.Shared.Return(_expressionFlagBuffer, clearArray: false);
                }

                _expressionFlagBuffer = ArrayPool<ulong>.Shared.Rent(flagWordCount);
            }
        }

        private void ReleaseExpressionBuffers(
            JsValue[] stackBuffer,
            ulong[] flagBuffer,
            int usedLength,
            bool rentedFromPool)
        {
            stackBuffer.AsSpan(0, usedLength).Clear();
            flagBuffer.AsSpan(0, GetExpressionFlagWordCount(usedLength)).Clear();
            _expressionBufferLeaseCount--;

            if (!rentedFromPool)
            {
                return;
            }

            ArrayPool<JsValue>.Shared.Return(stackBuffer, clearArray: false);
            ArrayPool<ulong>.Shared.Return(flagBuffer, clearArray: false);
        }

        private void ReturnCachedExpressionBuffers()
        {
            if (_expressionBufferLeaseCount != 0)
            {
                return;
            }

            if (_expressionStackBuffer is not null)
            {
                ArrayPool<JsValue>.Shared.Return(_expressionStackBuffer, clearArray: false);
                _expressionStackBuffer = null;
            }

            if (_expressionFlagBuffer is not null)
            {
                ArrayPool<ulong>.Shared.Return(_expressionFlagBuffer, clearArray: false);
                _expressionFlagBuffer = null;
            }
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue EvaluateProgramIdentifier(
            Symbol name,
            int scopeId,
            int slotIndex,
            bool isArguments,
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (isArguments)
            {
                return environment.TryGetIdentifierJsValue(name, context, out var argumentsValue)
                    ? argumentsValue
                    : HandleIdentifierNotFound(name, context);
            }

            if (!context.AllowIdentifierCache || environment.HasWithObjectInChain())
            {
                return environment.TryGetIdentifierJsValue(name, context, out var resolvedValue)
                    ? resolvedValue
                    : HandleIdentifierNotFound(name, context);
            }

            if (scopeId >= 0 && slotIndex >= 0)
            {
                if (environment.TryReadIdentifierWithSlot(
                        name,
                        scopeId,
                        slotIndex,
                        context,
                        out var slotValue))
                {
                    return slotValue;
                }
            }

            return environment.TryGetIdentifierJsValue(name, context, out var value)
                ? value
                : HandleIdentifierNotFound(name, context);
        }

        private void LoadProgramIdentifierCallTarget(
            IdentifierOperand identifier,
            bool isArguments,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue receiver,
            out JsValue callee)
        {
            if (!isArguments &&
                !context.AllowIdentifierCache &&
                environment.TryResolveWithBinding(identifier.Name, context, out var withBinding))
            {
                receiver = JsValue.FromObjectUnsafe(withBinding.BindingObject);
                try
                {
                    callee = JsEnvironment.GetWithBindingValueJsValue(withBinding);
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                                                               "ReferenceError:",
                                                               StringComparison.Ordinal))
                {
                    var errorObject = StandardLibrary.CreateReferenceError(
                        ex.Message,
                        context,
                        context.RealmState);
                    context.SetThrow(errorObject);
                    callee = JsValue.Undefined;
                }

                return;
            }

            receiver = JsValue.Undefined;
            callee = environment.TryGetIdentifierJsValueAfterWithMiss(identifier.Name, context, out var value)
                ? value
                : HandleIdentifierNotFound(identifier.Name, context);
        }

        private void ApplyProgramIdentifierAssignment(
            Symbol name,
            int scopeId,
            int slotIndex,
            int flatSlotId,
            bool allowNameInference,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (allowNameInference &&
                value is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(name.Name);
            }

            if (!context.AllowIdentifierCache || environment.HasWithObjectInChain())
            {
                var reference = environment.ResolveIdentifierAssignmentReference(name, context);
                reference.SetValue(value);
                return;
            }

            var variable = FlatSlotAccessor.Create(this, flatSlotId);
            if (variable.UseFlatSlot)
            {
                variable.EnsureAssignable(name, _realmState);
                variable.Variable.Write(value);
                return;
            }

            if (scopeId >= 0 && slotIndex >= 0)
            {
                environment.TryWriteIdentifierWithSlot(
                    name,
                    scopeId,
                    slotIndex,
                    value,
                    context);
                return;
            }

            environment.SetIdentifierJsValue(name, value, context);
        }

        private JsValue ExecuteProgramIdentifierUpdate(
            PackedExpressionOp update,
            ReadOnlySpan<IdentifierOperand> identifierConstants,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var identifier = update.GetIdentifier(identifierConstants);
            if (!context.AllowIdentifierCache || environment.HasWithObjectInChain())
            {
                var reference = environment.ResolveIdentifierAssignmentReference(identifier.Name, context);
                var referencedValue = reference.GetJsValue();
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                GetUpdatedNumericValue(
                    referencedValue,
                    update.IsIncrement,
                    context,
                    out var referencedOldNumericValue,
                    out var referencedNewValue);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                reference.SetValue(referencedNewValue);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                return update.IsPrefix ? referencedNewValue : referencedOldNumericValue;
            }

            var currentValue = EvaluateProgramIdentifier(
                identifier.Name,
                identifier.ScopeId,
                identifier.SlotIndex,
                update.IsArguments,
                environment,
                context);
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
                identifier.Name,
                identifier.ScopeId,
                identifier.SlotIndex,
                identifier.FlatSlotId,
                allowNameInference: false,
                newValue,
                environment,
                context);

            return update.IsPrefix ? newValue : oldNumericValue;
        }

        private static JsValue ExecuteProgramNamedPropertyUpdate(
            JsValue target,
            string propertyName,
            PackedExpressionOp update,
            EvaluationContext context)
        {
            var currentValue = GetProgramNamedPropertyValue(
                target,
                targetWasShortCircuited: false,
                propertyName,
                isOptional: false,
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
                propertyName,
                allowNameInference: false,
                newValue,
                context);

            return update.IsPrefix ? newValue : oldNumericValue;
        }

        private static JsValue ExecuteProgramComputedPropertyUpdate(
            JsValue target,
            JsValue propertyKey,
            PackedExpressionOp update,
            EvaluationContext context)
        {
            if (target.IsNullOrUndefined)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Cannot read properties of null or undefined",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                return JsValue.Undefined;
            }

            // ES spec: ToPropertyKey must be called exactly once for update expressions.
            // Convert the property key to a string/symbol once and reuse it for both get and set.
            var resolvedName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            // Use the resolved name to get the current value
            var currentValue = JsValue.Undefined;
            if (target.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                if (!accessor.TryGetProperty(resolvedName, out currentValue))
                {
                    currentValue = JsValue.Undefined;
                }
            }
            else
            {
                // For primitives, use the standard property lookup with the already-resolved string
                currentValue = GetProgramComputedPropertyValue(
                    target,
                    targetWasShortCircuited: false,
                    new JsValue(resolvedName),
                    context,
                    out _);
            }
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            GetUpdatedNumericValue(currentValue, update.IsIncrement, context, out var oldNumericValue, out var newValue);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            // Use the resolved name to set the new value (no second ToPropertyKey call)
            var handle = PropertyHandle.Resolve(
                target,
                resolvedName,
                context,
                context.CurrentScope.IsStrict,
                allowPrivate: false);
            handle.SetValue(newValue);

            return update.IsPrefix ? newValue : oldNumericValue;
        }

        private JsValue ExecuteProgramNamedSuperPropertyUpdate(
            string propertyName,
            PackedExpressionOp update,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var currentValue = GetProgramNamedSuperPropertyValue(propertyName, environment, context);
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
                propertyName,
                allowNameInference: false,
                newValue,
                environment,
                context);

            return update.IsPrefix ? newValue : oldNumericValue;
        }

        private JsValue ExecuteProgramComputedSuperPropertyUpdate(
            JsValue propertyKey,
            PackedExpressionOp update,
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
                allowNameInference: false,
                newValue,
                environment,
                context);

            return update.IsPrefix ? newValue : oldNumericValue;
        }

        private JsValue ExecuteProgramTypeOfIdentifier(
            PackedExpressionOp identifier,
            ReadOnlySpan<IdentifierOperand> identifierConstants,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var operand = identifier.GetIdentifier(identifierConstants);
            var hasBinding = environment.HasBinding(operand.Name);
            var operandValue = EvaluateProgramIdentifier(
                operand.Name,
                operand.ScopeId,
                operand.SlotIndex,
                identifier.IsArguments,
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
            PackedExpressionOp identifier,
            ReadOnlySpan<IdentifierOperand> identifierConstants,
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

            var outcome = environment.DeleteBinding(identifier.GetIdentifier(identifierConstants).Name);
            return outcome is DeleteBindingResult.Deleted or DeleteBindingResult.NotFound;
        }

        private static bool ExecuteProgramDeleteNamedProperty(
            JsValue target,
            string propertyName,
            EvaluationContext context)
        {
            var handle = PropertyHandle.Resolve(
                target,
                propertyName,
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
            string propertyName,
            PackedExpressionOp defineProperty,
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

        private static void DefineComputedObjectLiteralProperty(
            JsObject targetObject,
            JsValue propertyKey,
            PackedExpressionOp defineProperty,
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

        [MethodImpl(JsEngineConstants.Inlining)]
        private static bool TryPrepareProgramPropertyRead(
            JsValue target,
            bool targetWasShortCircuited,
            bool shortCircuitOnNullishTarget,
            EvaluationContext context,
            out bool resultWasShortCircuited)
        {
            if (targetWasShortCircuited)
            {
                resultWasShortCircuited = true;
                return false;
            }

            if (shortCircuitOnNullishTarget && target.IsNullOrUndefined)
            {
                resultWasShortCircuited = true;
                return false;
            }

            if (target.IsNullOrUndefined)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Cannot read properties of null or undefined",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                resultWasShortCircuited = false;
                return false;
            }

            resultWasShortCircuited = false;
            return true;
        }

        private static JsValue GetProgramNamedPropertyValue(
            JsValue target,
            bool targetWasShortCircuited,
            string propertyName,
            bool isOptional,
            EvaluationContext context,
            out bool resultWasShortCircuited)
        {
            if (!TryPrepareProgramPropertyRead(
                    target,
                    targetWasShortCircuited,
                    isOptional,
                    context,
                    out resultWasShortCircuited))
            {
                return JsValue.Undefined;
            }

            if (!propertyName.IsPrivateName())
            {
                return JsOps.TryGetPropertyValue(target, propertyName, out var directValue, context)
                    ? directValue
                    : JsValue.Undefined;
            }

            var handle = PropertyHandle.Resolve(
                target,
                propertyName,
                context,
                context.CurrentScope.IsStrict,
                allowPrivate: true);
            return handle.GetJsValue();
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static JsValue GetProgramNamedCallTargetValue(
            JsValue target,
            bool targetWasShortCircuited,
            string propertyName,
            EvaluationContext context,
            out bool resultWasShortCircuited)
        {
            if (!TryPrepareProgramPropertyRead(
                    target,
                    targetWasShortCircuited,
                    shortCircuitOnNullishTarget: false,
                    context,
                    out resultWasShortCircuited))
            {
                return JsValue.Undefined;
            }

            if (!propertyName.IsPrivateName())
            {
                return JsOps.TryGetPropertyValue(target, propertyName, out var directValue, context)
                    ? directValue
                    : JsValue.Undefined;
            }

            var handle = PropertyHandle.Resolve(
                target,
                propertyName,
                context,
                context.CurrentScope.IsStrict,
                allowPrivate: true);
            return handle.GetJsValue();
        }

        private static JsValue GetProgramComputedPropertyValue(
            JsValue target,
            bool targetWasShortCircuited,
            JsValue propertyKey,
            EvaluationContext context,
            out bool resultWasShortCircuited)
        {
            if (!TryPrepareProgramPropertyRead(
                    target,
                    targetWasShortCircuited,
                    shortCircuitOnNullishTarget: false,
                    context,
                    out resultWasShortCircuited))
            {
                return JsValue.Undefined;
            }

            return JsOps.TryGetPropertyValueJsValue(target, propertyKey, out var directValue, context)
                ? directValue
                : JsValue.Undefined;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static JsValue GetProgramComputedCallTargetValue(
            JsValue target,
            bool targetWasShortCircuited,
            JsValue propertyKey,
            EvaluationContext context,
            out bool resultWasShortCircuited)
        {
            if (!TryPrepareProgramPropertyRead(
                    target,
                    targetWasShortCircuited,
                    shortCircuitOnNullishTarget: false,
                    context,
                    out resultWasShortCircuited))
            {
                return JsValue.Undefined;
            }

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
            string propertyName,
            bool allowNameInference,
            JsValue value,
            EvaluationContext context)
        {
            if (allowNameInference &&
                value is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(propertyName);
            }

            var handle = PropertyHandle.Resolve(target, propertyName, context, context.CurrentScope.IsStrict);
            handle.SetValue(value);
        }

        private static void ApplyProgramComputedPropertyAssignment(
            JsValue target,
            JsValue propertyKey,
            bool allowNameInference,
            JsValue value,
            EvaluationContext context)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            if (allowNameInference &&
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
            string propertyName,
            bool allowNameInference,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (allowNameInference &&
                value is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(propertyName);
            }

            return AssignToSuperBinding(environment, context, propertyName, value, "property");
        }

        private static JsValue ApplyProgramComputedSuperPropertyAssignment(
            JsValue propertyKey,
            bool allowNameInference,
            JsValue value,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            if (allowNameInference &&
                value is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(propertyName);
            }

            return AssignToSuperBinding(environment, context, propertyName, value, "index");
        }

        private int ExecuteProgramCall(
            PackedExpressionOp call,
            Span<JsValue> stack,
            ref ExpressionFlagStack stackFlags,
            int stackIndex,
            ReadOnlySpan<ImmutableArray<int>> spreadMaskConstants,
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
                var calleeDescription = calleeValue.IsUndefined
                    ? "undefined"
                    : calleeValue.IsNull
                        ? "null"
                        : JsOps.ToJsString(calleeValue);
                var error = StandardLibrary.CreateTypeError(
                    $"Attempted to call a non-callable value '{calleeDescription}' of type '{calleeValue.Kind}'.",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                stack[baseIndex] = JsValue.Undefined;
                stackFlags.Set(baseIndex, false);
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
                stackFlags.Set(baseIndex, false);
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
                    evalHost.IsDirectCall = call.IsDirectEval &&
                                            ReferenceEquals(evalHostFunction.Engine, environment.RealmState?.Engine);
                    evalHost.InClassFieldInitializer = context.InClassFieldInitializer;
                }

                if (callable is DebugAwareHostFunction debugAware)
                {
                    debugFunction = debugAware;
                    debugFunction.CurrentJsEnvironment = environment;
                    debugFunction.CurrentContext = context;
                }

                if (call.SpreadMaskConstantIndex < 0)
                {
                    switch (call.ArgumentCount)
                    {
                        case 0:
                            result = InvokeCallableNoArgs(callable, thisValue, context, environment);
                            break;

                        case 1:
                            result = InvokeCallableSingleArg(
                                callable,
                                stack[calleeIndex + 1],
                                thisValue,
                                context,
                                environment);
                            break;

                        default:
                            var arguments = MaterializeProgramArguments(
                                call.ArgumentCount,
                                default,
                                stack,
                                calleeIndex + 1,
                                context,
                                out pooledArguments);
                            result = InvokeCallableJsValue(callable, arguments, thisValue, context, environment);
                            break;
                    }
                }
                else
                {
                    var arguments = MaterializeProgramArguments(
                        call.ArgumentCount,
                        call.GetSpreadIndices(spreadMaskConstants),
                        stack,
                        calleeIndex + 1,
                        context,
                        out pooledArguments);
                    result = InvokeCallableJsValue(callable, arguments, thisValue, context, environment);
                }
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
            stackFlags.Set(baseIndex, false);
            return baseIndex + 1;
        }

        private int ExecuteProgramConstruct(
            PackedExpressionOp construct,
            Span<JsValue> stack,
            ref ExpressionFlagStack stackFlags,
            int stackIndex,
            ReadOnlySpan<ImmutableArray<int>> spreadMaskConstants,
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
                stackFlags.Set(constructorIndex, false);
                return constructorIndex + 1;
            }

            JsValue[]? pooledArguments = null;

            try
            {
                var arguments = MaterializeProgramArguments(
                    construct.ArgumentCount,
                    construct.GetSpreadIndices(spreadMaskConstants),
                    stack,
                    constructorIndex + 1,
                    context,
                    out pooledArguments);

                stack[constructorIndex] = global::Asynkron.JsEngine.StdLib.ReflectHelper.Construct(
                    callable,
                    arguments,
                    callable,
                    context.RealmState);
                stackFlags.Set(constructorIndex, false);
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
                stack[constructorIndex] = signal.ThrownValue;
                stackFlags.Set(constructorIndex, false);
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
            PackedExpressionOp superConstruct,
            Span<JsValue> stack,
            ref ExpressionFlagStack stackFlags,
            int stackIndex,
            ReadOnlySpan<ImmutableArray<int>> spreadMaskConstants,
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
                    thisInitializationEnvironment = thisEnv.ResolveConstructorThisEnvironment();
                    if (thisInitializationEnvironment.TryGetJsValue(Symbol.ThisInitialized, out var initValue))
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
                    superConstruct.GetSpreadIndices(spreadMaskConstants),
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
                    stackFlags.Set(baseIndex, false);
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
                if (!ReferenceEquals(environment, targetEnvironment))
                {
                    environment.AssignJsValue(Symbol.This, initializedThis);
                }

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
                        stackFlags.Set(baseIndex, false);
                        return baseIndex + 1;
                    }
                }

                stack[baseIndex] = result;
                stackFlags.Set(baseIndex, false);
                return baseIndex + 1;
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
                stack[baseIndex] = signal.ThrownValue;
                stackFlags.Set(baseIndex, false);
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
            ImmutableArray<int> spreadIndices,
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

            if (!spreadIndices.IsDefaultOrEmpty)
            {
                var spreadArguments = ImmutableArray.CreateBuilder<JsValue>(argumentCount);
                var spreadIndexPosition = 0;
                for (var i = 0; i < argumentCount; i++)
                {
                    var argumentValue = stack[firstArgumentIndex + i];
                    if (spreadIndexPosition < spreadIndices.Length &&
                        spreadIndices[spreadIndexPosition] == i)
                    {
                        spreadArguments.AddRange(EnumerateSpread(argumentValue, context));
                        spreadIndexPosition++;
                    }
                    else
                    {
                        spreadArguments.Add(argumentValue);
                    }
                }

                return spreadArguments.ToImmutable();
            }

            var usePooledArray = argumentCount <= 4;
            var argumentArray = usePooledArray
                ? global::Asynkron.JsEngine.JsValueCache.RentJsValueArray(argumentCount)
                : new JsValue[argumentCount];

            if (usePooledArray)
            {
                pooledArguments = argumentArray;
            }

            switch (argumentCount)
            {
                case 1:
                    argumentArray[0] = stack[firstArgumentIndex];
                    return argumentArray;

                case 2:
                    argumentArray[0] = stack[firstArgumentIndex];
                    argumentArray[1] = stack[firstArgumentIndex + 1];
                    return argumentArray;

                case 3:
                    argumentArray[0] = stack[firstArgumentIndex];
                    argumentArray[1] = stack[firstArgumentIndex + 1];
                    argumentArray[2] = stack[firstArgumentIndex + 2];
                    return argumentArray;

                case 4:
                    argumentArray[0] = stack[firstArgumentIndex];
                    argumentArray[1] = stack[firstArgumentIndex + 1];
                    argumentArray[2] = stack[firstArgumentIndex + 2];
                    argumentArray[3] = stack[firstArgumentIndex + 3];
                    return argumentArray;
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

            if (!context.AllowIdentifierCache || environment.HasWithObjectInChain())
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
