using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeVirtualMachine
{
    private const int DefineObjectPropertyPrototypeMutationFlag = 1;
    private const int DefineObjectPropertyAllowNameInferenceFlag = 2;
    private const int DefineObjectPropertyKnownNewPropertyFlag = 4;
    private const int DeclarationBindingTargetHasInitializerFlag = 8;
    private const int DeclarationBindingTargetShift = 4;

    private readonly record struct EnvironmentScopeFrame(
        JsEnvironment Environment,
        ImmutableArray<int> SlotIndices,
        JsEnvironment?[] PreviousSlotEnvironments);

    private readonly record struct ActiveDriverSlot(int SlotIndex, int Ordinal);

    private enum AbruptKind
    {
        None,
        Return,
        Throw,
        Break,
        Continue
    }

    private readonly record struct PendingCompletion(
        AbruptKind Kind,
        JsValue Value,
        int Target,
        int ResumeTarget,
        bool OriginatedInFinally)
    {
        public static PendingCompletion None { get; } =
            new(AbruptKind.None, JsValue.Undefined, -1, -1, false);

        public static PendingCompletion FromNormal(int resumeTarget) =>
            new(AbruptKind.None, JsValue.Undefined, -1, resumeTarget, false);

        public static PendingCompletion FromValue(
            AbruptKind kind,
            JsValue value,
            bool originatedInFinally = false) =>
            new(kind, value, -1, -1, originatedInFinally);

        public static PendingCompletion FromTarget(
            AbruptKind kind,
            int target,
            bool originatedInFinally = false) =>
            new(kind, JsValue.Undefined, target, -1, originatedInFinally);
    }

    private sealed class TryFrame(
        UnifiedBytecodeTryDescriptor descriptor,
        JsEnvironment? entryEnvironment,
        int entryEnvironmentStackCount)
    {
        public UnifiedBytecodeTryDescriptor Descriptor { get; } = descriptor;
        public JsEnvironment? EntryEnvironment { get; } = entryEnvironment;
        public int EntryEnvironmentStackCount { get; } = entryEnvironmentStackCount;
        public bool CatchUsed { get; set; }
        public bool FinallyScheduled { get; set; }
        public PendingCompletion PendingCompletion { get; set; } = PendingCompletion.None;
        public JsValue ThrownValue { get; set; } = JsValue.Undefined;
        public UnifiedBytecodeCatchDescriptor? ActiveCatchDescriptor { get; set; }
    }

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
        AssignmentReference[]? dynamicIdentifierReferences = null;
        var dynamicIdentifierReferenceCount = 0;
        bool[]? inactiveCatchBindingSlots = null;
        Stack<TryFrame>? tryStack = null;
        var nextActiveDriverOrdinal = 0;

        var programCounter = 0;
        var instructions = program.Instructions;
        bool TryHandleCurrentContextThrow(Span<JsValue> currentSlots)
        {
            if (!HandleContextThrow(
                context,
                program,
                tryStack,
                currentSlots,
                ref programCounter,
                ref currentCallingEnvironment,
                slotEnvironments,
                ref environmentStack,
                ref environmentStackCount))
            {
                return false;
            }

            stackPointer = 0;
            return true;
        }

        while ((uint)programCounter < (uint)instructions.Length)
        {
            var instruction = instructions[programCounter];
            try
            {
                switch (instruction.OpCode)
                {
                case UnifiedBytecodeOpCode.LoadSlot:
                    if (IsInactiveCatchBindingSlot(inactiveCatchBindingSlots, instruction.Operand))
                    {
                        SetInactiveCatchBindingReferenceError(program, instruction.Operand, context);
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    var slotValue = slots[instruction.Operand];
                    if (slotValue.IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, instruction.Operand, context);
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    stack[stackPointer++] = slotValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadDynamicIdentifier:
                    var dynamicLoadEnvironment = RequireDynamicEnvironment(currentCallingEnvironment);
                    stack[stackPointer++] = GetDynamicIdentifierValue(
                        program.StringConstants[instruction.Operand],
                        dynamicLoadEnvironment,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return JsValue.Undefined;
                    }

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

                case UnifiedBytecodeOpCode.LoadImportMeta:
                    stack[stackPointer++] = GetImportMeta(currentCallingEnvironment, context);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadTemplateObject:
                    stack[stackPointer++] = JsValue.FromJsArray(GetOrCreateTemplateObject(
                        program.TemplateObjectConstants[instruction.Operand],
                        context));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadLiteral:
                    stack[stackPointer++] = program.LiteralConstants[instruction.Operand];
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadRegexLiteral:
                    stack[stackPointer++] = JsValue.FromObjectUnsafe(
                        RegExpHelper.CreateRegExpLiteral(
                            program.StringConstants[DecodeRegexLiteralPatternOperand(instruction.Operand)],
                            DecodeRegexLiteralFlagsOperand(instruction.Operand),
                            context.RealmState));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PrepareIdentifierCallTarget:
                    var callTarget = program.CallTargetConstants[instruction.Operand];
                    if (callTarget.Kind != UnifiedBytecodeCallTargetKind.Identifier)
                    {
                        throw new InvalidOperationException(
                            "Identifier call-target preparation requires an identifier call target constant.");
                    }

                    if (IsInactiveCatchBindingSlot(inactiveCatchBindingSlots, callTarget.SlotIndex))
                    {
                        SetInactiveCatchBindingReferenceError(program, callTarget.SlotIndex, context);
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    var callableValue = slots[callTarget.SlotIndex];
                    if (callableValue.IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, callTarget.SlotIndex, context);
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    stack[stackPointer++] = JsValue.Undefined;
                    stack[stackPointer++] = callableValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget:
                    var dynamicCallEnvironment = RequireDynamicEnvironment(currentCallingEnvironment);
                    PrepareDynamicIdentifierCallTarget(
                        program.StringConstants[instruction.Operand],
                        dynamicCallEnvironment,
                        stack,
                        ref stackPointer,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return JsValue.Undefined;
                    }

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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget:
                {
                    var optNamedCallTargetIdx = instruction.Operand & 0xFFFF;
                    var optNamedJumpTarget = instruction.Operand >> 16;
                    var optNamedCallTarget = program.CallTargetConstants[optNamedCallTargetIdx];

                    if (optNamedCallTarget.IsOptionalReceiverCheck)
                    {
                        // Case 1: box?.read() — check receiver; if nullish, short-circuit to undefined.
                        var optReceiver = stack[stackPointer - 1];
                        if (optReceiver.IsNullOrUndefined)
                        {
                            stack[stackPointer - 1] = JsValue.Undefined;
                            programCounter = optNamedJumpTarget;
                            break;
                        }

                        stack[stackPointer++] = GetNamedPropertyValue(
                            optReceiver,
                            program.StringConstants[optNamedCallTarget.NameConstantIndex],
                            context);
                        if (context.ShouldStopEvaluation)
                        {
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                        }
                    }
                    else
                    {
                        // Case 2: box.read?.() — load method; if nullish, short-circuit to undefined.
                        var calleeReceiver = stack[stackPointer - 1];
                        var callee = GetNamedPropertyValue(
                            calleeReceiver,
                            program.StringConstants[optNamedCallTarget.NameConstantIndex],
                            context);
                        if (context.ShouldStopEvaluation)
                        {
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                        }

                        if (callee.IsNullOrUndefined)
                        {
                            stack[stackPointer - 1] = JsValue.Undefined;
                            programCounter = optNamedJumpTarget;
                            break;
                        }

                        stack[stackPointer++] = callee;
                    }

                    programCounter++;
                    break;
                }

                case UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget:
                {
                    // Case 3: box[key]?.() — pop key, load method; if nullish, short-circuit to undefined.
                    var optComputedCallTargetIdx = instruction.Operand & 0xFFFF;
                    var optComputedJumpTarget = instruction.Operand >> 16;
                    _ = program.CallTargetConstants[optComputedCallTargetIdx];

                    var optComputedKey = stack[--stackPointer];
                    var optComputedReceiver = stack[stackPointer - 1];
                    var optComputedCallee = GetComputedCallTargetValue(optComputedReceiver, optComputedKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    if (optComputedCallee.IsNullOrUndefined)
                    {
                        stack[stackPointer - 1] = JsValue.Undefined;
                        programCounter = optComputedJumpTarget;
                        break;
                    }

                    stack[stackPointer++] = optComputedCallee;
                    programCounter++;
                    break;
                }

                case UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget:
                    PrepareNamedSuperCallTarget(
                        program,
                        instruction.Operand,
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        stack,
                        ref stackPointer,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget:
                    PrepareComputedSuperCallTarget(
                        program,
                        instruction.Operand,
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        stack,
                        ref stackPointer,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.CallInvocationBoundary:
                    stackPointer = ExecutePreparedCall(
                        DecodeCallBoundaryArgumentCount(instruction.Operand),
                        DecodeCallBoundarySpreadMask(program, instruction.Operand),
                        DecodeCallBoundaryIsDirectEval(instruction.Operand),
                        stack,
                        stackPointer,
                        slots,
                        slotEnvironments,
                        context,
                        currentCallingEnvironment);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ConstructInvocationBoundary:
                    stackPointer = ExecutePreparedConstruct(
                        DecodeCallBoundaryArgumentCount(instruction.Operand),
                        DecodeCallBoundarySpreadMask(program, instruction.Operand),
                        stack,
                        stackPointer,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SuperConstructInvocationBoundary:
                    stackPointer = ExecutePreparedSuperConstruct(
                        instruction.Operand,
                        stack,
                        stackPointer,
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.StoreSlot:
                    if (slots[instruction.Operand].IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, instruction.Operand, context);
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    var storedValue = stack[--stackPointer];
                    slots[instruction.Operand] = storedValue;
                    SyncSlotEnvironment(slotEnvironments, instruction.Operand, storedValue);

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateSlot:
                    var updateSlotIndex = DecodeUpdateIndex(instruction.Operand);
                    if (IsInactiveCatchBindingSlot(inactiveCatchBindingSlots, updateSlotIndex))
                    {
                        SetInactiveCatchBindingReferenceError(program, updateSlotIndex, context);
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    var updateSlotValue = slots[updateSlotIndex];
                    if (updateSlotValue.IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, updateSlotIndex, context);
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    GetUpdatedNumericValue(
                        updateSlotValue,
                        DecodeIsIncrement(instruction.Operand),
                        context,
                        out var oldSlotNumericValue,
                        out var newSlotValue);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    slots[updateSlotIndex] = newSlotValue;
                    SyncSlotEnvironment(slotEnvironments, updateSlotIndex, newSlotValue);
                    stack[stackPointer++] = DecodeIsPrefix(instruction.Operand) ? newSlotValue : oldSlotNumericValue;

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.InitializeSlot:
                    var initializedValue = stack[--stackPointer];
                    slots[instruction.Operand] = initializedValue;
                    SyncSlotEnvironment(slotEnvironments, instruction.Operand, initializedValue);

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DeclareDynamicVar:
                    DeclareDynamicVar(
                        program.StringConstants[instruction.Operand],
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.StoreDynamicIdentifier:
                    var dynamicStoredValue = stack[stackPointer - 1];
                    StoreDynamicIdentifierValue(
                        program.StringConstants[DecodeDynamicStoreNameOperand(instruction.Operand)],
                        DecodeDynamicStoreAllowsNameInference(instruction.Operand),
                        dynamicStoredValue,
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference:
                    dynamicIdentifierReferences ??= new AssignmentReference[instructions.Length];
                    dynamicIdentifierReferences[dynamicIdentifierReferenceCount++] =
                        RequireDynamicEnvironment(currentCallingEnvironment)
                            .ResolveIdentifierAssignmentReference(
                                Symbol.Intern(program.StringConstants[instruction.Operand]),
                                context);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadDynamicIdentifierReference:
                    if (dynamicIdentifierReferenceCount == 0 || dynamicIdentifierReferences is null)
                    {
                        throw new InvalidOperationException(
                            "Unified bytecode attempted to load a missing dynamic identifier reference.");
                    }

                    stack[stackPointer++] =
                        dynamicIdentifierReferences[dynamicIdentifierReferenceCount - 1].GetJsValue();
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.StoreDynamicIdentifierReference:
                    if (dynamicIdentifierReferenceCount == 0 || dynamicIdentifierReferences is null)
                    {
                        throw new InvalidOperationException(
                            "Unified bytecode attempted to store through a missing dynamic identifier reference.");
                    }

                    var dynamicReferenceValue = stack[stackPointer - 1];
                    var dynamicReferenceName = program.StringConstants[DecodeDynamicStoreNameOperand(instruction.Operand)];
                    if (DecodeDynamicStoreAllowsNameInference(instruction.Operand) &&
                        dynamicReferenceValue is
                            { Kind: JsValueKind.Object, ObjectValue: TypedAstEvaluator.IFunctionNameTarget nameTarget })
                    {
                        nameTarget.EnsureHasName(dynamicReferenceName);
                    }

                    dynamicIdentifierReferences[--dynamicIdentifierReferenceCount].SetValue(dynamicReferenceValue);
                    dynamicIdentifierReferences[dynamicIdentifierReferenceCount] = default;
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PopDynamicIdentifierReference:
                    if (dynamicIdentifierReferenceCount == 0 || dynamicIdentifierReferences is null)
                    {
                        throw new InvalidOperationException(
                            "Unified bytecode attempted to pop a missing dynamic identifier reference.");
                    }

                    dynamicIdentifierReferences[--dynamicIdentifierReferenceCount] = default;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Binary:
                    var op = (BinaryOperator)instruction.Operand;
                    var right = stack[--stackPointer];
                    var left = stack[--stackPointer];
                    stack[stackPointer++] = ApplyBinaryOperator(op, left, right, context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ResolvePropertyKey:
                    stack[stackPointer - 1] = ResolvePropertyKey(stack[stackPointer - 1], context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetNamedPropertyOptional:
                    if (stack[stackPointer - 1].IsNullOrUndefined)
                    {
                        stack[stackPointer - 1] = JsValue.Undefined;
                        programCounter++;
                        break;
                    }

                    stack[stackPointer - 1] = GetNamedPropertyValue(
                        stack[stackPointer - 1],
                        program.StringConstants[instruction.Operand],
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined:
                    if (stack[stackPointer - 1].IsNullOrUndefined)
                    {
                        stack[stackPointer - 1] = JsValue.Undefined;
                        programCounter = instruction.Operand;
                    }
                    else
                    {
                        programCounter++;
                    }

                    break;

                case UnifiedBytecodeOpCode.GetComputedProperty:
                    var propertyKey = stack[--stackPointer];
                    var target = stack[stackPointer - 1];
                    stack[stackPointer - 1] = JsOps.TryGetPropertyValueJsValue(target, propertyKey, out var computedValue, context)
                        ? computedValue
                        : JsValue.Undefined;
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.EnsureSuperReference:
                    if (!EnsureSuperReference(RequireDynamicEnvironment(currentCallingEnvironment), context))
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetNamedSuperProperty:
                    stack[stackPointer++] = GetNamedSuperPropertyValue(
                        program.StringConstants[instruction.Operand],
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetComputedSuperProperty:
                    var computedSuperKey = stack[--stackPointer];
                    stack[stackPointer++] = GetComputedSuperPropertyValue(
                        computedSuperKey,
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    SetPropertyValue(computedSetTarget, computedSetName, computedPropertyValue, context, isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    stack[stackPointer - 1] = computedPropertyValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SetNamedSuperProperty:
                    var namedSuperPropertyValue = stack[stackPointer - 1];
                    stack[stackPointer - 1] = SetNamedSuperPropertyValue(
                        program.StringConstants[DecodeDynamicStoreNameOperand(instruction.Operand)],
                        DecodeDynamicStoreAllowsNameInference(instruction.Operand),
                        namedSuperPropertyValue,
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context,
                        isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SetComputedSuperProperty:
                    var computedSuperPropertyValue = stack[--stackPointer];
                    var computedSuperSetKey = stack[--stackPointer];
                    stack[stackPointer++] = SetComputedSuperPropertyValue(
                        computedSuperSetKey,
                        DecodeDynamicStoreAllowsNameInference(instruction.Operand),
                        computedSuperPropertyValue,
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context,
                        isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateNamedSuperProperty:
                    stack[stackPointer++] = UpdateNamedSuperPropertyValue(
                        program.StringConstants[DecodeStringOperand(instruction.Operand)],
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context,
                        isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateComputedSuperProperty:
                    var computedSuperUpdateKey = stack[--stackPointer];
                    stack[stackPointer++] = UpdateComputedSuperPropertyValue(
                        computedSuperUpdateKey,
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context,
                        isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateComputedProperty:
                    var computedUpdateKey = stack[--stackPointer];
                    var computedUpdateTarget = stack[stackPointer - 1];
                    if (computedUpdateTarget.IsNullOrUndefined)
                    {
                        var error = StandardLibrary.CreateTypeError(
                            "Cannot read properties of null or undefined",
                            context,
                            context.RealmState);
                        context.SetThrow(error);
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    var computedUpdateName = JsOps.GetRequiredPropertyName(computedUpdateKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateDynamicIdentifier:
                    stack[stackPointer++] = UpdateDynamicIdentifierValue(
                        program.StringConstants[DecodeStringOperand(instruction.Operand)],
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TypeOf:
                    stack[stackPointer - 1] = new JsValue(GetTypeofStringValue(stack[stackPointer - 1]));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TypeOfIdentifier:
                    if (IsInactiveCatchBindingSlot(inactiveCatchBindingSlots, instruction.Operand))
                    {
                        stack[stackPointer++] = new JsValue("undefined");
                        programCounter++;
                        break;
                    }

                    var typeOfValue = slots[instruction.Operand];
                    if (typeOfValue.IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, instruction.Operand, context);
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    stack[stackPointer++] = new JsValue(GetTypeofStringValue(typeOfValue));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TypeOfDynamicIdentifier:
                    stack[stackPointer++] = TypeOfDynamicIdentifier(
                        program.StringConstants[instruction.Operand],
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DeleteDynamicIdentifier:
                    stack[stackPointer++] = DeleteDynamicIdentifier(
                        program.StringConstants[instruction.Operand],
                        RequireDynamicEnvironment(currentCallingEnvironment),
                        context,
                        isStrict)
                        ? JsValue.True
                        : JsValue.False;
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DeleteNamedProperty:
                    stack[stackPointer - 1] = DeleteNamedProperty(
                        stack[stackPointer - 1],
                        program.StringConstants[instruction.Operand],
                        context,
                        isStrict)
                        ? JsValue.True
                        : JsValue.False;
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DeleteComputedProperty:
                    var deleteComputedKey = stack[--stackPointer];
                    var deleteComputedTarget = stack[stackPointer - 1];
                    stack[stackPointer - 1] = DeleteComputedProperty(
                        deleteComputedTarget,
                        deleteComputedKey,
                        context,
                        isStrict)
                        ? JsValue.True
                        : JsValue.False;
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryPlus:
                    var plusOperand = stack[stackPointer - 1];
                    stack[stackPointer - 1] = new JsValue(JsOps.ToNumber(in plusOperand, context));
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryMinus:
                    stack[stackPointer - 1] = TypedAstEvaluator.NegateValue(stack[stackPointer - 1], context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryVoid:
                    stack[stackPointer - 1] = JsValue.Undefined;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PrivateFieldIn:
                    if (stack[stackPointer - 1] is not { Kind: JsValueKind.Object, ObjectValue: JsObject privateFieldTarget })
                    {
                        context.SetThrow(StandardLibrary.CreateTypeError(
                            "Cannot use 'in' operator to search for a private field in a non-object",
                            context,
                            context.RealmState));
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    stack[stackPointer - 1] = HasPrivateField(
                            privateFieldTarget,
                            program.StringConstants[instruction.Operand],
                            context)
                        ? JsValue.True
                        : JsValue.False;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ToString:
                    stack[stackPointer - 1] = new JsValue(JsOps.ToJsString(stack[stackPointer - 1], context));
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Pop:
                    stackPointer--;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DuplicateTop:
                    stack[stackPointer] = stack[stackPointer - 1];
                    stackPointer++;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ApplyBindingTarget:
                    var bindingTargetValue = stack[--stackPointer];
                    var bindingEnvironment = RequireDynamicEnvironment(currentCallingEnvironment);
                    SyncUnifiedSlotsToEnvironment(program, slots, slotEnvironments, bindingEnvironment);
                    TypedAstEvaluator.ApplyLoweredAssignmentBindingTargetProgram(
                        program.BindingTargetConstants[instruction.Operand],
                        bindingTargetValue,
                        bindingEnvironment,
                        context,
                        allowNameInference: false);
                    SyncEnvironmentToUnifiedSlots(program, slots, slotEnvironments, bindingEnvironment);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget:
                    var declarationBindingValue = stack[--stackPointer];
                    var declarationBindingEnvironment = RequireDynamicEnvironment(currentCallingEnvironment);
                    SyncUnifiedSlotsToEnvironment(program, slots, slotEnvironments, declarationBindingEnvironment);
                    TypedAstEvaluator.ApplyLoweredDeclarationBindingTargetProgram(
                        program.BindingTargetConstants[DecodeDeclarationBindingTargetIndex(instruction.Operand)],
                        declarationBindingValue,
                        declarationBindingEnvironment,
                        context,
                        DecodeDeclarationBindingTargetVariableKind(instruction.Operand),
                        DecodeDeclarationBindingTargetHasInitializer(instruction.Operand),
                        allowNameInference: false);
                    SyncEnvironmentToUnifiedSlots(
                        program,
                        slots,
                        slotEnvironments,
                        declarationBindingEnvironment);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SwapTopTwo:
                    var top = stack[stackPointer - 1];
                    stack[stackPointer - 1] = stack[stackPointer - 2];
                    stack[stackPointer - 2] = top;
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

                case UnifiedBytecodeOpCode.ArraySpread:
                    var spreadSourceValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetArray(out var spreadTargetArray))
                    {
                        throw new InvalidOperationException("Array spread unified bytecode op requires an array receiver.");
                    }

                    foreach (var spreadElement in TypedAstEvaluator.EnumerateSpread(spreadSourceValue, context))
                    {
                        spreadTargetArray.Push(spreadElement);
                    }

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
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    DefineComputedObjectLiteralProperty(
                        computedObjectLiteralTarget,
                        computedObjectPropertyName,
                        instruction.Operand,
                        computedObjectPropertyValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineObjectMethod:
                    var methodValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var methodObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Object method unified bytecode op requires an object receiver.");
                    }

                    DefineObjectLiteralMethod(
                        methodObjectLiteralTarget,
                        program.StringConstants[instruction.Operand],
                        methodValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineComputedObjectMethod:
                    var computedMethodValue = stack[--stackPointer];
                    var computedMethodKey = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var computedMethodObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Computed object method unified bytecode op requires an object receiver.");
                    }

                    var computedMethodName = JsOps.GetRequiredPropertyName(computedMethodKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    DefineObjectLiteralMethod(computedMethodObjectLiteralTarget, computedMethodName, computedMethodValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineObjectAccessor:
                    var accessorValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var accessorObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Object accessor unified bytecode op requires an object receiver.");
                    }

                    DefineObjectLiteralAccessor(
                        accessorObjectLiteralTarget,
                        program.StringConstants[DecodeObjectAccessorNameOperand(instruction.Operand)],
                        DecodeObjectAccessorKind(instruction.Operand),
                        accessorValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineComputedObjectAccessor:
                    var computedAccessorValue = stack[--stackPointer];
                    var computedAccessorKey = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var computedAccessorObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Computed object accessor unified bytecode op requires an object receiver.");
                    }

                    var computedAccessorName = JsOps.GetRequiredPropertyName(computedAccessorKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    DefineObjectLiteralAccessor(
                        computedAccessorObjectLiteralTarget,
                        computedAccessorName,
                        DecodeObjectAccessorKind(instruction.Operand),
                        computedAccessorValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ObjectSpread:
                    var objectSpreadValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var objectSpreadTarget))
                    {
                        throw new InvalidOperationException(
                            "Object spread unified bytecode op requires an object receiver.");
                    }

                    ApplyObjectLiteralSpread(objectSpreadTarget, objectSpreadValue, context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Jump:
                    programCounter = instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.JumpWithDriverCleanup:
                    CleanupDriverStatesForControlTarget(
                        instruction.Operand,
                        isBreak: true,
                        program,
                        slots,
                        slotEnvironments,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter = instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.JumpIfFalse:
                    programCounter = stack[--stackPointer].IsTruthy
                        ? programCounter + 1
                        : instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.JumpIfShortCircuitFalse:
                    programCounter = stack[stackPointer - 1].IsTruthy
                        ? programCounter + 1
                        : instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.JumpIfShortCircuitTrue:
                    programCounter = stack[stackPointer - 1].IsTruthy
                        ? instruction.Operand
                        : programCounter + 1;
                    break;

                case UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish:
                    programCounter = stack[stackPointer - 1].IsNullish
                        ? programCounter + 1
                        : instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.Break:
                    if (HandleAbruptCompletion(
                            program,
                            AbruptKind.Break,
                            JsValue.Undefined,
                            instruction.Operand,
                            hasControlTarget: true,
                            tryStack,
                            slots,
                            ref programCounter,
                            ref currentCallingEnvironment,
                            slotEnvironments,
                            ref environmentStack,
                            ref environmentStackCount))
                    {
                        break;
                    }

                    CleanupDriverStatesForControlTarget(
                        instruction.Operand,
                        isBreak: true,
                        program,
                        slots,
                        slotEnvironments,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter = instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.Continue:
                    if (HandleAbruptCompletion(
                            program,
                            AbruptKind.Continue,
                            JsValue.Undefined,
                            instruction.Operand,
                            hasControlTarget: true,
                            tryStack,
                            slots,
                            ref programCounter,
                            ref currentCallingEnvironment,
                            slotEnvironments,
                            ref environmentStack,
                            ref environmentStackCount))
                    {
                        break;
                    }

                    CleanupDriverStatesForControlTarget(
                        instruction.Operand,
                        isBreak: false,
                        program,
                        slots,
                        slotEnvironments,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter = instruction.Operand;
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
                        RestoreSlotEnvironmentOwners(slotEnvironments, slots, scopeFrame);
                        currentCallingEnvironment = scopeFrame.Environment.Enclosing ?? currentCallingEnvironment;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.EnterTry:
                    tryStack ??= new Stack<TryFrame>();
                    tryStack.Push(new TryFrame(
                        program.TryDescriptors[instruction.Operand],
                        currentCallingEnvironment,
                        environmentStackCount));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.EnterCatch:
                    var catchDescriptor = program.CatchDescriptors[instruction.Operand];
                    EnterCatch(
                        program,
                        catchDescriptor,
                        tryStack,
                        slots,
                        slotEnvironments,
                        context,
                        ref currentCallingEnvironment,
                        ref environmentStack,
                        ref environmentStackCount);
                    MarkCatchBindingSlots(
                        ref inactiveCatchBindingSlots,
                        slots.Length,
                        catchDescriptor,
                        isInactive: false);
                    if (context.ShouldStopEvaluation)
                    {
                        if (HandleContextThrow(
                                context,
                                program,
                                tryStack,
                                slots,
                                ref programCounter,
                                ref currentCallingEnvironment,
                                slotEnvironments,
                                ref environmentStack,
                                ref environmentStackCount))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LeaveTry:
                    if (tryStack?.Count > 0 &&
                        tryStack.Peek().Descriptor.LeaveTryTarget == programCounter)
                    {
                        CompleteTryNormally(
                            instruction.Operand,
                            tryStack,
                            slots,
                            ref inactiveCatchBindingSlots,
                            ref programCounter,
                            ref currentCallingEnvironment,
                            slotEnvironments,
                            ref environmentStack,
                            ref environmentStackCount);
                    }
                    else
                    {
                        programCounter = instruction.Operand;
                    }

                    break;

                case UnifiedBytecodeOpCode.EndFinally:
                    if (tryStack is null || tryStack.Count == 0)
                    {
                        programCounter = instruction.Operand;
                        break;
                    }

                    if (CompleteFinally(
                            program,
                            instruction.Operand,
                            tryStack,
                            slots,
                            slotEnvironments,
                            context,
                            ref programCounter,
                            ref currentCallingEnvironment,
                            ref environmentStack,
                            ref environmentStackCount,
                            out var finalReturn))
                    {
                        return finalReturn;
                    }

                    break;

                case UnifiedBytecodeOpCode.EnterWith:
                    var withObjectValue = stack[--stackPointer];
                    if (TypedAstEvaluator.TryConvertToWithBindingObject(withObjectValue, context, out var withObject))
                    {
                        currentCallingEnvironment = JsEnvironment.CreateInstance(
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            isFunctionScope: false,
                            isStrict: context.CurrentScope.IsStrict || isStrict,
                            description: "unified-bytecode-with",
                            withObject: withObject);
                    }

                    if (context.ShouldStopEvaluation)
                    {
                        if (TryHandleCurrentContextThrow(slots))
                        {
                            break;
                        }

                        return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LeaveWith:
                    if (currentCallingEnvironment is { Enclosing: { } enclosing })
                    {
                        currentCallingEnvironment = enclosing;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TdzHeadInit:
                    {
                        // Slice A (#2678): establish the loop-head temporal dead zone before the
                        // iterator/for-in source is evaluated. Marking the flat head slots
                        // uninitialized mirrors the EnterScope path so reads of `const x`/`let x`
                        // inside the source throw a ReferenceError on the production path.
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        var headSlots = descriptor.TdzHeadSlots;
                        for (var i = 0; i < headSlots.Length; i++)
                        {
                            var headSlot = headSlots[i];
                            slots[headSlot] = JsValue.Uninitialized;
                            SyncSlotEnvironment(slotEnvironments, headSlot, JsValue.Uninitialized);
                        }

                        programCounter++;
                        break;
                    }

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
                                ref nextActiveDriverOrdinal,
                                out var nextProgramCounter))
                        {
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                        }

                        programCounter = nextProgramCounter;
                        break;
                    }

                case UnifiedBytecodeOpCode.IteratorClose:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        CloseIteratorDriverState(
                            descriptor.StateSlot,
                            slots,
                            slotEnvironments,
                            context,
                            HasPendingThrowCompletion(tryStack));
                        if (context.ShouldStopEvaluation)
                        {
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
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
                        forInState.ActiveDriverOrdinal = ++nextActiveDriverOrdinal;
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
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                        }

                        slots[descriptor.StateSlot] = JsValue.FromObjectUnsafe(state);
                        state.ActiveDriverOrdinal = ++nextActiveDriverOrdinal;
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
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, true);
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
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, true);
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
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ObjectDestructuringInit:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        var sourceValue = stack[--stackPointer];
                        if (!TryGetSourceForObjectDestructuring(sourceValue, context, out var state))
                        {
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                        }

                        slots[descriptor.StateSlot] = JsValue.FromObjectUnsafe(state);
                        state.ActiveDriverOrdinal = ++nextActiveDriverOrdinal;
                        SyncSlotEnvironment(slotEnvironments, descriptor.StateSlot, slots[descriptor.StateSlot]);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ObjectDestructuringProperty:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        var propertyName = program.StringConstants[descriptor.NameConstantIndex];
                        if (!TryReadObjectDestructuringProperty(
                                descriptor.StateSlot,
                                propertyName,
                                slots,
                                slotEnvironments,
                                context,
                                out var value))
                        {
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                        }

                        if (descriptor.TargetSlot >= 0)
                        {
                            slots[descriptor.TargetSlot] = value;
                            SyncSlotEnvironment(slotEnvironments, descriptor.TargetSlot, value);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ObjectDestructuringRest:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        if (!TryReadObjectDestructuringRest(
                                descriptor.StateSlot,
                                slots,
                                slotEnvironments,
                                context,
                                out var restValue))
                        {
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                        }

                        slots[descriptor.TargetSlot] = restValue;
                        SyncSlotEnvironment(slotEnvironments, descriptor.TargetSlot, restValue);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ObjectDestructuringClose:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        CloseObjectDestructuringState(descriptor.StateSlot, slots, slotEnvironments);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.Return:
                    var result = stack[--stackPointer];
                    if (HandleAbruptCompletion(
                            program,
                            AbruptKind.Return,
                            result,
                            -1,
                            hasControlTarget: false,
                            tryStack,
                            slots,
                            ref programCounter,
                            ref currentCallingEnvironment,
                            slotEnvironments,
                            ref environmentStack,
                            ref environmentStackCount))
                    {
                        break;
                    }

                    CleanupActiveDriverStates(slots, slotEnvironments, context, false);
                    return context.ShouldStopEvaluation ? JsValue.Undefined : result;

                case UnifiedBytecodeOpCode.ReturnUndefined:
                    if (HandleAbruptCompletion(
                            program,
                            AbruptKind.Return,
                            JsValue.Undefined,
                            -1,
                            hasControlTarget: false,
                            tryStack,
                            slots,
                            ref programCounter,
                            ref currentCallingEnvironment,
                            slotEnvironments,
                            ref environmentStack,
                            ref environmentStackCount))
                    {
                        break;
                    }

                    CleanupActiveDriverStates(slots, slotEnvironments, context, false);
                    return JsValue.Undefined;

                case UnifiedBytecodeOpCode.Throw:
                    var thrownValue = stack[--stackPointer];
                    if (HandleAbruptCompletion(
                            program,
                            AbruptKind.Throw,
                            thrownValue,
                            -1,
                            hasControlTarget: false,
                            tryStack,
                            slots,
                            ref programCounter,
                            ref currentCallingEnvironment,
                            slotEnvironments,
                            ref environmentStack,
                            ref environmentStackCount))
                    {
                        break;
                    }

                    context.SetThrow(thrownValue);
                    CleanupActiveDriverStates(slots, slotEnvironments, context, true);
                    return JsValue.Undefined;

                case UnifiedBytecodeOpCode.ThrowReferenceError:
                    throw StandardLibrary.ThrowReferenceError(
                        program.StringConstants[instruction.Operand],
                        context,
                        context.RealmState);

                case UnifiedBytecodeOpCode.LoadFunctionLiteral:
                    {
                        var flDescriptor = program.FunctionLiteralConstants[instruction.Operand >> 1];
                        var isConstructor = (instruction.Operand & 1) != 0;
                        var closureEnv = currentCallingEnvironment
                            ?? throw new InvalidOperationException("Cannot create function literal without a calling environment.");
                        var functionCallable = TypedAstEvaluator.CreateFunctionValueFromLiteral(
                            flDescriptor.Function, closureEnv, context, isConstructor, flDescriptor.PlanSeed);
                        stack[stackPointer++] = JsValue.FromObjectUnsafe(functionCallable);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.LoadClassLiteral:
                    {
                        var closureEnv = currentCallingEnvironment
                            ?? throw new InvalidOperationException("Cannot create class literal without a calling environment.");
                        var classExpression = program.ClassLiteralConstants[instruction.Operand];
                        stack[stackPointer++] = TypedAstEvaluator.CreateClassValueFromLiteral(
                            classExpression,
                            closureEnv,
                            context);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.EnsureHasName:
                    {
                        var targetName = program.StringConstants[instruction.Operand];
                        if (stack[stackPointer - 1] is { Kind: JsValueKind.Object, ObjectValue: TypedAstEvaluator.IFunctionNameTarget ensureNameTarget })
                        {
                            ensureNameTarget.EnsureHasName(targetName);
                        }

                        programCounter++;
                        break;
                    }

                default:
                    throw new InvalidOperationException($"Unsupported unified opcode '{instruction.OpCode}'.");
                }
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
                if (TryHandleCurrentContextThrow(slots))
                {
                    continue;
                }

                return StopWithDriverCleanup(slots, slotEnvironments, context, true);
            }
        }

        throw new InvalidOperationException("Program terminated without Return.");
    }

    private static bool HandleContextThrow(
        EvaluationContext context,
        UnifiedBytecodeProgram program,
        Stack<TryFrame>? tryStack,
        Span<JsValue> slots,
        ref int programCounter,
        ref JsEnvironment? currentEnvironment,
        JsEnvironment?[]? slotEnvironments,
        ref EnvironmentScopeFrame[]? environmentStack,
        ref int environmentStackCount)
    {
        if (!context.IsThrow)
        {
            return false;
        }

        var thrownValue = context.FlowValue;
        context.Clear();
        if (HandleAbruptCompletion(
                program,
                AbruptKind.Throw,
                thrownValue,
                -1,
                hasControlTarget: false,
                tryStack,
                slots,
                ref programCounter,
                ref currentEnvironment,
                slotEnvironments,
                ref environmentStack,
                ref environmentStackCount))
        {
            return true;
        }

        context.SetThrow(thrownValue);
        return false;
    }

    private static bool HasPendingThrowCompletion(Stack<TryFrame>? tryStack)
    {
        return tryStack is { Count: > 0 } &&
               tryStack.Peek() is { FinallyScheduled: true, PendingCompletion.Kind: AbruptKind.Throw };
    }

    private static void CompleteTryNormally(
        int resumeTarget,
        Stack<TryFrame> tryStack,
        Span<JsValue> slots,
        ref bool[]? inactiveCatchBindingSlots,
        ref int programCounter,
        ref JsEnvironment? currentEnvironment,
        JsEnvironment?[]? slotEnvironments,
        ref EnvironmentScopeFrame[]? environmentStack,
        ref int environmentStackCount)
    {
        if (tryStack.Count == 0)
        {
            programCounter = resumeTarget;
            return;
        }

        var frame = tryStack.Peek();
        MarkCatchBindingSlots(
            ref inactiveCatchBindingSlots,
            slots.Length,
            frame.ActiveCatchDescriptor,
            isInactive: true);
        if (frame is { Descriptor.FinallyTarget: >= 0, FinallyScheduled: false })
        {
            frame.FinallyScheduled = true;
            frame.PendingCompletion = PendingCompletion.FromNormal(resumeTarget);
            RestoreEnvironmentToFrame(
                frame,
                slots,
                ref currentEnvironment,
                slotEnvironments,
                ref environmentStack,
                ref environmentStackCount);
            programCounter = frame.Descriptor.FinallyTarget;
            return;
        }

        RestoreEnvironmentToFrame(
            frame,
            slots,
            ref currentEnvironment,
            slotEnvironments,
            ref environmentStack,
            ref environmentStackCount);
        tryStack.Pop();
        programCounter = resumeTarget;
    }

    private static bool HandleAbruptCompletion(
        UnifiedBytecodeProgram program,
        AbruptKind kind,
        JsValue value,
        int controlTarget,
        bool hasControlTarget,
        Stack<TryFrame>? tryStack,
        Span<JsValue> slots,
        ref int programCounter,
        ref JsEnvironment? currentEnvironment,
        JsEnvironment?[]? slotEnvironments,
        ref EnvironmentScopeFrame[]? environmentStack,
        ref int environmentStackCount)
    {
        if (tryStack is null)
        {
            return false;
        }

        while (tryStack.Count > 0)
        {
            var frame = tryStack.Peek();
            if (kind == AbruptKind.Throw &&
                frame.Descriptor.HandlerTarget >= 0 &&
                !frame.CatchUsed &&
                !frame.FinallyScheduled)
            {
                frame.CatchUsed = true;
                frame.ThrownValue = value;
                RestoreEnvironmentToFrame(
                    frame,
                    slots,
                    ref currentEnvironment,
                    slotEnvironments,
                    ref environmentStack,
                    ref environmentStackCount);
                programCounter = frame.Descriptor.HandlerTarget;
                return true;
            }

            if (frame.Descriptor.FinallyTarget >= 0)
            {
                if (hasControlTarget &&
                    kind == AbruptKind.Continue &&
                    frame.Descriptor.LoopContinueTarget >= 0 &&
                    (IsSameLoopControlTarget(program, controlTarget, frame.Descriptor.LoopContinueTarget) ||
                     IsSameLoopControlTarget(program, frame.Descriptor.LoopContinueTarget, controlTarget)))
                {
                    return false;
                }

                if (hasControlTarget &&
                    kind == AbruptKind.Continue &&
                    frame.Descriptor.LoopBreakTarget >= 0 &&
                    IsContinueTargetInsideLoopFrame(program, controlTarget, frame.Descriptor.LoopBreakTarget))
                {
                    return false;
                }

                if (hasControlTarget &&
                    kind == AbruptKind.Break &&
                    frame.Descriptor.LoopBreakTarget >= 0 &&
                    !IsSameLoopControlTarget(program, controlTarget, frame.Descriptor.LoopBreakTarget) &&
                    IsBreakTargetInsideLoopFrame(program, controlTarget, frame.Descriptor.LoopBreakTarget))
                {
                    return false;
                }

                if (!frame.FinallyScheduled)
                {
                    frame.FinallyScheduled = true;
                    frame.PendingCompletion = hasControlTarget
                        ? PendingCompletion.FromTarget(kind, controlTarget)
                        : PendingCompletion.FromValue(kind, value);
                    RestoreEnvironmentToFrame(
                        frame,
                        slots,
                        ref currentEnvironment,
                        slotEnvironments,
                        ref environmentStack,
                        ref environmentStackCount);
                    programCounter = frame.Descriptor.FinallyTarget;
                    return true;
                }

                frame.PendingCompletion = hasControlTarget
                    ? PendingCompletion.FromTarget(kind, controlTarget, originatedInFinally: true)
                    : PendingCompletion.FromValue(kind, value, originatedInFinally: true);
                if (frame.Descriptor.EndFinallyTarget >= 0)
                {
                    programCounter = frame.Descriptor.EndFinallyTarget;
                    return true;
                }

                tryStack.Pop();
                continue;
            }

            tryStack.Pop();
        }

        return false;
    }

    private static bool IsBreakTargetInsideLoopFrame(
        UnifiedBytecodeProgram program,
        int target,
        int loopBreakTarget)
    {
        for (var index = program.DriverDescriptors.Length - 1; index >= 0; index--)
        {
            var descriptor = program.DriverDescriptors[index];
            // The inner driver can already be closed when its pending break reaches an outer frame.
            // Use descriptor topology instead of active driver state to mirror the runner's breakable stack.
            if (descriptor.BreakTarget < 0)
            {
                continue;
            }

            if (IsSameLoopControlTarget(program, target, descriptor.BreakTarget))
            {
                return descriptor.BreakTarget != loopBreakTarget;
            }

            if (descriptor.BreakTarget == loopBreakTarget)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsContinueTargetInsideLoopFrame(
        UnifiedBytecodeProgram program,
        int target,
        int loopBreakTarget)
    {
        foreach (var descriptor in program.DriverDescriptors)
        {
            if (descriptor.BreakTarget < 0 ||
                !IsSameDriverBreakTarget(program, descriptor.BreakTarget, loopBreakTarget))
            {
                continue;
            }

            if ((descriptor.ContinueTarget >= 0 &&
                 IsSameLoopControlTarget(program, target, descriptor.ContinueTarget)) ||
                IsControlTargetInsideDriverBody(program, target, descriptor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameDriverBreakTarget(
        UnifiedBytecodeProgram program,
        int left,
        int right)
    {
        return IsSameLoopControlTarget(program, left, right) ||
               IsSameLoopControlTarget(program, right, left);
    }

    private static bool IsSameLoopControlTarget(
        UnifiedBytecodeProgram program,
        int target,
        int loopControlTarget)
    {
        while ((uint)target < (uint)program.Instructions.Length)
        {
            if (target == loopControlTarget)
            {
                return true;
            }

            var next = GetCleanupChainNext(program.Instructions[target], target);
            if (next == target)
            {
                return false;
            }

            target = next;
        }

        return false;
    }

    private static int ResolveCleanupChainTarget(UnifiedBytecodeProgram program, int target)
    {
        var visited = 0;
        while ((uint)target < (uint)program.Instructions.Length &&
               visited++ < program.Instructions.Length)
        {
            var next = GetCleanupChainNext(program.Instructions[target], target);
            if (next == target)
            {
                break;
            }

            target = next;
        }

        return target;
    }

    private static int GetCleanupChainNext(UnifiedBytecodeInstruction instruction, int current) =>
        instruction.OpCode switch
        {
            UnifiedBytecodeOpCode.PopEnvironment or UnifiedBytecodeOpCode.LeaveWith => current + 1,
            _ => current
        };

    private static bool CompleteFinally(
        UnifiedBytecodeProgram program,
        int nextTarget,
        Stack<TryFrame> tryStack,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        ref int programCounter,
        ref JsEnvironment? currentEnvironment,
        ref EnvironmentScopeFrame[]? environmentStack,
        ref int environmentStackCount,
        out JsValue returnValue)
    {
        returnValue = JsValue.Undefined;
        var completedFrame = tryStack.Pop();
        var pending = completedFrame.PendingCompletion;
        if (pending.Kind == AbruptKind.None)
        {
            programCounter = pending.ResumeTarget >= 0 ? pending.ResumeTarget : nextTarget;
            return false;
        }

        if (pending.Kind == AbruptKind.Return)
        {
            if (HandleAbruptCompletion(
                    program,
                    AbruptKind.Return,
                    pending.Value,
                    -1,
                    hasControlTarget: false,
                    tryStack,
                    slots,
                    ref programCounter,
                    ref currentEnvironment,
                    slotEnvironments,
                    ref environmentStack,
                    ref environmentStackCount))
            {
                return false;
            }

            CleanupActiveDriverStates(slots, slotEnvironments, context, false);
            returnValue = context.ShouldStopEvaluation ? JsValue.Undefined : pending.Value;
            return true;
        }

        if (pending.Kind is AbruptKind.Break or AbruptKind.Continue)
        {
            if (HandleAbruptCompletion(
                    program,
                    pending.Kind,
                    JsValue.Undefined,
                    pending.Target,
                    hasControlTarget: true,
                    tryStack,
                    slots,
                    ref programCounter,
                    ref currentEnvironment,
                    slotEnvironments,
                    ref environmentStack,
                    ref environmentStackCount))
            {
                return false;
            }

            CleanupDriverStatesForControlTarget(
                pending.Target,
                isBreak: pending.Kind == AbruptKind.Break,
                program,
                slots,
                slotEnvironments,
                context);

            programCounter = pending.Target >= 0 ? pending.Target : nextTarget;
            return false;
        }

        if (HandleAbruptCompletion(
                program,
                AbruptKind.Throw,
                pending.Value,
                -1,
                hasControlTarget: false,
                tryStack,
                slots,
                ref programCounter,
                ref currentEnvironment,
                slotEnvironments,
                ref environmentStack,
                ref environmentStackCount))
        {
            return false;
        }

        context.SetThrow(pending.Value);
        CleanupActiveDriverStates(slots, slotEnvironments, context, true);
        returnValue = JsValue.Undefined;
        return true;
    }

    private static void EnterCatch(
        UnifiedBytecodeProgram program,
        UnifiedBytecodeCatchDescriptor descriptor,
        Stack<TryFrame>? tryStack,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        ref JsEnvironment? currentEnvironment,
        ref EnvironmentScopeFrame[]? environmentStack,
        ref int environmentStackCount)
    {
        var thrownValue = tryStack is { Count: > 0 }
            ? tryStack.Peek().ThrownValue
            : JsValue.Undefined;
        if (tryStack is { Count: > 0 })
        {
            tryStack.Peek().ActiveCatchDescriptor = descriptor;
        }

        if (currentEnvironment is null || slotEnvironments is null)
        {
            if (descriptor.BindingSlot >= 0)
            {
                slots[descriptor.BindingSlot] = thrownValue;
            }

            return;
        }

        var catchEnvironment = CreateCatchEnvironment(program, descriptor, currentEnvironment, context);
        var previousSlotEnvironments = new JsEnvironment?[descriptor.SlotIndices.Length];
        for (var i = 0; i < descriptor.SlotIndices.Length; i++)
        {
            var slotIndex = descriptor.SlotIndices[i];
            previousSlotEnvironments[i] = slotEnvironments[slotIndex];
            slotEnvironments[slotIndex] = catchEnvironment;
        }

        environmentStack ??= new EnvironmentScopeFrame[program.Instructions.Length];
        environmentStack[environmentStackCount++] = new EnvironmentScopeFrame(
            catchEnvironment,
            descriptor.SlotIndices,
            previousSlotEnvironments);
        currentEnvironment = catchEnvironment;

        if (descriptor.BindingName is { } bindingName)
        {
            catchEnvironment.SetSimpleCatchParameters(
                new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance) { bindingName });
            catchEnvironment.DefineJsValue(bindingName, thrownValue, false, isLexicalBinding: true);
            slots[descriptor.BindingSlot] = thrownValue;
            SyncSlotEnvironment(slotEnvironments, descriptor.BindingSlot, thrownValue);
        }
    }

    public static UnifiedBytecodeStepResult ExecuteResumable(
        UnifiedBytecodeResumeState state,
        UnifiedBytecodeResumeMode mode,
        JsValue resumeValue,
        EvaluationContext context)
    {
        if (state.IsCompleted)
        {
            return CompleteAlreadyFinishedResumable(mode, resumeValue);
        }

        if (state.ProgramCounter == 0 &&
            mode is UnifiedBytecodeResumeMode.Throw or UnifiedBytecodeResumeMode.Return)
        {
            state.IsCompleted = true;
            return CompleteAlreadyFinishedResumable(mode, resumeValue);
        }

        if (state.PendingAbruptCompletion.Kind is not UnifiedBytecodeAbruptCompletionKind.None)
        {
            return CompletePendingAbruptCompletion(state);
        }

        state.ResumePayloadKind = mode switch
        {
            UnifiedBytecodeResumeMode.Throw => UnifiedBytecodeResumePayloadKind.Throw,
            UnifiedBytecodeResumeMode.Return => UnifiedBytecodeResumePayloadKind.Return,
            _ => UnifiedBytecodeResumePayloadKind.Value
        };
        state.ResumePayload = resumeValue;

        var program = state.Program;
        var instructions = program.Instructions;
        var stack = state.OperandStack;
        var slots = state.Slots;
        var stackPointer = state.StackPointer;
        var programCounter = state.ProgramCounter;

        while ((uint)programCounter < (uint)instructions.Length)
        {
            var instruction = instructions[programCounter];
            switch (instruction.OpCode)
            {
                case UnifiedBytecodeOpCode.LoadSlot:
                    var slotValue = slots[instruction.Operand];
                    if (slotValue.IsUninitialized)
                    {
                        context.SetThrow(StandardLibrary.CreateReferenceError(
                            $"ReferenceError: Cannot access '{GetSlotName(program, instruction.Operand)}' before initialization",
                            context,
                            context.RealmState));
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    stack[stackPointer++] = slotValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadLiteral:
                    stack[stackPointer++] = program.LiteralConstants[instruction.Operand];
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadThis:
                    stack[stackPointer++] = state.ThisValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.StoreSlot:
                    if (slots[instruction.Operand].IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, instruction.Operand, context);
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    slots[instruction.Operand] = stack[--stackPointer];
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.InitializeSlot:
                    slots[instruction.Operand] = stack[--stackPointer];
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Binary:
                    var op = (BinaryOperator)instruction.Operand;
                    var right = stack[--stackPointer];
                    var left = stack[--stackPointer];
                    stack[stackPointer++] = ApplyBinaryOperator(op, left, right, context);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Pop:
                    stackPointer--;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DuplicateTop:
                    stack[stackPointer] = stack[stackPointer - 1];
                    stackPointer++;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SwapTopTwo:
                    var resumableTop = stack[stackPointer - 1];
                    stack[stackPointer - 1] = stack[stackPointer - 2];
                    stack[stackPointer - 2] = resumableTop;
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

                case UnifiedBytecodeOpCode.JumpIfShortCircuitFalse:
                    programCounter = !stack[stackPointer - 1].IsTruthy
                        ? instruction.Operand
                        : programCounter + 1;
                    break;

                case UnifiedBytecodeOpCode.JumpIfShortCircuitTrue:
                    programCounter = stack[stackPointer - 1].IsTruthy
                        ? instruction.Operand
                        : programCounter + 1;
                    break;

                case UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish:
                    programCounter = !stack[stackPointer - 1].IsNullish
                        ? instruction.Operand
                        : programCounter + 1;
                    break;

                case UnifiedBytecodeOpCode.Yield:
                    var yieldedValue = stackPointer > 0 ? stack[--stackPointer] : JsValue.Undefined;
                    state.ProgramCounter = programCounter + 1;
                    state.StackPointer = stackPointer;
                    state.ResumePayloadKind = UnifiedBytecodeResumePayloadKind.None;
                    state.ResumePayload = JsValue.Undefined;
                    return UnifiedBytecodeStepResult.Yield(yieldedValue);

                case UnifiedBytecodeOpCode.StoreResumeValue:
                    var resumeKind = state.ResumePayloadKind;
                    var payload = resumeKind == UnifiedBytecodeResumePayloadKind.None
                        ? JsValue.Undefined
                        : state.ResumePayload;
                    state.ResumePayloadKind = UnifiedBytecodeResumePayloadKind.None;
                    state.ResumePayload = JsValue.Undefined;
                    switch (resumeKind)
                    {
                        case UnifiedBytecodeResumePayloadKind.Throw:
                            state.PendingAbruptCompletion = new UnifiedBytecodePendingAbruptCompletion(
                                UnifiedBytecodeAbruptCompletionKind.Throw,
                                payload,
                                Target: -1,
                                ResumeTarget: programCounter + 1,
                                OriginatedInFinally: false);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(payload);
                        case UnifiedBytecodeResumePayloadKind.Return:
                            state.PendingAbruptCompletion = new UnifiedBytecodePendingAbruptCompletion(
                                UnifiedBytecodeAbruptCompletionKind.Return,
                                payload,
                                Target: -1,
                                ResumeTarget: programCounter + 1,
                                OriginatedInFinally: false);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Completed(payload);
                        default:
                            if (instruction.Operand >= 0)
                            {
                                slots[instruction.Operand] = payload;
                            }

                            programCounter++;
                            break;
                    }

                    break;

                case UnifiedBytecodeOpCode.AwaitAndDiscard:
                    if (TryConsumePendingAwaitResume(state, out var awaitedDiscard, out var awaitedDiscardThrow))
                    {
                        if (awaitedDiscardThrow)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(awaitedDiscard);
                        }

                        programCounter++;
                        break;
                    }

                    var awaitDiscardCandidate = stack[--stackPointer];
                    var awaitDiscardPendingPromise = state.PendingAwaitPromise;
                    if (!AwaitScheduler.TryResolvePromiseOrYield(
                            awaitDiscardCandidate,
                            asyncStepMode: true,
                            ref awaitDiscardPendingPromise,
                            context,
                            out _))
                    {
                        state.PendingAwaitPromise = awaitDiscardPendingPromise;
                        state.ProgramCounter = programCounter;
                        state.StackPointer = stackPointer;
                        state.ResumePayloadKind = UnifiedBytecodeResumePayloadKind.None;
                        state.ResumePayload = JsValue.Undefined;
                        return UnifiedBytecodeStepResult.PendingAwait(state.PendingAwaitPromise);
                    }

                    if (context.IsThrow)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.AwaitedReturn:
                    if (TryConsumePendingAwaitResume(state, out var awaitedReturn, out var awaitedReturnThrow))
                    {
                        state.IsCompleted = true;
                        state.ProgramCounter = programCounter + 1;
                        state.StackPointer = stackPointer;
                        return awaitedReturnThrow
                            ? UnifiedBytecodeStepResult.Throw(awaitedReturn)
                            : UnifiedBytecodeStepResult.Completed(awaitedReturn);
                    }

                    var awaitReturnCandidate = stack[--stackPointer];
                    var awaitReturnPendingPromise = state.PendingAwaitPromise;
                    if (!AwaitScheduler.TryResolvePromiseOrYield(
                            awaitReturnCandidate,
                            asyncStepMode: true,
                            ref awaitReturnPendingPromise,
                            context,
                            out var resolvedReturn))
                    {
                        state.PendingAwaitPromise = awaitReturnPendingPromise;
                        state.ProgramCounter = programCounter;
                        state.StackPointer = stackPointer;
                        state.ResumePayloadKind = UnifiedBytecodeResumePayloadKind.None;
                        state.ResumePayload = JsValue.Undefined;
                        return UnifiedBytecodeStepResult.PendingAwait(state.PendingAwaitPromise);
                    }

                    state.IsCompleted = true;
                    state.ProgramCounter = programCounter + 1;
                    state.StackPointer = stackPointer;
                    return context.IsThrow
                        ? UnifiedBytecodeStepResult.Throw(context.FlowValue)
                        : UnifiedBytecodeStepResult.Completed(resolvedReturn);

                case UnifiedBytecodeOpCode.YieldStar:
                    var yieldStarDescriptor = program.DriverDescriptors[instruction.Operand];
                    if (!TryGetDriverState<IteratorDriverState>(slots, yieldStarDescriptor.StateSlot, out var yieldStarState))
                    {
                        var iterable = stack[--stackPointer];
                        yieldStarState = CreateIteratorDriverState(iterable, IteratorDriverKind.Sync, context);
                        slots[yieldStarDescriptor.StateSlot] = JsValue.FromObjectUnsafe(yieldStarState);
                    }

                    var delegatedResumeKind = state.ResumePayloadKind;
                    var delegatedResumePayload = state.ResumePayload;
                    state.ResumePayloadKind = UnifiedBytecodeResumePayloadKind.None;
                    state.ResumePayload = JsValue.Undefined;
                    if (delegatedResumeKind == UnifiedBytecodeResumePayloadKind.Throw)
                    {
                        if (!TryResumeYieldStarAbrupt(
                                yieldStarState,
                                "throw",
                                delegatedResumePayload,
                                context,
                                out var throwResumeValue,
                                out var throwResumeDone,
                                out var throwMethodMissing))
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (throwMethodMissing)
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(StandardLibrary.CreateTypeError(
                                "The iterator does not provide a 'throw' method.",
                                context,
                                context.RealmState));
                        }

                        if (throwResumeDone)
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            if (yieldStarDescriptor.ValueSlot >= 0)
                            {
                                slots[yieldStarDescriptor.ValueSlot] = throwResumeValue;
                            }

                            programCounter++;
                            break;
                        }

                        state.ProgramCounter = programCounter;
                        state.StackPointer = stackPointer;
                        return UnifiedBytecodeStepResult.Yield(throwResumeValue);
                    }

                    if (delegatedResumeKind == UnifiedBytecodeResumePayloadKind.Return)
                    {
                        if (!TryResumeYieldStarAbrupt(
                                yieldStarState,
                                "return",
                                delegatedResumePayload,
                                context,
                                out var returnResumeValue,
                                out var returnResumeDone,
                                out var returnMethodMissing))
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (returnMethodMissing)
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Completed(delegatedResumePayload);
                        }

                        if (returnResumeDone)
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Completed(returnResumeValue);
                        }

                        state.ProgramCounter = programCounter;
                        state.StackPointer = stackPointer;
                        return UnifiedBytecodeStepResult.Yield(returnResumeValue);
                    }

                    if (!TryReadIteratorNextValue(
                            yieldStarState,
                            context,
                            callingEnvironment: null,
                            delegatedResumePayload,
                            hasSendValue: !delegatedResumePayload.IsUndefined ||
                                          delegatedResumeKind == UnifiedBytecodeResumePayloadKind.Value,
                            readDoneValue: true,
                            out var delegatedValue,
                            out var delegatedDone))
                    {
                        CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    if (delegatedDone)
                    {
                        CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                        if (yieldStarDescriptor.ValueSlot >= 0)
                        {
                            slots[yieldStarDescriptor.ValueSlot] = delegatedValue;
                        }

                        programCounter++;
                        break;
                    }

                    state.ProgramCounter = programCounter;
                    state.StackPointer = stackPointer;
                    return UnifiedBytecodeStepResult.Yield(delegatedValue);

                case UnifiedBytecodeOpCode.Return:
                    state.IsCompleted = true;
                    var returnValue = stack[--stackPointer];
                    state.ProgramCounter = programCounter + 1;
                    state.StackPointer = stackPointer;
                    return UnifiedBytecodeStepResult.Completed(returnValue);

                case UnifiedBytecodeOpCode.ReturnUndefined:
                    state.IsCompleted = true;
                    state.ProgramCounter = programCounter + 1;
                    state.StackPointer = stackPointer;
                    return UnifiedBytecodeStepResult.Completed(JsValue.Undefined);

                case UnifiedBytecodeOpCode.Throw:
                    state.IsCompleted = true;
                    var throwValue = stack[--stackPointer];
                    state.ProgramCounter = programCounter + 1;
                    state.StackPointer = stackPointer;
                    return UnifiedBytecodeStepResult.Throw(throwValue);

                default:
                    throw new NotSupportedException(
                        $"Unified bytecode opcode '{instruction.OpCode}' is not supported by the resumable execution path.");
            }
        }

        state.IsCompleted = true;
        state.ProgramCounter = programCounter;
        state.StackPointer = stackPointer;
        return UnifiedBytecodeStepResult.Completed(JsValue.Undefined);
    }

    private static bool TryConsumePendingAwaitResume(
        UnifiedBytecodeResumeState state,
        out JsValue value,
        out bool isThrow)
    {
        if (state.PendingAwaitPromise.IsUndefined)
        {
            value = JsValue.Undefined;
            isThrow = false;
            return false;
        }

        var resumeKind = state.ResumePayloadKind;
        value = resumeKind == UnifiedBytecodeResumePayloadKind.None
            ? JsValue.Undefined
            : state.ResumePayload;
        isThrow = resumeKind == UnifiedBytecodeResumePayloadKind.Throw;
        state.PendingAwaitPromise = JsValue.Undefined;
        state.ResumePayloadKind = UnifiedBytecodeResumePayloadKind.None;
        state.ResumePayload = JsValue.Undefined;
        return true;
    }

    private static UnifiedBytecodeStepResult CompletePendingAbruptCompletion(UnifiedBytecodeResumeState state)
    {
        var pending = state.PendingAbruptCompletion;
        state.PendingAbruptCompletion = UnifiedBytecodePendingAbruptCompletion.None;
        state.IsCompleted = true;
        state.ProgramCounter = pending.ResumeTarget >= 0 ? pending.ResumeTarget : state.ProgramCounter;
        return pending.Kind switch
        {
            UnifiedBytecodeAbruptCompletionKind.Throw => UnifiedBytecodeStepResult.Throw(pending.Value),
            UnifiedBytecodeAbruptCompletionKind.Return => UnifiedBytecodeStepResult.Completed(pending.Value),
            _ => UnifiedBytecodeStepResult.Completed(JsValue.Undefined)
        };
    }

    public static JsValue CreateIteratorResult(JsValue value, bool done)
    {
        return IteratorResultObject.Create(value, done);
    }

    private static UnifiedBytecodeStepResult CompleteAlreadyFinishedResumable(
        UnifiedBytecodeResumeMode mode,
        JsValue resumeValue)
    {
        return mode switch
        {
            UnifiedBytecodeResumeMode.Throw => UnifiedBytecodeStepResult.Throw(resumeValue),
            UnifiedBytecodeResumeMode.Return => UnifiedBytecodeStepResult.Completed(resumeValue),
            _ => UnifiedBytecodeStepResult.Completed(JsValue.Undefined)
        };
    }

    private static void MarkCatchBindingSlots(
        ref bool[]? inactiveCatchBindingSlots,
        int slotCount,
        UnifiedBytecodeCatchDescriptor? descriptor,
        bool isInactive)
    {
        if (descriptor is not { } catchDescriptor)
        {
            return;
        }

        inactiveCatchBindingSlots ??= new bool[slotCount];
        foreach (var slotIndex in catchDescriptor.SlotIndices)
        {
            if ((uint)slotIndex < (uint)inactiveCatchBindingSlots.Length)
            {
                inactiveCatchBindingSlots[slotIndex] = isInactive;
            }
        }
    }

    private static bool IsInactiveCatchBindingSlot(bool[]? inactiveCatchBindingSlots, int slotIndex) =>
        inactiveCatchBindingSlots is not null &&
        (uint)slotIndex < (uint)inactiveCatchBindingSlots.Length &&
        inactiveCatchBindingSlots[slotIndex];

    private static JsEnvironment CreateCatchEnvironment(
        UnifiedBytecodeProgram program,
        UnifiedBytecodeCatchDescriptor descriptor,
        JsEnvironment enclosing,
        EvaluationContext context)
    {
        var catchEnvironment = JsEnvironment.CreateInstance(
            enclosing,
            isFunctionScope: false,
            isStrict: context.CurrentScope.IsStrict || enclosing.IsStrict,
            description: "unified-bytecode-catch");
        catchEnvironment.InitializeSlots(GetScopeSlotCount(descriptor.SlotIndices), descriptor.ScopeId);
        SetScopeSlotNames(catchEnvironment, program, descriptor.SlotIndices);
        catchEnvironment.SetSlotsLexicalUninitialized(descriptor.SlotIndices);
        return catchEnvironment;
    }

    private static void RestoreEnvironmentToFrame(
        TryFrame frame,
        Span<JsValue> slots,
        ref JsEnvironment? currentEnvironment,
        JsEnvironment?[]? slotEnvironments,
        ref EnvironmentScopeFrame[]? environmentStack,
        ref int environmentStackCount)
    {
        if (slotEnvironments is not null)
        {
            while (environmentStackCount > frame.EntryEnvironmentStackCount && environmentStack is not null)
            {
                var scopeFrame = environmentStack[--environmentStackCount];
                RestoreSlotEnvironmentOwners(slotEnvironments, slots, scopeFrame);
            }
        }

        currentEnvironment = frame.EntryEnvironment ?? currentEnvironment;
    }

    private static JsEnvironment RequireDynamicEnvironment(JsEnvironment? environment)
    {
        return environment ??
               throw new InvalidOperationException("Dynamic unified bytecode operation requires an environment.");
    }

    private static JsValue GetDynamicIdentifierValue(
        string name,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var symbol = Symbol.Intern(name);
        try
        {
            return environment.TryGetIdentifierJsValue(symbol, context, out var value)
                ? value
                : SetIdentifierNotFound(symbol, context);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return JsValue.Undefined;
        }
    }

    private static void StoreDynamicIdentifierValue(
        string name,
        bool allowNameInference,
        JsValue value,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var symbol = Symbol.Intern(name);
        if (allowNameInference &&
            value is { Kind: JsValueKind.Object, ObjectValue: TypedAstEvaluator.IFunctionNameTarget nameTarget })
        {
            nameTarget.EnsureHasName(name);
        }

        try
        {
            environment.SetIdentifierJsValue(symbol, value, context);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
        }
    }

    private static void DeclareDynamicVar(
        string name,
        JsEnvironment environment,
        EvaluationContext context)
    {
        environment.DefineFunctionScoped(Symbol.Intern(name), JsValue.Undefined, hasInitializer: false,
            context: context);
    }

    private static JsValue UpdateDynamicIdentifierValue(
        string name,
        bool isIncrement,
        bool isPrefix,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var symbol = Symbol.Intern(name);
        var reference = environment.ResolveIdentifierAssignmentReference(symbol, context);
        JsValue currentValue;
        try
        {
            currentValue = reference.GetJsValue();
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return JsValue.Undefined;
        }

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

        try
        {
            reference.SetValue(newValue);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return JsValue.Undefined;
        }

        return isPrefix ? newValue : oldNumericValue;
    }

    private static JsValue TypeOfDynamicIdentifier(
        string name,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var symbol = Symbol.Intern(name);
        var hasBinding = environment.HasBinding(symbol);
        var value = GetDynamicIdentifierValue(name, environment, context);
        if (context.IsThrow && !hasBinding)
        {
            context.Clear();
            return new JsValue("undefined");
        }

        return context.ShouldStopEvaluation
            ? JsValue.Undefined
            : new JsValue(GetTypeofStringValue(value));
    }

    private static bool DeleteDynamicIdentifier(
        string name,
        JsEnvironment environment,
        EvaluationContext context,
        bool isStrict)
    {
        if (context.CurrentScope.IsStrict || isStrict)
        {
            context.SetThrow(StandardLibrary.CreateSyntaxError(
                "Delete of an unqualified identifier is not allowed in strict mode.",
                context,
                context.RealmState));
            return false;
        }

        var outcome = environment.DeleteBinding(Symbol.Intern(name));
        return outcome is DeleteBindingResult.Deleted or DeleteBindingResult.NotFound;
    }

    private static bool DeleteNamedProperty(
        JsValue target,
        string propertyName,
        EvaluationContext context,
        bool isStrict)
    {
        var handle = PropertyHandle.Resolve(
            target,
            propertyName,
            context,
            context.CurrentScope.IsStrict || isStrict,
            allowPrivate: false);
        return handle.Delete();
    }

    private static bool DeleteComputedProperty(
        JsValue target,
        JsValue propertyKey,
        EvaluationContext context,
        bool isStrict)
    {
        var handle = PropertyHandle.Resolve(
            target,
            propertyKey,
            context,
            context.CurrentScope.IsStrict || isStrict,
            allowPrivate: false);
        return handle.Delete();
    }

    private static void PrepareDynamicIdentifierCallTarget(
        string name,
        JsEnvironment environment,
        Span<JsValue> stack,
        ref int stackPointer,
        EvaluationContext context)
    {
        var symbol = Symbol.Intern(name);
        var hasWithObject = environment.HasWithObjectInChain();
        if (hasWithObject && environment.TryResolveWithBinding(symbol, context, out var withBinding))
        {
            stack[stackPointer++] = JsValue.FromObjectUnsafe(withBinding.BindingObject);
            try
            {
                stack[stackPointer++] = JsEnvironment.GetWithBindingValueJsValue(withBinding);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                                                       "ReferenceError:",
                                                       StringComparison.Ordinal))
            {
                context.SetThrow(StandardLibrary.CreateReferenceError(
                    ex.Message,
                    context,
                    context.RealmState));
                stack[stackPointer++] = JsValue.Undefined;
            }

            return;
        }

        if (!context.AllowIdentifierCache && !hasWithObject &&
            environment.TryResolveWithBinding(symbol, context, out withBinding))
        {
            stack[stackPointer++] = JsValue.FromObjectUnsafe(withBinding.BindingObject);
            try
            {
                stack[stackPointer++] = JsEnvironment.GetWithBindingValueJsValue(withBinding);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                                                       "ReferenceError:",
                                                       StringComparison.Ordinal))
            {
                context.SetThrow(StandardLibrary.CreateReferenceError(
                    ex.Message,
                    context,
                    context.RealmState));
                stack[stackPointer++] = JsValue.Undefined;
            }

            return;
        }

        stack[stackPointer++] = JsValue.Undefined;
        try
        {
            stack[stackPointer++] = environment.TryGetIdentifierJsValueAfterWithMiss(symbol, context, out var value)
                ? value
                : SetIdentifierNotFound(symbol, context);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            stack[stackPointer++] = JsValue.Undefined;
        }
    }

    private static JsValue SetIdentifierNotFound(Symbol name, EvaluationContext context)
    {
        context.SetThrow(StandardLibrary.CreateReferenceError(
            $"{name.Name} is not defined",
            context,
            context.RealmState));
        return JsValue.Undefined;
    }

    private static JsValue GetImportMeta(JsEnvironment? environment, EvaluationContext context)
    {
        if (environment is not null &&
            environment.TryFindBindingJsValue(Symbol.ImportMeta, true, out _, out var importMeta))
        {
            return importMeta;
        }

        throw StandardLibrary.ThrowReferenceError("import.meta is not defined", context, context.RealmState);
    }

    private static bool HasPrivateField(
        JsObject target,
        string privateName,
        EvaluationContext context)
    {
        var resolvedKey = context.ResolvePrivateNameKey($"#{privateName}");
        if (resolvedKey is null)
        {
            return false;
        }

        if (target.HasPrivateField(resolvedKey))
        {
            return true;
        }

        return PrivateNameScope.TryResolveScope(context.RealmState, resolvedKey, out var scope) &&
               scope is not null &&
               target.HasPrivateBrand(scope.BrandToken);
    }

    private static JsArray GetOrCreateTemplateObject(
        TaggedTemplateDescriptor descriptor,
        EvaluationContext context)
    {
        if (context.RealmState.TemplateObjectCache.TryGetValue(descriptor, out var cachedTemplate))
        {
            return (JsArray)cachedTemplate;
        }

        var stringsArray = new JsArray(descriptor.CookedStrings, context.RealmState);
        var rawStringsArray = new JsArray(descriptor.RawStrings, context.RealmState);
        var templateObject = stringsArray.CreateTemplateObject(rawStringsArray);
        context.RealmState.TemplateObjectCache[descriptor] = templateObject;
        return templateObject;
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

    private static void SyncUnifiedSlotsToEnvironment(
        UnifiedBytecodeProgram program,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        JsEnvironment environment)
    {
        var slotNames = program.SlotNames;
        var count = Math.Min(slots.Length, slotNames.Length);
        for (var i = 0; i < count; i++)
        {
            if (slotNames[i] is not { } name ||
                slots[i].IsUninitialized)
            {
                continue;
            }

            var slotEnvironment = GetSlotEnvironment(slotEnvironments, i, environment);
            if (slotEnvironment.TryGetSlotIndex(Symbol.Intern(name), out var slotIndex))
            {
                slotEnvironment.SetSlotDirect(slotIndex, slots[i]);
            }
        }
    }

    private static void SyncEnvironmentToUnifiedSlots(
        UnifiedBytecodeProgram program,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        JsEnvironment environment)
    {
        var slotNames = program.SlotNames;
        var count = Math.Min(slots.Length, slotNames.Length);
        for (var i = 0; i < count; i++)
        {
            if (slotNames[i] is not { } name)
            {
                continue;
            }

            var slotEnvironment = GetSlotEnvironment(slotEnvironments, i, environment);
            if (!slotEnvironment.TryGetSlotIndex(Symbol.Intern(name), out var slotIndex))
            {
                continue;
            }

            ref var slot = ref slotEnvironment.GetSlotByIndex(slotIndex);
            slots[i] = slot.IsUninitialized ? JsValue.Uninitialized : slot.Value;
        }
    }

    private static JsEnvironment GetSlotEnvironment(
        JsEnvironment?[]? slotEnvironments,
        int slotIndex,
        JsEnvironment fallback) =>
        slotEnvironments is not null &&
        (uint)slotIndex < (uint)slotEnvironments.Length &&
        slotEnvironments[slotIndex] is { } slotEnvironment
            ? slotEnvironment
            : fallback;

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
        Span<JsValue> slots,
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
        ref int nextActiveDriverOrdinal,
        out int programCounter)
    {
        if (!TryGetDriverState<IteratorDriverState>(slots, descriptor.StateSlot, out var state))
        {
            programCounter = descriptor.BreakTarget;
            return true;
        }

        try
        {
            if (!TryReadIteratorNextValue(
                    state,
                    context,
                    callingEnvironment,
                    sendValue: JsValue.Undefined,
                    hasSendValue: false,
                    readDoneValue: false,
                    out var value,
                    out var done))
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

            if (!state.HasEnteredLoop)
            {
                state.ActiveDriverOrdinal = ++nextActiveDriverOrdinal;
                state.HasEnteredLoop = true;
            }

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
        JsValue sendValue,
        bool hasSendValue,
        bool readDoneValue,
        out JsValue value,
        out bool done)
    {
        if (state.IteratorObject is { } iterator)
        {
            state.NextMethod ??= iterator.GetIteratorNextCallable(context);
            var nextResult = iterator.InvokeIteratorNext(
                state.NextMethod,
                sendValue,
                hasSendValue,
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
                value = readDoneValue && resultObject.TryGetProperty("value", out var completedValue)
                    ? completedValue
                    : JsValue.Undefined;
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

    private static bool TryResumeYieldStarAbrupt(
        IteratorDriverState state,
        string methodName,
        JsValue argument,
        EvaluationContext context,
        out JsValue value,
        out bool done,
        out bool methodMissing)
    {
        value = JsValue.Undefined;
        done = true;
        methodMissing = false;

        if (state.IteratorObject is not { } iterator)
        {
            methodMissing = true;
            return true;
        }

        if (!TryInvokeIteratorMethod(
                iterator,
                methodName,
                argument,
                context,
                out var result,
                out methodMissing))
        {
            return false;
        }

        if (methodMissing)
        {
            if (methodName == "throw")
            {
                try
                {
                    iterator.IteratorClose(context);
                }
                catch (ThrowSignal signal)
                {
                    context.SetThrow(signal.ThrownValue);
                    return false;
                }
            }

            return true;
        }

        if (!result.TryGetObject<IJsPropertyAccessor>(out var resultObject))
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Iterator result is not an object",
                context,
                context.RealmState));
            return false;
        }

        done = resultObject.TryGetProperty("done", out var doneValue) &&
               JsOps.ToBoolean(doneValue);
        value = resultObject.TryGetProperty("value", out var resultValue)
            ? resultValue
            : JsValue.Undefined;

        if (resultObject is IteratorResultObject poolableResult)
        {
            IteratorResultObjectPool.Return(poolableResult);
        }

        return true;
    }

    private static bool TryInvokeIteratorMethod(
        IJsObjectLike iterator,
        string methodName,
        JsValue argument,
        EvaluationContext context,
        out JsValue result,
        out bool methodMissing)
    {
        result = JsValue.Undefined;
        methodMissing = false;

        try
        {
            if (!iterator.TryGetProperty(methodName, out var methodValue) ||
                methodValue.IsNullish)
            {
                methodMissing = true;
                return true;
            }

            if (!methodValue.TryGetObject<IJsCallable>(out var callable))
            {
                context.SetThrow(StandardLibrary.CreateTypeError(
                    "Iterator method is not callable",
                    context,
                    context.RealmState));
                return false;
            }

            result = TypedAstEvaluator.InvokeCallableSingleArg(
                callable,
                argument,
                JsValue.FromObjectUnsafe(iterator),
                context);
            if (context.IsThrow)
            {
                return false;
            }

            return true;
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return false;
        }
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
        state.ActiveDriverOrdinal = 0;
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
        state.ActiveDriverOrdinal = 0;
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
        var preserveCloseThrow = preserveExistingThrow;
        List<ActiveDriverSlot>? activeDriverSlots = null;
        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            var slotValue = slots[slotIndex];
            if (slotValue.TryGetObject<IteratorDriverState>(out var iteratorState) &&
                iteratorState.ActiveDriverOrdinal > 0)
            {
                activeDriverSlots ??= new List<ActiveDriverSlot>();
                activeDriverSlots.Add(new ActiveDriverSlot(slotIndex, iteratorState.ActiveDriverOrdinal));
                continue;
            }

            if (slotValue.TryGetObject<ForInDriverState>(out var forInState) &&
                forInState.ActiveDriverOrdinal > 0)
            {
                activeDriverSlots ??= new List<ActiveDriverSlot>();
                activeDriverSlots.Add(new ActiveDriverSlot(slotIndex, forInState.ActiveDriverOrdinal));
                continue;
            }

            if (slotValue.TryGetObject<UnifiedArrayDestructuringState>(out var arrayState) &&
                arrayState.ActiveDriverOrdinal > 0)
            {
                activeDriverSlots ??= new List<ActiveDriverSlot>();
                activeDriverSlots.Add(new ActiveDriverSlot(slotIndex, arrayState.ActiveDriverOrdinal));
                continue;
            }

            if (slotValue.TryGetObject<UnifiedObjectDestructuringState>(out var objectState) &&
                objectState.ActiveDriverOrdinal > 0)
            {
                activeDriverSlots ??= new List<ActiveDriverSlot>();
                activeDriverSlots.Add(new ActiveDriverSlot(slotIndex, objectState.ActiveDriverOrdinal));
            }
        }

        if (activeDriverSlots is not null)
        {
            activeDriverSlots.Sort(static (left, right) => right.Ordinal.CompareTo(left.Ordinal));
            foreach (var activeDriverSlot in activeDriverSlots)
            {
                CleanupDriverStateSlot(
                    activeDriverSlot.SlotIndex,
                    slots,
                    slotEnvironments,
                    context,
                    ref preserveCloseThrow);
            }
        }

        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            CleanupDriverStateSlot(slotIndex, slots, slotEnvironments, context, ref preserveCloseThrow);
        }
    }

    private static void CleanupDriverStatesForControlTarget(
        int controlTarget,
        bool isBreak,
        UnifiedBytecodeProgram program,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context)
    {
        var effectiveTarget = ResolveBytecodeCleanupChainTarget(program.Instructions, controlTarget);
        List<ActiveDriverSlot>? activeDriverSlots = null;
        var targetDriverOrdinal = isBreak ? int.MaxValue : 0;
        foreach (var descriptor in program.DriverDescriptors)
        {
            if (descriptor.StateSlot < 0 ||
                !ShouldCleanupDriverForControlTarget(descriptor, controlTarget, effectiveTarget, program.Instructions) ||
                !TryGetActiveDriverOrdinal(slots, descriptor.StateSlot, out var ordinal))
            {
                continue;
            }

            activeDriverSlots ??= new List<ActiveDriverSlot>();
            activeDriverSlots.Add(new ActiveDriverSlot(descriptor.StateSlot, ordinal));

            if (isBreak && IsDriverBreakTarget(program, controlTarget, descriptor))
            {
                targetDriverOrdinal = Math.Min(targetDriverOrdinal, ordinal);
            }
            else if (!isBreak && IsDriverContinueTarget(program, controlTarget, descriptor))
            {
                targetDriverOrdinal = Math.Max(targetDriverOrdinal, ordinal);
            }
        }

        if (activeDriverSlots is null)
        {
            return;
        }

        var preserveCloseThrow = false;
        activeDriverSlots.Sort(static (left, right) => right.Ordinal.CompareTo(left.Ordinal));
        foreach (var activeDriverSlot in activeDriverSlots)
        {
            if (!ShouldCleanupActiveDriverForControlTarget(
                    activeDriverSlot,
                    targetDriverOrdinal,
                    controlTarget,
                    isBreak,
                    program))
            {
                continue;
            }

            CleanupDriverStateSlot(
                activeDriverSlot.SlotIndex,
                slots,
                slotEnvironments,
                context,
                ref preserveCloseThrow);
        }
    }

    private static bool ShouldCleanupActiveDriverForControlTarget(
        ActiveDriverSlot activeDriverSlot,
        int targetDriverOrdinal,
        int controlTarget,
        bool isBreak,
        UnifiedBytecodeProgram program)
    {
        if (isBreak && targetDriverOrdinal < int.MaxValue)
        {
            return activeDriverSlot.Ordinal >= targetDriverOrdinal;
        }

        if (!isBreak)
        {
            if (targetDriverOrdinal > 0)
            {
                return activeDriverSlot.Ordinal > targetDriverOrdinal;
            }

            foreach (var descriptor in program.DriverDescriptors)
            {
                if (descriptor.StateSlot == activeDriverSlot.SlotIndex &&
                    descriptor.BreakTarget >= 0)
                {
                    return !IsControlTargetInsideDriverBody(program, controlTarget, descriptor);
                }
            }

            return false;
        }

        foreach (var descriptor in program.DriverDescriptors)
        {
            if (descriptor.StateSlot == activeDriverSlot.SlotIndex &&
                descriptor.BreakTarget >= 0)
            {
                return !IsControlTargetInsideDriverBody(program, controlTarget, descriptor);
            }
        }

        return false;
    }

    private static bool IsDriverBreakTarget(
        UnifiedBytecodeProgram program,
        int controlTarget,
        UnifiedBytecodeDriverDescriptor descriptor)
    {
        return descriptor.BreakTarget >= 0 &&
               IsSameLoopControlTarget(program, controlTarget, descriptor.BreakTarget);
    }

    private static bool IsDriverContinueTarget(
        UnifiedBytecodeProgram program,
        int controlTarget,
        UnifiedBytecodeDriverDescriptor descriptor)
    {
        if (descriptor.ContinueTarget >= 0 &&
            IsSameLoopControlTarget(program, controlTarget, descriptor.ContinueTarget))
        {
            return true;
        }

        foreach (var tryDescriptor in program.TryDescriptors)
        {
            if (tryDescriptor.LoopContinueTarget >= 0 &&
                tryDescriptor.LoopBreakTarget >= 0 &&
                IsSameDriverBreakTarget(program, descriptor.BreakTarget, tryDescriptor.LoopBreakTarget) &&
                IsSameLoopControlTarget(program, controlTarget, tryDescriptor.LoopContinueTarget))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsControlTargetInsideDriverBody(
        UnifiedBytecodeProgram program,
        int controlTarget,
        UnifiedBytecodeDriverDescriptor descriptor)
    {
        if (descriptor.BreakTarget < 0)
        {
            return false;
        }

        var target = ResolveCleanupChainTarget(program, controlTarget);
        return descriptor.NextTarget >= 0 &&
               descriptor.BreakTarget >= 0 &&
               target >= descriptor.NextTarget &&
               target < descriptor.BreakTarget;
    }

    private static bool TryGetActiveDriverOrdinal(
        Span<JsValue> slots,
        int slotIndex,
        out int ordinal)
    {
        ordinal = 0;
        if ((uint)slotIndex >= (uint)slots.Length)
        {
            return false;
        }

        var slotValue = slots[slotIndex];
        if (slotValue.TryGetObject<IteratorDriverState>(out var iteratorState))
        {
            ordinal = iteratorState.ActiveDriverOrdinal;
            return ordinal > 0;
        }

        if (slotValue.TryGetObject<ForInDriverState>(out var forInState))
        {
            ordinal = forInState.ActiveDriverOrdinal;
            return ordinal > 0;
        }

        if (slotValue.TryGetObject<UnifiedArrayDestructuringState>(out var arrayState))
        {
            ordinal = arrayState.ActiveDriverOrdinal;
            return ordinal > 0;
        }

        if (slotValue.TryGetObject<UnifiedObjectDestructuringState>(out var objectState))
        {
            ordinal = objectState.ActiveDriverOrdinal;
            return ordinal > 0;
        }

        return false;
    }

    private static void CleanupDriverStateSlot(
        int slotIndex,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        ref bool preserveCloseThrow)
    {
        if (slots[slotIndex].TryGetObject<IteratorDriverState>(out _))
        {
            CloseIteratorDriverState(slotIndex, slots, slotEnvironments, context, preserveCloseThrow);
            preserveCloseThrow |= context.IsThrow;
            return;
        }

        if (slots[slotIndex].TryGetObject<ForInDriverState>(out var forInState))
        {
            CompleteForInDriverState(slotIndex, slots, slotEnvironments, forInState);
            return;
        }

        if (slots[slotIndex].TryGetObject<UnifiedArrayDestructuringState>(out _))
        {
            CloseArrayDestructuringState(slotIndex, slots, slotEnvironments, context, preserveCloseThrow);
            preserveCloseThrow |= context.IsThrow;
            return;
        }

        if (slots[slotIndex].TryGetObject<UnifiedObjectDestructuringState>(out _))
        {
            CloseObjectDestructuringState(slotIndex, slots, slotEnvironments);
        }
    }

    private static JsValue StopWithDriverCleanup(
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        bool preserveExistingThrow)
    {
        CleanupActiveDriverStates(slots, slotEnvironments, context, preserveExistingThrow);
        return JsValue.Undefined;
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
        public int ActiveDriverOrdinal;
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
            ActiveDriverOrdinal = 0;
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

    private sealed class UnifiedObjectDestructuringState : IDisposable
    {
        public IJsObjectLike Source = null!;
        public readonly HashSet<string> UsedKeys = new(StringComparer.Ordinal);
        public int ActiveDriverOrdinal;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ActiveDriverOrdinal = 0;
            UsedKeys.Clear();
            Source = null!;
        }
    }

    private static bool TryGetSourceForObjectDestructuring(
        JsValue sourceValue,
        EvaluationContext context,
        out UnifiedObjectDestructuringState state)
    {
        if (!TypedAstEvaluator.TryToObjectForDestructuring(sourceValue, context, out var source))
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Cannot destructure undefined or null",
                context,
                context.RealmState));
            state = null!;
            return false;
        }

        state = new UnifiedObjectDestructuringState
        {
            Source = source
        };
        return true;
    }

    private static bool TryReadObjectDestructuringProperty(
        int stateSlot,
        string propertyName,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        out JsValue value)
    {
        if (!TryGetDriverState<UnifiedObjectDestructuringState>(slots, stateSlot, out var state))
        {
            throw new InvalidOperationException("Object destructuring state not found.");
        }

        state.UsedKeys.Add(propertyName);

        try
        {
            var hasProperty = JsOps.TryGetPropertyValue(
                JsValue.FromObjectUnsafe(state.Source),
                propertyName,
                out value,
                context);
            if (!context.ShouldStopEvaluation)
            {
                if (!hasProperty)
                {
                    value = JsValue.Undefined;
                }

                return true;
            }
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
        }

        CloseObjectDestructuringState(stateSlot, slots, slotEnvironments);
        value = JsValue.Undefined;
        return false;
    }

    private static bool TryReadObjectDestructuringRest(
        int stateSlot,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
        EvaluationContext context,
        out JsValue restValue)
    {
        if (!TryGetDriverState<UnifiedObjectDestructuringState>(slots, stateSlot, out var state))
        {
            throw new InvalidOperationException("Object destructuring state not found.");
        }

        var restObject = new JsObject();
        if (context.RealmState?.ObjectPrototype is not null)
        {
            restObject.SetPrototype(context.RealmState.ObjectPrototype);
        }

        try
        {
            foreach (var key in state.Source.GetOwnPropertyKeysInOrder())
            {
                if (state.UsedKeys.Contains(key))
                {
                    continue;
                }

                var descriptor = state.Source.GetOwnPropertyDescriptor(key);
                if (descriptor is not { Enumerable: true })
                {
                    continue;
                }

                if (JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(state.Source), key, out var propertyValue,
                        context))
                {
                    restObject.SetProperty(key, propertyValue);
                    continue;
                }

                if (context.ShouldStopEvaluation)
                {
                    break;
                }
            }

            if (!context.ShouldStopEvaluation)
            {
                restValue = JsValue.FromObjectUnsafe(restObject);
                return true;
            }
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
        }

        CloseObjectDestructuringState(stateSlot, slots, slotEnvironments);
        restValue = JsValue.Undefined;
        return false;
    }

    private static void CloseObjectDestructuringState(
        int slotIndex,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments)
    {
        if (!TryGetDriverState<UnifiedObjectDestructuringState>(slots, slotIndex, out var state))
        {
            return;
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
            BinaryOperator.Power => JsOps.Exp(left, right, context),
            BinaryOperator.Equal => JsOps.LooseEquals(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.NotEqual => JsOps.LooseEquals(left, right, context) ? JsValue.False : JsValue.True,
            BinaryOperator.StrictEqual => JsOps.StrictEquals(left, right) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictNotEqual => JsOps.StrictEquals(left, right) ? JsValue.False : JsValue.True,
            BinaryOperator.LessThan => JsOps.LessThan(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.LessThanOrEqual => JsOps.LessThanOrEqual(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.GreaterThan => JsOps.GreaterThan(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.GreaterThanOrEqual => JsOps.GreaterThanOrEqual(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.BitwiseAnd => JsOps.BitAnd(left, right, context),
            BinaryOperator.BitwiseOr => JsOps.BitOr(left, right, context),
            BinaryOperator.BitwiseXor => JsOps.BitXor(left, right, context),
            BinaryOperator.LeftShift => JsOps.LeftShift(left, right, context),
            BinaryOperator.RightShift => JsOps.RightShift(left, right, context),
            BinaryOperator.UnsignedRightShift => JsOps.UnsignedRightShift(left, right, context),
            BinaryOperator.In => TypedAstEvaluator.InOperatorValue(left, right, context),
            BinaryOperator.InstanceOf => TypedAstEvaluator.InstanceOfOperatorValue(left, right, context),
            _ => throw new InvalidOperationException($"Unsupported unified binary operator '{op}'.")
        };
    }

    private static int ExecutePreparedCall(
        int argumentCount,
        ImmutableArray<int> spreadMask,
        bool isDirectEval,
        Span<JsValue> stack,
        int stackPointer,
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments,
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
        EvalHostFunction? singleArgDirectEvalFastHost = null;
        DebugAwareHostFunction? debugFunction = null;
        JsEnvironment? previousDebugEnvironment = null;
        EvaluationContext? previousDebugContext = null;
        JsValue result;
        try
        {
            if (isDirectEval &&
                spreadMask.IsDefaultOrEmpty &&
                argumentCount == 1 &&
                callingEnvironment is not null &&
                callable is EvalHostFunction evalHostFunction &&
                ReferenceEquals(evalHostFunction.Engine, callingEnvironment.RealmState?.Engine))
            {
                singleArgDirectEvalFastHost = evalHostFunction;
            }

            if (callingEnvironment is not null && callable is DebugAwareHostFunction debugAware)
            {
                debugFunction = debugAware;
                previousDebugEnvironment = debugFunction.CurrentJsEnvironment;
                previousDebugContext = debugFunction.CurrentContext;
                debugFunction.CurrentJsEnvironment = callingEnvironment;
                debugFunction.CurrentContext = context;
            }

            if (!spreadMask.IsDefaultOrEmpty)
            {
                // Synchronous spread call (gh2676): flatten spread iterables in source
                // order before invoking. Each pushed argument value is either a positional
                // value or a spread iterable; the mask names the spread positions.
                var spreadArguments = MaterializeSpreadCallArguments(
                    argumentCount,
                    spreadMask,
                    stack,
                    calleeIndex + 1,
                    context);
                result = TypedAstEvaluator.InvokeCallableJsValue(
                    callable,
                    spreadArguments,
                    thisValue,
                    context,
                    callingEnvironment);
            }
            else
            {
                result = argumentCount switch
                {
                    0 => TypedAstEvaluator.InvokeCallableNoArgs(callable, thisValue, context, callingEnvironment),
                    1 => singleArgDirectEvalFastHost is not null
                        ? singleArgDirectEvalFastHost.InvokeDirectSingleArgumentFast(
                            stack[calleeIndex + 1],
                            context,
                            callingEnvironment!,
                            context.InClassFieldInitializer)
                        : TypedAstEvaluator.InvokeCallableSingleArg(
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
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            result = signal.ThrownValue;
        }
        finally
        {
            if (singleArgDirectEvalFastHost is not null)
            {
                SyncSlotsFromEnvironments(slots, slotEnvironments);
            }

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

    private static void SyncSlotsFromEnvironments(
        Span<JsValue> slots,
        JsEnvironment?[]? slotEnvironments)
    {
        if (slotEnvironments is null)
        {
            return;
        }

        var count = Math.Min(slots.Length, slotEnvironments.Length);
        for (var i = 0; i < count; i++)
        {
            if (slotEnvironments[i] is { } environment &&
                (uint)i < (uint)environment.SlotCount)
            {
                slots[i] = environment.GetSlotByIndex(i).Value;
            }
        }
    }

    private static bool ShouldCleanupDriverForControlTarget(
        UnifiedBytecodeDriverDescriptor descriptor,
        int controlTarget,
        int effectiveTarget,
        ImmutableArray<UnifiedBytecodeInstruction> instructions)
    {
        if (descriptor.BreakTarget < 0)
        {
            return false;
        }

        var effectiveBreakTarget = ResolveBytecodeCleanupChainTarget(instructions, descriptor.BreakTarget);
        if (controlTarget == descriptor.BreakTarget ||
            effectiveTarget == descriptor.BreakTarget ||
            controlTarget == effectiveBreakTarget ||
            effectiveTarget == effectiveBreakTarget)
        {
            return true;
        }

        if (descriptor.MoveNextTarget < 0)
        {
            return false;
        }

        if (effectiveTarget == descriptor.MoveNextTarget)
        {
            return false;
        }

        return effectiveTarget < descriptor.MoveNextTarget ||
               effectiveTarget >= effectiveBreakTarget;
    }

    private static int ResolveBytecodeCleanupChainTarget(
        ImmutableArray<UnifiedBytecodeInstruction> instructions,
        int target)
    {
        var current = target;
        var remainingCleanupDepth = instructions.Length;
        while ((uint)current < (uint)instructions.Length &&
               remainingCleanupDepth-- > 0 &&
               instructions[current].OpCode is UnifiedBytecodeOpCode.PopEnvironment or UnifiedBytecodeOpCode.LeaveWith)
        {
            current++;
        }

        return current;
    }

    // Synchronous non-spread construct call (`new F(...)`, gh2690). Mirrors the
    // spec-conformant construct reference helper: the constructor and its simple
    // arguments are already on the stack ([constructor, arg0, .. arg(n-1)]); invoke
    // [[Construct]] with the constructor itself as new.target (per `new F()` semantics)
    // and replace the constructor slot with the result. Spread-onto-construct is declined
    // by eligibility, so only the no-spread path is modeled here.
    private static int ExecutePreparedConstruct(
        int argumentCount,
        ImmutableArray<int> spreadMask,
        Span<JsValue> stack,
        int stackPointer,
        EvaluationContext context)
    {
        var constructorIndex = stackPointer - argumentCount - 1;
        var constructorValue = stack[constructorIndex];

        if (!JsOps.IsConstructor(constructorValue) ||
            !constructorValue.TryGetObject<IJsCallable>(out var callable))
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Target is not a constructor",
                context,
                context.RealmState));
            stack[constructorIndex] = JsValue.Undefined;
            return constructorIndex + 1;
        }

        try
        {
            if (spreadMask.IsDefaultOrEmpty)
            {
                stack[constructorIndex] = ConstructNoSpread(
                    callable,
                    stack,
                    constructorIndex + 1,
                    argumentCount,
                    context.RealmState);
            }
            else
            {
                var spreadArguments = MaterializeSpreadCallArguments(
                    argumentCount,
                    spreadMask,
                    stack,
                    constructorIndex + 1,
                    context);
                stack[constructorIndex] = ReflectHelper.Construct(
                    callable,
                    spreadArguments,
                    callable,
                    context.RealmState);
            }
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            stack[constructorIndex] = signal.ThrownValue;
        }

        return constructorIndex + 1;
    }

    private static int ExecutePreparedSuperConstruct(
        int argumentCount,
        Span<JsValue> stack,
        int stackPointer,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var baseIndex = stackPointer - argumentCount;
        var callDepthIncremented = false;

        try
        {
            var superBindingForCall = environment.ExpectSuperBinding(context);

            if (!environment.TryResolveSuperConstructorForCall(superBindingForCall, out var constructorValue))
            {
                throw new InvalidOperationException(
                    "Super constructor is not available in this context.");
            }

            JsEnvironment? thisInitializationEnvironment = null;
            var thisInitializationValue = JsValue.Undefined;
            if (environment.TryGetObject<JsEnvironment>(Symbol.LexicalThisEnvironment, out var lexicalThisEnv) ||
                (environment.TryFindBindingJsValue(Symbol.LexicalThisEnvironment, true, out _, out var lexicalEnvValue) &&
                 lexicalEnvValue.TryGetObject<JsEnvironment>(out lexicalThisEnv)))
            {
                thisInitializationEnvironment = lexicalThisEnv;
                if (lexicalThisEnv.TryGetJsValue(Symbol.ThisInitialized, out var lexicalInitValue))
                {
                    thisInitializationValue = lexicalInitValue;
                }
            }
            else if (environment.TryFindBindingJsValue(Symbol.This, true, out var thisEnv, out _))
            {
                thisInitializationEnvironment = thisEnv.HasBindingLocal(Symbol.ThisInitialized)
                    ? thisEnv
                    : thisEnv.ResolveConstructorThisEnvironment();
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

            if (!JsOps.IsConstructor(constructorValue) ||
                !constructorValue.TryGetObject<IJsCallable>(out var callable))
            {
                var error = StandardLibrary.CreateTypeError(
                    "Super constructor is not a constructor",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                stack[baseIndex] = JsValue.Undefined;
                return baseIndex + 1;
            }

            var newTargetValue = environment.TryGetJsValue(Symbol.NewTarget, out var inheritedNewTarget)
                ? inheritedNewTarget
                : JsValue.Undefined;
            var newTargetCallable = newTargetValue.TryGetObject<IJsCallable>(out var nt)
                ? nt
                : callable;

            var result = ConstructNoSpread(
                callable,
                newTargetCallable,
                stack,
                baseIndex,
                argumentCount,
                context.RealmState);

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

                if (!alreadyInitialized.IsUndefined &&
                    (alreadyInitialized.IsBoolean
                        ? alreadyInitialized.AsBoolean()
                        : JsOps.ToBoolean(alreadyInitialized)))
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

            stack[baseIndex] = result;
            return baseIndex + 1;
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            stack[baseIndex] = signal.ThrownValue;
            return baseIndex + 1;
        }
        finally
        {
            if (callDepthIncremented)
            {
                context.CallDepth--;
            }
        }
    }

    // Invoke [[Construct]] with the constructor as new.target, mirroring the
    // no-spread construct reference helper: small arity uses the allocation-free
    // value-args structs, larger arity materializes an argument array.
    private static JsValue ConstructNoSpread(
        IJsCallable callable,
        Span<JsValue> stack,
        int firstArgumentIndex,
        int argumentCount,
        RealmState realm)
    {
        return argumentCount switch
        {
            0 => ReflectHelper.Construct(callable, EmptyValueArgs.Instance, callable, realm),
            1 => ReflectHelper.Construct(
                callable,
                new SingleValueArgs(stack[firstArgumentIndex]),
                callable,
                realm),
            2 => ReflectHelper.Construct(
                callable,
                new TwoValueArgs(stack[firstArgumentIndex], stack[firstArgumentIndex + 1]),
                callable,
                realm),
            3 => ReflectHelper.Construct(
                callable,
                new ThreeValueArgs(
                    stack[firstArgumentIndex],
                    stack[firstArgumentIndex + 1],
                    stack[firstArgumentIndex + 2]),
                callable,
                realm),
            4 => ReflectHelper.Construct(
                callable,
                new FourValueArgs(
                    stack[firstArgumentIndex],
                    stack[firstArgumentIndex + 1],
                    stack[firstArgumentIndex + 2],
                    stack[firstArgumentIndex + 3]),
                callable,
                realm),
            _ => ConstructNoSpreadMany(callable, stack, firstArgumentIndex, argumentCount, realm)
        };
    }

    private static JsValue ConstructNoSpread(
        IJsCallable callable,
        IJsCallable newTargetCallable,
        Span<JsValue> stack,
        int firstArgumentIndex,
        int argumentCount,
        RealmState realm)
    {
        return argumentCount switch
        {
            0 => ReflectHelper.Construct(callable, EmptyValueArgs.Instance, newTargetCallable, realm),
            1 => ReflectHelper.Construct(
                callable,
                new SingleValueArgs(stack[firstArgumentIndex]),
                newTargetCallable,
                realm),
            2 => ReflectHelper.Construct(
                callable,
                new TwoValueArgs(stack[firstArgumentIndex], stack[firstArgumentIndex + 1]),
                newTargetCallable,
                realm),
            3 => ReflectHelper.Construct(
                callable,
                new ThreeValueArgs(
                    stack[firstArgumentIndex],
                    stack[firstArgumentIndex + 1],
                    stack[firstArgumentIndex + 2]),
                newTargetCallable,
                realm),
            4 => ReflectHelper.Construct(
                callable,
                new FourValueArgs(
                    stack[firstArgumentIndex],
                    stack[firstArgumentIndex + 1],
                    stack[firstArgumentIndex + 2],
                    stack[firstArgumentIndex + 3]),
                newTargetCallable,
                realm),
            _ => ConstructNoSpreadMany(callable, newTargetCallable, stack, firstArgumentIndex, argumentCount, realm)
        };
    }

    private static JsValue ConstructNoSpreadMany(
        IJsCallable callable,
        Span<JsValue> stack,
        int firstArgumentIndex,
        int argumentCount,
        RealmState realm)
    {
        var arguments = new JsValue[argumentCount];
        for (var i = 0; i < argumentCount; i++)
        {
            arguments[i] = stack[firstArgumentIndex + i];
        }

        return ReflectHelper.Construct(callable, arguments, callable, realm);
    }

    private static JsValue ConstructNoSpreadMany(
        IJsCallable callable,
        IJsCallable newTargetCallable,
        Span<JsValue> stack,
        int firstArgumentIndex,
        int argumentCount,
        RealmState realm)
    {
        var arguments = new JsValue[argumentCount];
        for (var i = 0; i < argumentCount; i++)
        {
            arguments[i] = stack[firstArgumentIndex + i];
        }

        return ReflectHelper.Construct(callable, arguments, newTargetCallable, realm);
    }

    // Invocation-boundary operand packing for spread calls/constructs. Mirrors
    // UnifiedBytecodeCompiler.EncodeCallBoundaryOperand: low 16 bits are the pushed
    // argument value count, high bits are spreadMaskIndex + 1 (0 means "no spread"),
    // and bit 30 marks syntactic direct eval.
    private const int CallBoundarySpreadMask = 0x3FFF;
    private const int CallBoundaryDirectEvalFlag = 1 << 30;

    private static int DecodeCallBoundaryArgumentCount(int operand) => operand & 0xFFFF;

    private static bool DecodeCallBoundaryIsDirectEval(int operand) => (operand & CallBoundaryDirectEvalFlag) != 0;

    private static ImmutableArray<int> DecodeCallBoundarySpreadMask(
        UnifiedBytecodeProgram program,
        int operand)
    {
        var encoded = (operand >> 16) & CallBoundarySpreadMask;
        if (encoded <= 0 || program.CallSpreadMasks.IsDefaultOrEmpty)
        {
            return default;
        }

        return program.CallSpreadMasks[encoded - 1];
    }

    private static JsValue[] MaterializeSpreadCallArguments(
        int argumentCount,
        ImmutableArray<int> spreadMask,
        Span<JsValue> stack,
        int argumentsStartIndex,
        EvaluationContext context)
    {
        // One pushed value per argument position; spread positions hold the iterable to
        // expand. Flatten left-to-right so iteration order and side effects are preserved.
        var arguments = ImmutableArray.CreateBuilder<JsValue>(argumentCount);
        var spreadMaskPosition = 0;
        for (var i = 0; i < argumentCount; i++)
        {
            var argumentValue = stack[argumentsStartIndex + i];
            if (spreadMaskPosition < spreadMask.Length && spreadMask[spreadMaskPosition] == i)
            {
                arguments.AddRange(TypedAstEvaluator.EnumerateSpread(argumentValue, context));
                spreadMaskPosition++;
            }
            else
            {
                arguments.Add(argumentValue);
            }
        }

        return arguments.ToArray();
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

    private static void SetInactiveCatchBindingReferenceError(
        UnifiedBytecodeProgram program,
        int slotIndex,
        EvaluationContext context)
    {
        var slotName = GetSlotName(program, slotIndex) ?? "catch binding";
        context.SetThrow(StandardLibrary.CreateReferenceError(
            $"{slotName} is not defined",
            context,
            context.RealmState));
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

    private static void PrepareNamedSuperCallTarget(
        UnifiedBytecodeProgram program,
        int callTargetIndex,
        JsEnvironment environment,
        Span<JsValue> stack,
        ref int stackPointer,
        EvaluationContext context)
    {
        var callTarget = program.CallTargetConstants[callTargetIndex];
        if (callTarget.Kind != UnifiedBytecodeCallTargetKind.NamedSuperMember ||
            (uint)callTarget.NameConstantIndex >= (uint)program.StringConstants.Length)
        {
            throw new InvalidOperationException(
                "Named super call-target preparation requires a named super member call target constant.");
        }

        LoadNamedSuperCallTarget(
            program.StringConstants[callTarget.NameConstantIndex],
            environment,
            context,
            out var receiver,
            out var callee);
        stack[stackPointer++] = receiver;
        stack[stackPointer++] = callee;
    }

    private static void PrepareComputedSuperCallTarget(
        UnifiedBytecodeProgram program,
        int callTargetIndex,
        JsEnvironment environment,
        Span<JsValue> stack,
        ref int stackPointer,
        EvaluationContext context)
    {
        var callTarget = program.CallTargetConstants[callTargetIndex];
        if (callTarget.Kind != UnifiedBytecodeCallTargetKind.ComputedSuperMember)
        {
            throw new InvalidOperationException(
                "Computed super call-target preparation requires a computed super member call target constant.");
        }

        var propertyKey = stack[--stackPointer];
        var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
        if (context.ShouldStopEvaluation)
        {
            stack[stackPointer++] = JsValue.Undefined;
            stack[stackPointer++] = JsValue.Undefined;
            return;
        }

        LoadNamedSuperCallTarget(propertyName, environment, context, out var receiver, out var callee);
        stack[stackPointer++] = receiver;
        stack[stackPointer++] = callee;
    }

    private static void LoadNamedSuperCallTarget(
        string propertyName,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue receiver,
        out JsValue callee)
    {
        var binding = GetSuperBindingForRead(environment, context);
        if (binding is null)
        {
            receiver = JsValue.Undefined;
            callee = JsValue.Undefined;
            return;
        }

        receiver = binding.ThisValue;
        callee = context.ShouldStopEvaluation
            ? JsValue.Undefined
            : binding.TryGetProperty(propertyName, out var value)
                ? value
                : JsValue.Undefined;
    }

    private static bool EnsureSuperReference(JsEnvironment environment, EvaluationContext context)
    {
        if (environment.IsThisInitializationKnownTrue(context))
        {
            return true;
        }

        context.SetThrow(StandardLibrary.CreateReferenceError(
            "Super is not available in this context.",
            context,
            context.RealmState));
        return false;
    }

    private static SuperBinding? GetSuperBindingForRead(JsEnvironment environment, EvaluationContext context)
    {
        if (!EnsureSuperReference(environment, context))
        {
            return null;
        }

        var binding = environment.ExpectSuperBinding(context);
        if (!binding.IsThisInitialized || binding.ThisValue.IsUndefined || binding.ThisValue.IsUninitialized)
        {
            context.SetThrow(StandardLibrary.CreateReferenceError(
                "Super is not available in this context.",
                context,
                context.RealmState));
            return null;
        }

        if (binding.Prototype is null)
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Cannot read properties of null (reading from super)",
                context,
                context.RealmState));
            return null;
        }

        return binding;
    }

    private static JsValue GetNamedSuperPropertyValue(
        string propertyName,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var binding = GetSuperBindingForRead(environment, context);
        if (binding is null || context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return binding.TryGetProperty(propertyName, out var value)
            ? value
            : JsValue.Undefined;
    }

    private static JsValue GetComputedSuperPropertyValue(
        JsValue propertyKey,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return GetNamedSuperPropertyValue(propertyName, environment, context);
    }

    private static JsValue SetNamedSuperPropertyValue(
        string propertyName,
        bool allowNameInference,
        JsValue value,
        JsEnvironment environment,
        EvaluationContext context,
        bool isStrict)
    {
        if (allowNameInference &&
            value is { Kind: JsValueKind.Object, ObjectValue: TypedAstEvaluator.IFunctionNameTarget nameTarget })
        {
            nameTarget.EnsureHasName(propertyName);
        }

        return AssignToSuperBinding(environment, context, propertyName, value, isStrict);
    }

    private static JsValue SetComputedSuperPropertyValue(
        JsValue propertyKey,
        bool allowNameInference,
        JsValue value,
        JsEnvironment environment,
        EvaluationContext context,
        bool isStrict)
    {
        var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return SetNamedSuperPropertyValue(
            propertyName,
            allowNameInference,
            value,
            environment,
            context,
            isStrict);
    }

    private static JsValue UpdateNamedSuperPropertyValue(
        string propertyName,
        bool isIncrement,
        bool isPrefix,
        JsEnvironment environment,
        EvaluationContext context,
        bool isStrict)
    {
        var currentValue = GetNamedSuperPropertyValue(propertyName, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        GetUpdatedNumericValue(currentValue, isIncrement, context, out var oldNumericValue, out var newValue);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        AssignToSuperBinding(environment, context, propertyName, newValue, isStrict);
        return isPrefix ? newValue : oldNumericValue;
    }

    private static JsValue UpdateComputedSuperPropertyValue(
        JsValue propertyKey,
        bool isIncrement,
        bool isPrefix,
        JsEnvironment environment,
        EvaluationContext context,
        bool isStrict)
    {
        var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return UpdateNamedSuperPropertyValue(
            propertyName,
            isIncrement,
            isPrefix,
            environment,
            context,
            isStrict);
    }

    private static JsValue AssignToSuperBinding(
        JsEnvironment environment,
        EvaluationContext context,
        string propertyName,
        JsValue value,
        bool isStrict)
    {
        var binding = GetSuperBindingForRead(environment, context);
        if (binding is null || context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        if (!binding.TrySetProperty(propertyName, value, out _) &&
            (environment.IsStrict || isStrict))
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                $"Cannot assign to read only property '{propertyName}' of object",
                context,
                context.RealmState));
            return JsValue.Undefined;
        }

        return value;
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

        if (DecodeDefineObjectPropertyAllowNameInference(operand) &&
            propertyValue is { Kind: JsValueKind.Object, ObjectValue: TypedAstEvaluator.IFunctionNameTarget nameTarget })
        {
            nameTarget.EnsureHasName(propertyName);
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
        if (DecodeDefineObjectPropertyAllowNameInference(operand) &&
            propertyValue is { Kind: JsValueKind.Object, ObjectValue: TypedAstEvaluator.IFunctionNameTarget nameTarget })
        {
            nameTarget.EnsureHasName(propertyName);
        }

        targetObject.DefineDefaultDataProperty(propertyName, propertyValue);
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

        DefineAccessorProperty(
            targetObject,
            propertyName,
            accessorKind == ObjectAccessorKind.Getter ? callable : null,
            accessorKind == ObjectAccessorKind.Setter ? callable : null);
    }

    private static void DefineAccessorProperty(
        JsObject targetObject,
        string propertyName,
        IJsCallable? getter,
        IJsCallable? setter)
    {
        var descriptor = targetObject.GetOwnPropertyDescriptor(propertyName) ??
                         new PropertyDescriptor { Enumerable = true, Configurable = true };

        descriptor.Get = getter ?? descriptor.Get;
        descriptor.Set = setter ?? descriptor.Set;
        targetObject.DefineProperty(propertyName, descriptor);
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

        if (callable is IHomeObjectConfigurableCallable homeObjectConfigurable)
        {
            homeObjectConfigurable.SetHomeObject(targetObject);
            homeObjectConfigurable.DisableConstruction();
        }

        if (callable is TypedAstEvaluator.IFunctionNameTarget nameTarget)
        {
            var displayName = accessorKind switch
            {
                ObjectAccessorKind.Getter => $"get {BuildFunctionNameDisplay(propertyName)}",
                ObjectAccessorKind.Setter => $"set {BuildFunctionNameDisplay(propertyName)}",
                _ => BuildFunctionNameDisplay(propertyName)
            };
            nameTarget.EnsureHasName(displayName);
        }

        return callable;
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
                targetObject.DefineProperty(
                    key,
                    new PropertyDescriptor
                    {
                        JsValue = JsValue.FromObjectUnsafe(value),
                        Writable = true,
                        Enumerable = true,
                        Configurable = true
                    });
            }

            return;
        }

        var accessor = spreadValue.ObjectValue is IJsPropertyAccessor propertyAccessor
            ? propertyAccessor
            : TypedAstEvaluator.TryToObjectForDestructuring(spreadValue, context, out var objectLike)
                ? objectLike
                : null;
        if (accessor is null)
        {
            return;
        }

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
            targetObject.DefineProperty(
                key,
                new PropertyDescriptor
                {
                    JsValue = spreadPropertyValue,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                });
        }
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

    private static int DecodeUpdateIndex(int operand) => operand >> 2;

    private static int DecodeRegexLiteralPatternOperand(int operand) => operand >> 8;

    private static byte DecodeRegexLiteralFlagsOperand(int operand) => (byte)(operand & 0xFF);

    private static int DecodeDynamicStoreNameOperand(int operand) => operand >> 1;

    private static bool DecodeDynamicStoreAllowsNameInference(int operand) =>
        (operand & 1) != 0;

    private static int DecodeDeclarationBindingTargetIndex(int operand) =>
        operand >> DeclarationBindingTargetShift;

    private static bool DecodeDeclarationBindingTargetHasInitializer(int operand) =>
        (operand & DeclarationBindingTargetHasInitializerFlag) != 0;

    private static VariableKind DecodeDeclarationBindingTargetVariableKind(int operand) =>
        (VariableKind)(operand & 0x7);

    private static int DecodeDefineObjectPropertyNameOperand(int operand) => operand >> 3;

    private static bool DecodeDefineObjectPropertyIsPrototypeMutation(int operand) =>
        (operand & DefineObjectPropertyPrototypeMutationFlag) != 0;

    private static bool DecodeDefineObjectPropertyAllowNameInference(int operand) =>
        (operand & DefineObjectPropertyAllowNameInferenceFlag) != 0;

    private static bool DecodeDefineObjectPropertyIsKnownNewProperty(int operand) =>
        (operand & DefineObjectPropertyKnownNewPropertyFlag) != 0;

    private static int DecodeObjectAccessorNameOperand(int operand) => operand >> 1;

    private static ObjectAccessorKind DecodeObjectAccessorKind(int operand) =>
        (operand & 1) == 0 ? ObjectAccessorKind.Getter : ObjectAccessorKind.Setter;

    private static bool DecodeIsIncrement(int operand) => (operand & 1) != 0;

    private static bool DecodeIsPrefix(int operand) => (operand & 2) != 0;
}
