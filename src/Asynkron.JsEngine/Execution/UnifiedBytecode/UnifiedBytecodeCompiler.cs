using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeCompiler
{
    private const int UpdateIncrementFlag = 1;
    private const int UpdatePrefixFlag = 2;
    private const int DefineObjectPropertyPrototypeMutationFlag = 1;
    private const int DefineObjectPropertyAllowNameInferenceFlag = 2;
    private const int DefineObjectPropertyKnownNewPropertyFlag = 4;

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
        var stringConstants = ImmutableArray.CreateBuilder<string>();
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
                stringConstants,
                ref maxStackDepth,
                out reason))
        {
            program = EmptyProgram();
            return false;
        }

        program = new UnifiedBytecodeProgram(
            unified.ToImmutable(),
            maxStackDepth,
            literalConstants.ToImmutable(),
            stringConstants.ToImmutable());
        reason = string.Empty;
        return true;
    }

    private static UnifiedBytecodeProgram EmptyProgram() =>
        new(
            ImmutableArray<UnifiedBytecodeInstruction>.Empty,
            0,
            ImmutableArray<JsValue>.Empty,
            ImmutableArray<string>.Empty);

    private static bool TryCompileBlock(
        int instructionIndex,
        ImmutableArray<ExecutionInstruction> instructions,
        ActivationSlotShape activationSlots,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
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
                                stringConstants,
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
                                stringConstants,
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
                                stringConstants,
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
                                stringConstants,
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
                        if (!IsSupportedBreakableEnter(breakableEnter, out reason))
                        {
                            return false;
                        }

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
                                stringConstants,
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
                                stringConstants,
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
                                stringConstants,
                                ref maxStackDepth,
                                out reason))
                        {
                            return false;
                        }

                        PatchOperand(unified, jumpIfFalseIndex, instructionPcMap[branch.AlternateIndex]);
                        return true;

                    case ReturnInstruction { ReturnProgram: { } returnProgram, AwaitedProgram: null }:
                        if (!TryAppendExpressionProgramOps(
                                returnProgram,
                                activationSlots,
                                unified,
                                literalConstants,
                                stringConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return));
                        maxStackDepth = Math.Max(maxStackDepth, returnProgram.MaxStackDepth);
                        reason = string.Empty;
                        return true;

                    case EvaluateAndDiscardInstruction { ExpressionProgram: { } discardedProgram } discard:
                        if (!IsDirectiveLiteralDiscard(discardedProgram))
                        {
                            reason =
                                "Unsupported discarded expression in unified bytecode plan; only directive string literal discards are allowed.";
                            return false;
                        }

                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                discard.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = discard.Next;
                        continue;

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
        ImmutableArray<string>.Builder stringConstants,
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
            stringConstants,
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

    private static bool IsSupportedBreakableEnter(BreakableEnterInstruction breakableEnter, out string reason)
    {
        if (breakableEnter.Label is not null)
        {
            reason = "Unsupported breakable construct: labels are not eligible for unified bytecode compilation.";
            return false;
        }

        if (breakableEnter.ConstructKind != BreakableKind.ResetsCompletionValue)
        {
            reason =
                "Unsupported breakable construct: only loop-style breakable wrappers are eligible for unified bytecode compilation.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsDirectiveLiteralDiscard(ExpressionProgram expressionProgram)
    {
        if (expressionProgram.OperationCount != 1)
        {
            return false;
        }

        var operation = expressionProgram.GetOperation(0);
        return operation.Kind == ExpressionOpKind.LoadLiteral &&
               operation.GetLiteral(expressionProgram.LiteralConstants.AsSpan()).Kind == JsValueKind.String;
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
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (TryAppendFirstBoundaryNamedCompoundPropertySet(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

        if (TryAppendFirstBoundaryComputedCompoundPropertySet(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

        if (TryAppendFirstBoundaryNamedPropertySet(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

        if (TryAppendFirstBoundaryComputedPropertySet(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

        if (TryAppendFirstBoundaryNamedPropertyUpdate(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

        if (TryAppendFirstBoundaryComputedPropertyUpdate(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

        if (TryAppendFirstBoundaryNamedPropertyReadChain(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

        if (TryAppendFirstBoundaryComputedPropertyRead(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

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

                case ExpressionOpKind.ResolvePropertyKey:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
                    break;

                case ExpressionOpKind.CreateArray:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.CreateArray));
                    break;

                case ExpressionOpKind.ArrayPush:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ArrayPush));
                    break;

                case ExpressionOpKind.ArrayPushHole:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ArrayPushHole));
                    break;

                case ExpressionOpKind.CreateObject:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.CreateObject));
                    break;

                case ExpressionOpKind.DefineObjectProperty:
                    if (operation.AllowNameInference)
                    {
                        reason = "Object literal name inference is not supported.";
                        return false;
                    }

                    var propertyNameIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.DefineObjectProperty,
                        EncodeDefineObjectPropertyOperand(propertyNameIndex, operation)));
                    break;

                case ExpressionOpKind.DefineComputedObjectProperty:
                    if (operation.AllowNameInference)
                    {
                        reason = "Computed object literal name inference is not supported.";
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.DefineComputedObjectProperty));
                    break;

                default:
                    reason = $"Unsupported expression op '{operation.Kind}'.";
                    return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryNamedCompoundPropertySet(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount != 6)
        {
            reason = string.Empty;
            return false;
        }

        var duplicateTarget = expressionProgram.GetOperation(1);
        var propertyRead = expressionProgram.GetOperation(2);
        var rhs = expressionProgram.GetOperation(3);
        var binary = expressionProgram.GetOperation(4);
        var propertySet = expressionProgram.GetOperation(5);
        if (duplicateTarget.Kind != ExpressionOpKind.DuplicateTop ||
            propertyRead.Kind != ExpressionOpKind.GetNamedProperty ||
            binary.Kind != ExpressionOpKind.Binary ||
            propertySet.Kind != ExpressionOpKind.SetNamedProperty)
        {
            reason = string.Empty;
            return false;
        }

        if (propertyRead.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
        {
            reason = "Private named compound property writes are not supported.";
            return false;
        }

        if (propertyRead.IsOptional || propertyRead.ShortCircuitOnNullishTarget)
        {
            reason = "Optional named compound property writes are not supported.";
            return false;
        }

        if (propertySet.AllowNameInference)
        {
            reason = "Named compound property writes with name inference are not supported.";
            return false;
        }

        if (propertyRead.GetString(expressionProgram.StringConstants.AsSpan()) !=
            propertySet.GetString(expressionProgram.StringConstants.AsSpan()))
        {
            reason = "Mismatched named compound property read/write operands are not supported.";
            return false;
        }

        if (!IsSupportedBinaryOperator(binary.Operator))
        {
            reason = $"Unsupported compound property binary operator '{binary.Operator}'.";
            return false;
        }

        if (!TryAppendActivationIdentifierLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(propertyRead.GetString(expressionProgram.StringConstants.AsSpan()));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet,
            propertyNameIndex));

        if (!TryAppendSimpleOperandLoad(rhs, expressionProgram, activationSlots, unified, literalConstants, out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)binary.Operator));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetNamedProperty, propertyNameIndex));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryComputedCompoundPropertySet(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount != 9)
        {
            reason = string.Empty;
            return false;
        }

        var requireObjectCoercible = expressionProgram.GetOperation(2);
        var resolvePropertyKey = expressionProgram.GetOperation(3);
        var duplicateTargetAndKey = expressionProgram.GetOperation(4);
        var propertyRead = expressionProgram.GetOperation(5);
        var rhs = expressionProgram.GetOperation(6);
        var binary = expressionProgram.GetOperation(7);
        var propertySet = expressionProgram.GetOperation(8);
        if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible ||
            requireObjectCoercible.Depth != 1 ||
            resolvePropertyKey.Kind != ExpressionOpKind.ResolvePropertyKey ||
            duplicateTargetAndKey.Kind != ExpressionOpKind.DuplicateTopTwo ||
            propertyRead.Kind != ExpressionOpKind.GetComputedProperty ||
            binary.Kind != ExpressionOpKind.Binary ||
            propertySet.Kind != ExpressionOpKind.SetComputedProperty)
        {
            reason = string.Empty;
            return false;
        }

        if (propertyRead.ShortCircuitOnNullishTarget)
        {
            reason = "Optional computed compound property writes are not supported.";
            return false;
        }

        if (propertySet.AllowNameInference)
        {
            reason = "Computed compound property writes with name inference are not supported.";
            return false;
        }

        if (!IsSupportedBinaryOperator(binary.Operator))
        {
            reason = $"Unsupported compound computed property binary operator '{binary.Operator}'.";
            return false;
        }

        if (!TryAppendActivationIdentifierLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        if (!TryAppendComputedPropertyKeyLoad(
                expressionProgram.GetOperation(1),
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.RequireObjectCoercible, 1));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet));

        if (!TryAppendSimpleOperandLoad(rhs, expressionProgram, activationSlots, unified, literalConstants, out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)binary.Operator));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetComputedProperty));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryNamedPropertySet(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount != 3)
        {
            reason = string.Empty;
            return false;
        }

        var propertySet = expressionProgram.GetOperation(2);
        if (propertySet.Kind != ExpressionOpKind.SetNamedProperty)
        {
            reason = string.Empty;
            return false;
        }

        if (propertySet.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
        {
            reason = "Private named property writes are not supported.";
            return false;
        }

        if (propertySet.AllowNameInference)
        {
            reason = "Property writes with name inference are not supported.";
            return false;
        }

        if (!TryAppendActivationIdentifierLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        if (!TryAppendSimpleOperandLoad(
                expressionProgram.GetOperation(1),
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return false;
        }

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(propertySet.GetString(expressionProgram.StringConstants.AsSpan()));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetNamedProperty, propertyNameIndex));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryComputedPropertySet(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount != 4)
        {
            reason = string.Empty;
            return false;
        }

        var propertySet = expressionProgram.GetOperation(3);
        if (propertySet.Kind != ExpressionOpKind.SetComputedProperty)
        {
            reason = string.Empty;
            return false;
        }

        if (propertySet.AllowNameInference)
        {
            reason = "Computed property writes with name inference are not supported.";
            return false;
        }

        if (!TryAppendActivationIdentifierLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        if (!TryAppendSimpleOperandLoad(
                expressionProgram.GetOperation(1),
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return false;
        }

        if (!TryAppendSimpleOperandLoad(
                expressionProgram.GetOperation(2),
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetComputedProperty));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryNamedPropertyUpdate(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount != 2)
        {
            reason = string.Empty;
            return false;
        }

        var propertyUpdate = expressionProgram.GetOperation(1);
        if (propertyUpdate.Kind != ExpressionOpKind.UpdateNamedProperty)
        {
            reason = string.Empty;
            return false;
        }

        if (propertyUpdate.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
        {
            reason = "Private named property updates are not supported.";
            return false;
        }

        if (!TryAppendActivationIdentifierLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(propertyUpdate.GetString(expressionProgram.StringConstants.AsSpan()));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.UpdateNamedProperty,
            EncodeUpdateOperand(propertyNameIndex, propertyUpdate)));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryComputedPropertyUpdate(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount != 3)
        {
            reason = string.Empty;
            return false;
        }

        var propertyUpdate = expressionProgram.GetOperation(2);
        if (propertyUpdate.Kind != ExpressionOpKind.UpdateComputedProperty)
        {
            reason = string.Empty;
            return false;
        }

        if (!TryAppendActivationIdentifierLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        if (!TryAppendSimpleOperandLoad(
                expressionProgram.GetOperation(1),
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.UpdateComputedProperty,
            EncodeUpdateFlags(propertyUpdate)));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryNamedPropertyReadChain(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount is not (2 or 3))
        {
            reason = string.Empty;
            return false;
        }

        var baseLoad = expressionProgram.GetOperation(0);
        for (var operationIndex = 1; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            var propertyRead = expressionProgram.GetOperation(operationIndex);
            if (propertyRead.Kind != ExpressionOpKind.GetNamedProperty)
            {
                reason = string.Empty;
                return false;
            }

            if (propertyRead.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
            {
                reason = "Private named property reads are not supported.";
                return false;
            }

            if (propertyRead.IsOptional || propertyRead.ShortCircuitOnNullishTarget)
            {
                reason = "Optional named property reads are not supported.";
                return false;
            }
        }

        if (!TryAppendActivationIdentifierLoad(baseLoad, expressionProgram, activationSlots, unified, out reason))
        {
            return false;
        }

        for (var operationIndex = 1; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            var propertyRead = expressionProgram.GetOperation(operationIndex);
            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(propertyRead.GetString(expressionProgram.StringConstants.AsSpan()));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryComputedPropertyRead(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount != 5)
        {
            reason = string.Empty;
            return false;
        }

        var getComputedProperty = expressionProgram.GetOperation(4);
        if (getComputedProperty.Kind != ExpressionOpKind.GetComputedProperty)
        {
            reason = string.Empty;
            return false;
        }

        if (getComputedProperty.ShortCircuitOnNullishTarget)
        {
            reason = "Optional computed property reads are not supported.";
            return false;
        }

        var requireObjectCoercible = expressionProgram.GetOperation(2);
        if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible ||
            requireObjectCoercible.Depth != 1)
        {
            reason =
                "Computed property reads require RequireObjectCoercible(Depth: 1) in the first production boundary.";
            return false;
        }

        var resolvePropertyKey = expressionProgram.GetOperation(3);
        if (resolvePropertyKey.Kind != ExpressionOpKind.ResolvePropertyKey)
        {
            reason = "Computed property reads require ResolvePropertyKey in the first production boundary.";
            return false;
        }

        if (!TryAppendActivationIdentifierLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        if (!TryAppendComputedPropertyKeyLoad(
                expressionProgram.GetOperation(1),
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.RequireObjectCoercible, 1));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedProperty));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendComputedPropertyKeyLoad(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        out string reason)
    {
        switch (operation.Kind)
        {
            case ExpressionOpKind.LoadIdentifier:
                return TryAppendActivationIdentifierLoad(operation, expressionProgram, activationSlots, unified, out reason);

            case ExpressionOpKind.LoadLiteral:
                var literal = operation.GetLiteral(expressionProgram.LiteralConstants.AsSpan());
                var literalIndex = literalConstants.Count;
                literalConstants.Add(literal);
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, literalIndex));
                reason = string.Empty;
                return true;

            default:
                reason = $"Unsupported computed property key op '{operation.Kind}'.";
                return false;
        }
    }

    private static bool TryAppendSimpleOperandLoad(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        out string reason)
    {
        switch (operation.Kind)
        {
            case ExpressionOpKind.LoadIdentifier:
                return TryAppendActivationIdentifierLoad(operation, expressionProgram, activationSlots, unified, out reason);

            case ExpressionOpKind.LoadLiteral:
                var literal = operation.GetLiteral(expressionProgram.LiteralConstants.AsSpan());
                var literalIndex = literalConstants.Count;
                literalConstants.Add(literal);
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, literalIndex));
                reason = string.Empty;
                return true;

            default:
                reason = $"Unsupported simple operand op '{operation.Kind}'.";
                return false;
        }
    }

    private static int EncodeUpdateOperand(int stringConstantIndex, PackedExpressionOp update) =>
        (stringConstantIndex << 2) | EncodeUpdateFlags(update);

    private static int EncodeDefineObjectPropertyOperand(int stringConstantIndex, PackedExpressionOp defineProperty)
    {
        var flags = defineProperty.IsPrototypeMutation ? DefineObjectPropertyPrototypeMutationFlag : 0;
        if (defineProperty.AllowNameInference)
        {
            flags |= DefineObjectPropertyAllowNameInferenceFlag;
        }

        if (defineProperty.IsKnownNewObjectProperty)
        {
            flags |= DefineObjectPropertyKnownNewPropertyFlag;
        }

        return (stringConstantIndex << 3) | flags;
    }

    private static int EncodeUpdateFlags(PackedExpressionOp update)
    {
        var flags = update.IsIncrement ? UpdateIncrementFlag : 0;
        return update.IsPrefix ? flags | UpdatePrefixFlag : flags;
    }

    private static bool TryAppendActivationIdentifierLoad(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        out string reason)
    {
        if (operation.Kind != ExpressionOpKind.LoadIdentifier || operation.IsArguments)
        {
            reason = $"Unsupported property-read base op '{operation.Kind}'.";
            return false;
        }

        var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        if (!TryResolveActivationSlot(identifier, activationSlots, out var slotIndex))
        {
            reason = $"Unsupported identifier '{identifier.Name.Name}'.";
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, slotIndex));
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
            BinaryOperator.Equal or
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
