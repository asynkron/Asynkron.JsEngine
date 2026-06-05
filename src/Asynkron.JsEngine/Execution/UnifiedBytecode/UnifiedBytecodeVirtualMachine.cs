using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
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
    private const int FunctionDeclarationIndexMask = 0xFFFF;
    private const int FunctionDeclarationNameIndexShift = 16;

    private readonly record struct UnifiedSlotEnvironmentBinding(
        JsEnvironment Environment,
        int SlotIndex);

    private readonly record struct EnvironmentScopeFrame(
        JsEnvironment Environment,
        ImmutableArray<int> SlotIndices,
        UnifiedSlotEnvironmentBinding?[] PreviousSlotEnvironments);

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
        var stackShortCircuitFlags = program.RequiresShortCircuitStackFlags
            ? new ulong[(stack.Length + 63) >> 6]
            : null;
        var currentCallingEnvironment = callingEnvironment;

        var slotEnvironments = callingEnvironment is null
            ? null
            : InitializeSlotEnvironments(program, callingEnvironment);
        if (callingEnvironment is not null)
        {
            SyncEnvironmentToUnifiedSlots(program, slots, slotEnvironments, callingEnvironment);
        }

        EnvironmentScopeFrame[]? environmentStack = null;
        var environmentStackCount = 0;
        AssignmentReference[]? dynamicIdentifierReferences = null;
        var dynamicIdentifierReferenceCount = 0;
        bool[]? inactiveCatchBindingSlots = null;
        bool[]? constSlots = null;
        // Seed the per-slot const bitmap with the function/block-scope const declarations recorded by the
        // compiler (own-slot StoreSlot/UpdateSlot enforcement). Loop-head TDZ consts are added later via
        // TdzHeadInit; block-scope consts are added when their PushEnvironment scope is entered.
        if (!program.ConstSlotIndices.IsDefaultOrEmpty)
        {
            var programConstSlots = program.ConstSlotIndices;
            constSlots = new bool[slots.Length];
            for (var i = 0; i < programConstSlots.Length; i++)
            {
                var constSlotIndex = programConstSlots[i];
                if ((uint)constSlotIndex < (uint)constSlots.Length)
                {
                    constSlots[constSlotIndex] = true;
                }
            }
        }

        Stack<TryFrame>? tryStack = null;
        var nextActiveDriverOrdinal = 0;

        var programCounter = 0;
        var instructions = program.Instructions;
        bool GetShortCircuitFlag(int index)
        {
            return stackShortCircuitFlags is not null &&
                (uint)index < (uint)stack.Length &&
                GetStackFlag(stackShortCircuitFlags, index);
        }

        void SetShortCircuitFlag(int index, bool value)
        {
            if (stackShortCircuitFlags is not null && (uint)index < (uint)stack.Length)
            {
                SetStackFlag(stackShortCircuitFlags, index, value);
            }
        }

        void ClearShortCircuitFlag(int index)
        {
            SetShortCircuitFlag(index, false);
        }

        void ClearTopTwoShortCircuitFlags()
        {
            ClearShortCircuitFlag(stackPointer - 2);
            ClearShortCircuitFlag(stackPointer - 1);
        }

        void PushValue(JsValue value)
        {
            stack[stackPointer] = value;
            ClearShortCircuitFlag(stackPointer);
            stackPointer++;
        }

        void PushValueWithShortCircuitFlag(JsValue value, bool wasShortCircuited)
        {
            stack[stackPointer] = value;
            SetShortCircuitFlag(stackPointer, wasShortCircuited);
            stackPointer++;
        }

        void ReplaceTopValue(JsValue value)
        {
            stack[stackPointer - 1] = value;
            ClearShortCircuitFlag(stackPointer - 1);
        }

        void ReplaceTopValueWithShortCircuitFlag(JsValue value, bool wasShortCircuited)
        {
            stack[stackPointer - 1] = value;
            SetShortCircuitFlag(stackPointer - 1, wasShortCircuited);
        }

        void CopyShortCircuitFlag(int source, int target)
        {
            SetShortCircuitFlag(target, GetShortCircuitFlag(source));
        }

        void SwapShortCircuitFlags(int left, int right)
        {
            if (stackShortCircuitFlags is null)
            {
                return;
            }

            var leftValue = GetStackFlag(stackShortCircuitFlags, left);
            SetStackFlag(stackShortCircuitFlags, left, GetStackFlag(stackShortCircuitFlags, right));
            SetStackFlag(stackShortCircuitFlags, right, leftValue);
        }

        void RotateShortCircuitFlagsRight(int first, int second, int third)
        {
            if (stackShortCircuitFlags is null)
            {
                return;
            }

            var thirdValue = GetStackFlag(stackShortCircuitFlags, third);
            SetStackFlag(stackShortCircuitFlags, third, GetStackFlag(stackShortCircuitFlags, second));
            SetStackFlag(stackShortCircuitFlags, second, GetStackFlag(stackShortCircuitFlags, first));
            SetStackFlag(stackShortCircuitFlags, first, thirdValue);
        }

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

                        PushValue(slotValue);
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.LoadDynamicIdentifier:
                        var dynamicLoadEnvironment = RequireDynamicEnvironment(currentCallingEnvironment);
                        PushValue(GetDynamicIdentifierValue(
                            program.StringConstants[instruction.Operand],
                            dynamicLoadEnvironment,
                            context));
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
                        PushValue(ResolveCurrentThisValue(currentCallingEnvironment, thisValue, context));
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

                    case UnifiedBytecodeOpCode.LoadNewTarget:
                        PushValue(newTarget);
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.LoadImportMeta:
                        PushValue(GetImportMeta(currentCallingEnvironment, context));
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.LoadTemplateObject:
                        PushValue(JsValue.FromJsArray(GetOrCreateTemplateObject(
                            program.TemplateObjectConstants[instruction.Operand],
                            context)));
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.LoadLiteral:
                        PushValue(program.LiteralConstants[instruction.Operand]);
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.LoadRegexLiteral:
                        PushValue(JsValue.FromObjectUnsafe(
                            RegExpHelper.CreateRegExpLiteral(
                                program.StringConstants[DecodeRegexLiteralPatternOperand(instruction.Operand)],
                                DecodeRegexLiteralFlagsOperand(instruction.Operand),
                                context.RealmState)));
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

                        PushValue(JsValue.Undefined);
                        PushValue(callableValue);
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.PrepareIdentifierOptionalCallTarget:
                        {
                            var optCallTargetIdx = instruction.Operand & 0xFFFF;
                            var optJumpTarget = instruction.Operand >> 16;
                            var optCallTarget = program.CallTargetConstants[optCallTargetIdx];
                            if (optCallTarget.Kind != UnifiedBytecodeCallTargetKind.Identifier)
                            {
                                throw new InvalidOperationException(
                                    "Optional identifier call-target preparation requires an identifier call target constant.");
                            }

                            if (IsInactiveCatchBindingSlot(inactiveCatchBindingSlots, optCallTarget.SlotIndex))
                            {
                                SetInactiveCatchBindingReferenceError(program, optCallTarget.SlotIndex, context);
                                if (TryHandleCurrentContextThrow(slots))
                                {
                                    break;
                                }

                                return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                            }

                            var optCallableValue = slots[optCallTarget.SlotIndex];
                            if (optCallableValue.IsUninitialized)
                            {
                                SetUninitializedSlotReferenceError(program, optCallTarget.SlotIndex, context);
                                if (TryHandleCurrentContextThrow(slots))
                                {
                                    break;
                                }

                                return StopWithDriverCleanup(slots, slotEnvironments, context, true);
                            }

                            if (optCallableValue.IsNullOrUndefined)
                            {
                                PushValue(JsValue.Undefined);
                                programCounter = optJumpTarget;
                                break;
                            }

                            PushValue(JsValue.Undefined);
                            PushValue(optCallableValue);
                            programCounter++;
                            break;
                        }

                    case UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget:
                        var dynamicCallEnvironment = RequireDynamicEnvironment(currentCallingEnvironment);
                        PrepareDynamicIdentifierCallTarget(
                            program.StringConstants[instruction.Operand],
                            dynamicCallEnvironment,
                            stack,
                            ref stackPointer,
                            context);
                        ClearTopTwoShortCircuitFlags();
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

                    case UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget:
                        {
                            var dynamicOptionalCallEnvironment = RequireDynamicEnvironment(currentCallingEnvironment);
                            var dynamicOptionalNameIndex = instruction.Operand & 0xFFFF;
                            var dynamicOptionalJumpTarget = instruction.Operand >> 16;
                            PrepareDynamicIdentifierCallTarget(
                                program.StringConstants[dynamicOptionalNameIndex],
                                dynamicOptionalCallEnvironment,
                                stack,
                                ref stackPointer,
                                context);
                            ClearTopTwoShortCircuitFlags();
                            if (context.ShouldStopEvaluation)
                            {
                                if (TryHandleCurrentContextThrow(slots))
                                {
                                    break;
                                }

                                return JsValue.Undefined;
                            }

                            var dynamicOptionalCallable = stack[stackPointer - 1];
                            if (dynamicOptionalCallable.IsNullOrUndefined)
                            {
                                stackPointer -= 2;
                                PushValue(JsValue.Undefined);
                                programCounter = dynamicOptionalJumpTarget;
                                break;
                            }

                            programCounter++;
                            break;
                        }

                    case UnifiedBytecodeOpCode.PrepareNamedCallTarget:
                        var namedCallTarget = program.CallTargetConstants[instruction.Operand];
                        if (namedCallTarget.Kind != UnifiedBytecodeCallTargetKind.NamedMember ||
                            (uint)namedCallTarget.NameConstantIndex >= (uint)program.StringConstants.Length)
                        {
                            throw new InvalidOperationException(
                                "Named member call-target preparation requires a named member call target constant.");
                        }

                        var namedReceiver = stack[stackPointer - 1];
                        if (GetShortCircuitFlag(stackPointer - 1))
                        {
                            PushValueWithShortCircuitFlag(JsValue.Undefined, wasShortCircuited: true);
                        }
                        else
                        {
                            PushValue(GetNamedPropertyValue(
                                namedReceiver,
                                program.StringConstants[namedCallTarget.NameConstantIndex],
                                context));
                            if (context.ShouldStopEvaluation)
                            {
                                if (TryHandleCurrentContextThrow(slots))
                                {
                                    break;
                                }

                                return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                            }
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
                        if (GetShortCircuitFlag(stackPointer - 1))
                        {
                            PushValueWithShortCircuitFlag(JsValue.Undefined, wasShortCircuited: true);
                        }
                        else
                        {
                            PushValue(GetComputedCallTargetValue(computedCallReceiver, computedCallKey, context));
                            if (context.ShouldStopEvaluation)
                            {
                                if (TryHandleCurrentContextThrow(slots))
                                {
                                    break;
                                }

                                return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                            }
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
                                    ReplaceTopValue(JsValue.Undefined);
                                    programCounter = optNamedJumpTarget;
                                    break;
                                }

                                PushValue(GetNamedPropertyValue(
                                    optReceiver,
                                    program.StringConstants[optNamedCallTarget.NameConstantIndex],
                                    context));
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
                                    ReplaceTopValue(JsValue.Undefined);
                                    programCounter = optNamedJumpTarget;
                                    break;
                                }

                                PushValue(callee);
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
                                ReplaceTopValue(JsValue.Undefined);
                                programCounter = optComputedJumpTarget;
                                break;
                            }

                            PushValue(optComputedCallee);
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
                        ClearTopTwoShortCircuitFlags();
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
                        ClearTopTwoShortCircuitFlags();
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
                        ClearShortCircuitFlag(stackPointer - 1);
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
                            slots,
                            slotEnvironments,
                            context);
                        ClearShortCircuitFlag(stackPointer - 1);
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
                            DecodeCallBoundaryArgumentCount(instruction.Operand),
                            DecodeCallBoundarySpreadMask(program, instruction.Operand),
                            stack,
                            stackPointer,
                            slots,
                            slotEnvironments,
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            context);
                        ClearShortCircuitFlag(stackPointer - 1);
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

                        if (IsConstSlot(instruction.Operand, constSlots, slotEnvironments))
                        {
                            SetConstantSlotTypeError(program, instruction.Operand, context);
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

                        if (IsConstSlot(updateSlotIndex, constSlots, slotEnvironments))
                        {
                            SetConstantSlotTypeError(program, updateSlotIndex, context);
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
                        PushValue(DecodeIsPrefix(instruction.Operand) ? newSlotValue : oldSlotNumericValue);

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

                    case UnifiedBytecodeOpCode.DeclareDynamicLexical:
                        var declaredDynamicLexicalName =
                            program.StringConstants[DecodeDynamicStoreNameOperand(instruction.Operand)];
                        DeclareDynamicLexical(
                            declaredDynamicLexicalName,
                            DecodeDynamicLexicalDeclarationIsConst(instruction.Operand),
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

                        // A lexical declaration target can carry BOTH a materialized-env binding (written
                        // above) AND a flat slot that own-scope LoadSlot reads use. The dynamic-lexical
                        // declaration only puts the binding into the environment in its TDZ (uninitialized)
                        // state; mirror that TDZ state into the bound flat slot so a premature read still
                        // throws and a later InitializeDynamicLexical can lift it (see below).
                        MirrorDynamicLexicalToFlatSlot(
                            slotEnvironments,
                            slots,
                            currentCallingEnvironment,
                            declaredDynamicLexicalName,
                            JsValue.Uninitialized);

                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.InitializeDynamicLexical:
                        var dynamicLexicalValue = stack[--stackPointer];
                        var initializedDynamicLexicalName =
                            program.StringConstants[DecodeDynamicStoreNameOperand(instruction.Operand)];
                        InitializeDynamicLexical(
                            initializedDynamicLexicalName,
                            DecodeDynamicStoreAllowsNameInference(instruction.Operand),
                            dynamicLexicalValue,
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

                        // Keep the bound flat slot in lock-step with the env binding the dynamic-lexical
                        // path just initialized, so own-scope LoadSlot reads of this lexical observe the
                        // initialized value instead of a stale TDZ slot. Without this, a per-iteration
                        // const/let that the compiler lowered to dynamic-lexical ops (e.g. because a loop
                        // body containing `continue` over the per-iteration scope kept its declaration off
                        // the flat-slot path) would throw a spurious "before initialization" error on read.
                        MirrorDynamicLexicalToFlatSlot(
                            slotEnvironments,
                            slots,
                            currentCallingEnvironment,
                            initializedDynamicLexicalName,
                            dynamicLexicalValue);

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

                        PushValue(dynamicIdentifierReferences[dynamicIdentifierReferenceCount - 1].GetJsValue());
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
                        PushValue(ApplyBinaryOperator(op, left, right, context));
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
                        ReplaceTopValue(ResolvePropertyKey(stack[stackPointer - 1], context));
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
                        if (GetShortCircuitFlag(stackPointer - 1))
                        {
                            ReplaceTopValueWithShortCircuitFlag(JsValue.Undefined, wasShortCircuited: true);
                        }
                        else
                        {
                            ReplaceTopValue(GetNamedPropertyValue(
                                stack[stackPointer - 1],
                                program.StringConstants[instruction.Operand],
                                context));
                            if (context.ShouldStopEvaluation)
                            {
                                if (TryHandleCurrentContextThrow(slots))
                                {
                                    break;
                                }

                                return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                            }
                        }

                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.GetNamedPropertyOptional:
                        if (GetShortCircuitFlag(stackPointer - 1) || stack[stackPointer - 1].IsNullOrUndefined)
                        {
                            ReplaceTopValueWithShortCircuitFlag(JsValue.Undefined, wasShortCircuited: true);
                            programCounter++;
                            break;
                        }

                        ReplaceTopValue(GetNamedPropertyValue(
                            stack[stackPointer - 1],
                            program.StringConstants[instruction.Operand],
                            context));
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
                        if (GetShortCircuitFlag(stackPointer - 1) || stack[stackPointer - 1].IsNullOrUndefined)
                        {
                            ReplaceTopValueWithShortCircuitFlag(JsValue.Undefined, wasShortCircuited: true);
                            programCounter = instruction.Operand;
                        }
                        else
                        {
                            programCounter++;
                        }

                        break;

                    case UnifiedBytecodeOpCode.JumpIfShortCircuited:
                        programCounter = GetShortCircuitFlag(stackPointer - 1)
                            ? instruction.Operand
                            : programCounter + 1;
                        break;

                    case UnifiedBytecodeOpCode.GetComputedProperty:
                        var propertyKey = stack[--stackPointer];
                        var target = stack[stackPointer - 1];
                        if (GetShortCircuitFlag(stackPointer - 1))
                        {
                            ReplaceTopValueWithShortCircuitFlag(JsValue.Undefined, wasShortCircuited: true);
                        }
                        else
                        {
                            ReplaceTopValue(
                                JsOps.TryGetPropertyValueJsValue(target, propertyKey, out var computedValue, context)
                                    ? computedValue
                                    : JsValue.Undefined);
                            if (context.ShouldStopEvaluation)
                            {
                                if (TryHandleCurrentContextThrow(slots))
                                {
                                    break;
                                }

                                return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                            }
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
                        PushValue(GetNamedSuperPropertyValue(
                            program.StringConstants[instruction.Operand],
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            context));
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
                        PushValue(GetComputedSuperPropertyValue(
                            computedSuperKey,
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            context));
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
                        PushValue(GetNamedPropertyValue(
                            namedCompoundTarget,
                            program.StringConstants[instruction.Operand],
                            context));
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
                        PushValue(JsOps.TryGetPropertyValueJsValue(
                                computedCompoundTarget,
                                computedCompoundKey,
                                out var computedCompoundValue,
                                context)
                            ? computedCompoundValue
                            : JsValue.Undefined);
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

                        ReplaceTopValue(namedPropertyValue);
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

                        // Computed property assignment (`obj[key] = value`) is always an ordinary
                        // property set. A string key that happens to start with '#' (e.g.
                        // `obj["#x"]`) is an ordinary property, not a private member, so private
                        // resolution must stay disabled — matching the IR runner's
                        // ApplyProgramComputedPropertyAssignment (allowPrivate: false).
                        SetPropertyValue(
                            computedSetTarget,
                            computedSetName,
                            computedPropertyValue,
                            context,
                            isStrict,
                            allowPrivate: false);
                        if (context.ShouldStopEvaluation)
                        {
                            if (TryHandleCurrentContextThrow(slots))
                            {
                                break;
                            }

                            return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                        }

                        ReplaceTopValue(computedPropertyValue);
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.SetNamedSuperProperty:
                        var namedSuperPropertyValue = stack[stackPointer - 1];
                        ReplaceTopValue(SetNamedSuperPropertyValue(
                            program.StringConstants[DecodeDynamicStoreNameOperand(instruction.Operand)],
                            DecodeDynamicStoreAllowsNameInference(instruction.Operand),
                            namedSuperPropertyValue,
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            context,
                            isStrict));
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
                        PushValue(SetComputedSuperPropertyValue(
                            computedSuperSetKey,
                            DecodeDynamicStoreAllowsNameInference(instruction.Operand),
                            computedSuperPropertyValue,
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            context,
                            isStrict));
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
                        PushValue(UpdateNamedSuperPropertyValue(
                            program.StringConstants[DecodeStringOperand(instruction.Operand)],
                            DecodeIsIncrement(instruction.Operand),
                            DecodeIsPrefix(instruction.Operand),
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            context,
                            isStrict));
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
                        PushValue(UpdateComputedSuperPropertyValue(
                            computedSuperUpdateKey,
                            DecodeIsIncrement(instruction.Operand),
                            DecodeIsPrefix(instruction.Operand),
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            context,
                            isStrict));
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
                        ReplaceTopValue(UpdatePropertyValue(
                            namedUpdateTarget,
                            program.StringConstants[DecodeStringOperand(instruction.Operand)],
                            DecodeIsIncrement(instruction.Operand),
                            DecodeIsPrefix(instruction.Operand),
                            context,
                            isStrict));
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

                        // Computed property update (`obj[key]++`) is an ordinary property update;
                        // a '#'-prefixed string key is an ordinary property, not a private member.
                        ReplaceTopValue(UpdatePropertyValue(
                            computedUpdateTarget,
                            computedUpdateName,
                            DecodeIsIncrement(instruction.Operand),
                            DecodeIsPrefix(instruction.Operand),
                            context,
                            isStrict,
                            allowPrivate: false));
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
                        PushValue(UpdateDynamicIdentifierValue(
                            program.StringConstants[DecodeStringOperand(instruction.Operand)],
                            DecodeIsIncrement(instruction.Operand),
                            DecodeIsPrefix(instruction.Operand),
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            context));
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
                        ReplaceTopValue(new JsValue(GetTypeofStringValue(stack[stackPointer - 1])));
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.TypeOfIdentifier:
                        if (IsInactiveCatchBindingSlot(inactiveCatchBindingSlots, instruction.Operand))
                        {
                            PushValue(new JsValue("undefined"));
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

                        PushValue(new JsValue(GetTypeofStringValue(typeOfValue)));
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.TypeOfDynamicIdentifier:
                        PushValue(TypeOfDynamicIdentifier(
                            program.StringConstants[instruction.Operand],
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            context));
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.DeleteDynamicIdentifier:
                        PushValue(DeleteDynamicIdentifier(
                            program.StringConstants[instruction.Operand],
                            RequireDynamicEnvironment(currentCallingEnvironment),
                            context,
                            isStrict)
                            ? JsValue.True
                            : JsValue.False);
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
                        ReplaceTopValue(DeleteNamedProperty(
                            stack[stackPointer - 1],
                            program.StringConstants[instruction.Operand],
                            context,
                            isStrict)
                            ? JsValue.True
                            : JsValue.False);
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
                        ReplaceTopValue(DeleteComputedProperty(
                            deleteComputedTarget,
                            deleteComputedKey,
                            context,
                            isStrict)
                            ? JsValue.True
                            : JsValue.False);
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
                        ReplaceTopValue(new JsValue(JsOps.ToNumber(in plusOperand, context)));
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
                        ReplaceTopValue(TypedAstEvaluator.NegateValue(stack[stackPointer - 1], context));
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
                        ReplaceTopValue(stack[stackPointer - 1].IsTruthy ? JsValue.False : JsValue.True);
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.UnaryBitwiseNot:
                        ReplaceTopValue(TypedAstEvaluator.BitwiseNot(stack[stackPointer - 1], context));
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
                        ReplaceTopValue(JsValue.Undefined);
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

                        ReplaceTopValue(HasPrivateField(
                                privateFieldTarget,
                                program.StringConstants[instruction.Operand],
                                context)
                            ? JsValue.True
                            : JsValue.False);
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.ToString:
                        ReplaceTopValue(new JsValue(JsOps.ToJsString(stack[stackPointer - 1], context)));
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
                        CopyShortCircuitFlag(stackPointer - 1, stackPointer);
                        stackPointer++;
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.DuplicateTopTwo:
                        stack[stackPointer] = stack[stackPointer - 2];
                        stack[stackPointer + 1] = stack[stackPointer - 1];
                        CopyShortCircuitFlag(stackPointer - 2, stackPointer);
                        CopyShortCircuitFlag(stackPointer - 1, stackPointer + 1);
                        stackPointer += 2;
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
                        SwapShortCircuitFlags(stackPointer - 1, stackPointer - 2);
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.RotateTopThreeRight:
                        var rotateTop = stack[stackPointer - 1];
                        stack[stackPointer - 1] = stack[stackPointer - 2];
                        stack[stackPointer - 2] = stack[stackPointer - 3];
                        stack[stackPointer - 3] = rotateTop;
                        RotateShortCircuitFlagsRight(stackPointer - 3, stackPointer - 2, stackPointer - 1);
                        programCounter++;
                        break;

                    case UnifiedBytecodeOpCode.CreateArray:
                        PushValue(JsValue.FromJsArray(new JsArray(context.RealmState)));
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

                        PushValue(JsValue.FromJsObject(targetObject));
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

                        // Record block-scope const declarations into the per-slot const bitmap so own-slot
                        // StoreSlot/UpdateSlot writes throw a TypeError, and so the captured-env marking
                        // below (IsConstSlotIndex) tags the scope env slot with SlotFlags.Const.
                        var scopeConstSlotIndices = scopeDescriptor.ConstSlotIndices;
                        if (!scopeConstSlotIndices.IsDefaultOrEmpty)
                        {
                            constSlots ??= new bool[slots.Length];
                            for (var i = 0; i < scopeConstSlotIndices.Length; i++)
                            {
                                var constSlotIndex = scopeConstSlotIndices[i];
                                if ((uint)constSlotIndex < (uint)constSlots.Length)
                                {
                                    constSlots[constSlotIndex] = true;
                                }
                            }
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
                            var previousSlotEnvironments = new UnifiedSlotEnvironmentBinding?[lexicalSlotIndices.Length];
                            for (var i = 0; i < lexicalSlotIndices.Length; i++)
                            {
                                var slotIndex = lexicalSlotIndices[i];
                                var isConst = IsConstSlotIndex(slotIndex, constSlots);
                                previousSlotEnvironments[i] = slotEnvironments[slotIndex];
                                slotEnvironments[slotIndex] = new UnifiedSlotEnvironmentBinding(
                                    scopeEnvironment,
                                    slotIndex);
                                MarkSlotEnvironmentLexical(slotEnvironments, slotIndex, isConst);
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
                                if (descriptor.TdzHeadIsConst)
                                {
                                    constSlots ??= new bool[slots.Length];
                                    constSlots[headSlot] = true;
                                }

                                MarkSlotEnvironmentLexical(slotEnvironments, headSlot, descriptor.TdzHeadIsConst);
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
                            else if (descriptor.TargetNameConstantIndex >= 0)
                            {
                                StoreDynamicIdentifierValue(
                                    program.StringConstants[descriptor.TargetNameConstantIndex],
                                    false,
                                    value,
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

                            if (descriptor.TargetSlot >= 0)
                            {
                                slots[descriptor.TargetSlot] = restValue;
                                SyncSlotEnvironment(slotEnvironments, descriptor.TargetSlot, restValue);
                            }
                            else if (descriptor.TargetNameConstantIndex >= 0)
                            {
                                StoreDynamicIdentifierValue(
                                    program.StringConstants[descriptor.TargetNameConstantIndex],
                                    false,
                                    restValue,
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
                            }

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
                            else if (descriptor.TargetNameConstantIndex >= 0)
                            {
                                StoreDynamicIdentifierValue(
                                    program.StringConstants[descriptor.TargetNameConstantIndex],
                                    false,
                                    value,
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

                            if (descriptor.TargetSlot >= 0)
                            {
                                slots[descriptor.TargetSlot] = restValue;
                                SyncSlotEnvironment(slotEnvironments, descriptor.TargetSlot, restValue);
                            }
                            else if (descriptor.TargetNameConstantIndex >= 0)
                            {
                                StoreDynamicIdentifierValue(
                                    program.StringConstants[descriptor.TargetNameConstantIndex],
                                    false,
                                    restValue,
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
                            }

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

                    case UnifiedBytecodeOpCode.DeclareClass:
                        {
                            var classDeclarationEnvironment = RequireDynamicEnvironment(currentCallingEnvironment);
                            SyncUnifiedSlotsToEnvironment(program, slots, slotEnvironments, classDeclarationEnvironment);
                            var classDeclaration = program.ClassDeclarationConstants[instruction.Operand];
                            var classValue = TypedAstEvaluator.CreateClassValueFromDeclaration(
                                classDeclaration,
                                classDeclarationEnvironment,
                                context);
                            if (context.ShouldStopEvaluation)
                            {
                                if (TryHandleCurrentContextThrow(slots))
                                {
                                    break;
                                }

                                return StopWithDriverCleanup(slots, slotEnvironments, context, context.IsThrow);
                            }

                            classDeclarationEnvironment.DefineJsValue(
                                classDeclaration.Name,
                                classValue,
                                isLexicalBinding: true,
                                blocksFunctionScopeOverride: true);
                            SyncEnvironmentToUnifiedSlots(
                                program,
                                slots,
                                slotEnvironments,
                                classDeclarationEnvironment);
                            programCounter++;
                            break;
                        }

                    case UnifiedBytecodeOpCode.DeclareFunction:
                        {
                            var functionDeclarationEnvironment = RequireDynamicEnvironment(currentCallingEnvironment);
                            SyncUnifiedSlotsToEnvironment(program, slots, slotEnvironments, functionDeclarationEnvironment);
                            DeclareFunction(
                                program,
                                instruction.Operand,
                                functionDeclarationEnvironment,
                                context);
                            SyncEnvironmentToUnifiedSlots(
                                program,
                                slots,
                                slotEnvironments,
                                functionDeclarationEnvironment);
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

                    case UnifiedBytecodeOpCode.LoadFunctionLiteral:
                        {
                            var flDescriptor = program.FunctionLiteralConstants[instruction.Operand >> 1];
                            var isConstructor = (instruction.Operand & 1) != 0;
                            var closureEnv = currentCallingEnvironment
                                ?? throw new InvalidOperationException("Cannot create function literal without a calling environment.");
                            var functionCallable = TypedAstEvaluator.CreateFunctionValueFromLiteral(
                                flDescriptor.Function, closureEnv, context, isConstructor, flDescriptor.PlanSeed);
                            PushValue(JsValue.FromObjectUnsafe(functionCallable));
                            programCounter++;
                            break;
                        }

                    case UnifiedBytecodeOpCode.LoadClassLiteral:
                        {
                            var closureEnv = currentCallingEnvironment
                                ?? throw new InvalidOperationException("Cannot create class literal without a calling environment.");
                            var classExpression = program.ClassLiteralConstants[instruction.Operand];
                            PushValue(TypedAstEvaluator.CreateClassValueFromLiteral(
                                classExpression,
                                closureEnv,
                                context));
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

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool GetStackFlag(ulong[] flags, int index)
    {
        return (flags[index >> 6] & (1UL << (index & 63))) != 0;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static void SetStackFlag(ulong[] flags, int index, bool value)
    {
        var wordIndex = index >> 6;
        var bit = 1UL << (index & 63);
        ref var word = ref flags[wordIndex];
        if (value)
        {
            word |= bit;
        }
        else
        {
            word &= ~bit;
        }
    }

    private static bool HandleContextThrow(
        EvaluationContext context,
        UnifiedBytecodeProgram program,
        Stack<TryFrame>? tryStack,
        Span<JsValue> slots,
        ref int programCounter,
        ref JsEnvironment? currentEnvironment,
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        var previousSlotEnvironments = new UnifiedSlotEnvironmentBinding?[descriptor.SlotIndices.Length];
        for (var i = 0; i < descriptor.SlotIndices.Length; i++)
        {
            var slotIndex = descriptor.SlotIndices[i];
            previousSlotEnvironments[i] = slotEnvironments[slotIndex];
            slotEnvironments[slotIndex] = new UnifiedSlotEnvironmentBinding(
                catchEnvironment,
                slotIndex);
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
        var resumableTryFrames = state.ResumableTryFrames;
        var resumableInactiveCatchBindingSlots = state.ResumableInactiveCatchBindingSlots;
        var slotEnvironments = state.CallingEnvironment is null
            ? null
            : InitializeSlotEnvironments(program, state.CallingEnvironment);
        if (state.CallingEnvironment is not null)
        {
            SyncEnvironmentToUnifiedSlots(program, slots, slotEnvironments, state.CallingEnvironment);
        }

        // Short-circuit flag column, index-aligned with the operand stack. Stored on the resume state
        // (not a loop local) so it persists across yield/await suspension in lockstep with OperandStack:
        // both arrays are stable backing stores referenced here, and a suspend only saves StackPointer,
        // so the live flag window stays aligned with the live operand window across resume. Allocated
        // only when program.RequiresShortCircuitStackFlags, matching the sync Execute path; when null
        // every flag query is false and the optional opcodes fall back to pure jump-based short-circuit.
        var stackShortCircuitFlags = state.OperandStackShortCircuitFlags;

        bool GetResumableShortCircuitFlag(int index)
        {
            return stackShortCircuitFlags is not null &&
                (uint)index < (uint)stack.Length &&
                GetStackFlag(stackShortCircuitFlags, index);
        }

        void SetResumableShortCircuitFlag(int index, bool value)
        {
            if (stackShortCircuitFlags is not null && (uint)index < (uint)stack.Length)
            {
                SetStackFlag(stackShortCircuitFlags, index, value);
            }
        }

        // Mirror of the sync VM push/replace discipline: every value landing on a slot clears that
        // slot's flag unless the producing optional opcode explicitly carries short-circuit=true. This
        // is what prevents a non-nullish read after a resume from inheriting a stale flag left in the
        // flag column by an earlier optional chain that reused the same operand slot.
        void PushResumableValue(JsValue value)
        {
            stack[stackPointer] = value;
            SetResumableShortCircuitFlag(stackPointer, false);
            stackPointer++;
        }

        void PushResumableValueWithFlag(JsValue value, bool wasShortCircuited)
        {
            stack[stackPointer] = value;
            SetResumableShortCircuitFlag(stackPointer, wasShortCircuited);
            stackPointer++;
        }

        void ReplaceResumableTop(JsValue value)
        {
            stack[stackPointer - 1] = value;
            SetResumableShortCircuitFlag(stackPointer - 1, false);
        }

        void ReplaceResumableTopWithFlag(JsValue value, bool wasShortCircuited)
        {
            stack[stackPointer - 1] = value;
            SetResumableShortCircuitFlag(stackPointer - 1, wasShortCircuited);
        }

        void SaveResumableState()
        {
            state.ProgramCounter = programCounter;
            state.StackPointer = stackPointer;
        }

        bool TryHandleResumableAbruptCompletion(
            UnifiedBytecodeAbruptCompletionKind kind,
            JsValue value,
            int controlTarget,
            bool hasControlTarget)
        {
            if (resumableTryFrames is null)
            {
                return false;
            }

            while (resumableTryFrames.Count > 0)
            {
                var frame = resumableTryFrames.Peek();
                var descriptor = program.TryDescriptors[frame.DescriptorIndex];
                if (kind == UnifiedBytecodeAbruptCompletionKind.Throw &&
                    descriptor.HandlerTarget >= 0 &&
                    !frame.CatchUsed &&
                    !frame.FinallyScheduled)
                {
                    frame.CatchUsed = true;
                    frame.ThrownValue = value;
                    programCounter = descriptor.HandlerTarget;
                    return true;
                }

                if (descriptor.FinallyTarget >= 0)
                {
                    if (hasControlTarget &&
                        kind == UnifiedBytecodeAbruptCompletionKind.Continue &&
                        descriptor.LoopContinueTarget >= 0 &&
                        (IsSameLoopControlTarget(program, controlTarget, descriptor.LoopContinueTarget) ||
                         IsSameLoopControlTarget(program, descriptor.LoopContinueTarget, controlTarget)))
                    {
                        return false;
                    }

                    if (hasControlTarget &&
                        kind == UnifiedBytecodeAbruptCompletionKind.Continue &&
                        descriptor.LoopBreakTarget >= 0 &&
                        IsContinueTargetInsideLoopFrame(program, controlTarget, descriptor.LoopBreakTarget))
                    {
                        return false;
                    }

                    if (hasControlTarget &&
                        kind == UnifiedBytecodeAbruptCompletionKind.Break &&
                        descriptor.LoopBreakTarget >= 0 &&
                        !IsSameLoopControlTarget(program, controlTarget, descriptor.LoopBreakTarget) &&
                        IsBreakTargetInsideLoopFrame(program, controlTarget, descriptor.LoopBreakTarget))
                    {
                        return false;
                    }

                    if (!frame.FinallyScheduled)
                    {
                        frame.FinallyScheduled = true;
                        frame.PendingCompletion = new UnifiedBytecodePendingAbruptCompletion(
                            kind,
                            value,
                            hasControlTarget ? controlTarget : -1,
                            ResumeTarget: -1,
                            OriginatedInFinally: false);
                        programCounter = descriptor.FinallyTarget;
                        return true;
                    }

                    frame.PendingCompletion = new UnifiedBytecodePendingAbruptCompletion(
                        kind,
                        value,
                        hasControlTarget ? controlTarget : -1,
                        ResumeTarget: -1,
                        OriginatedInFinally: true);
                    if (descriptor.EndFinallyTarget >= 0)
                    {
                        programCounter = descriptor.EndFinallyTarget;
                        return true;
                    }

                    resumableTryFrames.Pop();
                    continue;
                }

                resumableTryFrames.Pop();
            }

            return false;
        }

        bool TryCompleteResumableFinally(int nextTarget, out UnifiedBytecodeStepResult stepResult)
        {
            stepResult = default;
            if (resumableTryFrames is null || resumableTryFrames.Count == 0)
            {
                programCounter = nextTarget;
                return false;
            }

            var completedFrame = resumableTryFrames.Pop();
            var pending = completedFrame.PendingCompletion;
            if (pending.Kind == UnifiedBytecodeAbruptCompletionKind.None)
            {
                programCounter = pending.ResumeTarget >= 0 ? pending.ResumeTarget : nextTarget;
                return false;
            }

            if (pending.Kind == UnifiedBytecodeAbruptCompletionKind.Return)
            {
                if (TryHandleResumableAbruptCompletion(
                        UnifiedBytecodeAbruptCompletionKind.Return,
                        pending.Value,
                        -1,
                        hasControlTarget: false))
                {
                    return false;
                }

                state.IsCompleted = true;
                SaveResumableState();
                stepResult = UnifiedBytecodeStepResult.Completed(pending.Value);
                return true;
            }

            if (pending.Kind is UnifiedBytecodeAbruptCompletionKind.Break or UnifiedBytecodeAbruptCompletionKind.Continue)
            {
                if (TryHandleResumableAbruptCompletion(
                        pending.Kind,
                        JsValue.Undefined,
                        pending.Target,
                        hasControlTarget: true))
                {
                    return false;
                }

                programCounter = pending.Target >= 0 ? pending.Target : nextTarget;
                return false;
            }

            if (TryHandleResumableAbruptCompletion(
                    UnifiedBytecodeAbruptCompletionKind.Throw,
                    pending.Value,
                    -1,
                    hasControlTarget: false))
            {
                return false;
            }

            state.IsCompleted = true;
            SaveResumableState();
            stepResult = UnifiedBytecodeStepResult.Throw(pending.Value);
            return true;
        }

        // Mirrors the sync Execute path's CopyShortCircuitFlag/SwapShortCircuitFlags/
        // RotateShortCircuitFlagsRight so the stack-permuting opcodes keep the short-circuit flag
        // column aligned with the operand stack. The null-guarded resumable accessors make these
        // no-ops when no flag column is allocated. Keeping the invariant here (rather than relying on
        // eligibility never admitting a JumpIfShortCircuited-bearing resumable program) means a future
        // relaxation of the resumable gate cannot silently corrupt flag alignment across these opcodes.
        void CopyResumableShortCircuitFlag(int source, int target)
        {
            SetResumableShortCircuitFlag(target, GetResumableShortCircuitFlag(source));
        }

        void SwapResumableShortCircuitFlags(int left, int right)
        {
            if (stackShortCircuitFlags is null)
            {
                return;
            }

            var leftValue = GetStackFlag(stackShortCircuitFlags, left);
            SetStackFlag(stackShortCircuitFlags, left, GetStackFlag(stackShortCircuitFlags, right));
            SetStackFlag(stackShortCircuitFlags, right, leftValue);
        }

        void RotateResumableShortCircuitFlagsRight(int first, int second, int third)
        {
            if (stackShortCircuitFlags is null)
            {
                return;
            }

            var thirdValue = GetStackFlag(stackShortCircuitFlags, third);
            SetStackFlag(stackShortCircuitFlags, third, GetStackFlag(stackShortCircuitFlags, second));
            SetStackFlag(stackShortCircuitFlags, second, GetStackFlag(stackShortCircuitFlags, first));
            SetStackFlag(stackShortCircuitFlags, first, thirdValue);
        }

        // Make the step run under a scope frame matching the resumable body's own strictness. The sync
        // VM inherits strictness from the function invoker's scope; a resumable step instead runs under
        // whatever context the resume call (iterator `.next()` / promise continuation) supplies, which is
        // not the body's scope. Strictness-sensitive opcodes — notably the property-write opcodes, whose
        // throw-on-non-writable decision is read from context.CurrentScope.IsStrict deep inside
        // JsOps/PropertyHandle — must observe the body's strictness, so push it for the duration of the
        // step. The scope is pushed and popped within this single synchronous traversal (a suspension
        // returns out of ExecuteResumable entirely, after the using disposes), keeping the scope stack
        // balanced across yield/await.
        using var resumableBodyScope = context.PushScope(
            ScopeKind.Function,
            state.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy);

        // Re-enter the private-name scopes lexically active where this resumable body was DEFINED (the
        // class brand scope plus any enclosing captured scopes), captured once on the resume state by the
        // invoker. The sync VM gets these for free because the regular function-invocation path enters
        // them around the body, but each resumable step runs on a fresh per-step context, so without this
        // the PrivateFieldIn handler (and any future private-name opcode) could not map `#name` to its
        // mangled key via context.ResolvePrivateNameKey and would wrongly report the field absent. The
        // scopes are read-only and identical on every resume; they are pushed and popped within this single
        // synchronous traversal (a suspension returns out of ExecuteResumable entirely, after the using
        // disposes), keeping the private-name scope stack balanced across yield/await.
        using var resumablePrivateNameScopes = state.PrivateNameScopes.IsDefaultOrEmpty
            ? null
            : context.EnterPrivateNameScopes(state.PrivateNameScopes);

        while ((uint)programCounter < (uint)instructions.Length)
        {
            var instruction = instructions[programCounter];
            switch (instruction.OpCode)
            {
                case UnifiedBytecodeOpCode.LoadSlot:
                    if (IsInactiveCatchBindingSlot(resumableInactiveCatchBindingSlots, instruction.Operand))
                    {
                        SetInactiveCatchBindingReferenceError(program, instruction.Operand, context);
                        state.IsCompleted = true;
                        SaveResumableState();
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

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

                case UnifiedBytecodeOpCode.LoadNewTarget:
                    // `new.target` for the resumable activation is the per-activation value the invoker
                    // captured on the resume state: `undefined` for an ordinary generator/async function
                    // (never a constructor — its own binding shadows any enclosing constructor's new.target)
                    // and the lexically-inherited value for an async arrow. Reading it directly (rather than
                    // walking the closure chain, which leaked an enclosing constructor for a body nested
                    // inside one) is correct for every admitted shape and stable across yield/await.
                    stack[stackPointer++] = state.NewTargetValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadImportMeta:
                    {
                        // `import.meta` (B20) inside a resumable async/generator body. The meta-property
                        // resolves a single `Symbol.ImportMeta` binding against the live closure environment
                        // threaded onto UnifiedBytecodeResumeState.CallingEnvironment (#3108) — the same
                        // captured module environment the admitted free dynamic READS use, stable across
                        // yield/await — so a resumed step reads the SAME stable per-module import.meta object.
                        // `import.meta` is only ever bound in a MODULE environment (EnsureModuleImportMeta);
                        // outside a module the binding is absent and the sync GetImportMeta throws a
                        // ReferenceError. To keep the resumable loop (which carries no ThrowSignal catch) sound,
                        // the binding is resolved directly here and an absent binding is surfaced via the
                        // resumable Throw step rather than by throwing. The opcode pushes exactly one value,
                        // carries no AwaitedProgram, and cannot itself suspend, so it always runs to completion
                        // inside one resumable step with no resume-state restoration.
                        var importMetaEnvironment = state.CallingEnvironment;
                        if (importMetaEnvironment is not null &&
                            importMetaEnvironment.TryFindBindingJsValue(
                                Symbol.ImportMeta, true, out _, out var resumableImportMeta))
                        {
                            stack[stackPointer++] = resumableImportMeta;
                            programCounter++;
                            break;
                        }

                        context.SetThrow(StandardLibrary.CreateReferenceError(
                            "import.meta is not defined",
                            context,
                            context.RealmState));
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                case UnifiedBytecodeOpCode.LoadTemplateObject:
                    // Tagged-template template-object materialization (B21). Literal twin of the sync VM
                    // handler: the compiled TaggedTemplateDescriptor is the callsite identity used by the
                    // realm cache, so repeated evaluations of this callsite reuse the same template object
                    // while separate parsed callsites do not collapse by source text.
                    PushResumableValue(JsValue.FromJsArray(GetOrCreateTemplateObject(
                        program.TemplateObjectConstants[instruction.Operand],
                        context)));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ToString:
                    // Template-substitution / explicit ToString coercion (B37) inside a resumable body — the
                    // per-hole String(value) coercion an untagged template literal emits. The operand to coerce
                    // sits on top of UnifiedBytecodeResumeState.OperandStack — pushed by a preceding admitted value
                    // load and restored across any suspension in a sibling sub-expression (`` `v${yield 1}` ``),
                    // exactly like the admitted unaries. Literal twin of the sync VM handler, reusing
                    // JsOps.ToJsString: a throwing `Symbol`/`toString`/`Symbol.toPrimitive` surfaces as the resumable
                    // Throw step. The opcode replaces the top value in place, carries no AwaitedProgram, and cannot
                    // itself suspend.
                    stack[stackPointer - 1] = new JsValue(JsOps.ToJsString(stack[stackPointer - 1], context));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadRegexLiteral:
                    // Regex LITERAL (`/pat/flags`) inside a resumable body. Literal twin of the sync VM's
                    // handler (UnifiedBytecodeOpCode.LoadRegexLiteral): read the interned pattern string and
                    // encoded flags byte from the program and build a FRESH RegExp object via
                    // RegExpHelper.CreateRegExpLiteral against the realm. ECMAScript requires a distinct
                    // RegExp per evaluation, so the object is constructed anew on every step (including each
                    // turn of a loop across yields) rather than cached. Nothing lands on the operand stack
                    // across a suspension — the opcode cannot itself yield/await and pushes exactly one value
                    // — so no resume-state restoration is involved.
                    stack[stackPointer++] = JsValue.FromObjectUnsafe(
                        RegExpHelper.CreateRegExpLiteral(
                            program.StringConstants[DecodeRegexLiteralPatternOperand(instruction.Operand)],
                            DecodeRegexLiteralFlagsOperand(instruction.Operand),
                            context.RealmState));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadFunctionLiteral:
                    {
                        var descriptor = program.FunctionLiteralConstants[instruction.Operand >> 1];
                        var isConstructorFunction = (instruction.Operand & 1) != 0;
                        var closureEnvironment = RequireDynamicEnvironment(state.CallingEnvironment);
                        var functionCallable = TypedAstEvaluator.CreateFunctionValueFromLiteral(
                            descriptor.Function,
                            closureEnvironment,
                            context,
                            isConstructorFunction,
                            descriptor.PlanSeed);
                        PushResumableValue(JsValue.FromObjectUnsafe(functionCallable));
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.LoadClassLiteral:
                    {
                        var classExpression = program.ClassLiteralConstants[instruction.Operand];
                        var callingEnvironment = RequireDynamicEnvironment(state.CallingEnvironment);
                        var classEnvironment = RequiresResumableClassLiteralSlotEnvironment(classExpression)
                            ? CreateResumableClassLiteralEnvironment(
                                program,
                                slots,
                                callingEnvironment,
                                state.IsStrict)
                            : callingEnvironment;
                        try
                        {
                            PushResumableValue(TypedAstEvaluator.CreateClassValueFromLiteral(
                                classExpression,
                                classEnvironment,
                                context));
                        }
                        catch (ThrowSignal signal)
                        {
                            context.SetThrow(signal.ThrownValue);
                        }

                        if (!ReferenceEquals(classEnvironment, callingEnvironment))
                        {
                            SyncEnvironmentToUnifiedSlots(program, slots, slotEnvironments: null, classEnvironment);
                        }

                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.EnsureHasName:
                    {
                        var targetName = program.StringConstants[instruction.Operand];
                        if (stack[stackPointer - 1] is { Kind: JsValueKind.Object, ObjectValue: TypedAstEvaluator.IFunctionNameTarget nameTarget })
                        {
                            nameTarget.EnsureHasName(targetName);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.LoadDynamicIdentifier:
                    {
                        // Free variable READ (`yield outerVar`). Resolve by name against the live closure
                        // environment captured on the resume state. The environment is a live reference (not
                        // a snapshot), so after a resume this reads the CURRENT binding value: a variable a
                        // closure captured and mutates across yields, or one outer code mutated while this
                        // frame was suspended, is observed correctly, and an uninitialized binding throws a
                        // ReferenceError (via GetDynamicIdentifierValue -> SetThrow).
                        var resumableDynamicLoadEnvironment = RequireDynamicEnvironment(state.CallingEnvironment);
                        PushResumableValue(GetDynamicIdentifierValue(
                            program.StringConstants[instruction.Operand],
                            resumableDynamicLoadEnvironment,
                            context));
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget:
                    {
                        // Free function CALL target (`yield helper(x)` where `helper` is module/script-level
                        // or a captured outer binding). Resolve the callee by name against the live closure
                        // environment, pushing the <thisValue, callee> pair the CallInvocationBoundary
                        // consumes. Resolution is live, so the binding observed after a resume reflects any
                        // reassignment/shadowing performed by outer code between yields.
                        var resumableDynamicCallEnvironment = RequireDynamicEnvironment(state.CallingEnvironment);
                        PrepareDynamicIdentifierCallTarget(
                            program.StringConstants[instruction.Operand],
                            resumableDynamicCallEnvironment,
                            stack,
                            ref stackPointer,
                            context);
                        SetResumableShortCircuitFlag(stackPointer - 1, false);
                        SetResumableShortCircuitFlag(stackPointer - 2, false);
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference:
                    {
                        // Free/captured plain WRITE (`outer = <rhs>`) resolves its assignment reference
                        // before evaluating the RHS, per §13.15.2. Store the pending reference on the resume
                        // state so an RHS suspension (`outer = yield v` / `outer = await p`) keeps the exact
                        // target selected before the suspension.
                        state.DynamicIdentifierReferences ??= new AssignmentReference[instructions.Length];
                        state.DynamicIdentifierReferences[state.DynamicIdentifierReferenceCount++] =
                            RequireDynamicEnvironment(state.CallingEnvironment)
                                .ResolveIdentifierAssignmentReference(
                                    Symbol.Intern(program.StringConstants[instruction.Operand]),
                                    context);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.LoadDynamicIdentifierReference:
                    {
                        if (state.DynamicIdentifierReferenceCount == 0 ||
                            state.DynamicIdentifierReferences is null)
                        {
                            throw new InvalidOperationException(
                                "Unified bytecode attempted to load a missing dynamic identifier reference.");
                        }

                        PushResumableValue(
                            state.DynamicIdentifierReferences[state.DynamicIdentifierReferenceCount - 1].GetJsValue());
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.StoreDynamicIdentifierReference:
                    {
                        if (state.DynamicIdentifierReferenceCount == 0 ||
                            state.DynamicIdentifierReferences is null)
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

                        state.DynamicIdentifierReferences[--state.DynamicIdentifierReferenceCount]
                            .SetValue(dynamicReferenceValue);
                        state.DynamicIdentifierReferences[state.DynamicIdentifierReferenceCount] = default;
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PopDynamicIdentifierReference:
                    {
                        if (state.DynamicIdentifierReferenceCount == 0 ||
                            state.DynamicIdentifierReferences is null)
                        {
                            throw new InvalidOperationException(
                                "Unified bytecode attempted to pop a missing dynamic identifier reference.");
                        }

                        state.DynamicIdentifierReferences[--state.DynamicIdentifierReferenceCount] = default;
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.StoreSlot:
                    if (slots[instruction.Operand].IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, instruction.Operand, context);
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    if (state.IsConstSlot(instruction.Operand))
                    {
                        SetConstantSlotTypeError(program, instruction.Operand, context);
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    var resumableStoredSlotValue = stack[--stackPointer];
                    slots[instruction.Operand] = resumableStoredSlotValue;
                    SyncSlotEnvironment(slotEnvironments, instruction.Operand, resumableStoredSlotValue);
                    ClearInactiveCatchBindingSlot(resumableInactiveCatchBindingSlots, instruction.Operand);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateSlot:
                    {
                        // `x++` / `x--` / `++x` / `--x` on an activation slot. Const-slot metadata is stored
                        // on the resume state, matching the sync VM's const bitmap, so lexical `let` updates
                        // can route while `const` updates still raise TypeError before numeric coercion.
                        var resumableUpdateIndex = DecodeUpdateIndex(instruction.Operand);
                        if (IsInactiveCatchBindingSlot(resumableInactiveCatchBindingSlots, resumableUpdateIndex))
                        {
                            SetInactiveCatchBindingReferenceError(program, resumableUpdateIndex, context);
                            state.IsCompleted = true;
                            SaveResumableState();
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        var resumableUpdateValue = slots[resumableUpdateIndex];
                        if (resumableUpdateValue.IsUninitialized)
                        {
                            // Temporal dead zone: updating a lexical slot before its initializer ran throws a
                            // ReferenceError, identical to the sync VM. (Parameter / `var` slots are never
                            // uninitialized at an update site, so this guards the residual TDZ window only.)
                            SetUninitializedSlotReferenceError(program, resumableUpdateIndex, context);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (state.IsConstSlot(resumableUpdateIndex))
                        {
                            SetConstantSlotTypeError(program, resumableUpdateIndex, context);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        GetUpdatedNumericValue(
                            resumableUpdateValue,
                            DecodeIsIncrement(instruction.Operand),
                            context,
                            out var resumableOldNumericValue,
                            out var resumableNewSlotValue);
                        if (context.ShouldStopEvaluation)
                        {
                            // ToNumeric on a non-coercible operand (e.g. a Symbol, or a BigInt/Number mix)
                            // throws; surface it as the resumable Throw step rather than mutating the slot.
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        slots[resumableUpdateIndex] = resumableNewSlotValue;
                        SyncSlotEnvironment(slotEnvironments, resumableUpdateIndex, resumableNewSlotValue);
                        PushResumableValue(DecodeIsPrefix(instruction.Operand)
                            ? resumableNewSlotValue
                            : resumableOldNumericValue);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.InitializeSlot:
                    var resumableInitializedSlotValue = stack[--stackPointer];
                    slots[instruction.Operand] = resumableInitializedSlotValue;
                    SyncSlotEnvironment(slotEnvironments, instruction.Operand, resumableInitializedSlotValue);
                    ClearInactiveCatchBindingSlot(resumableInactiveCatchBindingSlots, instruction.Operand);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget:
                    {
                        // B44: `let [a,b] = await p` / `const {x} = await p`. Reached only after AwaitValue has
                        // settled the source value on top of the operand stack. Pop it and run the synchronous
                        // destructuring of the lowered binding-target program. The body's own declared
                        // bindings are flat slots backed by the resume state's CallingEnvironment via the slot
                        // environment map; sync the flat slots into that environment, apply the binding (which
                        // writes each name), then sync the environment back into the flat slots so a later
                        // LoadSlot reads the bound value. The destructuring is synchronous and cannot itself
                        // suspend, so this always completes inside one resumed step; a non-iterable /
                        // non-coercible source or a throwing element getter surfaces as the resumable Throw
                        // step.
                        var declarationBindingValue = stack[--stackPointer];
                        var declarationBindingEnvironment = RequireDynamicEnvironment(state.CallingEnvironment);
                        SyncUnifiedSlotsToEnvironment(program, slots, slotEnvironments, declarationBindingEnvironment);
                        TypedAstEvaluator.ApplyLoweredDeclarationBindingTargetProgram(
                            program.BindingTargetConstants[DecodeDeclarationBindingTargetIndex(instruction.Operand)],
                            declarationBindingValue,
                            declarationBindingEnvironment,
                            context,
                            DecodeDeclarationBindingTargetVariableKind(instruction.Operand),
                            DecodeDeclarationBindingTargetHasInitializer(instruction.Operand),
                            allowNameInference: false);
                        SyncEnvironmentToUnifiedSlots(program, slots, slotEnvironments, declarationBindingEnvironment);
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.UpdateDynamicIdentifier:
                    {
                        // Captured / free UPDATE (`n++`, `n--`, `++n`, `--n` where `n` escapes this
                        // activation's slots). The instruction resolves an assignment reference against the
                        // live closure environment threaded onto UnifiedBytecodeResumeState.CallingEnvironment
                        // (#3108, stable across yield/await), reads the current value, applies the numeric
                        // ++/--, and writes it back — so the update mutates the SAME enclosing heap slot before
                        // and after every suspension (the captured binding aliases across yields). const-safety
                        // is enforced by the environment: ResolveIdentifierAssignmentReference ->
                        // reference.SetValue throws the `TypeError: Assignment to constant variable` for a
                        // captured `const`, surfaced here as the resumable Throw step (unlike the slot-update
                        // path, no const-slot metadata is needed). The opcode carries no AwaitedProgram and
                        // cannot itself suspend, so it runs to completion inside one resumable step and pushes
                        // the prefix (new) or postfix (old) value.
                        PushResumableValue(UpdateDynamicIdentifierValue(
                            program.StringConstants[DecodeStringOperand(instruction.Operand)],
                            DecodeIsIncrement(instruction.Operand),
                            DecodeIsPrefix(instruction.Operand),
                            RequireDynamicEnvironment(state.CallingEnvironment),
                            context));
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.TdzHeadInit:
                    {
                        // Establish the loop-head temporal dead zone before the for-in/iterator source is
                        // evaluated: mark the flat head slots uninitialized so a read of `let x`/`const x`
                        // inside the source throws ReferenceError (the resumable LoadSlot handler raises it).
                        // The sync handler additionally records const-slot / slot-environment metadata. The
                        // resumable VM mirrors the const side on UnifiedBytecodeResumeState so later
                        // StoreSlot/UpdateSlot can enforce const reassignment inside the VM.
                        var tdzDescriptor = program.DriverDescriptors[instruction.Operand];
                        var tdzHeadSlots = tdzDescriptor.TdzHeadSlots;
                        for (var tdzIndex = 0; tdzIndex < tdzHeadSlots.Length; tdzIndex++)
                        {
                            var headSlot = tdzHeadSlots[tdzIndex];
                            slots[headSlot] = JsValue.Uninitialized;
                            SyncSlotEnvironment(slotEnvironments, headSlot, JsValue.Uninitialized);
                            if (tdzDescriptor.TdzHeadIsConst)
                            {
                                state.MarkConstSlot(headSlot);
                            }
                        }

                        programCounter++;
                        break;
                    }

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

                case UnifiedBytecodeOpCode.GetNamedProperty:
                    // Honors the short-circuit flag so a flagged-undefined operand (set by a prior
                    // optional hop) propagates undefined instead of re-reading a property off it. When
                    // no flag column is allocated GetResumableShortCircuitFlag is always false and this
                    // is the plain property read.
                    if (GetResumableShortCircuitFlag(stackPointer - 1))
                    {
                        ReplaceResumableTopWithFlag(JsValue.Undefined, wasShortCircuited: true);
                        programCounter++;
                        break;
                    }

                    ReplaceResumableTop(GetNamedPropertyValue(
                        stack[stackPointer - 1],
                        program.StringConstants[instruction.Operand],
                        context));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetNamedPropertyOptional:
                    // `o?.a` (head hop). A nullish base OR an already-short-circuited operand yields the
                    // synthetic undefined and marks the result short-circuited so any trailing reads in
                    // the same chain propagate undefined.
                    if (GetResumableShortCircuitFlag(stackPointer - 1) || stack[stackPointer - 1].IsNullOrUndefined)
                    {
                        ReplaceResumableTopWithFlag(JsValue.Undefined, wasShortCircuited: true);
                        programCounter++;
                        break;
                    }

                    ReplaceResumableTop(GetNamedPropertyValue(
                        stack[stackPointer - 1],
                        program.StringConstants[instruction.Operand],
                        context));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined:
                    // Optional hop lowered to a jump: if the base is nullish (or already short-circuited)
                    // replace it with undefined, flag it, and jump to the chain end so the remaining
                    // reads/calls are skipped. Otherwise fall through to evaluate the chain normally.
                    if (GetResumableShortCircuitFlag(stackPointer - 1) || stack[stackPointer - 1].IsNullOrUndefined)
                    {
                        ReplaceResumableTopWithFlag(JsValue.Undefined, wasShortCircuited: true);
                        programCounter = instruction.Operand;
                    }
                    else
                    {
                        programCounter++;
                    }

                    break;

                case UnifiedBytecodeOpCode.JumpIfShortCircuited:
                    // Flag-based chain end for optional-call shapes that keep intermediate reads live
                    // (RequiresShortCircuitStackFlags). Jumps past the call when the operand carries the
                    // short-circuit flag, leaving the synthetic undefined in place.
                    programCounter = GetResumableShortCircuitFlag(stackPointer - 1)
                        ? instruction.Operand
                        : programCounter + 1;
                    break;

                case UnifiedBytecodeOpCode.GetComputedProperty:
                    var resumableComputedKey = stack[--stackPointer];
                    var resumableComputedTarget = stack[stackPointer - 1];
                    if (GetResumableShortCircuitFlag(stackPointer - 1))
                    {
                        ReplaceResumableTopWithFlag(JsValue.Undefined, wasShortCircuited: true);
                        programCounter++;
                        break;
                    }

                    ReplaceResumableTop(
                        JsOps.TryGetPropertyValueJsValue(resumableComputedTarget, resumableComputedKey, out var resumableComputedValue, context)
                            ? resumableComputedValue
                            : JsValue.Undefined);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SetNamedProperty:
                    // `o.x = v` / `this.x = v` inside a resumable body. Stack layout mirrors the sync
                    // Execute path: [base, value] with value on top. The base survives a suspension in the
                    // value (`o.x = yield 1`) because OperandStack is the resume state's stable backing
                    // store. Strictness is the generator/async body's own (state.IsStrict, captured at
                    // construction) — NOT the resume call's scope — so a strict write to a non-writable
                    // property throws while a sloppy one is silently ignored. A thrown set translates to
                    // the resumable Throw step.
                    var resumableNamedSetValue = stack[--stackPointer];
                    var resumableNamedSetTarget = stack[stackPointer - 1];
                    SetPropertyValue(
                        resumableNamedSetTarget,
                        program.StringConstants[instruction.Operand],
                        resumableNamedSetValue,
                        context,
                        state.IsStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    // The assignment expression evaluates to the assigned value; replace the base operand
                    // (now on top after the value pop) with it.
                    ReplaceResumableTop(resumableNamedSetValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SetComputedProperty:
                    // `o[k] = v` inside a resumable body. Stack layout [base, key, value]; pop value then
                    // key, leaving base on top. Private resolution stays disabled so a string key starting
                    // with '#' is treated as an ordinary property, matching the sync Execute path and the
                    // IR runner.
                    var resumableComputedSetValue = stack[--stackPointer];
                    var resumableComputedSetKey = stack[--stackPointer];
                    var resumableComputedSetTarget = stack[stackPointer - 1];
                    var resumableComputedSetName = JsOps.GetRequiredPropertyName(resumableComputedSetKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    SetPropertyValue(
                        resumableComputedSetTarget,
                        resumableComputedSetName,
                        resumableComputedSetValue,
                        context,
                        state.IsStrict,
                        allowPrivate: false);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    ReplaceResumableTop(resumableComputedSetValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.EnsureSuperReference:
                    // `super[...]` reference validation inside a resumable body. The live method
                    // environment is threaded onto the resume state; validate it before any computed-key
                    // side effects, matching the expression bytecode ordering rule and sync VM handler.
                    if (!EnsureSuperReference(RequireDynamicEnvironment(state.CallingEnvironment), context))
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetNamedSuperProperty:
                    PushResumableValue(GetNamedSuperPropertyValue(
                        program.StringConstants[instruction.Operand],
                        RequireDynamicEnvironment(state.CallingEnvironment),
                        context));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetComputedSuperProperty:
                    var resumableComputedSuperKey = stack[--stackPointer];
                    PushResumableValue(GetComputedSuperPropertyValue(
                        resumableComputedSuperKey,
                        RequireDynamicEnvironment(state.CallingEnvironment),
                        context));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SetNamedSuperProperty:
                    var resumableNamedSuperPropertyValue = stack[stackPointer - 1];
                    ReplaceResumableTop(SetNamedSuperPropertyValue(
                        program.StringConstants[DecodeDynamicStoreNameOperand(instruction.Operand)],
                        DecodeDynamicStoreAllowsNameInference(instruction.Operand),
                        resumableNamedSuperPropertyValue,
                        RequireDynamicEnvironment(state.CallingEnvironment),
                        context,
                        state.IsStrict));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SetComputedSuperProperty:
                    var resumableComputedSuperPropertyValue = stack[--stackPointer];
                    var resumableComputedSuperSetKey = stack[--stackPointer];
                    PushResumableValue(SetComputedSuperPropertyValue(
                        resumableComputedSuperSetKey,
                        DecodeDynamicStoreAllowsNameInference(instruction.Operand),
                        resumableComputedSuperPropertyValue,
                        RequireDynamicEnvironment(state.CallingEnvironment),
                        context,
                        state.IsStrict));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateNamedSuperProperty:
                    PushResumableValue(UpdateNamedSuperPropertyValue(
                        program.StringConstants[DecodeStringOperand(instruction.Operand)],
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        RequireDynamicEnvironment(state.CallingEnvironment),
                        context,
                        state.IsStrict));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateComputedSuperProperty:
                    var resumableComputedSuperUpdateKey = stack[--stackPointer];
                    PushResumableValue(UpdateComputedSuperPropertyValue(
                        resumableComputedSuperUpdateKey,
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        RequireDynamicEnvironment(state.CallingEnvironment),
                        context,
                        state.IsStrict));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateNamedProperty:
                    // `o.x++` / `++o.x` (and the `--` forms) inside a resumable body. Stack layout mirrors
                    // the sync Execute path: the base sits on top, and is replaced in place by the
                    // expression result (old value for postfix, new value for prefix). An update opcode
                    // cannot itself suspend (its operands are already materialized on the stack — there is
                    // no AwaitedProgram), so it always runs to completion inside one resumable step and the
                    // base never needs operand-stack restoration across a yield/await. Strictness is the
                    // body's own (state.IsStrict) so a strict update of a read-only property throws while a
                    // sloppy one is silently ignored, matching the sync path's UpdatePropertyValue. A thrown
                    // update (e.g. a non-writable accessor target) translates to the resumable Throw step.
                    var resumableNamedUpdateTarget = stack[stackPointer - 1];
                    ReplaceResumableTop(UpdatePropertyValue(
                        resumableNamedUpdateTarget,
                        program.StringConstants[DecodeStringOperand(instruction.Operand)],
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        context,
                        state.IsStrict));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateComputedProperty:
                    // `o[k]++` inside a resumable body. Stack layout [base, key]; pop the key, leaving the
                    // base on top to be replaced by the update result. A null/undefined base throws a
                    // TypeError before any read, matching the sync path. The key is coerced to a property
                    // name with private resolution disabled (a '#'-prefixed string key is an ordinary
                    // property, not a private member). Like the named form, this opcode cannot suspend, so
                    // no operand-stack restoration is involved.
                    var resumableComputedUpdateKey = stack[--stackPointer];
                    var resumableComputedUpdateTarget = stack[stackPointer - 1];
                    if (resumableComputedUpdateTarget.IsNullOrUndefined)
                    {
                        context.SetThrow(StandardLibrary.CreateTypeError(
                            "Cannot read properties of null or undefined",
                            context,
                            context.RealmState));
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    var resumableComputedUpdateName =
                        JsOps.GetRequiredPropertyName(resumableComputedUpdateKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    ReplaceResumableTop(UpdatePropertyValue(
                        resumableComputedUpdateTarget,
                        resumableComputedUpdateName,
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        context,
                        state.IsStrict,
                        allowPrivate: false));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DeleteNamedProperty:
                    // `delete o.x` inside a resumable body. The base sits on top and is replaced by the
                    // boolean delete result. Private resolution is disabled (matching the sync path). A
                    // delete cannot suspend, so the base never needs restoration across a yield/await. A
                    // strict delete of a non-configurable property throws and translates to the resumable
                    // Throw step.
                    ReplaceResumableTop(DeleteNamedProperty(
                        stack[stackPointer - 1],
                        program.StringConstants[instruction.Operand],
                        context,
                        state.IsStrict)
                        ? JsValue.True
                        : JsValue.False);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DeleteComputedProperty:
                    // `delete o[k]` inside a resumable body. Stack layout [base, key]; pop the key, leaving
                    // the base on top to be replaced by the boolean result. Strictness is the body's own.
                    var resumableComputedDeleteKey = stack[--stackPointer];
                    var resumableComputedDeleteTarget = stack[stackPointer - 1];
                    ReplaceResumableTop(DeleteComputedProperty(
                        resumableComputedDeleteTarget,
                        resumableComputedDeleteKey,
                        context,
                        state.IsStrict)
                        ? JsValue.True
                        : JsValue.False);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TypeOf:
                    stack[stackPointer - 1] = new JsValue(GetTypeofStringValue(stack[stackPointer - 1]));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TypeOfIdentifier:
                    if (IsInactiveCatchBindingSlot(resumableInactiveCatchBindingSlots, instruction.Operand))
                    {
                        stack[stackPointer++] = new JsValue("undefined");
                        programCounter++;
                        break;
                    }

                    var resumableTypeOfValue = slots[instruction.Operand];
                    if (resumableTypeOfValue.IsUninitialized)
                    {
                        SetUninitializedSlotReferenceError(program, instruction.Operand, context);
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    stack[stackPointer++] = new JsValue(GetTypeofStringValue(resumableTypeOfValue));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TypeOfDynamicIdentifier:
                    {
                        // `typeof freeVar` where `freeVar` is module/script-level or a captured outer
                        // binding (escapes this activation's slots), inside a resumable body. Resolve the
                        // name against the live closure environment threaded onto
                        // UnifiedBytecodeResumeState.CallingEnvironment (#3108) — the same env the admitted
                        // free dynamic READS / CALL targets use — so a resumed step observes the CURRENT
                        // binding. `typeof` NEVER throws ReferenceError: TypeOfDynamicIdentifier swallows the
                        // unbound-binding throw and returns "undefined". The ShouldStopEvaluation guard below
                        // is defensive — it only fires for a non-ReferenceError throw (e.g. a thrown getter on
                        // the global object), which surfaces as the resumable Throw step. Literal twin of the
                        // sync VM's TypeOfDynamicIdentifier handler.
                        var resumableDynamicTypeOfEnvironment = RequireDynamicEnvironment(state.CallingEnvironment);
                        PushResumableValue(TypeOfDynamicIdentifier(
                            program.StringConstants[instruction.Operand],
                            resumableDynamicTypeOfEnvironment,
                            context));
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.DeleteDynamicIdentifier:
                    {
                        // `delete freeVar` of a free/dynamic identifier (module/script-level binding or a
                        // captured outer binding that escapes this activation's slots) inside a resumable body.
                        // The name resolves against the live closure environment threaded onto
                        // UnifiedBytecodeResumeState.CallingEnvironment (#3108 — the same env the admitted free
                        // dynamic READS / CALL targets / typeof already use, captured at construction and stable
                        // across yield/await), so a resumed step deletes against the CURRENT environment. The
                        // opcode is self-contained: it neither reads nor writes the operand stack across a
                        // suspension (a delete cannot itself yield/await — its operand is the resolved name, not
                        // a sub-expression), it carries no AwaitedProgram, and it pushes exactly one boolean, so
                        // it always runs to completion inside one resumable step with no resume-state
                        // restoration. It does NOT use the transient dynamicIdentifierReferences array (the
                        // reason the dynamic plain/compound STORE stays declined) — DeleteDynamicIdentifier
                        // takes name + environment + isStrict and returns a bool directly. Strictness is the
                        // body's own (state.IsStrict, captured at construction): a strict-mode `delete freeVar`
                        // of an unqualified identifier is an early SyntaxError so never reaches here, while a
                        // strict delete of a non-configurable global property returns false / throws per the
                        // sync DeleteDynamicIdentifier helper, surfacing a throw as the resumable Throw step.
                        // Literal twin of the sync VM's DeleteDynamicIdentifier handler.
                        var resumableDynamicDeleteEnvironment = RequireDynamicEnvironment(state.CallingEnvironment);
                        PushResumableValue(DeleteDynamicIdentifier(
                            program.StringConstants[instruction.Operand],
                            resumableDynamicDeleteEnvironment,
                            context,
                            state.IsStrict)
                            ? JsValue.True
                            : JsValue.False);
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.UnaryPlus:
                    var resumablePlusOperand = stack[stackPointer - 1];
                    stack[stackPointer - 1] = new JsValue(JsOps.ToNumber(in resumablePlusOperand, context));
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryMinus:
                    stack[stackPointer - 1] = TypedAstEvaluator.NegateValue(stack[stackPointer - 1], context);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
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
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryVoid:
                    stack[stackPointer - 1] = JsValue.Undefined;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.PrivateFieldIn:
                    // `#field in obj` (PrivateFieldIn) inside a resumable body. A pure boolean brand check:
                    // the operand (the object being tested) sits on top of the operand stack — pushed by a
                    // preceding admitted value load — and any suspension in that sub-expression (`#x in (yield o)`)
                    // is restored through UnifiedBytecodeResumeState.OperandStack, the stable backing store, just
                    // like every other admitted unary. The opcode itself carries no AwaitedProgram and cannot
                    // suspend, so it always runs to completion inside one resumable step. The private-name key is
                    // resolved against context.ResolvePrivateNameKey / context.RealmState (stable across
                    // yield/await), so this is the literal twin of the sync VM's PrivateFieldIn handler: a
                    // non-object operand throws the same TypeError (surfaced as the resumable Throw step) and a
                    // matching field/brand returns true.
                    if (stack[stackPointer - 1] is not { Kind: JsValueKind.Object, ObjectValue: JsObject resumablePrivateFieldTarget })
                    {
                        context.SetThrow(StandardLibrary.CreateTypeError(
                            "Cannot use 'in' operator to search for a private field in a non-object",
                            context,
                            context.RealmState));
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    stack[stackPointer - 1] = HasPrivateField(
                            resumablePrivateFieldTarget,
                            program.StringConstants[instruction.Operand],
                            context)
                        ? JsValue.True
                        : JsValue.False;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.RequireObjectCoercible:
                    var resumableCoercibleIndex = stackPointer - 1 - instruction.Operand;
                    if (stack[resumableCoercibleIndex].IsNullOrUndefined)
                    {
                        context.SetThrow(StandardLibrary.CreateTypeError(
                            "Cannot read properties of null or undefined",
                            context,
                            context.RealmState));
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ResolvePropertyKey:
                    stack[stackPointer - 1] = ResolvePropertyKey(stack[stackPointer - 1], context);
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
                    CopyResumableShortCircuitFlag(stackPointer - 1, stackPointer);
                    stackPointer++;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DuplicateTopTwo:
                    stack[stackPointer] = stack[stackPointer - 2];
                    stack[stackPointer + 1] = stack[stackPointer - 1];
                    CopyResumableShortCircuitFlag(stackPointer - 2, stackPointer);
                    CopyResumableShortCircuitFlag(stackPointer - 1, stackPointer + 1);
                    stackPointer += 2;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SwapTopTwo:
                    var resumableTop = stack[stackPointer - 1];
                    stack[stackPointer - 1] = stack[stackPointer - 2];
                    stack[stackPointer - 2] = resumableTop;
                    SwapResumableShortCircuitFlags(stackPointer - 1, stackPointer - 2);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.RotateTopThreeRight:
                    var resumableRotateTop = stack[stackPointer - 1];
                    stack[stackPointer - 1] = stack[stackPointer - 2];
                    stack[stackPointer - 2] = stack[stackPointer - 3];
                    stack[stackPointer - 3] = resumableRotateTop;
                    RotateResumableShortCircuitFlagsRight(stackPointer - 3, stackPointer - 2, stackPointer - 1);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.CreateArray:
                    // ARRAY literal head (`[`). Allocate a FRESH JsArray against the realm and push it as
                    // the receiver the trailing ArrayPush/ArrayPushHole/ArraySpread opcodes mutate in place.
                    // PushResumableValue clears the new slot's short-circuit flag so a non-optional receiver
                    // can never inherit a stale flag from an earlier optional chain that reused this slot.
                    // A new array per evaluation is required by ECMAScript, so this allocates anew on every
                    // step (including each turn of a loop across yields) rather than caching.
                    PushResumableValue(JsValue.FromJsArray(new JsArray(context.RealmState)));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ArrayPush:
                    // `[..., element]`. Pop the element (its flag sits above the pointer and is irrelevant)
                    // and append it to the array receiver one slot below, which stays on the stack. The
                    // receiver's flag was cleared by CreateArray and is never re-flagged here.
                    var resumableArrayElementValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetArray(out var resumableTargetArray))
                    {
                        throw new InvalidOperationException("Array push unified bytecode op requires an array receiver.");
                    }

                    resumableTargetArray.Push(resumableArrayElementValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ArrayPushHole:
                    // Array elision (`[a, , b]`). Append a hole to the receiver, leaving the array on top.
                    if (!stack[stackPointer - 1].TryGetArray(out var resumableTargetArrayWithHole))
                    {
                        throw new InvalidOperationException("Array hole unified bytecode op requires an array receiver.");
                    }

                    resumableTargetArrayWithHole.PushHole();
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ArraySpread:
                    // `[...iterable]`. Pop the spread source and append each yielded element to the array
                    // receiver using the same EnumerateSpread helper the sync VM uses, so iterator protocol
                    // (Symbol.iterator lookup, next()/done) is identical. EnumerateSpread runs the iterator
                    // eagerly here; a throwing iterator step surfaces via context as the resumable Throw step
                    // on the next guard. The array receiver stays on the stack with its flag unchanged.
                    var resumableSpreadSourceValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetArray(out var resumableSpreadTargetArray))
                    {
                        throw new InvalidOperationException("Array spread unified bytecode op requires an array receiver.");
                    }

                    foreach (var resumableSpreadElement in TypedAstEvaluator.EnumerateSpread(resumableSpreadSourceValue, context))
                    {
                        resumableSpreadTargetArray.Push(resumableSpreadElement);
                    }

                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.CreateObject:
                    // OBJECT literal head (`{`). Allocate a FRESH JsObject wired to the realm's
                    // Object.prototype and push it as the receiver the trailing Define*/ObjectSpread opcodes
                    // mutate in place. Literal twin of the sync VM's CreateObject. A new object per evaluation
                    // is required by ECMAScript, so this allocates anew on every step rather than caching.
                    var resumableTargetObject = new JsObject
                    {
                        RealmState = context.RealmState
                    };
                    if (context.RealmState.ObjectPrototype is { } resumableObjectPrototype)
                    {
                        resumableTargetObject.SetPrototype(resumableObjectPrototype);
                    }

                    PushResumableValue(JsValue.FromJsObject(resumableTargetObject));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineObjectProperty:
                    // `{ name: value }` (and shorthand `{ name }`, `__proto__: v`). Pop the value, leaving
                    // the object receiver on top, and install the data property via the sync VM's helper so
                    // prototype-mutation, name inference, and known-new-property fast paths are identical.
                    var resumableDefinePropertyValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var resumableObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Object property unified bytecode op requires an object receiver.");
                    }

                    DefineObjectLiteralProperty(
                        resumableObjectLiteralTarget,
                        program.StringConstants[DecodeDefineObjectPropertyNameOperand(instruction.Operand)],
                        instruction.Operand,
                        resumableDefinePropertyValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineComputedObjectProperty:
                    // `{ [expr]: value }`. Stack layout [object, key, value]; pop value then key, leaving the
                    // object on top. The key is coerced with JsOps.GetRequiredPropertyName (ToPropertyKey),
                    // which can throw (e.g. a Symbol-less object with a throwing toString) — surfaced as the
                    // resumable Throw step before any property is defined.
                    var resumableComputedObjectPropertyValue = stack[--stackPointer];
                    var resumableComputedObjectPropertyKey = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var resumableComputedObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Computed object property unified bytecode op requires an object receiver.");
                    }

                    var resumableComputedObjectPropertyName =
                        JsOps.GetRequiredPropertyName(resumableComputedObjectPropertyKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    DefineComputedObjectLiteralProperty(
                        resumableComputedObjectLiteralTarget,
                        resumableComputedObjectPropertyName,
                        instruction.Operand,
                        resumableComputedObjectPropertyValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineObjectMethod:
                    // `{ m(){} }`. Pop the method function, leaving the object on top, and install it via the
                    // sync VM's helper (sets [[HomeObject]] / name and the data-property descriptor).
                    var resumableMethodValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var resumableMethodObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Object method unified bytecode op requires an object receiver.");
                    }

                    DefineObjectLiteralMethod(
                        resumableMethodObjectLiteralTarget,
                        program.StringConstants[instruction.Operand],
                        resumableMethodValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineComputedObjectMethod:
                    // `{ [expr](){} }`. Stack layout [object, key, method]; pop method then key. Computed-key
                    // coercion can throw and surfaces as the resumable Throw step.
                    var resumableComputedMethodValue = stack[--stackPointer];
                    var resumableComputedMethodKey = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var resumableComputedMethodObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Computed object method unified bytecode op requires an object receiver.");
                    }

                    var resumableComputedMethodName =
                        JsOps.GetRequiredPropertyName(resumableComputedMethodKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    DefineObjectLiteralMethod(
                        resumableComputedMethodObjectLiteralTarget,
                        resumableComputedMethodName,
                        resumableComputedMethodValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineObjectAccessor:
                    // `{ get x(){} }` / `{ set x(v){} }`. Pop the accessor function, leaving the object on
                    // top, and merge it into the property's accessor descriptor via the sync VM's helper so
                    // a get+set pair on the same name combines into one accessor (operand encodes kind).
                    var resumableAccessorValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var resumableAccessorObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Object accessor unified bytecode op requires an object receiver.");
                    }

                    DefineObjectLiteralAccessor(
                        resumableAccessorObjectLiteralTarget,
                        program.StringConstants[DecodeObjectAccessorNameOperand(instruction.Operand)],
                        DecodeObjectAccessorKind(instruction.Operand),
                        resumableAccessorValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.DefineComputedObjectAccessor:
                    // `{ get [expr](){} }` / `{ set [expr](v){} }`. Stack layout [object, key, accessor]; pop
                    // accessor then key. Computed-key coercion can throw and surfaces as the resumable Throw
                    // step before the accessor is defined.
                    var resumableComputedAccessorValue = stack[--stackPointer];
                    var resumableComputedAccessorKey = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var resumableComputedAccessorObjectLiteralTarget))
                    {
                        throw new InvalidOperationException(
                            "Computed object accessor unified bytecode op requires an object receiver.");
                    }

                    var resumableComputedAccessorName =
                        JsOps.GetRequiredPropertyName(resumableComputedAccessorKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    DefineObjectLiteralAccessor(
                        resumableComputedAccessorObjectLiteralTarget,
                        resumableComputedAccessorName,
                        DecodeObjectAccessorKind(instruction.Operand),
                        resumableComputedAccessorValue);
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ObjectSpread:
                    // `{ ...source }`. Pop the spread source, leaving the object on top, and copy its own
                    // enumerable properties (invoking getters in property order) onto the receiver via the
                    // sync VM's ApplyObjectLiteralSpread. A throwing getter or a non-object coercion failure
                    // surfaces as the resumable Throw step.
                    var resumableObjectSpreadValue = stack[--stackPointer];
                    if (!stack[stackPointer - 1].TryGetObject<JsObject>(out var resumableObjectSpreadTarget))
                    {
                        throw new InvalidOperationException(
                            "Object spread unified bytecode op requires an object receiver.");
                    }

                    ApplyObjectLiteralSpread(resumableObjectSpreadTarget, resumableObjectSpreadValue, context);
                    if (context.ShouldStopEvaluation)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Jump:
                    programCounter = instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.JumpWithDriverCleanup:
                    if (!TryCleanupDriverStatesForControlTargetResumable(
                            instruction.Operand,
                            isBreak: true,
                            program,
                            slots,
                            context,
                            state,
                            programCounter,
                            stackPointer,
                            out var jumpCleanupStep))
                    {
                        return jumpCleanupStep;
                    }

                    if (context.IsThrow)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

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

                case UnifiedBytecodeOpCode.Break:
                    if (!TryCleanupDriverStatesForControlTargetResumable(
                            instruction.Operand,
                            isBreak: true,
                            program,
                            slots,
                            context,
                            state,
                            programCounter,
                            stackPointer,
                            out var breakCleanupStep))
                    {
                        return breakCleanupStep;
                    }

                    if (context.IsThrow)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter = instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.Continue:
                    if (!TryCleanupDriverStatesForControlTargetResumable(
                            instruction.Operand,
                            isBreak: false,
                            program,
                            slots,
                            context,
                            state,
                            programCounter,
                            stackPointer,
                            out var continueCleanupStep))
                    {
                        return continueCleanupStep;
                    }

                    if (context.IsThrow)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter = instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.EnterTry:
                    resumableTryFrames ??= state.ResumableTryFrames = new Stack<UnifiedBytecodeResumableTryFrame>();
                    resumableTryFrames.Push(new UnifiedBytecodeResumableTryFrame(instruction.Operand));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.EnterCatch:
                    {
                        var catchDescriptor = program.CatchDescriptors[instruction.Operand];
                        var thrownValue = resumableTryFrames is { Count: > 0 }
                            ? resumableTryFrames.Peek().ThrownValue
                            : JsValue.Undefined;
                        if (resumableTryFrames is { Count: > 0 })
                        {
                            resumableTryFrames.Peek().ActiveCatchDescriptor = catchDescriptor;
                        }

                        if (catchDescriptor.BindingSlot >= 0)
                        {
                            slots[catchDescriptor.BindingSlot] = thrownValue;
                            SyncSlotEnvironment(slotEnvironments, catchDescriptor.BindingSlot, thrownValue);
                        }

                        MarkCatchBindingSlots(
                            ref resumableInactiveCatchBindingSlots,
                            slots.Length,
                            catchDescriptor,
                            isInactive: false);
                        state.ResumableInactiveCatchBindingSlots = resumableInactiveCatchBindingSlots;
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PopEnvironment:
                    if (resumableTryFrames is { Count: > 0 } &&
                        resumableTryFrames.Peek().ActiveCatchDescriptor is { } activeCatchDescriptor &&
                        activeCatchDescriptor.ScopeId == instruction.Operand)
                    {
                        MarkCatchBindingSlots(
                            ref resumableInactiveCatchBindingSlots,
                            slots.Length,
                            activeCatchDescriptor,
                            isInactive: true);
                        state.ResumableInactiveCatchBindingSlots = resumableInactiveCatchBindingSlots;
                        resumableTryFrames.Peek().ActiveCatchDescriptor = null;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LeaveTry:
                    if (resumableTryFrames is { Count: > 0 })
                    {
                        var frame = resumableTryFrames.Peek();
                        var descriptor = program.TryDescriptors[frame.DescriptorIndex];
                        if (descriptor.LeaveTryTarget == programCounter &&
                            descriptor.FinallyTarget >= 0 &&
                            !frame.FinallyScheduled)
                        {
                            frame.FinallyScheduled = true;
                            frame.PendingCompletion = new UnifiedBytecodePendingAbruptCompletion(
                                UnifiedBytecodeAbruptCompletionKind.None,
                                JsValue.Undefined,
                                Target: -1,
                                ResumeTarget: instruction.Operand,
                                OriginatedInFinally: false);
                            programCounter = descriptor.FinallyTarget;
                        }
                        else if (descriptor.LeaveTryTarget == programCounter)
                        {
                            resumableTryFrames.Pop();
                            programCounter = instruction.Operand;
                        }
                        else
                        {
                            programCounter = instruction.Operand;
                        }
                    }
                    else
                    {
                        programCounter = instruction.Operand;
                    }

                    break;

                case UnifiedBytecodeOpCode.EndFinally:
                    if (TryCompleteResumableFinally(instruction.Operand, out var endFinallyStep))
                    {
                        return endFinallyStep;
                    }

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
                            if (TryHandleResumableAbruptCompletion(
                                UnifiedBytecodeAbruptCompletionKind.Throw,
                                payload,
                                -1,
                                hasControlTarget: false))
                            {
                                break;
                            }

                            state.PendingAbruptCompletion = new UnifiedBytecodePendingAbruptCompletion(
                                UnifiedBytecodeAbruptCompletionKind.Throw,
                                payload,
                                Target: -1,
                                ResumeTarget: programCounter + 1,
                                OriginatedInFinally: false);
                            state.IsCompleted = true;
                            SaveResumableState();
                            return UnifiedBytecodeStepResult.Throw(payload);
                        case UnifiedBytecodeResumePayloadKind.Return:
                            if (TryHandleResumableAbruptCompletion(
                                UnifiedBytecodeAbruptCompletionKind.Return,
                                payload,
                                -1,
                                hasControlTarget: false))
                            {
                                break;
                            }

                            state.PendingAbruptCompletion = new UnifiedBytecodePendingAbruptCompletion(
                                UnifiedBytecodeAbruptCompletionKind.Return,
                                payload,
                                Target: -1,
                                ResumeTarget: programCounter + 1,
                                OriginatedInFinally: false);
                            state.IsCompleted = true;
                            SaveResumableState();
                            return UnifiedBytecodeStepResult.Completed(payload);
                        default:
                            if (instruction.Operand >= 0)
                            {
                                slots[instruction.Operand] = payload;
                                SyncSlotEnvironment(slotEnvironments, instruction.Operand, payload);
                                ClearInactiveCatchBindingSlot(resumableInactiveCatchBindingSlots, instruction.Operand);
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
                            if (TryHandleResumableAbruptCompletion(
                                    UnifiedBytecodeAbruptCompletionKind.Throw,
                                    awaitedDiscard,
                                    -1,
                                    hasControlTarget: false))
                            {
                                break;
                            }

                            state.IsCompleted = true;
                            SaveResumableState();
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
                        if (TryHandleResumableAbruptCompletion(
                                UnifiedBytecodeAbruptCompletionKind.Throw,
                                context.FlowValue,
                                -1,
                                hasControlTarget: false))
                        {
                            context.Clear();
                            break;
                        }

                        state.IsCompleted = true;
                        SaveResumableState();
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.AwaitValue:
                    if (TryConsumePendingAwaitResume(state, out var awaitedValue, out var awaitedValueThrow))
                    {
                        if (awaitedValueThrow)
                        {
                            if (TryHandleResumableAbruptCompletion(
                                    UnifiedBytecodeAbruptCompletionKind.Throw,
                                    awaitedValue,
                                    -1,
                                    hasControlTarget: false))
                            {
                                break;
                            }

                            state.IsCompleted = true;
                            SaveResumableState();
                            return UnifiedBytecodeStepResult.Throw(awaitedValue);
                        }

                        stack[stackPointer++] = awaitedValue;
                        programCounter++;
                        break;
                    }

                    var awaitValueCandidate = stack[--stackPointer];
                    var awaitValuePendingPromise = state.PendingAwaitPromise;
                    if (!AwaitScheduler.TryResolvePromiseOrYield(
                            awaitValueCandidate,
                            asyncStepMode: true,
                            ref awaitValuePendingPromise,
                            context,
                            out var awaitValueResult))
                    {
                        state.PendingAwaitPromise = awaitValuePendingPromise;
                        state.ProgramCounter = programCounter;
                        state.StackPointer = stackPointer;
                        state.ResumePayloadKind = UnifiedBytecodeResumePayloadKind.None;
                        state.ResumePayload = JsValue.Undefined;
                        return UnifiedBytecodeStepResult.PendingAwait(state.PendingAwaitPromise);
                    }

                    if (context.IsThrow)
                    {
                        if (TryHandleResumableAbruptCompletion(
                                UnifiedBytecodeAbruptCompletionKind.Throw,
                                context.FlowValue,
                                -1,
                                hasControlTarget: false))
                        {
                            context.Clear();
                            break;
                        }

                        state.IsCompleted = true;
                        SaveResumableState();
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    stack[stackPointer++] = awaitValueResult;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.IteratorInit:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        var iterableValue = stack[--stackPointer];
                        var iteratorState = CreateIteratorDriverState(iterableValue, descriptor.IteratorKind, context);
                        slots[descriptor.StateSlot] = iteratorState.AsJsValue;
                        SyncSlotEnvironment(slotEnvironments, descriptor.StateSlot, iteratorState.AsJsValue);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.IteratorMoveNext:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        if (descriptor.IteratorKind == IteratorDriverKind.Await)
                        {
                            if (!TryMoveAsyncIteratorNext(
                                    descriptor,
                                    slots,
                                    context,
                                    state,
                                    programCounter,
                                    stackPointer,
                                    out var asyncNextProgramCounter,
                                    out var asyncIteratorStep))
                            {
                                if (asyncIteratorStep.Kind == UnifiedBytecodeStepKind.Throw &&
                                    TryHandleResumableAbruptCompletion(
                                        UnifiedBytecodeAbruptCompletionKind.Throw,
                                        asyncIteratorStep.Value,
                                        -1,
                                        hasControlTarget: false))
                                {
                                    state.IsCompleted = false;
                                    context.Clear();
                                    break;
                                }

                                return asyncIteratorStep;
                            }

                            programCounter = asyncNextProgramCounter;
                            break;
                        }

                        if (!TryMoveIteratorNext(
                                descriptor,
                                slots,
                                slotEnvironments,
                                state.CallingEnvironment,
                                context,
                                ref state.NextActiveDriverOrdinal,
                                out var nextProgramCounter))
                        {
                            if (TryHandleResumableAbruptCompletion(
                                    UnifiedBytecodeAbruptCompletionKind.Throw,
                                    context.FlowValue,
                                    -1,
                                    hasControlTarget: false))
                            {
                                context.Clear();
                                break;
                            }

                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter = nextProgramCounter;
                        break;
                    }

                case UnifiedBytecodeOpCode.IteratorClose:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        if (!TryCloseIteratorDriverStateResumable(
                                descriptor.StateSlot,
                                slots,
                                context,
                                state,
                                programCounter,
                                stackPointer,
                                preserveExistingThrow: context.IsThrow,
                                out var closeStepResult))
                        {
                            return closeStepResult;
                        }

                        if (context.IsThrow)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
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
                        forInState.ActiveDriverOrdinal = ++state.NextActiveDriverOrdinal;
                        CollectEnumerablePropertyKeys(objectValue, forInState.PropertyKeys);
                        slots[descriptor.StateSlot] = forInState.AsJsValue;
                        SyncSlotEnvironment(slotEnvironments, descriptor.StateSlot, forInState.AsJsValue);
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
                        if (!TryGetIteratorForArrayDestructuring(sourceValue, context, out var destructuringState))
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        slots[descriptor.StateSlot] = JsValue.FromObjectUnsafe(destructuringState);
                        SyncSlotEnvironment(
                            slotEnvironments,
                            descriptor.StateSlot,
                            slots[descriptor.StateSlot]);
                        destructuringState.ActiveDriverOrdinal = ++state.NextActiveDriverOrdinal;
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
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (descriptor.TargetSlot >= 0)
                        {
                            slots[descriptor.TargetSlot] = value;
                            SyncSlotEnvironment(slotEnvironments, descriptor.TargetSlot, value);
                        }
                        else if (descriptor.TargetNameConstantIndex >= 0)
                        {
                            StoreDynamicIdentifierValue(
                                program.StringConstants[descriptor.TargetNameConstantIndex],
                                false,
                                value,
                                RequireDynamicEnvironment(state.CallingEnvironment),
                                context);
                            if (context.ShouldStopEvaluation)
                            {
                                state.IsCompleted = true;
                                return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                            }
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
                                slotEnvironments: null,
                                context,
                                out var restValue))
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (descriptor.TargetSlot >= 0)
                        {
                            slots[descriptor.TargetSlot] = restValue;
                            SyncSlotEnvironment(slotEnvironments, descriptor.TargetSlot, restValue);
                        }
                        else if (descriptor.TargetNameConstantIndex >= 0)
                        {
                            StoreDynamicIdentifierValue(
                                program.StringConstants[descriptor.TargetNameConstantIndex],
                                false,
                                restValue,
                                RequireDynamicEnvironment(state.CallingEnvironment),
                                context);
                            if (context.ShouldStopEvaluation)
                            {
                                state.IsCompleted = true;
                                return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                            }
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ArrayDestructuringClose:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        CloseArrayDestructuringState(
                            descriptor.StateSlot,
                            slots,
                            slotEnvironments,
                            context,
                            preserveExistingThrow: false);
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ObjectDestructuringInit:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        var sourceValue = stack[--stackPointer];
                        if (!TryGetSourceForObjectDestructuring(sourceValue, context, out var objectState))
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        slots[descriptor.StateSlot] = JsValue.FromObjectUnsafe(objectState);
                        SyncSlotEnvironment(
                            slotEnvironments,
                            descriptor.StateSlot,
                            slots[descriptor.StateSlot]);
                        objectState.ActiveDriverOrdinal = ++state.NextActiveDriverOrdinal;
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
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (descriptor.TargetSlot >= 0)
                        {
                            slots[descriptor.TargetSlot] = value;
                            SyncSlotEnvironment(slotEnvironments, descriptor.TargetSlot, value);
                        }
                        else if (descriptor.TargetNameConstantIndex >= 0)
                        {
                            StoreDynamicIdentifierValue(
                                program.StringConstants[descriptor.TargetNameConstantIndex],
                                false,
                                value,
                                RequireDynamicEnvironment(state.CallingEnvironment),
                                context);
                            if (context.ShouldStopEvaluation)
                            {
                                state.IsCompleted = true;
                                return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                            }
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
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (descriptor.TargetSlot >= 0)
                        {
                            slots[descriptor.TargetSlot] = restValue;
                            SyncSlotEnvironment(slotEnvironments, descriptor.TargetSlot, restValue);
                        }
                        else if (descriptor.TargetNameConstantIndex >= 0)
                        {
                            StoreDynamicIdentifierValue(
                                program.StringConstants[descriptor.TargetNameConstantIndex],
                                false,
                                restValue,
                                RequireDynamicEnvironment(state.CallingEnvironment),
                                context);
                            if (context.ShouldStopEvaluation)
                            {
                                state.IsCompleted = true;
                                return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                            }
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ObjectDestructuringClose:
                    {
                        var descriptor = program.DriverDescriptors[instruction.Operand];
                        CloseObjectDestructuringState(
                            descriptor.StateSlot,
                            slots,
                            slotEnvironments: null);
                        programCounter++;
                        break;
                    }

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
                    var isAsyncYieldStar = state.IsAsyncGenerator;
                    if (!TryGetDriverState<IteratorDriverState>(slots, yieldStarDescriptor.StateSlot, out var yieldStarState))
                    {
                        var iterable = stack[--stackPointer];
                        try
                        {
                            yieldStarState = CreateIteratorDriverState(
                                iterable,
                                isAsyncYieldStar ? IteratorDriverKind.Await : IteratorDriverKind.Sync,
                                context);
                        }
                        catch (ThrowSignal signal)
                        {
                            state.IsCompleted = true;
                            state.ProgramCounter = programCounter;
                            state.StackPointer = stackPointer;
                            return UnifiedBytecodeStepResult.Throw(signal.ThrownValue);
                        }

                        slots[yieldStarDescriptor.StateSlot] = JsValue.FromObjectUnsafe(yieldStarState);
                    }

                    if (yieldStarState.YieldStarPendingAwaitKind is not YieldStarPendingAwaitKind.None)
                    {
                        var pendingKind = yieldStarState.YieldStarPendingAwaitKind;
                        yieldStarState.YieldStarPendingAwaitKind = YieldStarPendingAwaitKind.None;
                        if (!TryConsumePendingAwaitResume(state, out var awaitedYieldStarResult, out var awaitedYieldStarThrow))
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(StandardLibrary.CreateTypeError(
                                "Missing awaited yield* iterator result.",
                                context,
                                context.RealmState));
                        }

                        if (awaitedYieldStarThrow)
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(awaitedYieldStarResult);
                        }

                        if (!TryReadYieldStarResolvedIteratorResult(
                                awaitedYieldStarResult,
                                context,
                                readDoneValue: true,
                                forceYieldWhenReturnPromiseDone: false,
                                awaitedPromise: true,
                                readYieldValue: true,
                                out var awaitedDelegatedValue,
                                out var awaitedDelegatedDone,
                                out var awaitedDelegatedIteratorResult))
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (pendingKind == YieldStarPendingAwaitKind.Return)
                        {
                            if (awaitedDelegatedDone)
                            {
                                CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                                state.IsCompleted = true;
                                return UnifiedBytecodeStepResult.Completed(awaitedDelegatedValue);
                            }

                            state.ProgramCounter = programCounter;
                            state.StackPointer = stackPointer;
                            return UnifiedBytecodeStepResult.Yield(awaitedDelegatedValue);
                        }

                        if (awaitedDelegatedDone)
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            if (yieldStarDescriptor.ValueSlot >= 0)
                            {
                                slots[yieldStarDescriptor.ValueSlot] = awaitedDelegatedValue;
                            }

                            programCounter++;
                            break;
                        }

                        state.ProgramCounter = programCounter;
                        state.StackPointer = stackPointer;
                        return isAsyncYieldStar || awaitedDelegatedIteratorResult.IsUndefined
                            ? UnifiedBytecodeStepResult.Yield(awaitedDelegatedValue)
                            : UnifiedBytecodeStepResult.YieldIteratorResult(awaitedDelegatedIteratorResult);
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
                                out var throwIteratorResult,
                                out var throwMethodMissing,
                                isAsyncYieldStar,
                                state,
                                programCounter,
                                stackPointer,
                                out var throwPendingStep))
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (throwPendingStep is { } pendingStep)
                        {
                            return pendingStep;
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
                        return isAsyncYieldStar || throwIteratorResult.IsUndefined
                            ? UnifiedBytecodeStepResult.Yield(throwResumeValue)
                            : UnifiedBytecodeStepResult.YieldIteratorResult(throwIteratorResult);
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
                                out var returnIteratorResult,
                                out var returnMethodMissing,
                                isAsyncYieldStar,
                                state,
                                programCounter,
                                stackPointer,
                                out var returnPendingStep))
                        {
                            CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (returnPendingStep is { } pendingStep)
                        {
                            return pendingStep;
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
                        return isAsyncYieldStar || returnIteratorResult.IsUndefined
                            ? UnifiedBytecodeStepResult.Yield(returnResumeValue)
                            : UnifiedBytecodeStepResult.YieldIteratorResult(returnIteratorResult);
                    }

                    var isFirstYieldStarEntry = !yieldStarState.YieldStarStarted;
                    var nextSendValue = isFirstYieldStarEntry
                        ? JsValue.Undefined
                        : delegatedResumePayload;
                    var hasNextSendValue = isFirstYieldStarEntry ||
                                           !delegatedResumePayload.IsUndefined ||
                                           delegatedResumeKind == UnifiedBytecodeResumePayloadKind.Value;
                    yieldStarState.YieldStarStarted = true;
                    if (!TryReadYieldStarIteratorNextValue(
                            yieldStarState,
                            context,
                            callingEnvironment: null,
                            nextSendValue,
                            hasNextSendValue,
                            readDoneValue: true,
                            out var delegatedValue,
                            out var delegatedDone,
                            out var delegatedIteratorResult,
                            isAsyncYieldStar,
                            state,
                            programCounter,
                            stackPointer,
                            out var nextPendingStep))
                    {
                        CompleteIteratorDriverState(yieldStarDescriptor.StateSlot, slots, null, yieldStarState);
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    if (nextPendingStep is { } pendingNext)
                    {
                        return pendingNext;
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
                    return isAsyncYieldStar || delegatedIteratorResult.IsUndefined
                        ? UnifiedBytecodeStepResult.Yield(delegatedValue)
                        : UnifiedBytecodeStepResult.YieldIteratorResult(delegatedIteratorResult);

                case UnifiedBytecodeOpCode.Return:
                    var returnValue = stack[--stackPointer];
                    if (TryHandleResumableAbruptCompletion(
                            UnifiedBytecodeAbruptCompletionKind.Return,
                            returnValue,
                            -1,
                            hasControlTarget: false))
                    {
                        break;
                    }

                    if (!TryCleanupActiveDriverStatesResumable(
                            slots,
                            context,
                            state,
                            programCounter,
                            stackPointer,
                            preserveExistingThrow: false,
                            out var returnCleanupStep))
                    {
                        return returnCleanupStep;
                    }

                    if (context.IsThrow)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    state.IsCompleted = true;
                    state.ProgramCounter = programCounter + 1;
                    state.StackPointer = stackPointer;
                    return UnifiedBytecodeStepResult.Completed(returnValue);

                case UnifiedBytecodeOpCode.ReturnUndefined:
                    if (TryHandleResumableAbruptCompletion(
                            UnifiedBytecodeAbruptCompletionKind.Return,
                            JsValue.Undefined,
                            -1,
                            hasControlTarget: false))
                    {
                        break;
                    }

                    if (!TryCleanupActiveDriverStatesResumable(
                            slots,
                            context,
                            state,
                            programCounter,
                            stackPointer,
                            preserveExistingThrow: false,
                            out var returnUndefinedCleanupStep))
                    {
                        return returnUndefinedCleanupStep;
                    }

                    if (context.IsThrow)
                    {
                        state.IsCompleted = true;
                        return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                    }

                    state.IsCompleted = true;
                    state.ProgramCounter = programCounter + 1;
                    state.StackPointer = stackPointer;
                    return UnifiedBytecodeStepResult.Completed(JsValue.Undefined);

                case UnifiedBytecodeOpCode.Throw:
                    var throwValue = stack[--stackPointer];
                    if (TryHandleResumableAbruptCompletion(
                            UnifiedBytecodeAbruptCompletionKind.Throw,
                            throwValue,
                            -1,
                            hasControlTarget: false))
                    {
                        break;
                    }

                    context.SetThrow(throwValue);
                    if (!TryCleanupActiveDriverStatesResumable(
                            slots,
                            context,
                            state,
                            programCounter,
                            stackPointer + 1,
                            preserveExistingThrow: true,
                            out var throwCleanupStep))
                    {
                        return throwCleanupStep;
                    }

                    state.IsCompleted = true;
                    state.ProgramCounter = programCounter + 1;
                    state.StackPointer = stackPointer;
                    return UnifiedBytecodeStepResult.Throw(throwValue);

                case UnifiedBytecodeOpCode.PrepareIdentifierCallTarget:
                    {
                        // Plain `f()`: load the callee from its activation slot, pushing the
                        // <undefined this, callee> receiver/callee pair the invocation boundary expects.
                        // Eligible resumable programs have no captured/dynamic activation, so the callee
                        // always resolves through a slot — no calling environment is consulted here.
                        var resumableCallTarget = program.CallTargetConstants[instruction.Operand];
                        if (resumableCallTarget.Kind != UnifiedBytecodeCallTargetKind.Identifier)
                        {
                            throw new InvalidOperationException(
                                "Identifier call-target preparation requires an identifier call target constant.");
                        }

                        if (IsInactiveCatchBindingSlot(resumableInactiveCatchBindingSlots, resumableCallTarget.SlotIndex))
                        {
                            SetInactiveCatchBindingReferenceError(program, resumableCallTarget.SlotIndex, context);
                            state.IsCompleted = true;
                            SaveResumableState();
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        var resumableCallableValue = slots[resumableCallTarget.SlotIndex];
                        if (resumableCallableValue.IsUninitialized)
                        {
                            SetUninitializedSlotReferenceError(program, resumableCallTarget.SlotIndex, context);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        stack[stackPointer++] = JsValue.Undefined;
                        stack[stackPointer++] = resumableCallableValue;
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PrepareNamedCallTarget:
                    {
                        // `o.m()`: receiver already on the stack; read the named method and push it as the
                        // callee, leaving the receiver in place as the call boundary's `this`.
                        var resumableNamedCallTarget = program.CallTargetConstants[instruction.Operand];
                        if (resumableNamedCallTarget.Kind != UnifiedBytecodeCallTargetKind.NamedMember ||
                            (uint)resumableNamedCallTarget.NameConstantIndex >= (uint)program.StringConstants.Length)
                        {
                            throw new InvalidOperationException(
                                "Named member call-target preparation requires a named member call target constant.");
                        }

                        var resumableNamedReceiver = stack[stackPointer - 1];
                        if (GetResumableShortCircuitFlag(stackPointer - 1))
                        {
                            // Receiver is the synthetic short-circuit undefined (`(o?.a).m()` head was
                            // nullish): push undefined as the callee, also flagged, so the boundary skips.
                            PushResumableValueWithFlag(JsValue.Undefined, wasShortCircuited: true);
                            programCounter++;
                            break;
                        }

                        PushResumableValue(GetNamedPropertyValue(
                            resumableNamedReceiver,
                            program.StringConstants[resumableNamedCallTarget.NameConstantIndex],
                            context));
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PrepareComputedCallTarget:
                    {
                        // `o[k]()`: pop the key, keep the receiver as `this`, push the resolved method.
                        var resumableComputedCallTarget = program.CallTargetConstants[instruction.Operand];
                        if (resumableComputedCallTarget.Kind != UnifiedBytecodeCallTargetKind.ComputedMember)
                        {
                            throw new InvalidOperationException(
                                "Computed member call-target preparation requires a computed member call target constant.");
                        }

                        var resumableComputedCallKey = stack[--stackPointer];
                        var resumableComputedCallReceiver = stack[stackPointer - 1];
                        if (GetResumableShortCircuitFlag(stackPointer - 1))
                        {
                            PushResumableValueWithFlag(JsValue.Undefined, wasShortCircuited: true);
                            programCounter++;
                            break;
                        }

                        PushResumableValue(GetComputedCallTargetValue(
                            resumableComputedCallReceiver,
                            resumableComputedCallKey,
                            context));
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget:
                    {
                        // `super.m()`: resolve the super method through the captured method environment,
                        // pushing the derived receiver and callee pair expected by CallInvocationBoundary.
                        PrepareNamedSuperCallTarget(
                            program,
                            instruction.Operand,
                            RequireDynamicEnvironment(state.CallingEnvironment),
                            stack,
                            ref stackPointer,
                            context);
                        SetResumableShortCircuitFlag(stackPointer - 1, false);
                        SetResumableShortCircuitFlag(stackPointer - 2, false);
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget:
                    {
                        // `super[k]()`: pop/resolve the key, then use the same super lookup helper as the
                        // synchronous VM. The receiver remains the derived instance, not the prototype.
                        PrepareComputedSuperCallTarget(
                            program,
                            instruction.Operand,
                            RequireDynamicEnvironment(state.CallingEnvironment),
                            stack,
                            ref stackPointer,
                            context);
                        SetResumableShortCircuitFlag(stackPointer - 1, false);
                        SetResumableShortCircuitFlag(stackPointer - 2, false);
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PrepareIdentifierOptionalCallTarget:
                    {
                        // `f?.()`: load the callee from its activation slot. If nullish, short-circuit the
                        // whole call to undefined by pushing undefined and jumping to the chain end; the
                        // packed operand carries the call-target index (low 16 bits) and the jump target
                        // (high bits), matching the sync VM's PrepareIdentifierOptionalCallTarget encoding.
                        var optCallTargetIdx = instruction.Operand & 0xFFFF;
                        var optJumpTarget = instruction.Operand >> 16;
                        var optCallTarget = program.CallTargetConstants[optCallTargetIdx];
                        if (optCallTarget.Kind != UnifiedBytecodeCallTargetKind.Identifier)
                        {
                            throw new InvalidOperationException(
                                "Optional identifier call-target preparation requires an identifier call target constant.");
                        }

                        var optCallableValue = slots[optCallTarget.SlotIndex];
                        if (optCallableValue.IsUninitialized)
                        {
                            SetUninitializedSlotReferenceError(program, optCallTarget.SlotIndex, context);
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (optCallableValue.IsNullOrUndefined)
                        {
                            PushResumableValue(JsValue.Undefined);
                            programCounter = optJumpTarget;
                            break;
                        }

                        PushResumableValue(JsValue.Undefined);
                        PushResumableValue(optCallableValue);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget:
                    {
                        // Mirrors the sync VM verbatim. Two encodings:
                        //   IsOptionalReceiverCheck  -> `box?.read()`: check the receiver; nullish short-circuits.
                        //   otherwise                -> `box.read?.()`: load the method; nullish method short-circuits.
                        // The packed operand holds the call-target index (low 16) and chain-end jump target (high).
                        // Short-circuit here is realized by the JUMP (not flag propagation): the replaced
                        // undefined is the final call result, so ReplaceResumableTop clears the flag.
                        var optNamedCallTargetIdx = instruction.Operand & 0xFFFF;
                        var optNamedJumpTarget = instruction.Operand >> 16;
                        var optNamedCallTarget = program.CallTargetConstants[optNamedCallTargetIdx];

                        if (optNamedCallTarget.IsOptionalReceiverCheck)
                        {
                            // Case 1: box?.read() — check receiver; if nullish, short-circuit to undefined.
                            var optReceiver = stack[stackPointer - 1];
                            if (optReceiver.IsNullOrUndefined)
                            {
                                ReplaceResumableTop(JsValue.Undefined);
                                programCounter = optNamedJumpTarget;
                                break;
                            }

                            PushResumableValue(GetNamedPropertyValue(
                                optReceiver,
                                program.StringConstants[optNamedCallTarget.NameConstantIndex],
                                context));
                            if (context.ShouldStopEvaluation)
                            {
                                state.IsCompleted = true;
                                return UnifiedBytecodeStepResult.Throw(context.FlowValue);
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
                                state.IsCompleted = true;
                                return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                            }

                            if (callee.IsNullOrUndefined)
                            {
                                ReplaceResumableTop(JsValue.Undefined);
                                programCounter = optNamedJumpTarget;
                                break;
                            }

                            PushResumableValue(callee);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget:
                    {
                        // `o[k]?.()`: computed-member receiver already on the stack, key on top. Pop the key,
                        // load the method off the receiver; if the method is nullish, short-circuit the whole
                        // call to undefined by replacing the receiver with undefined and jumping to the chain
                        // end. The packed operand carries the call-target index (low 16) and chain-end jump
                        // target (high) — identical encoding to the sync VM's handler. Short-circuit here is
                        // realized by the JUMP (not flag propagation): the replaced undefined is the final call
                        // result, so ReplaceResumableTop clears the flag. Literal twin of the sync VM case.
                        var optComputedCallTargetIdx = instruction.Operand & 0xFFFF;
                        var optComputedJumpTarget = instruction.Operand >> 16;
                        _ = program.CallTargetConstants[optComputedCallTargetIdx];

                        var optComputedKey = stack[--stackPointer];
                        var optComputedReceiver = stack[stackPointer - 1];
                        var optComputedCallee = GetComputedCallTargetValue(optComputedReceiver, optComputedKey, context);
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        if (optComputedCallee.IsNullOrUndefined)
                        {
                            ReplaceResumableTop(JsValue.Undefined);
                            programCounter = optComputedJumpTarget;
                            break;
                        }

                        PushResumableValue(optComputedCallee);
                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget:
                    {
                        // `freeFn?.()` where `freeFn` is a free/dynamic identifier (module/script-level or a
                        // captured outer binding). Resolve the callee by name against the live closure
                        // environment threaded onto UnifiedBytecodeResumeState.CallingEnvironment, pushing the
                        // <thisValue, callee> pair. If the resolved callee is nullish, short-circuit the whole
                        // call to undefined: drop the pushed pair, push undefined, and jump to the chain end.
                        // The packed operand carries the name constant index (low 16) and the chain-end jump
                        // target (high) — identical encoding to the sync VM's handler. Literal twin of the
                        // sync VM case; resolution is live, so a resumed step reflects any reassignment.
                        var resumableDynamicOptionalCallEnvironment = RequireDynamicEnvironment(state.CallingEnvironment);
                        var dynamicOptionalNameIndex = instruction.Operand & 0xFFFF;
                        var dynamicOptionalJumpTarget = instruction.Operand >> 16;
                        PrepareDynamicIdentifierCallTarget(
                            program.StringConstants[dynamicOptionalNameIndex],
                            resumableDynamicOptionalCallEnvironment,
                            stack,
                            ref stackPointer,
                            context);
                        SetResumableShortCircuitFlag(stackPointer - 1, false);
                        SetResumableShortCircuitFlag(stackPointer - 2, false);
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        var resumableDynamicOptionalCallable = stack[stackPointer - 1];
                        if (resumableDynamicOptionalCallable.IsNullOrUndefined)
                        {
                            stackPointer -= 2;
                            PushResumableValue(JsValue.Undefined);
                            programCounter = dynamicOptionalJumpTarget;
                            break;
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.CallInvocationBoundary:
                    {
                        // Synchronous call dispatch from inside the resumable frame. ExecutePreparedCall
                        // runs the callee to completion (its own suspension, if any, is a separate resumable
                        // frame) and returns a value. slotEnvironments is null because eligible resumable
                        // programs have no environment-backed slots; the calling environment is threaded for
                        // caller-context-sensitive callees (e.g. environment-aware host functions).
                        stackPointer = ExecutePreparedCall(
                            DecodeCallBoundaryArgumentCount(instruction.Operand),
                            DecodeCallBoundarySpreadMask(program, instruction.Operand),
                            DecodeCallBoundaryIsDirectEval(instruction.Operand),
                            stack,
                            stackPointer,
                            slots,
                            slotEnvironments: null,
                            context,
                            state.CallingEnvironment);
                        // Clear the flag on the call result slot, matching the sync VM. A non-short-circuited
                        // call result must never inherit a stale short-circuit flag from a prior chain.
                        SetResumableShortCircuitFlag(stackPointer - 1, false);
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

                case UnifiedBytecodeOpCode.ConstructInvocationBoundary:
                    {
                        // Synchronous construct dispatch from inside the resumable frame (`new C(args)`).
                        // The constructor value and its arguments sit on the operand stack
                        // ([constructor, arg0 .. arg(n-1)]) — pushed by preceding ops in source order, each of
                        // which can itself have suspended (`new C(yield 1)`, `new C(o.a)` between two yields) and
                        // been restored from UnifiedBytecodeResumeState.OperandStack. ExecutePreparedConstruct
                        // runs [[Construct]] to completion (using the constructor itself as new.target, the
                        // `new C()` semantics) and replaces the constructor slot with the result, reusing the
                        // sync VM handler verbatim so this-binding/prototype wiring and the non-constructor
                        // TypeError are identical. slotEnvironments is null because eligible resumable programs
                        // have no environment-backed slots. A thrown constructor (ThrowSignal) is translated to
                        // the resumable Throw step exactly like the call boundary.
                        stackPointer = ExecutePreparedConstruct(
                            DecodeCallBoundaryArgumentCount(instruction.Operand),
                            DecodeCallBoundarySpreadMask(program, instruction.Operand),
                            stack,
                            stackPointer,
                            slots,
                            slotEnvironments: null,
                            context);
                        // Clear the flag on the construct result slot, matching the sync VM. A construct result
                        // must never inherit a stale short-circuit flag from a prior chain on the reused slot.
                        SetResumableShortCircuitFlag(stackPointer - 1, false);
                        if (context.ShouldStopEvaluation)
                        {
                            state.IsCompleted = true;
                            return UnifiedBytecodeStepResult.Throw(context.FlowValue);
                        }

                        programCounter++;
                        break;
                    }

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

    private static void ClearInactiveCatchBindingSlot(bool[]? inactiveCatchBindingSlots, int slotIndex)
    {
        if (inactiveCatchBindingSlots is not null &&
            (uint)slotIndex < (uint)inactiveCatchBindingSlots.Length)
        {
            inactiveCatchBindingSlots[slotIndex] = false;
        }
    }

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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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

    private static void DeclareDynamicLexical(
        string name,
        bool isConst,
        JsEnvironment environment,
        EvaluationContext context)
    {
        try
        {
            environment.DefineJsValue(
                Symbol.Intern(name),
                JsValue.Uninitialized,
                isConst,
                isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
        }
    }

    private static void InitializeDynamicLexical(
        string name,
        bool allowNameInference,
        JsValue value,
        JsEnvironment environment,
        EvaluationContext context)
    {
        if (allowNameInference &&
            value is { Kind: JsValueKind.Object, ObjectValue: TypedAstEvaluator.IFunctionNameTarget nameTarget })
        {
            nameTarget.EnsureHasName(name);
        }

        try
        {
            environment.DefineJsValue(
                Symbol.Intern(name),
                value,
                isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
        }
    }

    private static void DeclareFunction(
        UnifiedBytecodeProgram program,
        int operand,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var descriptor = program.FunctionLiteralConstants[DecodeFunctionDeclarationIndex(operand)];
        var name = Symbol.Intern(program.StringConstants[DecodeFunctionDeclarationNameIndex(operand)]);
        var functionValue = TypedAstEvaluator.CreateFunctionValueFromDeclaration(
            descriptor,
            environment,
            context);
        var functionJsValue = JsValue.FromObjectUnsafe(functionValue);
        var varEnvironment = environment.GetVarEnvironment();
        var isAtVarEnvironment = ReferenceEquals(varEnvironment, environment) ||
                                 environment.IsEvalDeclarationEnvironment;
        var suppressAnnexBVarUpdate = (descriptor.Function.IsAsync ||
                                       descriptor.Function.WasAsync ||
                                       descriptor.Function.IsGenerator) &&
                                      context.ExecutionKind != ExecutionKind.Eval;

        var isHoistedUndefinedBinding = false;
        if (!suppressAnnexBVarUpdate &&
            isAtVarEnvironment &&
            !context.CurrentScope.IsStrict &&
            varEnvironment.HasFunctionScopedBinding(name))
        {
            var existingValue = varEnvironment.GetBindingValueDirect(name);
            if (existingValue.IsUndefined)
            {
                isHoistedUndefinedBinding = true;
            }
        }

        if (isAtVarEnvironment)
        {
            if (isHoistedUndefinedBinding && !environment.IsAnnexBBlocked(name))
            {
                varEnvironment.AssignJsValue(name, functionJsValue);

                if (varEnvironment.IsGlobalFunctionScope)
                {
                    varEnvironment.GetRootGlobalObject()?.SetProperty(name.Name, functionJsValue);
                }
            }

            return;
        }

        var isBlocked = !varEnvironment.IsStrict &&
                        (environment.IsAnnexBBlocked(name) ||
                         HasEnclosingLexicalBinding(environment.Enclosing, name));
        if (!isBlocked || !environment.IsBodyEnvironment)
        {
            environment.DefineJsValue(
                name,
                functionJsValue,
                isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
        }

        if (suppressAnnexBVarUpdate || varEnvironment.IsStrict || isBlocked)
        {
            return;
        }

        if (varEnvironment.HasFunctionScopedBinding(name))
        {
            varEnvironment.AssignJsValue(name, functionJsValue);
        }
        else
        {
            varEnvironment.DefineFunctionScoped(
                name,
                functionJsValue,
                hasInitializer: true,
                isFunctionDeclaration: true,
                context: context);
        }

        UpdateIntermediateVarBindings(environment.Enclosing, varEnvironment, name, functionJsValue);
    }

    private static bool HasEnclosingLexicalBinding(JsEnvironment? start, Symbol name)
    {
        var current = start;
        while (current is not null)
        {
            if (current.IsFunctionScope)
            {
                break;
            }

            if (current.IsBodyEnvironment || current.IsSimpleCatchParameter(name))
            {
                current = current.Enclosing;
                continue;
            }

            if (current.TryGetSlotIndex(name, out var slotIndex))
            {
                ref var slot = ref current.GetSlotByIndex(slotIndex);
                if (slot.IsLexical && slot.BlocksFunctionScopeOverride)
                {
                    return true;
                }
            }

            current = current.Enclosing;
        }

        return false;
    }

    private static void UpdateIntermediateVarBindings(
        JsEnvironment? start,
        JsEnvironment stop,
        Symbol name,
        JsValue value)
    {
        var current = start;
        while (current is not null && !ReferenceEquals(current, stop))
        {
            if (current.TryGetSlotIndex(name, out var slotIndex))
            {
                current.SetSlotDirect(slotIndex, value);
            }

            current = current.Enclosing;
        }
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

    private static UnifiedSlotEnvironmentBinding?[] InitializeSlotEnvironments(
        UnifiedBytecodeProgram program,
        JsEnvironment callingEnvironment)
    {
        var slotCount = program.SlotCount;
        var slotEnvironments = new UnifiedSlotEnvironmentBinding?[slotCount];
        var slotNames = program.SlotNames;
        var rootSlotCount = Math.Min(slotCount, slotNames.Length);
        for (var i = 0; i < rootSlotCount; i++)
        {
            if (slotNames[i] is { } name &&
                callingEnvironment.TryGetSlotIndex(Symbol.Intern(name), out var environmentSlotIndex))
            {
                slotEnvironments[i] = new UnifiedSlotEnvironmentBinding(
                    callingEnvironment,
                    environmentSlotIndex);
            }
        }

        return slotEnvironments;
    }

    private static void SyncUnifiedSlotsToEnvironment(
        UnifiedBytecodeProgram program,
        Span<JsValue> slots,
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
        JsEnvironment environment)
    {
        var slotNames = program.SlotNames;
        var count = Math.Min(slots.Length, slotNames.Length);
        for (var i = 0; i < count; i++)
        {
            if (slotNames[i] is not { } ||
                slots[i].IsUninitialized)
            {
                continue;
            }

            if (TryGetSlotEnvironmentBinding(
                    program,
                    slotEnvironments,
                    i,
                    environment,
                    out var binding))
            {
                binding.Environment.SetSlotDirect(binding.SlotIndex, slots[i]);
            }
        }
    }

    private static void SyncEnvironmentToUnifiedSlots(
        UnifiedBytecodeProgram program,
        Span<JsValue> slots,
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
        JsEnvironment environment)
    {
        var slotNames = program.SlotNames;
        var count = Math.Min(slots.Length, slotNames.Length);
        for (var i = 0; i < count; i++)
        {
            if (slotNames[i] is not { })
            {
                continue;
            }

            if (!TryGetSlotEnvironmentBinding(
                    program,
                    slotEnvironments,
                    i,
                    environment,
                    out var binding))
            {
                continue;
            }

            ref var slot = ref binding.Environment.GetSlotByIndex(binding.SlotIndex);
            slots[i] = slot.IsUninitialized ? JsValue.Uninitialized : slot.Value;
        }
    }

    private static JsEnvironment CreateResumableClassLiteralEnvironment(
        UnifiedBytecodeProgram program,
        Span<JsValue> slots,
        JsEnvironment parentEnvironment,
        bool isStrict)
    {
        var environment = JsEnvironment.CreateInstance(
            parentEnvironment,
            isFunctionScope: true,
            isStrict,
            description: "resumable class literal activation");
        var slotNames = program.SlotNames;
        var count = Math.Min(slots.Length, slotNames.Length);
        for (var i = 0; i < count; i++)
        {
            if (slotNames[i] is not { } name)
            {
                continue;
            }

            environment.DefineJsValue(
                Symbol.Intern(name),
                slots[i],
                isConst: IsConstSlotIndex(i, program.ConstSlotIndices));
        }

        return environment;
    }

    private static bool RequiresResumableClassLiteralSlotEnvironment(ClassExpression classExpression)
    {
        var definition = classExpression.Definition;
        if (!definition.StaticElements.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (var field in definition.Fields)
        {
            if (field.IsStatic)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConstSlotIndex(int slotIndex, ImmutableArray<int> constSlotIndices)
    {
        if (constSlotIndices.IsDefaultOrEmpty)
        {
            return false;
        }

        for (var i = 0; i < constSlotIndices.Length; i++)
        {
            if (constSlotIndices[i] == slotIndex)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSlotEnvironmentBinding(
        UnifiedBytecodeProgram program,
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
        int unifiedSlotIndex,
        JsEnvironment fallback,
        out UnifiedSlotEnvironmentBinding binding)
    {
        if (slotEnvironments is not null &&
            (uint)unifiedSlotIndex < (uint)slotEnvironments.Length &&
            slotEnvironments[unifiedSlotIndex] is { } existingBinding)
        {
            binding = existingBinding;
            return true;
        }

        var slotNames = program.SlotNames;
        if ((uint)unifiedSlotIndex < (uint)slotNames.Length &&
            slotNames[unifiedSlotIndex] is { } name)
        {
            if (fallback.TryGetSlotIndex(Symbol.Intern(name), out var fallbackSlotIndex))
            {
                binding = new UnifiedSlotEnvironmentBinding(fallback, fallbackSlotIndex);
                if (slotEnvironments is not null &&
                    (uint)unifiedSlotIndex < (uint)slotEnvironments.Length)
                {
                    slotEnvironments[unifiedSlotIndex] = binding;
                }

                return true;
            }

            binding = default;
            return false;
        }

        if ((uint)unifiedSlotIndex < (uint)fallback.SlotCount)
        {
            binding = new UnifiedSlotEnvironmentBinding(fallback, unifiedSlotIndex);
            return true;
        }

        binding = default;
        return false;
    }

    private static void SyncSlotEnvironment(
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
        int slotIndex,
        JsValue value)
    {
        if (slotEnvironments is null ||
            (uint)slotIndex >= (uint)slotEnvironments.Length ||
            slotEnvironments[slotIndex] is not { } binding ||
            (uint)binding.SlotIndex >= (uint)binding.Environment.SlotCount)
        {
            return;
        }

        if (value.IsUninitialized)
        {
            ref var slot = ref binding.Environment.GetSlotByIndex(binding.SlotIndex);
            slot.Value = value;
            slot.Flags |= SlotFlags.Uninitialized;
            return;
        }

        binding.Environment.SetSlotDirect(binding.SlotIndex, value);
    }

    /// <summary>
    /// Inverse of <see cref="SyncSlotEnvironment"/> for the dynamic-lexical write path. A lexical binding
    /// in the materialized call environment can be shadowed by a flat VM slot that own-scope
    /// <c>LoadSlot</c> reads use as the source of truth. The <c>DeclareDynamicLexical</c> /
    /// <c>InitializeDynamicLexical</c> opcodes only touch the environment binding (by name), so the bound
    /// flat slot would otherwise keep its stale value. This writes <paramref name="value"/> into the flat
    /// slot whose <see cref="UnifiedSlotEnvironmentBinding"/> resolves to <paramref name="environment"/>
    /// and the env-slot index that <paramref name="name"/> maps to, keeping the two representations
    /// consistent. No-op when there is no materialized slot-environment mapping (pure slot path) or when
    /// the name has no flat-slot shadow.
    /// </summary>
    private static void MirrorDynamicLexicalToFlatSlot(
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
        Span<JsValue> slots,
        JsEnvironment? environment,
        string name,
        JsValue value)
    {
        if (slotEnvironments is null ||
            environment is null ||
            !environment.TryGetSlotIndex(Symbol.Intern(name), out var envSlotIndex))
        {
            return;
        }

        for (var flatSlotIndex = 0; flatSlotIndex < slotEnvironments.Length; flatSlotIndex++)
        {
            if (slotEnvironments[flatSlotIndex] is { } binding &&
                binding.SlotIndex == envSlotIndex &&
                ReferenceEquals(binding.Environment, environment) &&
                (uint)flatSlotIndex < (uint)slots.Length)
            {
                slots[flatSlotIndex] = value;
                return;
            }
        }
    }

    private static bool IsConstSlot(
        int slotIndex,
        bool[]? constSlots,
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments)
    {
        if (IsConstSlotIndex(slotIndex, constSlots))
        {
            return true;
        }

        return slotEnvironments is not null &&
               (uint)slotIndex < (uint)slotEnvironments.Length &&
               slotEnvironments[slotIndex] is { } binding &&
               (uint)binding.SlotIndex < (uint)binding.Environment.SlotCount &&
               binding.Environment.IsSlotConst(binding.SlotIndex);
    }

    private static bool IsConstSlotIndex(int slotIndex, bool[]? constSlots) =>
        constSlots is not null &&
        (uint)slotIndex < (uint)constSlots.Length &&
        constSlots[slotIndex];

    private static void MarkSlotEnvironmentLexical(
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
        int slotIndex,
        bool isConst)
    {
        if (slotEnvironments is null ||
            (uint)slotIndex >= (uint)slotEnvironments.Length ||
            slotEnvironments[slotIndex] is not { } binding ||
            (uint)binding.SlotIndex >= (uint)binding.Environment.SlotCount)
        {
            return;
        }

        ref var slot = ref binding.Environment.GetSlotByIndex(binding.SlotIndex);
        slot.Flags |= SlotFlags.Lexical | SlotFlags.Uninitialized | SlotFlags.BlocksFunctionScopeOverride;
        if (isConst)
        {
            slot.Flags |= SlotFlags.Const;
        }
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
        UnifiedSlotEnvironmentBinding?[] slotEnvironments,
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
            if (context.IsThrow)
            {
                throw new ThrowSignal(context.FlowValue);
            }

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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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

    private static bool TryMoveAsyncIteratorNext(
        UnifiedBytecodeDriverDescriptor descriptor,
        Span<JsValue> slots,
        EvaluationContext context,
        UnifiedBytecodeResumeState resumeState,
        int currentProgramCounter,
        int stackPointer,
        out int programCounter,
        out UnifiedBytecodeStepResult stepResult)
    {
        stepResult = default;
        if (!TryGetDriverState<IteratorDriverState>(slots, descriptor.StateSlot, out var state))
        {
            programCounter = descriptor.BreakTarget;
            return true;
        }

        var awaitedNextResult = JsValue.Undefined;
        var hasAwaitedNextResult = false;
        var value = JsValue.Undefined;
        var hasValue = false;

        if (state.AwaitingNextResult || state.AwaitingValue)
        {
            var wasAwaitingValue = state.AwaitingValue;
            state.AwaitingNextResult = false;
            state.AwaitingValue = false;
            if (!TryConsumePendingAwaitResume(resumeState, out var resumedValue, out var resumedThrow))
            {
                programCounter = currentProgramCounter;
                return true;
            }

            if (resumedThrow)
            {
                resumeState.IsCompleted = true;
                programCounter = descriptor.BreakTarget;
                stepResult = UnifiedBytecodeStepResult.Throw(resumedValue);
                return false;
            }

            if (wasAwaitingValue)
            {
                value = resumedValue;
                hasValue = true;
            }
            else
            {
                awaitedNextResult = resumedValue;
                hasAwaitedNextResult = true;
            }
        }

        try
        {
            if (!hasValue)
            {
                if (state.IteratorObject is { } iterator)
                {
                    if (!hasAwaitedNextResult)
                    {
                        state.NextMethod ??= iterator.GetIteratorNextCallable(context);
                        var nextResult = iterator.InvokeIteratorNext(
                            state.NextMethod,
                            JsValue.Undefined,
                            hasSendValue: false,
                            context: context,
                            callingEnvironment: resumeState.CallingEnvironment);
                        if (!TryAwaitAsyncIteratorValue(
                                nextResult,
                                context,
                                resumeState,
                                currentProgramCounter,
                                stackPointer,
                                markAwaitingNextResult: true,
                                state,
                                out awaitedNextResult,
                                out stepResult))
                        {
                            programCounter = currentProgramCounter;
                            return false;
                        }
                    }

                    if (!awaitedNextResult.TryGetObject<IJsPropertyAccessor>(out var resultObject))
                    {
                        resumeState.IsCompleted = true;
                        programCounter = descriptor.BreakTarget;
                        stepResult = UnifiedBytecodeStepResult.Throw(StandardLibrary.CreateTypeError(
                            "Iterator result is not an object",
                            context,
                            context.RealmState));
                        return false;
                    }

                    var done = resultObject.TryGetProperty("done", out var doneValue) &&
                               JsOps.ToBoolean(doneValue);
                    if (done)
                    {
                        if (resultObject is IteratorResultObject poolableResult)
                        {
                            IteratorResultObjectPool.Return(poolableResult);
                        }

                        CompleteIteratorDriverState(descriptor.StateSlot, slots, null, state);
                        programCounter = descriptor.BreakTarget;
                        return true;
                    }

                    var rawValue = resultObject.TryGetProperty("value", out var yielded)
                        ? yielded
                        : JsValue.Undefined;
                    if (resultObject is IteratorResultObject poolableResult2)
                    {
                        IteratorResultObjectPool.Return(poolableResult2);
                    }

                    if (!TryAwaitAsyncIteratorValue(
                            rawValue,
                            context,
                            resumeState,
                            currentProgramCounter,
                            stackPointer,
                            markAwaitingNextResult: false,
                            state,
                            out value,
                            out stepResult))
                    {
                        programCounter = currentProgramCounter;
                        return false;
                    }
                }
                else if (state.Enumerator is { } enumerator)
                {
                    if (!enumerator.MoveNext())
                    {
                        CompleteIteratorDriverState(descriptor.StateSlot, slots, null, state);
                        programCounter = descriptor.BreakTarget;
                        return true;
                    }

                    if (!TryAwaitAsyncIteratorValue(
                            enumerator.Current,
                            context,
                            resumeState,
                            currentProgramCounter,
                            stackPointer,
                            markAwaitingNextResult: false,
                            state,
                            out value,
                            out stepResult))
                    {
                        programCounter = currentProgramCounter;
                        return false;
                    }
                }
                else
                {
                    CompleteIteratorDriverState(descriptor.StateSlot, slots, null, state);
                    programCounter = descriptor.BreakTarget;
                    return true;
                }
            }

            if (!state.HasEnteredLoop)
            {
                state.ActiveDriverOrdinal = ++resumeState.NextActiveDriverOrdinal;
                state.HasEnteredLoop = true;
            }

            slots[descriptor.ValueSlot] = value;
            programCounter = descriptor.NextTarget;
            return true;
        }
        catch (ThrowSignal signal)
        {
            resumeState.IsCompleted = true;
            programCounter = descriptor.BreakTarget;
            stepResult = UnifiedBytecodeStepResult.Throw(signal.ThrownValue);
            return false;
        }
    }

    private static bool TryAwaitAsyncIteratorValue(
        JsValue candidate,
        EvaluationContext context,
        UnifiedBytecodeResumeState resumeState,
        int currentProgramCounter,
        int stackPointer,
        bool markAwaitingNextResult,
        IteratorDriverState iteratorState,
        out JsValue value,
        out UnifiedBytecodeStepResult stepResult)
    {
        var pendingPromise = resumeState.PendingAwaitPromise;
        if (!AwaitScheduler.TryResolvePromiseOrYield(
                candidate,
                asyncStepMode: true,
                ref pendingPromise,
                context,
                out value))
        {
            iteratorState.AwaitingNextResult = markAwaitingNextResult;
            iteratorState.AwaitingValue = !markAwaitingNextResult;
            resumeState.PendingAwaitPromise = pendingPromise;
            resumeState.ProgramCounter = currentProgramCounter;
            resumeState.StackPointer = stackPointer;
            resumeState.ResumePayloadKind = UnifiedBytecodeResumePayloadKind.None;
            resumeState.ResumePayload = JsValue.Undefined;
            stepResult = UnifiedBytecodeStepResult.PendingAwait(resumeState.PendingAwaitPromise);
            return false;
        }

        if (context.IsThrow)
        {
            resumeState.IsCompleted = true;
            stepResult = UnifiedBytecodeStepResult.Throw(context.FlowValue);
            return false;
        }

        stepResult = default;
        return true;
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
        out JsValue iteratorResult,
        out bool methodMissing,
        bool asyncStepMode,
        UnifiedBytecodeResumeState resumeState,
        int programCounter,
        int stackPointer,
        out UnifiedBytecodeStepResult? pendingStep)
    {
        value = JsValue.Undefined;
        done = true;
        iteratorResult = JsValue.Undefined;
        methodMissing = false;
        pendingStep = null;

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

        return TryReadYieldStarIteratorResult(
            result,
            context,
            readDoneValue: true,
            forceYieldWhenReturnPromiseDone: methodName == "return",
            asyncStepMode,
            resumeState,
            state,
            methodName == "return" ? YieldStarPendingAwaitKind.Return : YieldStarPendingAwaitKind.Throw,
            programCounter,
            stackPointer,
            out value,
            out done,
            out iteratorResult,
            out pendingStep);
    }

    private static bool TryReadYieldStarIteratorNextValue(
        IteratorDriverState state,
        EvaluationContext context,
        JsEnvironment? callingEnvironment,
        JsValue sendValue,
        bool hasSendValue,
        bool readDoneValue,
        out JsValue value,
        out bool done,
        out JsValue iteratorResult,
        bool asyncStepMode,
        UnifiedBytecodeResumeState resumeState,
        int programCounter,
        int stackPointer,
        out UnifiedBytecodeStepResult? pendingStep)
    {
        value = JsValue.Undefined;
        done = true;
        iteratorResult = JsValue.Undefined;
        pendingStep = null;

        if (state.IteratorObject is { } iterator)
        {
            try
            {
                state.NextMethod ??= iterator.GetIteratorNextCallable(context);
                var nextResult = iterator.InvokeIteratorNext(
                    state.NextMethod,
                    sendValue,
                    hasSendValue,
                    context: context,
                    callingEnvironment: callingEnvironment);
                return TryReadYieldStarIteratorResult(
                    nextResult,
                    context,
                    readDoneValue,
                    forceYieldWhenReturnPromiseDone: false,
                    asyncStepMode,
                    resumeState,
                    state,
                    YieldStarPendingAwaitKind.Next,
                    programCounter,
                    stackPointer,
                    out value,
                    out done,
                    out iteratorResult,
                    out pendingStep);
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
                return false;
            }
        }

        if (state.Enumerator is { } enumerator)
        {
            if (!enumerator.MoveNext())
            {
                return true;
            }

            value = enumerator.Current;
            done = false;
            return true;
        }

        return true;
    }

    private static bool TryReadYieldStarIteratorResult(
        JsValue result,
        EvaluationContext context,
        bool readDoneValue,
        bool forceYieldWhenReturnPromiseDone,
        bool asyncStepMode,
        UnifiedBytecodeResumeState resumeState,
        IteratorDriverState driverState,
        YieldStarPendingAwaitKind pendingAwaitKind,
        int programCounter,
        int stackPointer,
        out JsValue value,
        out bool done,
        out JsValue iteratorResult,
        out UnifiedBytecodeStepResult? pendingStep)
    {
        value = JsValue.Undefined;
        done = true;
        iteratorResult = JsValue.Undefined;
        pendingStep = null;
        if (asyncStepMode)
        {
            var pendingPromise = resumeState.PendingAwaitPromise;
            if (!AwaitScheduler.TryResolvePromiseOrYield(
                    result,
                    asyncStepMode: true,
                    ref pendingPromise,
                    context,
                    out result))
            {
                resumeState.PendingAwaitPromise = pendingPromise;
                resumeState.ProgramCounter = programCounter;
                resumeState.StackPointer = stackPointer;
                resumeState.ResumePayloadKind = UnifiedBytecodeResumePayloadKind.None;
                resumeState.ResumePayload = JsValue.Undefined;
                driverState.YieldStarPendingAwaitKind = pendingAwaitKind;
                pendingStep = UnifiedBytecodeStepResult.PendingAwait(resumeState.PendingAwaitPromise);
                return true;
            }
        }

        var awaitedPromise = false;
        if (result.IsObject && AwaitScheduler.IsPromiseLike(result))
        {
            awaitedPromise = true;
            if (!AwaitScheduler.TryAwaitPromiseSync(
                    result,
                    context,
                    out result,
                    context.DrainAwaitMicrotasks))
            {
                return false;
            }
        }

        return TryReadYieldStarResolvedIteratorResult(
            result,
            context,
            readDoneValue,
            forceYieldWhenReturnPromiseDone,
            awaitedPromise || asyncStepMode,
            readYieldValue: asyncStepMode,
            out value,
            out done,
            out iteratorResult);
    }

    private static bool TryReadYieldStarResolvedIteratorResult(
        JsValue result,
        EvaluationContext context,
        bool readDoneValue,
        bool forceYieldWhenReturnPromiseDone,
        bool awaitedPromise,
        bool readYieldValue,
        out JsValue value,
        out bool done,
        out JsValue iteratorResult)
    {
        value = JsValue.Undefined;
        done = true;
        iteratorResult = JsValue.Undefined;

        if (!result.TryGetObject<IJsPropertyAccessor>(out var resultObject))
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Iterator result is not an object.",
                context,
                context.RealmState));
            return false;
        }

        var resultValue = JsValue.FromObjectUnsafe(resultObject);
        var gotDone = JsOps.TryGetPropertyValue(resultValue, "done", out var doneValue, context);
        if (context.IsThrow)
        {
            return false;
        }

        done = gotDone && JsOps.ToBoolean(doneValue);
        if (done)
        {
            if (readDoneValue &&
                JsOps.TryGetPropertyValue(resultValue, "value", out var resultValueProperty, context))
            {
                value = resultValueProperty;
            }

            if (context.IsThrow)
            {
                return false;
            }

            if (forceYieldWhenReturnPromiseDone && awaitedPromise)
            {
                iteratorResult = resultValue;
                IteratorResultObject.CaptureIfSurfaced(in iteratorResult);
                done = false;
                return true;
            }

            if (resultObject is IteratorResultObject poolableResult)
            {
                IteratorResultObjectPool.Return(poolableResult);
            }

            return true;
        }

        if (readYieldValue &&
            JsOps.TryGetPropertyValue(resultValue, "value", out var yieldedValue, context))
        {
            value = yieldedValue;
        }

        if (context.IsThrow)
        {
            return false;
        }

        iteratorResult = resultValue;
        IteratorResultObject.CaptureIfSurfaced(in iteratorResult);
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments)
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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

    private static bool TryCloseIteratorDriverStateResumable(
        int slotIndex,
        Span<JsValue> slots,
        EvaluationContext context,
        UnifiedBytecodeResumeState resumeState,
        int currentProgramCounter,
        int stackPointer,
        bool preserveExistingThrow,
        out UnifiedBytecodeStepResult stepResult)
    {
        stepResult = default;
        if (!TryGetDriverState<IteratorDriverState>(slots, slotIndex, out var state))
        {
            return true;
        }

        if (!state.IteratorClosed &&
            state.IteratorObject is { } iterator &&
            state.HasEnteredLoop)
        {
            if (state.AwaitingCloseReturnResult)
            {
                state.AwaitingCloseReturnResult = false;
                if (!TryConsumePendingAwaitResume(resumeState, out var resumedCloseResult, out var resumedCloseThrow))
                {
                    return true;
                }

                if (resumedCloseThrow)
                {
                    return CompleteResumableIteratorCloseAfterThrow(
                        slotIndex,
                        slots,
                        state,
                        context,
                        resumeState,
                        resumedCloseResult,
                        out stepResult);
                }

                if (!TryValidateResumableIteratorCloseResult(
                        resumedCloseResult,
                        slotIndex,
                        slots,
                        state,
                        context,
                        resumeState,
                        out stepResult))
                {
                    return false;
                }

                return true;
            }

            if (!TryStartResumableIteratorClose(
                    iterator,
                    slotIndex,
                    slots,
                    state,
                    context,
                    resumeState,
                    currentProgramCounter,
                    stackPointer,
                    preserveExistingThrow,
                    out stepResult))
            {
                return false;
            }

            return true;
        }

        CompleteIteratorDriverState(slotIndex, slots, slotEnvironments: null, state);
        return true;
    }

    private static bool TryStartResumableIteratorClose(
        IJsObjectLike iterator,
        int slotIndex,
        Span<JsValue> slots,
        IteratorDriverState state,
        EvaluationContext context,
        UnifiedBytecodeResumeState resumeState,
        int currentProgramCounter,
        int stackPointer,
        bool preserveExistingThrow,
        out UnifiedBytecodeStepResult stepResult)
    {
        stepResult = default;
        state.PreserveCloseCompletion = preserveExistingThrow;
        state.SavedCloseCompletion = preserveExistingThrow
            ? context.SaveCompletionState()
            : default;

        if (preserveExistingThrow && context.IsThrow)
        {
            context.Clear();
        }

        if (!TryInvokeIteratorReturn(iterator, context, out var closeResult, out var methodMissing))
        {
            if (preserveExistingThrow)
            {
                context.RestoreCompletionState(state.SavedCloseCompletion);
                CompleteIteratorDriverState(slotIndex, slots, slotEnvironments: null, state);
                return true;
            }

            resumeState.IsCompleted = true;
            stepResult = UnifiedBytecodeStepResult.Throw(context.FlowValue);
            return false;
        }

        if (methodMissing)
        {
            RestorePreservedCloseCompletion(state, context);
            CompleteIteratorDriverState(slotIndex, slots, slotEnvironments: null, state);
            return true;
        }

        var pendingPromise = resumeState.PendingAwaitPromise;
        if (!AwaitScheduler.TryResolvePromiseOrYield(
                closeResult,
                asyncStepMode: resumeState.IsAsyncLike,
                ref pendingPromise,
                context,
                out var resolvedCloseResult))
        {
            state.AwaitingCloseReturnResult = true;
            resumeState.PendingAwaitPromise = pendingPromise;
            resumeState.ProgramCounter = currentProgramCounter;
            resumeState.StackPointer = stackPointer;
            resumeState.ResumePayloadKind = UnifiedBytecodeResumePayloadKind.None;
            resumeState.ResumePayload = JsValue.Undefined;
            stepResult = UnifiedBytecodeStepResult.PendingAwait(resumeState.PendingAwaitPromise);
            return false;
        }

        if (context.IsThrow)
        {
            return CompleteResumableIteratorCloseAfterThrow(
                slotIndex,
                slots,
                state,
                context,
                resumeState,
                context.FlowValue,
                out stepResult);
        }

        return TryValidateResumableIteratorCloseResult(
            resolvedCloseResult,
            slotIndex,
            slots,
            state,
            context,
            resumeState,
            out stepResult);
    }

    private static bool TryInvokeIteratorReturn(
        IJsObjectLike iterator,
        EvaluationContext context,
        out JsValue result,
        out bool methodMissing)
    {
        result = JsValue.Undefined;
        methodMissing = false;

        try
        {
            if (!iterator.TryGetProperty("return", out var methodValue) ||
                methodValue.IsNullish)
            {
                methodMissing = true;
                return true;
            }

            if (!methodValue.TryGetObject<IJsCallable>(out var callable))
            {
                context.SetThrow(StandardLibrary.CreateTypeError(
                    "Iterator.return() must be callable",
                    context,
                    context.RealmState));
                return false;
            }

            result = TypedAstEvaluator.InvokeCallableJsValue(
                callable,
                [],
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

    private static bool TryValidateResumableIteratorCloseResult(
        JsValue closeResult,
        int slotIndex,
        Span<JsValue> slots,
        IteratorDriverState state,
        EvaluationContext context,
        UnifiedBytecodeResumeState resumeState,
        out UnifiedBytecodeStepResult stepResult)
    {
        stepResult = default;
        if (!closeResult.TryGetObject<IJsObjectLike>(out _))
        {
            if (state.PreserveCloseCompletion)
            {
                context.RestoreCompletionState(state.SavedCloseCompletion);
                CompleteIteratorDriverState(slotIndex, slots, slotEnvironments: null, state);
                return true;
            }

            resumeState.IsCompleted = true;
            stepResult = UnifiedBytecodeStepResult.Throw(StandardLibrary.CreateTypeError(
                "Iterator.return() must return an object",
                context,
                context.RealmState));
            return false;
        }

        RestorePreservedCloseCompletion(state, context);
        CompleteIteratorDriverState(slotIndex, slots, slotEnvironments: null, state);
        return true;
    }

    private static bool CompleteResumableIteratorCloseAfterThrow(
        int slotIndex,
        Span<JsValue> slots,
        IteratorDriverState state,
        EvaluationContext context,
        UnifiedBytecodeResumeState resumeState,
        JsValue thrownValue,
        out UnifiedBytecodeStepResult stepResult)
    {
        if (state.PreserveCloseCompletion)
        {
            context.RestoreCompletionState(state.SavedCloseCompletion);
            CompleteIteratorDriverState(slotIndex, slots, slotEnvironments: null, state);
            stepResult = default;
            return true;
        }

        resumeState.IsCompleted = true;
        CompleteIteratorDriverState(slotIndex, slots, slotEnvironments: null, state);
        stepResult = UnifiedBytecodeStepResult.Throw(thrownValue);
        return false;
    }

    private static void RestorePreservedCloseCompletion(
        IteratorDriverState state,
        EvaluationContext context)
    {
        if (state.PreserveCloseCompletion)
        {
            context.RestoreCompletionState(state.SavedCloseCompletion);
        }
    }

    private static void CompleteForInDriverState(
        int slotIndex,
        Span<JsValue> slots,
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
        ForInDriverState state)
    {
        state.ActiveDriverOrdinal = 0;
        ForInDriverStatePool.Return(state);
        ClearDriverSlot(slotIndex, slots, slotEnvironments);
    }

    private static void ClearDriverSlot(
        int slotIndex,
        Span<JsValue> slots,
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments)
    {
        if ((uint)slotIndex >= (uint)slots.Length)
        {
            return;
        }

        slots[slotIndex] = JsValue.Undefined;
        SyncSlotEnvironment(slotEnvironments, slotIndex, JsValue.Undefined);
    }

    private static bool TryCleanupActiveDriverStatesResumable(
        Span<JsValue> slots,
        EvaluationContext context,
        UnifiedBytecodeResumeState resumeState,
        int currentProgramCounter,
        int stackPointer,
        bool preserveExistingThrow,
        out UnifiedBytecodeStepResult stepResult)
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
                if (!TryCleanupDriverStateSlotResumable(
                        activeDriverSlot.SlotIndex,
                        slots,
                        context,
                        resumeState,
                        currentProgramCounter,
                        stackPointer,
                        ref preserveCloseThrow,
                        out stepResult))
                {
                    return false;
                }
            }
        }

        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            if (!TryCleanupDriverStateSlotResumable(
                    slotIndex,
                    slots,
                    context,
                    resumeState,
                    currentProgramCounter,
                    stackPointer,
                    ref preserveCloseThrow,
                    out stepResult))
            {
                return false;
            }
        }

        stepResult = default;
        return true;
    }

    private static bool TryCleanupDriverStatesForControlTargetResumable(
        int controlTarget,
        bool isBreak,
        UnifiedBytecodeProgram program,
        Span<JsValue> slots,
        EvaluationContext context,
        UnifiedBytecodeResumeState resumeState,
        int currentProgramCounter,
        int stackPointer,
        out UnifiedBytecodeStepResult stepResult)
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
            stepResult = default;
            return true;
        }

        activeDriverSlots.Sort(static (left, right) => right.Ordinal.CompareTo(left.Ordinal));
        var preserveCloseThrow = context.IsThrow;
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

            if (!TryCleanupDriverStateSlotResumable(
                    activeDriverSlot.SlotIndex,
                    slots,
                    context,
                    resumeState,
                    currentProgramCounter,
                    stackPointer,
                    ref preserveCloseThrow,
                    out stepResult))
            {
                return false;
            }
        }

        stepResult = default;
        return true;
    }

    private static bool TryCleanupDriverStateSlotResumable(
        int slotIndex,
        Span<JsValue> slots,
        EvaluationContext context,
        UnifiedBytecodeResumeState resumeState,
        int currentProgramCounter,
        int stackPointer,
        ref bool preserveCloseThrow,
        out UnifiedBytecodeStepResult stepResult)
    {
        if (slots[slotIndex].TryGetObject<IteratorDriverState>(out _))
        {
            if (!TryCloseIteratorDriverStateResumable(
                    slotIndex,
                    slots,
                    context,
                    resumeState,
                    currentProgramCounter,
                    stackPointer,
                    preserveCloseThrow,
                    out stepResult))
            {
                return false;
            }

            preserveCloseThrow |= context.IsThrow;
            return true;
        }

        if (slots[slotIndex].TryGetObject<ForInDriverState>(out var forInState))
        {
            CompleteForInDriverState(slotIndex, slots, slotEnvironments: null, forInState);
            stepResult = default;
            return true;
        }

        if (slots[slotIndex].TryGetObject<UnifiedArrayDestructuringState>(out _))
        {
            CloseArrayDestructuringState(slotIndex, slots, slotEnvironments: null, context, preserveCloseThrow);
            preserveCloseThrow |= context.IsThrow;
            stepResult = default;
            return true;
        }

        if (slots[slotIndex].TryGetObject<UnifiedObjectDestructuringState>(out _))
        {
            CloseObjectDestructuringState(slotIndex, slots, slotEnvironments: null);
        }

        stepResult = default;
        return true;
    }

    private static void CleanupActiveDriverStates(
        Span<JsValue> slots,
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments)
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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
            SyncSlotsFromEnvironments(slots, slotEnvironments);

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
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments)
    {
        if (slotEnvironments is null)
        {
            return;
        }

        var count = Math.Min(slots.Length, slotEnvironments.Length);
        for (var i = 0; i < count; i++)
        {
            if (slotEnvironments[i] is { } binding &&
                (uint)binding.SlotIndex < (uint)binding.Environment.SlotCount)
            {
                slots[i] = binding.Environment.GetSlotByIndex(binding.SlotIndex).Value;
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
        Span<JsValue> slots,
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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
        finally
        {
            SyncSlotsFromEnvironments(slots, slotEnvironments);
        }

        return constructorIndex + 1;
    }

    private static int ExecutePreparedSuperConstruct(
        int argumentCount,
        ImmutableArray<int> spreadMask,
        Span<JsValue> stack,
        int stackPointer,
        Span<JsValue> slots,
        UnifiedSlotEnvironmentBinding?[]? slotEnvironments,
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

            var result = spreadMask.IsDefaultOrEmpty
                ? ConstructNoSpread(
                    callable,
                    newTargetCallable,
                    stack,
                    baseIndex,
                    argumentCount,
                    context.RealmState)
                : TryGetDefaultDerivedConstructorForwardedArguments(
                    environment,
                    argumentCount,
                    spreadMask,
                    stack,
                    baseIndex,
                    out var forwardedArguments)
                    ? ReflectHelper.Construct(
                        callable,
                        forwardedArguments,
                        newTargetCallable,
                        context.RealmState)
                    : ReflectHelper.Construct(
                        callable,
                        MaterializeSpreadCallArguments(
                            argumentCount,
                            spreadMask,
                            stack,
                            baseIndex,
                            context),
                        newTargetCallable,
                        context.RealmState);

            var callResultObject = result.Kind == JsValueKind.Object ? result.ObjectValue : null;
            var thisAfterSuper = callResultObject;
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

            if (thisAfterSuper is IJsObjectLike initializedObject &&
                context.TryPopClassFieldInitializer(out var pendingInitializer) &&
                pendingInitializer.Constructor is TypedAstEvaluator.SyncFunctionInvoker pendingConstructor)
            {
                pendingConstructor.InitializeInstance(
                    initializedObject,
                    pendingInitializer.Environment,
                    context);
                if (context.ShouldStopEvaluation)
                {
                    stack[baseIndex] = context.FlowValue;
                    return baseIndex + 1;
                }
            }

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
            SyncSlotsFromEnvironments(slots, slotEnvironments);
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

    private static bool TryGetDefaultDerivedConstructorForwardedArguments(
        JsEnvironment environment,
        int argumentCount,
        ImmutableArray<int> spreadMask,
        Span<JsValue> stack,
        int argumentsStartIndex,
        out IReadOnlyList<JsValue> arguments)
    {
        if (environment.IsDefaultDerivedConstructor &&
            argumentCount == 1 &&
            spreadMask.Length == 1 &&
            spreadMask[0] == 0 &&
            stack[argumentsStartIndex].TryGetObject<JsArray>(out var restArray))
        {
            arguments = restArray.Items;
            return true;
        }

        arguments = [];
        return false;
    }

    private static JsValue ResolveCurrentThisValue(
        JsEnvironment? currentCallingEnvironment,
        JsValue fallbackThisValue,
        EvaluationContext context)
    {
        if (currentCallingEnvironment is not null &&
            currentCallingEnvironment.TryFindBindingJsValue(Symbol.This, true, out _, out var environmentThis))
        {
            if (IsUninitializedThisValue(environmentThis))
            {
                var error = StandardLibrary.CreateReferenceError(
                    "ReferenceError: this is not defined - must call super() in derived class constructor",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                return error;
            }

            return environmentThis;
        }

        if (IsUninitializedThisValue(fallbackThisValue))
        {
            var error = StandardLibrary.CreateReferenceError(
                "ReferenceError: this is not defined - must call super() in derived class constructor",
                context,
                context.RealmState);
            context.SetThrow(error);
            return error;
        }

        return fallbackThisValue;
    }

    private static bool IsUninitializedThisValue(JsValue value) =>
        value.IsUninitialized ||
        value.Kind == JsValueKind.Object &&
        ReferenceEquals(value.ObjectValue, JsEnvironment.Uninitialized);

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

    private static void SetConstantSlotTypeError(
        UnifiedBytecodeProgram program,
        int slotIndex,
        EvaluationContext context)
    {
        var slotName = GetSlotName(program, slotIndex);
        var message = slotName is null
            ? "Assignment to constant variable."
            : $"Assignment to constant variable '{slotName}'.";
        context.SetThrow(StandardLibrary.CreateTypeError(message, context, context.RealmState));
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

        if (propertyName.IsPrivateName())
        {
            var handle = PropertyHandle.Resolve(
                target,
                propertyName,
                context,
                context.CurrentScope.IsStrict,
                allowPrivate: true);
            return handle.GetJsValue();
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
        bool isStrict,
        bool allowPrivate = true)
    {
        var handle = PropertyHandle.Resolve(
            target,
            propertyName,
            context,
            context.CurrentScope.IsStrict || isStrict,
            allowPrivate: allowPrivate);
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
        bool isStrict,
        bool allowPrivate = true)
    {
        var handle = PropertyHandle.Resolve(
            target,
            propertyName,
            context,
            context.CurrentScope.IsStrict || isStrict,
            allowPrivate: allowPrivate);
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

    private static bool DecodeDynamicLexicalDeclarationIsConst(int operand) =>
        (operand & 1) != 0;

    private static int DecodeFunctionDeclarationIndex(int operand) =>
        operand & FunctionDeclarationIndexMask;

    private static int DecodeFunctionDeclarationNameIndex(int operand) =>
        operand >> FunctionDeclarationNameIndexShift;

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
