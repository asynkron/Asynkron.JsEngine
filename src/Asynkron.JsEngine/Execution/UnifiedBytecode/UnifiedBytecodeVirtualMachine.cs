using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeVirtualMachine
{
    private const int DefineObjectPropertyPrototypeMutationFlag = 1;
    private const int DefineObjectPropertyAllowNameInferenceFlag = 2;
    private const int DefineObjectPropertyKnownNewPropertyFlag = 4;

    private readonly record struct EnvironmentScopeFrame(
        JsEnvironment Environment,
        ImmutableArray<int> SlotIndices,
        JsEnvironment?[] PreviousSlotEnvironments);

    public static JsValue Execute(
        UnifiedBytecodeProgram program,
        Span<JsValue> slots,
        EvaluationContext context,
        JsEnvironment? callingEnvironment = null,
        JsValue thisValue = default,
        JsValue newTarget = default,
        bool isStrict = false)
    {
        var stack = new JsValue[Math.Max(program.MaxStackDepth, 2)];
        var stackPointer = 0;
        var currentCallingEnvironment = callingEnvironment;
        var slotEnvironments = callingEnvironment is null
            ? null
            : InitializeSlotEnvironments(program, callingEnvironment);
        EnvironmentScopeFrame[]? environmentStack = null;
        var environmentStackCount = 0;

        var programCounter = 0;
        var instructions = program.Instructions;
        while ((uint)programCounter < (uint)instructions.Length)
        {
            var instruction = instructions[programCounter];
            switch (instruction.OpCode)
            {
                case UnifiedBytecodeOpCode.LoadSlot:
                    var slotValue = slots[instruction.Operand];
                    if (slotValue.IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, instruction.Operand, context);
                        return JsValue.Undefined;
                    }

                    stack[stackPointer++] = slotValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadThis:
                    stack[stackPointer++] = thisValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadNewTarget:
                    stack[stackPointer++] = newTarget;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadLiteral:
                    stack[stackPointer++] = program.LiteralConstants[instruction.Operand];
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PrepareIdentifierCallTarget:
                    var callTarget = program.CallTargetConstants[instruction.Operand];
                    if (callTarget.Kind != UnifiedBytecodeCallTargetKind.Identifier)
                    {
                        throw new InvalidOperationException(
                            "Identifier call-target preparation requires an identifier call target constant.");
                    }

                    var callableValue = slots[callTarget.SlotIndex];
                    if (callableValue.IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, callTarget.SlotIndex, context);
                        return JsValue.Undefined;
                    }

                    stack[stackPointer++] = JsValue.Undefined;
                    stack[stackPointer++] = callableValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PrepareNamedCallTarget:
                    var namedCallTarget = program.CallTargetConstants[instruction.Operand];
                    if (namedCallTarget.Kind != UnifiedBytecodeCallTargetKind.NamedMember ||
                        (uint)namedCallTarget.NameConstantIndex >= (uint)program.StringConstants.Length)
                    {
                        throw new InvalidOperationException(
                            "Named member call-target preparation requires a named member call target constant.");
                    }

                    var namedReceiver = stack[stackPointer - 1];
                    stack[stackPointer++] = GetNamedPropertyValue(
                        namedReceiver,
                        program.StringConstants[namedCallTarget.NameConstantIndex],
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PrepareComputedCallTarget:
                    var computedCallTarget = program.CallTargetConstants[instruction.Operand];
                    if (computedCallTarget.Kind != UnifiedBytecodeCallTargetKind.ComputedMember)
                    {
                        throw new InvalidOperationException(
                            "Computed member call-target preparation requires a computed member call target constant.");
                    }

                    var computedCallKey = stack[--stackPointer];
                    var computedCallReceiver = stack[stackPointer - 1];
                    stack[stackPointer++] = GetComputedCallTargetValue(computedCallReceiver, computedCallKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.CallInvocationBoundary:
                    stackPointer = ExecutePreparedCall(
                        instruction.Operand,
                        stack,
                        stackPointer,
                        context,
                        currentCallingEnvironment);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.StoreSlot:
                    var storedValue = stack[--stackPointer];
                    slots[instruction.Operand] = storedValue;
                    SyncSlotEnvironment(slotEnvironments, instruction.Operand, storedValue);

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Binary:
                    var op = (BinaryOperator)instruction.Operand;
                    var right = stack[--stackPointer];
                    var left = stack[--stackPointer];
                    stack[stackPointer++] = ApplyBinaryOperator(op, left, right, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.RequireObjectCoercible:
                    var checkIndex = stackPointer - 1 - instruction.Operand;
                    if (stack[checkIndex].IsNullOrUndefined)
                    {
                        context.SetThrow(StandardLibrary.CreateTypeError(
                            "Cannot read properties of null or undefined",
                            context,
                            context.RealmState));
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ResolvePropertyKey:
                    stack[stackPointer - 1] = ResolvePropertyKey(stack[stackPointer - 1], context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetNamedProperty:
                    stack[stackPointer - 1] = GetNamedPropertyValue(
                        stack[stackPointer - 1],
                        program.StringConstants[instruction.Operand],
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetComputedProperty:
                    var propertyKey = stack[--stackPointer];
                    var target = stack[stackPointer - 1];
                    stack[stackPointer - 1] = JsOps.TryGetPropertyValueJsValue(target, propertyKey, out var computedValue, context)
                        ? computedValue
                        : JsValue.Undefined;
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet:
                    var namedCompoundTarget = stack[stackPointer - 1];
                    stack[stackPointer++] = GetNamedPropertyValue(
                        namedCompoundTarget,
                        program.StringConstants[instruction.Operand],
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet:
                    var computedCompoundKey = stack[stackPointer - 1];
                    var computedCompoundTarget = stack[stackPointer - 2];
                    stack[stackPointer++] = JsOps.TryGetPropertyValueJsValue(
                            computedCompoundTarget,
                            computedCompoundKey,
                            out var computedCompoundValue,
                            context)
                        ? computedCompoundValue
                        : JsValue.Undefined;
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SetNamedProperty:
                    var namedPropertyValue = stack[--stackPointer];
                    var namedSetTarget = stack[stackPointer - 1];
                    SetPropertyValue(
                        namedSetTarget,
                        program.StringConstants[instruction.Operand],
                        namedPropertyValue,
                        context,
                        isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    stack[stackPointer - 1] = namedPropertyValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SetComputedProperty:
                    var computedPropertyValue = stack[--stackPointer];
                    var computedSetKey = stack[--stackPointer];
                    var computedSetTarget = stack[stackPointer - 1];
                    var computedSetName = JsOps.GetRequiredPropertyName(computedSetKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    SetPropertyValue(computedSetTarget, computedSetName, computedPropertyValue, context, isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    stack[stackPointer - 1] = computedPropertyValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateNamedProperty:
                    var namedUpdateTarget = stack[stackPointer - 1];
                    stack[stackPointer - 1] = UpdatePropertyValue(
                        namedUpdateTarget,
                        program.StringConstants[DecodeStringOperand(instruction.Operand)],
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        context,
                        isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateComputedProperty:
                    var computedUpdateKey = stack[--stackPointer];
                    var computedUpdateTarget = stack[stackPointer - 1];
                    var computedUpdateName = JsOps.GetRequiredPropertyName(computedUpdateKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    stack[stackPointer - 1] = UpdatePropertyValue(
                        computedUpdateTarget,
                        computedUpdateName,
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        context,
                        isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TypeOf:
                    stack[stackPointer - 1] = new JsValue(GetTypeofStringValue(stack[stackPointer - 1]));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TypeOfIdentifier:
                    var typeOfValue = slots[instruction.Operand];
                    if (typeOfValue.IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, instruction.Operand, context);
                        return JsValue.Undefined;
                    }

                    stack[stackPointer++] = new JsValue(GetTypeofStringValue(typeOfValue));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryPlus:
                    var plusOperand = stack[stackPointer - 1];
                    stack[stackPointer - 1] = new JsValue(JsOps.ToNumber(in plusOperand, context));
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryMinus:
                    stack[stackPointer - 1] = TypedAstEvaluator.NegateValue(stack[stackPointer - 1], context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryLogicalNot:
                    stack[stackPointer - 1] = stack[stackPointer - 1].IsTruthy ? JsValue.False : JsValue.True;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryBitwiseNot:
                    stack[stackPointer - 1] = TypedAstEvaluator.BitwiseNot(stack[stackPointer - 1], context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryVoid:
                    stack[stackPointer - 1] = JsValue.Undefined;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ToString:
                    stack[stackPointer - 1] = new JsValue(JsOps.ToJsString(stack[stackPointer - 1], context));
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Pop:
                    stackPointer--;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.CreateArray:
                    stack[stackPointer++] = JsValue.FromJsArray(new JsArray(context.RealmState));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ArrayPush:
                    var arrayElementValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetArray(out var targetArray))
                    {
                        throw new InvalidOperationException("Array push unified bytecode op requires an array receiver.");
                    }

                    targetArray.Push(arrayElementValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ArrayPushHole:
                    if (!stack[stackPointer - 1].TryGetArray(out var targetArrayWithHole))
                    {
                        throw new InvalidOperationException("Array hole unified bytecode op requires an array receiver.");
                    }

                    targetArrayWithHole.PushHole();
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.CreateObject:
                    var targetObject = new JsObject
                    {
                        RealmState = context.RealmState
                    };
                    if (context.RealmState.ObjectPrototype is { } objectPrototype)
                    {
                        targetObject.SetPrototype(objectPrototype);
                    }

                    stack[stackPointer++] = JsValue.FromJsObject(targetObject);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineObjectProperty:
                    var propertyValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var objectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Object property unified bytecode op requires an object receiver.");
                    }

                    DefineObjectLiteralProperty(
                        objectLiteralTarget,
                        program.StringConstants[DecodeDefineObjectPropertyNameOperand(instruction.Operand)],
                        instruction.Operand,
                        propertyValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineComputedObjectProperty:
                    var computedObjectPropertyValue = stack[--stackPointer];
                    var computedObjectPropertyKey = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var computedObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Computed object property unified bytecode op requires an object receiver.");
                    }

                    var computedObjectPropertyName = JsOps.GetRequiredPropertyName(computedObjectPropertyKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    DefineComputedObjectLiteralProperty(
                        computedObjectLiteralTarget,
                        computedObjectPropertyName,
                        instruction.Operand,
                        computedObjectPropertyValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Jump:
                    programCounter = instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.JumpIfFalse:
                    programCounter = stack[--stackPointer].IsTruthy
                        ? programCounter + 1
                        : instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.PushEnvironment:
                    var scopeDescriptor = program.ScopeDescriptors[instruction.Operand];
                    var lexicalSlotIndices = scopeDescriptor.LexicalSlotIndices;
                    for (var i = 0; i < lexicalSlotIndices.Length; i++)
                    {
                        slots[lexicalSlotIndices[i]] = JsValue.Uninitialized;
                    }

                    if (currentCallingEnvironment is not null && slotEnvironments is not null)
                    {
                        var scopeEnvironment = CreateScopeEnvironment(
                            program,
                            scopeDescriptor,
                            lexicalSlotIndices,
                            currentCallingEnvironment,
                            context,
                            isStrict);
                        var previousSlotEnvironments = new JsEnvironment?[lexicalSlotIndices.Length];
                        for (var i = 0; i < lexicalSlotIndices.Length; i++)
                        {
                            var slotIndex = lexicalSlotIndices[i];
                            previousSlotEnvironments[i] = slotEnvironments[slotIndex];
                            slotEnvironments[slotIndex] = scopeEnvironment;
                        }

                        environmentStack ??= new EnvironmentScopeFrame[instructions.Length];
                        environmentStack[environmentStackCount++] = new EnvironmentScopeFrame(
                            scopeEnvironment,
                            lexicalSlotIndices,
                            previousSlotEnvironments);
                        currentCallingEnvironment = scopeEnvironment;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PopEnvironment:
                    if (environmentStackCount > 0 && slotEnvironments is not null)
                    {
                        var scopeFrame = environmentStack![--environmentStackCount];
                        RestoreSlotEnvironmentOwners(slotEnvironments, scopeFrame);
                        currentCallingEnvironment = scopeFrame.Environment.Enclosing ?? currentCallingEnvironment;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.IteratorInit:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        var iterableValue = stack[--stackPointer];
                        var iteratorState = CreateIteratorDriverState(iterableValue, descriptor.IteratorKind, context);
                        var iteratorStateValue = iteratorState.AsJsValue;
                        slots[descriptor.StateSlot] = iteratorStateValue;
                        SyncSlotEnvironment(slotEnvironments, descriptor.StateSlot, iteratorStateValue);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.IteratorMoveNext:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        if (!TryMoveIteratorNext(
                                descriptor,
                                slots,
                                slotEnvironments,
                                currentCallingEnvironment,
                                context,
                                out var nextProgramCounter))
                        {
                            return JsValue.Undefined;
                        }

                        programCounter = nextProgramCounter;
                        break;
                    }

                case UnifiedBytecodeOpCode.IteratorClose:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        CloseIteratorDriverState(descriptor.StateSlot, slots, slotEnvironments, context, false);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ForInInit:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        var objectValue = stack[--stackPointer];
                        var forInState = ForInDriverStatePool.Rent();
                        forInState.SourceObject = objectValue;
                        CollectEnumerablePropertyKeys(objectValue, forInState.PropertyKeys);
                        var forInStateValue = forInState.AsJsValue;
                        slots[descriptor.StateSlot] = forInStateValue;
                        SyncSlotEnvironment(slotEnvironments, descriptor.StateSlot, forInStateValue);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ForInMoveNext:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        programCounter = MoveForInNext(
                            descriptor,
                            slots,
                            slotEnvironments);
                        break;
                    }

                case UnifiedBytecodeOpCode.ArrayDestructuringInit:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        var sourceValue = stack[--stackPointer];
                        if (!TryGetIteratorForArrayDestructuring(sourceValue, context, out var state))
                        {
                            CleanupActiveDriverStates(slots, slotEnvironments, context, true);
                            return JsValue.Undefined;
                        }

                        slots[descriptor.StateSlot] = JsValue.FromObjectUnsafe(state);
                        SyncSlotEnvironment(slotEnvironments, descriptor.StateSlot, slots[descriptor.StateSlot]);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ArrayDestructuringElement:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        if (!TryReadArrayDestructuringNext(
                                descriptor.StateSlot,
                                slots,
                                slotEnvironments,
                                context,
                                out var value))
                        {
                            return JsValue.Undefined;
                        }

                        if (descriptor.TargetSlot >= 0)
                        {
                            slots[descriptor.TargetSlot] = value;
                            SyncSlotEnvironment(slotEnvironments, descriptor.TargetSlot, value);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ArrayDestructuringRest:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        if (!TryReadArrayDestructuringRest(
                                descriptor.StateSlot,
                                slots,
                                slotEnvironments,
                                context,
                                out var restValue))
                        {
                            return JsValue.Undefined;
                        }

                        slots[descriptor.TargetSlot] = restValue;
                        SyncSlotEnvironment(slotEnvironments, descriptor.TargetSlot, restValue);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ArrayDestructuringClose:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        CloseArrayDestructuringState(descriptor.StateSlot, slots, slotEnvironments, context, false);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.Return:
                    var result = stack[stackPointer - 1];
                    CleanupActiveDriverStates(slots, slotEnvironments, context, false);
                    return context.ShouldStopEvaluation ? JsValue.Undefined : result;

                case UnifiedBytecodeOpCode.ReturnUndefined:
                    CleanupActiveDriverStates(slots, slotEnvironments, context, false);
                    return JsValue.Undefined;

                case UnifiedBytecodeOpCode.Throw:
                    context.SetThrow(stack[--stackPointer]);
                    CleanupActiveDriverStates(slots, slotEnvironments, context, true);
                    return JsValue.Undefined;

                default:
                    throw new InvalidOperationException($"Unsupported unified opcode '{instruction.OpCode}'.");
            }
        }

        throw new InvalidOperationException("Program terminated without Return.");
    }

    private static JsEnvironment?[] InitializeSlotEnvironments(
        UnifiedBytecodeProgram program,
        JsEnvironment callingEnvironment)
    {
        var slotCount = program.SlotCount;
        var slotEnvironments = new JsEnvironment?[slotCount];
        var rootSlotCount = Math.Min(slotCount, callingEnvironment.SlotCount);
        for (var i = 0; i < rootSlotCount; i++)
        {
            if (SlotNameMatchesEnvironment(program, callingEnvironment, i))
            {
                slotEnvironments[i] = callingEnvironment;
            }
        }

        return slotEnvironments;
    }

    private static bool SlotNameMatchesEnvironment(
        UnifiedBytecodeProgram program,
        JsEnvironment environment,
        int slotIndex)
    {
        var slotNames = program.SlotNames;
        if (slotNames.IsDefaultOrEmpty ||
            (uint)slotIndex >= (uint)slotNames.Length ||
            slotNames[slotIndex] is not { } expectedName)
        {
            return false;
        }

        return string.Equals(
            environment.GetSlotByIndex(slotIndex).Name?.Name,
            expectedName,
            StringComparison.Ordinal);
    }

    private static void SyncSlotEnvironment(
        JsEnvironment?[]? slotEnvironments,
        int slotIndex,
        JsValue value)
    {
        if (slotEnvironments is null ||
            (uint)slotIndex >= (uint)slotEnvironments.Length ||
            slotEnvironments[slotIndex] is not { } environment ||
            (uint)slotIndex >= (uint)environment.SlotCount)
        {
            return;
        }

        environment.SetSlotDirect(slotIndex, value);
    }

    private static JsEnvironment CreateScopeEnvironment(
        UnifiedBytecodeProgram program,
        UnifiedBytecodeScopeDescriptor scopeDescriptor,
        ImmutableArray<int> lexicalSlotIndices,
        JsEnvironment enclosing,
        EvaluationContext context,
        bool isStrict)
    {
        var scopeEnvironment = JsEnvironment.CreateInstance(
            enclosing,
            isFunctionScope: false,
            isStrict: isStrict || context.CurrentScope.IsStrict,
            description: "unified-bytecode-scope");
        scopeEnvironment.InitializeSlots(GetScopeSlotCount(lexicalSlotIndices), scopeDescriptor.ScopeId);
        SetScopeSlotNames(scopeEnvironment, program, lexicalSlotIndices);
        scopeEnvironment.SetSlotsLexicalUninitialized(lexicalSlotIndices);
        return scopeEnvironment;
    }

    private static int GetScopeSlotCount(ImmutableArray<int> lexicalSlotIndices)
    {
        var slotCount = 0;
        for (var i = 0; i < lexicalSlotIndices.Length; i++)
        {
            slotCount = Math.Max(slotCount, lexicalSlotIndices[i] + 1);
        }

        return slotCount;
    }

    private static void SetScopeSlotNames(
        JsEnvironment environment,
        UnifiedBytecodeProgram program,
        ImmutableArray<int> lexicalSlotIndices)
    {
        var slotNames = program.SlotNames;
        if (slotNames.IsDefaultOrEmpty)
        {
            return;
        }

        var builder = ImmutableArray.CreateBuilder<(Symbol Name, int SlotIndex)>(lexicalSlotIndices.Length);
        for (var i = 0; i < lexicalSlotIndices.Length; i++)
        {
            var slotIndex = lexicalSlotIndices[i];
            if ((uint)slotIndex < (uint)slotNames.Length && slotNames[slotIndex] is { } slotName)
            {
                builder.Add((Symbol.Intern(slotName), slotIndex));
            }
        }

        environment.SetSlotNames(builder.ToImmutable());
    }

    private static void RestoreSlotEnvironmentOwners(
        JsEnvironment?[] slotEnvironments,
        EnvironmentScopeFrame scopeFrame)
    {
        var slotIndices = scopeFrame.SlotIndices;
        var previousSlotEnvironments = scopeFrame.PreviousSlotEnvironments;
        for (var i = 0; i < slotIndices.Length; i++)
        {
            slotEnvironments[slotIndices[i]] = previousSlotEnvironments[i];
        }
    }

    private static IteratorDriverState CreateIteratorDriverState(
        JsValue iterable,
        IteratorDriverKind kind,
        EvaluationContext context)
    {
        var fastEnumerator = TypedAstEvaluator.TryGetFastEnumeratorForIteration(iterable);
        if (fastEnumerator is not null)
        {
            return new IteratorDriverState
            {
                Enumerator = fastEnumerator,
                IsAsyncIterator = kind == IteratorDriverKind.Await
            };
        }

        var iteratorTarget = TypedAstEvaluator.NormalizeIterableTarget(iterable, context);
        if (!TypedAstEvaluator.TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) ||
            iterator is null)
        {
            throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
        }

        return new IteratorDriverState
        {
            IteratorObject = iterator,
            IsAsyncIterator = kind == IteratorDriverKind.Await,
            NextMethod = iterator.GetIteratorNextCallable(context)
        };
    }

    private static bool TryMoveIteratorNext(
        UnifiedBytecodeDriverDescriptor descriptor,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        JsEnvironment? callingEnvironment,
        EvaluationContext context,
        out int programCounter)
    {
        if (!TryGetDriverState<IteratorDriverState>(slots, descriptor.StateSlot, out var state))
        {
            programCounter = descriptor.BreakTarget;
            return true;
        }

        try
        {
            if (!TryReadIteratorNextValue(state, context, callingEnvironment, out var value, out var done))
            {
                programCounter = descriptor.BreakTarget;
                return true;
            }

            if (done)
            {
                CompleteIteratorDriverState(descriptor.StateSlot, slots, slotEnvironments, state);
                programCounter = descriptor.BreakTarget;
                return true;
            }

            state.HasEnteredLoop = true;
            slots[descriptor.ValueSlot] = value;
            SyncSlotEnvironment(slotEnvironments, descriptor.ValueSlot, value);
            programCounter = descriptor.NextTarget;
            return true;
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            programCounter = descriptor.BreakTarget;
            return false;
        }
    }

    private static bool TryReadIteratorNextValue(
        IteratorDriverState state,
        EvaluationContext context,
        JsEnvironment? callingEnvironment,
        out JsValue value,
        out bool done)
    {
        if (state.IteratorObject is { } iterator)
        {
            state.NextMethod ??= iterator.GetIteratorNextCallable(context);
            var nextResult = iterator.InvokeIteratorNext(
                state.NextMethod,
                context: context,
                callingEnvironment: callingEnvironment);
            if (!nextResult.TryGetObject<IJsPropertyAccessor>(out var resultObject))
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    "Iterator result is not an object",
                    context,
                    context.RealmState));
            }

            done = resultObject.TryGetProperty("done", out var doneValue) &&
                   JsOps.ToBoolean(doneValue);
            if (done)
            {
                value = JsValue.Undefined;
            }
            else
            {
                value = resultObject.TryGetProperty("value", out var yielded)
                    ? yielded
                    : JsValue.Undefined;
            }

            if (resultObject is IteratorResultObject poolableResult)
            {
                IteratorResultObjectPool.Return(poolableResult);
            }

            return true;
        }

        if (state.Enumerator is { } enumerator)
        {
            if (!enumerator.MoveNext())
            {
                value = JsValue.Undefined;
                done = true;
                return true;
            }

            value = enumerator.Current;
            done = false;
            return true;
        }

        value = JsValue.Undefined;
        done = true;
        return true;
    }

    private static int MoveForInNext(
        UnifiedBytecodeDriverDescriptor descriptor,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments)
    {
        if (!TryGetDriverState<ForInDriverState>(slots, descriptor.StateSlot, out var state))
        {
            return descriptor.BreakTarget;
        }

        while (state.CurrentIndex < state.PropertyKeys.Count)
        {
            var currentKey = state.PropertyKeys[state.CurrentIndex++];
            if (!PropertyStillExists(state.SourceObject, currentKey))
            {
                continue;
            }

            slots[descriptor.ValueSlot] = currentKey;
            SyncSlotEnvironment(slotEnvironments, descriptor.ValueSlot, currentKey);
            return descriptor.NextTarget;
        }

        CompleteForInDriverState(descriptor.StateSlot, slots, slotEnvironments, state);
        return descriptor.BreakTarget;
    }

    private static bool TryGetDriverState<TState>(
        Span<JsValue> slots,
        int slotIndex,
        out TState state)
        where TState : class
    {
        if ((uint)slotIndex < (uint)slots.Length &&
            slots[slotIndex].TryGetObject<TState>(out var candidate))
        {
            state = candidate;
            return true;
        }

        state = null!;
        return false;
    }

    private static void CompleteIteratorDriverState(
        int slotIndex,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        IteratorDriverState state)
    {
        state.MarkIteratorClosed();
        if (state.Enumerator is IDisposable disposable)
        {
            disposable.Dispose();
        }

        ClearDriverSlot(slotIndex, slots, slotEnvironments);
    }

    private static void CloseIteratorDriverState(
        int slotIndex,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        bool preserveExistingThrow)
    {
        if (!TryGetDriverState<IteratorDriverState>(slots, slotIndex, out var state))
        {
            return;
        }

        if (!state.IteratorClosed &&
            state.IteratorObject is { } iterator &&
            state.HasEnteredLoop)
        {
            try
            {
                iterator.IteratorClose(context, preserveExistingThrow);
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
            }
        }

        CompleteIteratorDriverState(slotIndex, slots, slotEnvironments, state);
    }

    private static void CompleteForInDriverState(
        int slotIndex,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        ForInDriverState state)
    {
        ForInDriverStatePool.Return(state);
        ClearDriverSlot(slotIndex, slots, slotEnvironments);
    }

    private static void ClearDriverSlot(
        int slotIndex,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments)
    {
        if ((uint)slotIndex >= (uint)slots.Length)
        {
            return;
        }

        slots[slotIndex] = JsValue.Undefined;
        SyncSlotEnvironment(slotEnvironments, slotIndex, JsValue.Undefined);
    }

    private static void CleanupActiveDriverStates(
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        bool preserveExistingThrow)
    {
        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            if (slots[slotIndex].TryGetObject<IteratorDriverState>(out var iteratorState))
            {
                CloseIteratorDriverState(slotIndex, slots, slotEnvironments, context, preserveExistingThrow);
                continue;
            }

            if (slots[slotIndex].TryGetObject<ForInDriverState>(out var forInState))
            {
                CompleteForInDriverState(slotIndex, slots, slotEnvironments, forInState);
                continue;
            }

            if (slots[slotIndex].TryGetObject<UnifiedArrayDestructuringState>(out _))
            {
                CloseArrayDestructuringState(slotIndex, slots, slotEnvironments, context, preserveExistingThrow);
            }
        }
    }

    private static void CollectEnumerablePropertyKeys(JsValue value, List<JsValue> keys)
    {
        if (value.IsNull || value.IsUndefined)
        {
            return;
        }

        switch (value.Kind)
        {
            case JsValueKind.Object when value.ObjectValue is JsArray array:
                CollectArrayPropertyKeys(array, keys);
                break;

            case JsValueKind.Object when value.ObjectValue is TypedArrayBase typedArray:
                CollectTypedArrayPropertyKeys(typedArray, keys);
                break;

            case JsValueKind.String when value.ObjectValue is string text:
                CollectStringPropertyKeys(text, keys);
                break;

            case JsValueKind.Object when value.ObjectValue is IJsObjectLike accessor:
                CollectObjectPropertyKeys(accessor, keys);
                break;
        }
    }

    private static void CollectArrayPropertyKeys(JsArray array, List<JsValue> keys)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < array.Items.Count; i++)
        {
            var indexKey = i.ToString(CultureInfo.InvariantCulture);
            seenKeys.Add(indexKey);
            if (array.GetOwnPropertyDescriptor(indexKey) is { Enumerable: false })
            {
                continue;
            }

            keys.Add(JsValue.FromString(indexKey));
        }

        CollectEnumerablePropertyKeysFromPrototypeChain(array, seenKeys, keys, skipLength: true);
    }

    private static void CollectTypedArrayPropertyKeys(TypedArrayBase typedArray, List<JsValue> keys)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in typedArray.GetOwnPropertyNames().ToList())
        {
            if (!seenKeys.Add(key))
            {
                continue;
            }

            if (typedArray.GetOwnPropertyDescriptor(key) is null or { Enumerable: false })
            {
                continue;
            }

            keys.Add(JsValue.FromString(key));
        }
    }

    private static void CollectStringPropertyKeys(string text, List<JsValue> keys)
    {
        for (var i = 0; i < text.Length; i++)
        {
            keys.Add(JsValue.FromString(JsValueCache.GetIndexString(i)));
        }
    }

    private static void CollectObjectPropertyKeys(IJsObjectLike accessor, List<JsValue> keys)
    {
        CollectEnumerablePropertyKeysFromPrototypeChain(
            accessor,
            new HashSet<string>(StringComparer.Ordinal),
            keys,
            skipLength: false);
    }

    private static void CollectEnumerablePropertyKeysFromPrototypeChain(
        IJsPropertyAccessor accessor,
        HashSet<string> seenKeys,
        List<JsValue> keys,
        bool skipLength)
    {
        IJsPropertyAccessor? current = accessor;
        while (current is not null)
        {
            foreach (var key in current.GetOwnPropertyNames().ToList())
            {
                if (!seenKeys.Add(key) ||
                    (skipLength && string.Equals(key, "length", StringComparison.Ordinal)))
                {
                    continue;
                }

                if (current.GetOwnPropertyDescriptor(key) is null or { Enumerable: false })
                {
                    continue;
                }

                keys.Add(JsValue.FromString(key));
            }

            current = current switch
            {
                IJsObjectLike objectLike when objectLike.Prototype is not null => objectLike.Prototype,
                IPrototypeAccessorProvider provider when provider.PrototypeAccessor is not null =>
                    provider.PrototypeAccessor,
                IJsObjectLike objectLike2 when objectLike2 is IPrototypeAccessorProvider provider2 =>
                    provider2.PrototypeAccessor,
                _ => null
            };
        }
    }

    private static bool PropertyStillExists(JsValue sourceObject, JsValue key)
    {
        if (sourceObject.ObjectValue is not IJsObjectLike obj)
        {
            return true;
        }

        var keyText = key.IsString && key.ObjectValue is string text ? text : key.ToString();
        IJsPropertyAccessor? current = obj;
        while (current is not null)
        {
            var descriptor = current.GetOwnPropertyDescriptor(keyText);
            if (descriptor is not null)
            {
                return descriptor is not { Enumerable: false };
            }

            current = current switch
            {
                IJsObjectLike objectLike when objectLike.Prototype is not null => objectLike.Prototype,
                IPrototypeAccessorProvider provider when provider.PrototypeAccessor is not null =>
                    provider.PrototypeAccessor,
                _ => null
            };
        }

        return false;
    }

    private sealed class UnifiedArrayDestructuringState : IDisposable
    {
        public IJsObjectLike? Iterator;
        public IEnumerator<JsValue>? Enumerator;
        public IJsCallable? NextMethod;
        public bool Done;
        private bool _disposed;

        public (JsValue Value, bool Done) Next(EvaluationContext context)
        {
            if (Done)
            {
                return (JsValue.Undefined, true);
            }

            if (Iterator is null)
            {
                if (Enumerator?.MoveNext() != true)
                {
                    Done = true;
                    return (JsValue.Undefined, true);
                }

                return (Enumerator.Current, false);
            }

            NextMethod ??= Iterator.GetIteratorNextCallable(context);
            if (Iterator is JsArrayIterator arrayIterator &&
                arrayIterator.TryNextValueFast(NextMethod, context, out var fastValue, out var fastDone))
            {
                Done = fastDone;
                return fastDone ? (JsValue.Undefined, true) : (fastValue, false);
            }

            var candidate = Iterator.InvokeIteratorNext(NextMethod, context: context);
            if (candidate.TryGetObject<IteratorResultObject>(out var iteratorResult))
            {
                iteratorResult.Deconstruct(out var resultValue, out var resultDone);
                IteratorResultObjectPool.Return(iteratorResult);
                Done = resultDone;
                return resultDone ? (JsValue.Undefined, true) : (resultValue, false);
            }

            if (!candidate.TryGetObject<IJsObjectLike>(out var result))
            {
                throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
            }

            var done =
                JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(result), "done", out var doneValue, context) &&
                JsOps.ToBoolean(doneValue);
            if (done)
            {
                Done = true;
                return (JsValue.Undefined, true);
            }

            var value = JsOps.TryGetPropertyValue(
                    JsValue.FromObjectUnsafe(result),
                    "value",
                    out var yieldedValue,
                    context)
                ? yieldedValue
                : JsValue.Undefined;

            return (value, false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Enumerator?.Dispose();
            Enumerator = null;
        }
    }

    private static bool TryGetIteratorForArrayDestructuring(
        JsValue sourceValue,
        EvaluationContext context,
        out UnifiedArrayDestructuringState state)
    {
        if (!TypedAstEvaluator.TryGetIteratorForDestructuring(sourceValue, context, out var iterator, out var enumerator))
        {
            if (context.ShouldStopEvaluation)
            {
                state = null!;
                return false;
            }

            context.SetThrow(StandardLibrary.CreateTypeError(
                "Cannot destructure non-iterable value.",
                context,
                context.RealmState));
            state = null!;
            return false;
        }

        state = new UnifiedArrayDestructuringState
        {
            Iterator = iterator,
            Enumerator = enumerator
        };
        return true;
    }

    private static bool TryReadArrayDestructuringNext(
        int stateSlot,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        out JsValue value)
    {
        if (!TryGetDriverState<UnifiedArrayDestructuringState>(slots, stateSlot, out var state))
        {
            throw new InvalidOperationException("Array destructuring state not found.");
        }

        try
        {
            (value, _) = state.Next(context);
            if (!context.ShouldStopEvaluation)
            {
                return true;
            }
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
        }

        CloseArrayDestructuringState(stateSlot, slots, slotEnvironments, context, true);
        value = JsValue.Undefined;
        return false;
    }

    private static bool TryReadArrayDestructuringRest(
        int stateSlot,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        out JsValue restValue)
    {
        if (!TryGetDriverState<UnifiedArrayDestructuringState>(slots, stateSlot, out var state))
        {
            throw new InvalidOperationException("Array destructuring state not found.");
        }

        var restArray = new JsArray(context.RealmState);
        try
        {
            while (true)
            {
                var (value, done) = state.Next(context);
                if (context.ShouldStopEvaluation)
                {
                    break;
                }

                if (done)
                {
                    restValue = JsValue.FromObjectUnsafe(restArray);
                    return true;
                }

                restArray.Push(value);
            }
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
        }

        CloseArrayDestructuringState(stateSlot, slots, slotEnvironments, context, true);
        restValue = JsValue.Undefined;
        return false;
    }

    private static void CloseArrayDestructuringState(
        int slotIndex,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        bool preserveExistingThrow)
    {
        if (!TryGetDriverState<UnifiedArrayDestructuringState>(slots, slotIndex, out var state))
        {
            return;
        }

        if (state.Iterator is not null && !state.Done)
        {
            try
            {
                state.Iterator.IteratorClose(context, preserveExistingThrow);
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
            }
        }

        state.Dispose();
        ClearDriverSlot(slotIndex, slots, slotEnvironments);
    }

    private static JsValue ApplyBinaryOperator(
        BinaryOperator op,
        in JsValue left,
        in JsValue right,
        EvaluationContext context)
    {
        return op switch
        {
            BinaryOperator.Add => TypedAstEvaluator.AddValue(left, right, context),
            BinaryOperator.Subtract => TypedAstEvaluator.SubtractValue(left, right, context),
            BinaryOperator.Multiply => TypedAstEvaluator.MultiplyValue(left, right, context),
            BinaryOperator.Divide => TypedAstEvaluator.DivideValue(left, right, context),
            BinaryOperator.Modulo => TypedAstEvaluator.ModuloValue(left, right, context),
            BinaryOperator.Equal => JsOps.LooseEquals(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictEqual => JsOps.StrictEquals(left, right) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictNotEqual => JsOps.StrictEquals(left, right) ? JsValue.False : JsValue.True,
            BinaryOperator.LessThan => JsOps.LessThan(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.LessThanOrEqual => JsOps.LessThanOrEqual(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.GreaterThan => JsOps.GreaterThan(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.GreaterThanOrEqual => JsOps.GreaterThanOrEqual(left, right, context) ? JsValue.True : JsValue.False,
            _ => throw new InvalidOperationException($"Unsupported unified binary operator '{op}'.")
        };
    }

    private static int ExecutePreparedCall(
        int argumentCount,
        Span<JsValue> stack,
        int stackPointer,
        EvaluationContext context,
        JsEnvironment? callingEnvironment)
    {
        var calleeIndex = stackPointer - argumentCount - 1;
        var receiverIndex = calleeIndex - 1;
        var baseIndex = receiverIndex;
        var calleeValue = stack[calleeIndex];
        var thisValue = stack[receiverIndex];

        if (!calleeValue.TryGetObject<IJsCallable>(out var callable))
        {
            var calleeDescription = calleeValue.IsUndefined
                ? "undefined"
                : calleeValue.IsNull
                    ? "null"
                    : JsOps.ToJsString(calleeValue);
            context.SetThrow(StandardLibrary.CreateTypeError(
                $"Attempted to call a non-callable value '{calleeDescription}' of type '{calleeValue.Kind}'.",
                context,
                context.RealmState));
            stack[baseIndex] = JsValue.Undefined;
            return baseIndex + 1;
        }

        if (callable is TypedAstEvaluator.SyncFunctionInvoker { IsClassConstructor: true } classConstructor)
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Class constructor cannot be invoked without 'new'",
                context,
                classConstructor.RealmState));
            stack[baseIndex] = JsValue.Undefined;
            return baseIndex + 1;
        }

        if (++context.CallDepth > context.MaxCallDepth)
        {
            context.CallDepth--;
            throw new InvalidOperationException(
                $"Exceeded maximum call depth of {context.MaxCallDepth}.");
        }

        JsValue[]? pooledArguments = null;
        DebugAwareHostFunction? debugFunction = null;
        JsEnvironment? previousDebugEnvironment = null;
        EvaluationContext? previousDebugContext = null;
        JsValue result;
        try
        {
            if (callingEnvironment is not null && callable is DebugAwareHostFunction debugAware)
            {
                debugFunction = debugAware;
                previousDebugEnvironment = debugFunction.CurrentJsEnvironment;
                previousDebugContext = debugFunction.CurrentContext;
                debugFunction.CurrentJsEnvironment = callingEnvironment;
                debugFunction.CurrentContext = context;
            }

            result = argumentCount switch
            {
                0 => TypedAstEvaluator.InvokeCallableNoArgs(callable, thisValue, context, callingEnvironment),
                1 => TypedAstEvaluator.InvokeCallableSingleArg(
                    callable,
                    stack[calleeIndex + 1],
                    thisValue,
                    context,
                    callingEnvironment),
                2 => TypedAstEvaluator.InvokeCallableTwoArgs(
                    callable,
                    stack[calleeIndex + 1],
                    stack[calleeIndex + 2],
                    thisValue,
                    context,
                    callingEnvironment),
                _ => TypedAstEvaluator.InvokeCallableJsValue(
                    callable,
                    MaterializeCallArguments(argumentCount, stack, calleeIndex + 1, out pooledArguments),
                    thisValue,
                    context,
                    callingEnvironment)
            };
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            result = signal.ThrownValue;
        }
        finally
        {
            if (debugFunction is not null)
            {
                debugFunction.CurrentJsEnvironment = previousDebugEnvironment;
                debugFunction.CurrentContext = previousDebugContext;
            }

            if (pooledArguments is not null)
            {
                JsValueCache.ReturnJsValueArray(pooledArguments);
            }

            context.CallDepth--;
        }

        stack[baseIndex] = result;
        return baseIndex + 1;
    }

    private static IReadOnlyList<JsValue> MaterializeCallArguments(
        int argumentCount,
        Span<JsValue> stack,
        int firstArgumentIndex,
        out JsValue[] pooledArguments)
    {
        pooledArguments = JsValueCache.RentJsValueArray(argumentCount);
        for (var argumentIndex = 0; argumentIndex < argumentCount; argumentIndex++)
        {
            pooledArguments[argumentIndex] = stack[firstArgumentIndex + argumentIndex];
        }

        return pooledArguments;
    }

    private static string GetTypeofStringValue(in JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "object",
            JsValueKind.Boolean => "boolean",
            JsValueKind.Number => "number",
            JsValueKind.BigInt => "bigint",
            JsValueKind.String => "string",
            JsValueKind.Symbol => "symbol",
            JsValueKind.Object => GetTypeofStringForObject(value.ObjectValue),
            _ => "undefined"
        };
    }

    private static void SetUninitializedSlotReferenceError(
        UnifiedBytecodeProgram program,
        int slotIndex,
        EvaluationContext context)
    {
        var slotName = GetSlotName(program, slotIndex);
        var message = slotName is null
            ? "Cannot access lexical binding before initialization"
            : $"Cannot access '{slotName}' before initialization";
        context.SetThrow(StandardLibrary.CreateReferenceError(message, context, context.RealmState));
    }

    private static string? GetSlotName(UnifiedBytecodeProgram program, int slotIndex)
    {
        var slotNames = program.SlotNames;
        return !slotNames.IsDefaultOrEmpty && (uint)slotIndex < (uint)slotNames.Length
            ? slotNames[slotIndex]
            : null;
    }

    private static string GetTypeofStringForObject(object? value)
    {
        if (value is IIsHtmlDda)
        {
            return "undefined";
        }

        if (value is JsProxy proxy)
        {
            return proxy.IsCallableTarget() ? "function" : "object";
        }

        return value is IJsCallable ? "function" : "object";
    }

    private static JsValue ResolvePropertyKey(JsValue propertyKey, EvaluationContext context)
    {
        var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
        return context.ShouldStopEvaluation ? JsValue.Undefined : new JsValue(propertyName);
    }

    private static JsValue GetNamedPropertyValue(JsValue target, string propertyName, EvaluationContext context)
    {
        if (target.IsNullOrUndefined)
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Cannot read properties of null or undefined",
                context,
                context.RealmState));
            return JsValue.Undefined;
        }

        return JsOps.TryGetPropertyValue(target, propertyName, out var directValue, context)
            ? directValue
            : JsValue.Undefined;
    }

    private static JsValue GetComputedCallTargetValue(
        JsValue target,
        JsValue propertyKey,
        EvaluationContext context)
    {
        if (target.IsNullOrUndefined)
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Cannot read properties of null or undefined",
                context,
                context.RealmState));
            return JsValue.Undefined;
        }

        return JsOps.TryGetPropertyValueJsValue(target, propertyKey, out var directValue, context)
            ? directValue
            : JsValue.Undefined;
    }

    private static void SetPropertyValue(
        JsValue target,
        string propertyName,
        JsValue propertyValue,
        EvaluationContext context,
        bool isStrict)
    {
        var handle = PropertyHandle.Resolve(
            target,
            propertyName,
            context,
            context.CurrentScope.IsStrict || isStrict,
            allowPrivate: false);
        handle.SetValue(propertyValue);
    }

    private static void DefineObjectLiteralProperty(
        JsObject targetObject,
        string propertyName,
        int operand,
        JsValue propertyValue)
    {
        if (DecodeDefineObjectPropertyIsPrototypeMutation(operand))
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

        if (DecodeDefineObjectPropertyAllowNameInference(operand))
        {
            throw new InvalidOperationException(
                "Object literal name inference is not supported by unified bytecode.");
        }

        if (DecodeDefineObjectPropertyIsKnownNewProperty(operand))
        {
            targetObject.DefineKnownNewDefaultDataProperty(propertyName, propertyValue);
            return;
        }

        targetObject.DefineDefaultDataProperty(propertyName, propertyValue);
    }

    private static void DefineComputedObjectLiteralProperty(
        JsObject targetObject,
        string propertyName,
        int operand,
        JsValue propertyValue)
    {
        if (DecodeDefineObjectPropertyAllowNameInference(operand))
        {
            throw new InvalidOperationException(
                "Computed object literal name inference is not supported by unified bytecode.");
        }

        targetObject.DefineDefaultDataProperty(propertyName, propertyValue);
    }

    private static JsValue UpdatePropertyValue(
        JsValue target,
        string propertyName,
        bool isIncrement,
        bool isPrefix,
        EvaluationContext context,
        bool isStrict)
    {
        var handle = PropertyHandle.Resolve(
            target,
            propertyName,
            context,
            context.CurrentScope.IsStrict || isStrict,
            allowPrivate: false);
        var currentValue = handle.GetJsValue();
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        GetUpdatedNumericValue(
            currentValue,
            isIncrement,
            context,
            out var oldNumericValue,
            out var newValue);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        handle.SetValue(newValue);
        return isPrefix ? newValue : oldNumericValue;
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
            : JsOps.ToNumericAsJsValue(in currentValue, context);
        if (context.ShouldStopEvaluation)
        {
            oldNumericValue = JsValue.Undefined;
            newValue = JsValue.Undefined;
            return;
        }

        oldNumericValue = numericValue;
        newValue = isIncrement
            ? IncrementValue(numericValue)
            : DecrementValue(numericValue);
    }

    private static JsValue IncrementValue(in JsValue value)
    {
        if (value.IsNumber)
        {
            return JsValue.FromDouble(value.NumberValue + 1.0);
        }

        if (value.IsBigInt)
        {
            return new JsValue(new JsBigInt(value.AsBigInt().Value + BigInteger.One));
        }

        return JsValue.NaN;
    }

    private static JsValue DecrementValue(in JsValue value)
    {
        if (value.IsNumber)
        {
            return JsValue.FromDouble(value.NumberValue - 1.0);
        }

        if (value.IsBigInt)
        {
            return new JsValue(new JsBigInt(value.AsBigInt().Value - BigInteger.One));
        }

        return JsValue.NaN;
    }

    private static int DecodeStringOperand(int operand) => operand >> 2;

    private static int DecodeDefineObjectPropertyNameOperand(int operand) => operand >> 3;

    private static bool DecodeDefineObjectPropertyIsPrototypeMutation(int operand) =>
        (operand & DefineObjectPropertyPrototypeMutationFlag) != 0;

    private static bool DecodeDefineObjectPropertyAllowNameInference(int operand) =>
        (operand & DefineObjectPropertyAllowNameInferenceFlag) != 0;

    private static bool DecodeDefineObjectPropertyIsKnownNewProperty(int operand) =>
        (operand & DefineObjectPropertyKnownNewPropertyFlag) != 0;

    private static bool DecodeIsIncrement(int operand) => (operand & 1) != 0;

    private static bool DecodeIsPrefix(int operand) => (operand & 2) != 0;
}
