using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeCompiler
{
    public static bool TryCompile(
        ExecutionPlan plan,
        bool isAsync,
        bool isGenerator,
        out UnifiedBytecodeProgram program,
        out string reason)
    {
        if (isAsync || isGenerator)
        {
            program = EmptyProgram();
            reason = "Async and generator functions are not eligible for unified bytecode compilation.";
            return false;
        }

        if ((uint)plan.EntryPoint >= (uint)plan.Instructions.Length)
        {
            program = EmptyProgram();
            reason = "Unsupported entrypoint.";
            return false;
        }

        if (plan.ActivationSlots is null)
        {
            program = EmptyProgram();
            reason = "Activation slot metadata is required.";
            return false;
        }

        var instructions = plan.Instructions;

        var unified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        var literalConstants = ImmutableArray.CreateBuilder<JsValue>();
        var instructionPcMap = new Dictionary<int, int>();
        var activeInstructions = new HashSet<int>();
        var maxStackDepth = 0;

        if (!TryCompileBlock(
                plan.EntryPoint,
                instructions,
                plan.ActivationSlots,
                instructionPcMap,
                activeInstructions,
                unified,
                literalConstants,
                ref maxStackDepth,
                out reason))
        {
            program = EmptyProgram();
            return false;
        }

        program = new UnifiedBytecodeProgram(unified.ToImmutable(), maxStackDepth, literalConstants.ToImmutable());
        reason = string.Empty;
        return true;
    }

    private static UnifiedBytecodeProgram EmptyProgram() =>
        new(ImmutableArray<UnifiedBytecodeInstruction>.Empty, 0, ImmutableArray<JsValue>.Empty);

    private static bool TryCompileBlock(
        int instructionIndex,
        ImmutableArray<ExecutionInstruction> instructions,
        ActivationSlotShape activationSlots,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ref int maxStackDepth,
        out string reason)
    {
        var activated = new List<int>();
        try
        {
            while (true)
            {
                if ((uint)instructionIndex >= (uint)instructions.Length)
                {
                    reason = "Instruction flow reached an invalid target index.";
                    return false;
                }

                if (activeInstructions.Contains(instructionIndex))
                {
                    reason = $"Loop-shaped unified bytecode plan detected at instruction {instructionIndex}.";
                    return false;
                }

                if (instructionPcMap.TryGetValue(instructionIndex, out var existingProgramCounter))
                {
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, existingProgramCounter));
                    reason = string.Empty;
                    return true;
                }

                activeInstructions.Add(instructionIndex);
                activated.Add(instructionIndex);
                instructionPcMap[instructionIndex] = unified.Count;

                switch (instructions[instructionIndex])
                {
                    case SimpleVariableDeclarationInstruction
                        {
                            InitializerProgram: { } initializerProgram,
                            AwaitedProgram: null,
                            TargetSymbol: { } targetSymbol
                        } declaration:
                        if (!TryResolveActivationSlot(targetSymbol, activationSlots, out var storeSlot))
                        {
                            reason = $"Unsupported declaration target '{targetSymbol.Name}'.";
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                initializerProgram,
                                activationSlots,
                                unified,
                                literalConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.StoreSlot, storeSlot));
                        maxStackDepth = Math.Max(maxStackDepth, initializerProgram.MaxStackDepth);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                declaration.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = declaration.Next;
                        continue;

                    case AssignmentSlotInstruction
                        {
                            ValueProgram: { } valueProgram,
                            AwaitedProgram: null,
                            TargetSymbol: { } targetSymbol
                        } assignment:
                        if (!TryResolveActivationSlot(targetSymbol, activationSlots, out var assignmentSlot))
                        {
                            reason = $"Unsupported assignment target '{targetSymbol.Name}'.";
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                valueProgram,
                                activationSlots,
                                unified,
                                literalConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.StoreSlot, assignmentSlot));
                        maxStackDepth = Math.Max(maxStackDepth, valueProgram.MaxStackDepth);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                assignment.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = assignment.Next;
                        continue;

                    case CompoundAssignmentSlotInstruction
                        {
                            RhsProgram: { } rhsProgram,
                            AwaitedProgram: null,
                            TargetSymbol: { } targetSymbol
                        } compoundAssignment
                        when IsSupportedBinaryOperator(compoundAssignment.Operator):
                        if (!TryResolveActivationSlot(targetSymbol, activationSlots, out var compoundSlot))
                        {
                            reason = $"Unsupported compound assignment target '{targetSymbol.Name}'.";
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, compoundSlot));
                        if (!TryAppendExpressionProgramOps(
                                rhsProgram,
                                activationSlots,
                                unified,
                                literalConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.Binary,
                            (int)compoundAssignment.Operator));
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.StoreSlot, compoundSlot));
                        maxStackDepth = Math.Max(maxStackDepth, rhsProgram.MaxStackDepth + 1);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                compoundAssignment.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = compoundAssignment.Next;
                        continue;

                    case JumpInstruction jump:
                        var jumpIndex = unified.Count;
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump));
                        if (!TryCompileTarget(
                                jump.TargetIndex,
                                instructions,
                                activationSlots,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                literalConstants,
                                ref maxStackDepth,
                                out reason))
                        {
                            return false;
                        }

                        PatchOperand(unified, jumpIndex, instructionPcMap[jump.TargetIndex]);
                        return true;

                    case SetCompletionValueInstruction setCompletionValue:
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                setCompletionValue.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = setCompletionValue.Next;
                        continue;

                    case BreakableEnterInstruction breakableEnter:
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                breakableEnter.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = breakableEnter.Next;
                        continue;

                    case BreakableExitInstruction breakableExit:
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                breakableExit.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = breakableExit.Next;
                        continue;

                    case BranchInstruction branch:
                        if (!TryAppendExpressionProgramOps(
                                branch.ConditionProgram,
                                activationSlots,
                                unified,
                                literalConstants,
                                out reason))
                        {
                            return false;
                        }

                        maxStackDepth = Math.Max(maxStackDepth, branch.ConditionProgram.MaxStackDepth);
                        var jumpIfFalseIndex = unified.Count;
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfFalse));

                        if (!TryCompileTarget(
                                branch.ConsequentIndex,
                                instructions,
                                activationSlots,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                literalConstants,
                                ref maxStackDepth,
                                out reason))
                        {
                            return false;
                        }

                        if (!TryCompileTarget(
                                branch.AlternateIndex,
                                instructions,
                                activationSlots,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                literalConstants,
                                ref maxStackDepth,
                                out reason))
                        {
                            return false;
                        }

                        PatchOperand(unified, jumpIfFalseIndex, instructionPcMap[branch.AlternateIndex]);
                        return true;

                    case ReturnInstruction { ReturnProgram: { } returnProgram, AwaitedProgram: null }:
                        if (!TryAppendExpressionProgramOps(returnProgram, activationSlots, unified, literalConstants, out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return));
                        maxStackDepth = Math.Max(maxStackDepth, returnProgram.MaxStackDepth);
                        reason = string.Empty;
                        return true;

                    default:
                        reason = $"Unsupported instruction in unified bytecode plan: {instructions[instructionIndex].GetType().Name}.";
                        return false;
                }
            }
        }
        finally
        {
            foreach (var activatedInstruction in activated)
            {
                activeInstructions.Remove(activatedInstruction);
            }
        }
    }

    private static bool TryCompileTarget(
        int targetIndex,
        ImmutableArray<ExecutionInstruction> instructions,
        ActivationSlotShape activationSlots,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ref int maxStackDepth,
        out string reason)
    {
        if ((uint)targetIndex >= (uint)instructions.Length)
        {
            reason = "Instruction flow reached an invalid target index.";
            return false;
        }

        if (activeInstructions.Contains(targetIndex))
        {
            reason = $"Loop-shaped unified bytecode plan detected at instruction {targetIndex}.";
            return false;
        }

        if (instructionPcMap.ContainsKey(targetIndex))
        {
            reason = string.Empty;
            return true;
        }

        return TryCompileBlock(
            targetIndex,
            instructions,
            activationSlots,
            instructionPcMap,
            activeInstructions,
            unified,
            literalConstants,
            ref maxStackDepth,
            out reason);
    }

    private static bool TryAppendJumpToCompiledTarget(
        int sourceInstructionIndex,
        int targetIndex,
        ImmutableArray<ExecutionInstruction> instructions,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        out string reason)
    {
        if (activeInstructions.Contains(targetIndex))
        {
            if (!IsCanonicalLoopBackEdgeTarget(sourceInstructionIndex, targetIndex, instructions) ||
                !instructionPcMap.TryGetValue(targetIndex, out var loopHeadProgramCounter))
            {
                reason = $"Unsupported loop control flow at instruction {sourceInstructionIndex}.";
                return false;
            }

            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, loopHeadProgramCounter));
            reason = string.Empty;
            return true;
        }

        if (!instructionPcMap.TryGetValue(targetIndex, out var targetProgramCounter))
        {
            reason = string.Empty;
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, targetProgramCounter));
        reason = string.Empty;
        return true;
    }

    private static bool IsCanonicalLoopBackEdgeTarget(
        int sourceInstructionIndex,
        int targetIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        if ((uint)sourceInstructionIndex >= (uint)instructions.Length ||
            (uint)targetIndex >= (uint)instructions.Length)
        {
            return false;
        }

        if (instructions[targetIndex] is not BranchInstruction branch ||
            sourceInstructionIndex == targetIndex ||
            sourceInstructionIndex == branch.AlternateIndex)
        {
            return false;
        }

        if (instructions[sourceInstructionIndex] is not AssignmentSlotInstruction and not CompoundAssignmentSlotInstruction)
        {
            return false;
        }

        return TryIsLinearCanonicalWhileBody(branch.ConsequentIndex, sourceInstructionIndex, instructions) &&
               !HasForStyleContinueTarget(sourceInstructionIndex, targetIndex, branch.AlternateIndex, instructions) &&
               !HasExplicitJumpIntoLoopBackEdgeSource(sourceInstructionIndex, instructions);
    }

    private static bool TryIsLinearCanonicalWhileBody(
        int startInstructionIndex,
        int endInstructionIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        if ((uint)startInstructionIndex >= (uint)instructions.Length ||
            (uint)endInstructionIndex >= (uint)instructions.Length)
        {
            return false;
        }

        var visited = new HashSet<int>();
        var current = startInstructionIndex;
        while (current != endInstructionIndex)
        {
            if (!visited.Add(current))
            {
                return false;
            }

            if ((uint)current >= (uint)instructions.Length)
            {
                return false;
            }

            switch (instructions[current])
            {
                case SimpleVariableDeclarationInstruction declaration:
                    current = declaration.Next;
                    break;
                case AssignmentSlotInstruction assignment:
                    current = assignment.Next;
                    break;
                case CompoundAssignmentSlotInstruction compound:
                    current = compound.Next;
                    break;
                case SetCompletionValueInstruction setCompletion:
                    current = setCompletion.Next;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool HasExplicitJumpIntoLoopBackEdgeSource(
        int loopBackEdgeSourceInstructionIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        for (var index = 0; index < instructions.Length; index++)
        {
            if (index == loopBackEdgeSourceInstructionIndex)
            {
                continue;
            }

            if (instructions[index] is JumpInstruction jump &&
                jump.TargetIndex == loopBackEdgeSourceInstructionIndex)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasForStyleContinueTarget(
        int loopBackEdgeSourceInstructionIndex,
        int loopConditionInstructionIndex,
        int loopBreakInstructionIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction is not BreakableEnterInstruction breakableEnter)
            {
                continue;
            }

            if (breakableEnter.Next == loopConditionInstructionIndex &&
                breakableEnter.BreakTarget == loopBreakInstructionIndex &&
                breakableEnter.ContinueTarget == loopBackEdgeSourceInstructionIndex)
            {
                return true;
            }

            if (breakableEnter.BreakTarget == loopBreakInstructionIndex &&
                breakableEnter.ContinueTarget == loopBackEdgeSourceInstructionIndex &&
                ReachesInstructionLinearly(breakableEnter.Next, loopConditionInstructionIndex, instructions))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReachesInstructionLinearly(
        int startInstructionIndex,
        int targetInstructionIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        if ((uint)startInstructionIndex >= (uint)instructions.Length ||
            (uint)targetInstructionIndex >= (uint)instructions.Length)
        {
            return false;
        }

        var visited = new HashSet<int>();
        var current = startInstructionIndex;
        while (current != targetInstructionIndex)
        {
            if (!visited.Add(current) || (uint)current >= (uint)instructions.Length)
            {
                return false;
            }

            current = instructions[current] switch
            {
                SimpleVariableDeclarationInstruction declaration => declaration.Next,
                AssignmentSlotInstruction assignment => assignment.Next,
                CompoundAssignmentSlotInstruction compound => compound.Next,
                SetCompletionValueInstruction setCompletion => setCompletion.Next,
                _ => -1
            };

            if (current < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void PatchOperand(
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        int instructionIndex,
        int operand)
    {
        var instruction = unified[instructionIndex];
        unified[instructionIndex] = instruction with { Operand = operand };
    }

    private static bool TryAppendExpressionProgramOps(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        out string reason)
    {
        foreach (var operation in expressionProgram.EnumerateOperations())
        {
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadIdentifier:
                    if (operation.IsArguments)
                    {
                        reason = "arguments is not supported.";
                        return false;
                    }

                    var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (!TryResolveActivationSlot(identifier, activationSlots, out var slotIndex))
                    {
                        reason = $"Unsupported identifier '{identifier.Name.Name}'.";
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, slotIndex));
                    break;

                case ExpressionOpKind.LoadLiteral:
                    var literal = operation.GetLiteral(expressionProgram.LiteralConstants.AsSpan());
                    var literalIndex = literalConstants.Count;
                    literalConstants.Add(literal);
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, literalIndex));
                    break;

                case ExpressionOpKind.Binary when IsSupportedBinaryOperator(operation.Operator):
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)operation.Operator));
                    break;

                default:
                    reason = $"Unsupported expression op '{operation.Kind}'.";
                    return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsSupportedBinaryOperator(BinaryOperator binaryOperator) =>
        binaryOperator is
            BinaryOperator.Add or
            BinaryOperator.Subtract or
            BinaryOperator.Multiply or
            BinaryOperator.Divide or
            BinaryOperator.Modulo or
            BinaryOperator.LessThan or
            BinaryOperator.LessThanOrEqual or
            BinaryOperator.GreaterThan or
            BinaryOperator.GreaterThanOrEqual;

    private static bool TryResolveActivationSlot(IdentifierOperand identifier, ActivationSlotShape activationSlots, out int slotIndex)
    {
        if (identifier.ScopeId == activationSlots.ScopeId && identifier.SlotIndex >= 0)
        {
            slotIndex = identifier.SlotIndex;
            return true;
        }

        if (activationSlots.SlotMap.TryGetValue(identifier.Name, out var mappedSlot))
        {
            slotIndex = mappedSlot;
            return true;
        }

        slotIndex = -1;
        return false;
    }

    private static bool TryResolveActivationSlot(Symbol symbol, ActivationSlotShape activationSlots, out int slotIndex) =>
        activationSlots.SlotMap.TryGetValue(symbol, out slotIndex);
}
