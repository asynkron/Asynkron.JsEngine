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

    private readonly record struct UnifiedBytecodeScopeFrame(
        int ScopeId,
        ImmutableDictionary<Symbol, int> SlotMap,
        ImmutableArray<(int SlotIndex, int FlatSlotId)> FlatSlotMappings);

    private sealed record UnifiedBytecodeSlotLayout(
        int SlotCount,
        ActivationSlotShape ActivationSlots,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> FlatSlotMappings,
        ImmutableArray<int> ParameterSlotIndices,
        ImmutableArray<int> LexicalSlotIndices,
        ImmutableArray<string?> SlotNames);

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
        var slotLayout = BuildSlotLayout(plan);

        var unified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        var literalConstants = ImmutableArray.CreateBuilder<JsValue>();
        var stringConstants = ImmutableArray.CreateBuilder<string>();
        var callTargetConstants = ImmutableArray.CreateBuilder<UnifiedBytecodeCallTarget>();
        var scopeDescriptors = ImmutableArray.CreateBuilder<UnifiedBytecodeScopeDescriptor>();
        var instructionPcMap = new Dictionary<int, int>();
        var activeInstructions = new HashSet<int>();
        var activeScopes = new Stack<UnifiedBytecodeScopeFrame>();
        activeScopes.Push(new UnifiedBytecodeScopeFrame(
            slotLayout.ActivationSlots.ScopeId,
            slotLayout.ActivationSlots.SlotMap,
            GetFlatSlotMappings(slotLayout, slotLayout.ActivationSlots.ScopeId)));
        var maxStackDepth = 0;

        if (!TryCompileBlock(
                plan.EntryPoint,
                instructions,
                slotLayout,
                activeScopes,
                instructionPcMap,
                activeInstructions,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                scopeDescriptors,
                ref maxStackDepth,
                out reason))
        {
            program = EmptyProgram();
            return false;
        }

        program = new UnifiedBytecodeProgram(
            unified.ToImmutable(),
            maxStackDepth,
            slotLayout.SlotCount,
            literalConstants.ToImmutable(),
            stringConstants.ToImmutable(),
            slotLayout.SlotNames,
            slotLayout.ParameterSlotIndices,
            slotLayout.LexicalSlotIndices,
            callTargetConstants.ToImmutable(),
            scopeDescriptors.ToImmutable());
        reason = string.Empty;
        return true;
    }

    private static UnifiedBytecodeProgram EmptyProgram() =>
        new(
            ImmutableArray<UnifiedBytecodeInstruction>.Empty,
            0,
            0,
            ImmutableArray<JsValue>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string?>.Empty,
            ImmutableArray<int>.Empty,
            ImmutableArray<int>.Empty,
            ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty);

    private static UnifiedBytecodeSlotLayout BuildSlotLayout(ExecutionPlan plan)
    {
        var activationSlots = plan.ActivationSlots!;
        var flatSlotMappings = plan.FlatSlotMappings ??
                               ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>.Empty;
        flatSlotMappings = EnsureActivationSlotMappings(activationSlots, flatSlotMappings);
        var slotCount = GetSlotCount(plan.FlatSlotCount, flatSlotMappings);
        var names = BuildSlotNames(plan.Instructions, activationSlots, flatSlotMappings, slotCount);
        var parameterSlotIndices = RemapParameterSlotIndices(
            activationSlots.ScopeId,
            activationSlots.ParameterSlotIndices,
            flatSlotMappings);
        var lexicalSlotIndices = RemapSlotIndices(
            activationSlots.ScopeId,
            activationSlots.LexicalSlotIndices,
            flatSlotMappings);

        return new UnifiedBytecodeSlotLayout(
            slotCount,
            activationSlots,
            flatSlotMappings,
            parameterSlotIndices,
            lexicalSlotIndices,
            names);
    }

    private static ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> EnsureActivationSlotMappings(
        ActivationSlotShape activationSlots,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> flatSlotMappings)
    {
        if (!flatSlotMappings.TryGetValue(activationSlots.ScopeId, out var mappings))
        {
            var rootMappings = ImmutableArray.CreateBuilder<(int SlotIndex, int FlatSlotId)>(activationSlots.SlotCount);
            for (var slotIndex = 0; slotIndex < activationSlots.SlotCount; slotIndex++)
            {
                rootMappings.Add((slotIndex, slotIndex));
            }

            return flatSlotMappings.Add(activationSlots.ScopeId, rootMappings.ToImmutable());
        }

        var mappedSlots = new HashSet<int>();
        var usedFlatSlots = new HashSet<int>();
        var nextFlatSlotId = 0;
        foreach (var scopeMappings in flatSlotMappings.Values)
        {
            foreach (var mapping in scopeMappings)
            {
                usedFlatSlots.Add(mapping.FlatSlotId);
                nextFlatSlotId = Math.Max(nextFlatSlotId, mapping.FlatSlotId + 1);
            }
        }

        foreach (var mapping in mappings)
        {
            mappedSlots.Add(mapping.SlotIndex);
        }

        var hasAllActivationSlots = true;
        for (var slotIndex = 0; slotIndex < activationSlots.SlotCount; slotIndex++)
        {
            if (!mappedSlots.Contains(slotIndex))
            {
                hasAllActivationSlots = false;
                break;
            }
        }

        if (hasAllActivationSlots)
        {
            return flatSlotMappings;
        }

        var builder = mappings.ToBuilder();
        for (var slotIndex = 0; slotIndex < activationSlots.SlotCount; slotIndex++)
        {
            if (mappedSlots.Contains(slotIndex))
            {
                continue;
            }

            var flatSlotId = slotIndex;
            if (!usedFlatSlots.Add(flatSlotId))
            {
                while (usedFlatSlots.Contains(nextFlatSlotId))
                {
                    nextFlatSlotId++;
                }

                flatSlotId = nextFlatSlotId;
                usedFlatSlots.Add(flatSlotId);
            }

            builder.Add((slotIndex, flatSlotId));
        }

        return flatSlotMappings.SetItem(activationSlots.ScopeId, builder.ToImmutable());
    }

    private static int GetSlotCount(
        int flatSlotCount,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> flatSlotMappings)
    {
        var slotCount = Math.Max(flatSlotCount, 0);
        foreach (var mappings in flatSlotMappings.Values)
        {
            foreach (var mapping in mappings)
            {
                slotCount = Math.Max(slotCount, mapping.FlatSlotId + 1);
            }
        }

        return slotCount;
    }

    private static ImmutableArray<string?> BuildSlotNames(
        ImmutableArray<ExecutionInstruction> instructions,
        ActivationSlotShape activationSlots,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> flatSlotMappings,
        int slotCount)
    {
        if (slotCount == 0)
        {
            return ImmutableArray<string?>.Empty;
        }

        var names = new string?[slotCount];
        foreach (var (name, slotIndex) in activationSlots.SlotNames)
        {
            if (TryMapSlot(activationSlots.ScopeId, slotIndex, flatSlotMappings, out var flatSlotId) &&
                (uint)flatSlotId < (uint)names.Length)
            {
                names[flatSlotId] = name.Name;
            }
        }

        foreach (var instruction in instructions)
        {
            if (instruction is not PushEnvironmentInstruction push)
            {
                continue;
            }

            if (!push.SlotNames.IsDefaultOrEmpty)
            {
                foreach (var (name, slotIndex) in push.SlotNames)
                {
                    if (TryMapSlot(push.ScopeId, slotIndex, flatSlotMappings, out var flatSlotId) &&
                        (uint)flatSlotId < (uint)names.Length)
                    {
                        names[flatSlotId] = name.Name;
                    }
                }

                continue;
            }

            foreach (var (name, slotIndex) in push.SlotMap)
            {
                if (TryMapSlot(push.ScopeId, slotIndex, flatSlotMappings, out var flatSlotId) &&
                    (uint)flatSlotId < (uint)names.Length)
                {
                    names[flatSlotId] = name.Name;
                }
            }
        }

        return names.ToImmutableArray();
    }

    private static ImmutableArray<int> RemapSlotIndices(
        int scopeId,
        ImmutableArray<int> slotIndices,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> flatSlotMappings)
    {
        if (slotIndices.IsDefaultOrEmpty)
        {
            return ImmutableArray<int>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<int>(slotIndices.Length);
        foreach (var slotIndex in slotIndices)
        {
            if (TryMapSlot(scopeId, slotIndex, flatSlotMappings, out var flatSlotId))
            {
                builder.Add(flatSlotId);
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<int> RemapParameterSlotIndices(
        int scopeId,
        ImmutableArray<int> slotIndices,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> flatSlotMappings)
    {
        if (slotIndices.IsDefault)
        {
            return default;
        }

        if (slotIndices.IsEmpty)
        {
            return ImmutableArray<int>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<int>(slotIndices.Length);
        foreach (var slotIndex in slotIndices)
        {
            builder.Add(TryMapSlot(scopeId, slotIndex, flatSlotMappings, out var flatSlotId)
                ? flatSlotId
                : -1);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<(int SlotIndex, int FlatSlotId)> GetFlatSlotMappings(
        UnifiedBytecodeSlotLayout slotLayout,
        int scopeId) =>
        slotLayout.FlatSlotMappings.TryGetValue(scopeId, out var mappings)
            ? mappings
            : ImmutableArray<(int SlotIndex, int FlatSlotId)>.Empty;

    private static bool TryMapSlot(
        int scopeId,
        int slotIndex,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> flatSlotMappings,
        out int flatSlotId)
    {
        if (flatSlotMappings.TryGetValue(scopeId, out var mappings))
        {
            foreach (var mapping in mappings)
            {
                if (mapping.SlotIndex == slotIndex)
                {
                    flatSlotId = mapping.FlatSlotId;
                    return true;
                }
            }
        }

        flatSlotId = -1;
        return false;
    }

    private static bool TryCompileBlock(
        int instructionIndex,
        ImmutableArray<ExecutionInstruction> instructions,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ref int maxStackDepth,
        out string reason)
    {
        var activated = new List<int>();
        var pushedScopeCount = 0;
        var activationSlots = slotLayout.ActivationSlots;
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
                        if (!TryResolveDeclarationSlot(targetSymbol, declaration.VarKind, slotLayout, activeScopes, out var storeSlot))
                        {
                            reason = $"Unsupported declaration target '{targetSymbol.Name}'.";
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                initializerProgram,
                                slotLayout,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
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
                        if (!TryResolveInstructionSlot(targetSymbol, assignment.FlatSlotId, slotLayout, out var assignmentSlot))
                        {
                            reason = $"Unsupported assignment target '{targetSymbol.Name}'.";
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                valueProgram,
                                slotLayout,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
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
                        if (!TryResolveInstructionSlot(targetSymbol, compoundAssignment.FlatSlotId, slotLayout, out var compoundSlot))
                        {
                            reason = $"Unsupported compound assignment target '{targetSymbol.Name}'.";
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, compoundSlot));
                        if (!TryAppendExpressionProgramOps(
                                rhsProgram,
                                slotLayout,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
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

                    case PushEnvironmentInstruction pushEnvironment:
                        if (!pushEnvironment.PerIterationBindings.IsDefaultOrEmpty)
                        {
                            reason = "Loop iteration environments are not eligible for unified bytecode compilation.";
                            return false;
                        }

                        var lexicalSlotIndices = RemapSlotIndices(
                            pushEnvironment.ScopeId,
                            pushEnvironment.LexicalSlotIndices,
                            slotLayout.FlatSlotMappings);
                        var scopeDescriptorIndex = scopeDescriptors.Count;
                        scopeDescriptors.Add(new UnifiedBytecodeScopeDescriptor(
                            pushEnvironment.ScopeId,
                            lexicalSlotIndices));
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.PushEnvironment,
                            scopeDescriptorIndex));
                        activeScopes.Push(new UnifiedBytecodeScopeFrame(
                            pushEnvironment.ScopeId,
                            pushEnvironment.SlotMap,
                            GetFlatSlotMappings(slotLayout, pushEnvironment.ScopeId)));
                        pushedScopeCount++;
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                pushEnvironment.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = pushEnvironment.Next;
                        continue;

                    case PopEnvironmentInstruction popEnvironment:
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.PopEnvironment));
                        if (activeScopes.Count > 1 && activeScopes.Peek().ScopeId == popEnvironment.ScopeId)
                        {
                            activeScopes.Pop();
                            pushedScopeCount--;
                        }

                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                popEnvironment.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = popEnvironment.Next;
                        continue;

                    case JumpInstruction jump:
                        return TryAppendResolvedJump(
                            instructionIndex,
                            jump.TargetIndex,
                            instructions,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            scopeDescriptors,
                            ref maxStackDepth,
                            out reason);

                    case BreakInstruction breakInstruction:
                        return TryAppendResolvedJump(
                            instructionIndex,
                            breakInstruction.TargetIndex,
                            instructions,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            scopeDescriptors,
                            ref maxStackDepth,
                            out reason);

                    case ContinueInstruction continueInstruction:
                        return TryAppendResolvedJump(
                            instructionIndex,
                            continueInstruction.TargetIndex,
                            instructions,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            scopeDescriptors,
                            ref maxStackDepth,
                            out reason);

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
                                slotLayout,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                out reason))
                        {
                            return false;
                        }

                        maxStackDepth = Math.Max(maxStackDepth, branch.ConditionProgram.MaxStackDepth);
                        var jumpIfFalseIndex = unified.Count;
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfFalse));

                        if (activeInstructions.Contains(branch.ConsequentIndex))
                        {
                            if (!IsSupportedBranchConsequentBackEdge(
                                    instructionIndex,
                                    branch,
                                    instructions) ||
                                !instructionPcMap.TryGetValue(branch.ConsequentIndex, out var consequentProgramCounter))
                            {
                                reason = $"Unsupported loop control flow at instruction {instructionIndex}.";
                                return false;
                            }

                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.Jump,
                                consequentProgramCounter));
                        }
                        else if (!TryCompileTarget(
                                     branch.ConsequentIndex,
                                     instructions,
                                     slotLayout,
                                     activeScopes,
                                     instructionPcMap,
                                     activeInstructions,
                                     unified,
                                     literalConstants,
                                     stringConstants,
                                     callTargetConstants,
                                     scopeDescriptors,
                                     ref maxStackDepth,
                                     out reason))
                        {
                            return false;
                        }

                        if (!TryCompileTarget(
                                branch.AlternateIndex,
                                instructions,
                                slotLayout,
                                activeScopes,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                scopeDescriptors,
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
                                slotLayout,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return));
                        maxStackDepth = Math.Max(maxStackDepth, returnProgram.MaxStackDepth);
                        reason = string.Empty;
                        return true;

                    case ReturnInstruction { ReturnProgram: null, AwaitedProgram: null }:
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined));
                        reason = string.Empty;
                        return true;

                    case ThrowInstruction { ThrowProgram: { } throwProgram, AwaitedProgram: null }:
                        if (!TryAppendExpressionProgramOps(
                                throwProgram,
                                slotLayout,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Throw));
                        maxStackDepth = Math.Max(maxStackDepth, throwProgram.MaxStackDepth);
                        reason = string.Empty;
                        return true;

                    case EvaluateAndDiscardInstruction { ExpressionProgram: { } discardedProgram } discard:
                        if (!TryAppendExpressionProgramOps(
                                discardedProgram,
                                slotLayout,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
                        maxStackDepth = Math.Max(maxStackDepth, discardedProgram.MaxStackDepth);
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

            while (pushedScopeCount > 0 && activeScopes.Count > 1)
            {
                activeScopes.Pop();
                pushedScopeCount--;
            }
        }
    }

    private static bool TryCompileTarget(
        int targetIndex,
        ImmutableArray<ExecutionInstruction> instructions,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
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
            slotLayout,
            activeScopes,
            instructionPcMap,
            activeInstructions,
            unified,
            literalConstants,
            stringConstants,
            callTargetConstants,
            scopeDescriptors,
            ref maxStackDepth,
            out reason);
    }

    private static bool TryAppendResolvedJump(
        int sourceInstructionIndex,
        int targetIndex,
        ImmutableArray<ExecutionInstruction> instructions,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ref int maxStackDepth,
        out string reason)
    {
        if ((uint)targetIndex >= (uint)instructions.Length)
        {
            reason = "Instruction flow reached an invalid target index.";
            return false;
        }

        var jumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump));

        if (activeInstructions.Contains(targetIndex))
        {
            if (!IsSupportedLoopBackEdgeTarget(sourceInstructionIndex, targetIndex, instructions) ||
                !instructionPcMap.TryGetValue(targetIndex, out var loopHeadProgramCounter))
            {
                reason = $"Unsupported loop control flow at instruction {sourceInstructionIndex}.";
                return false;
            }

            PatchOperand(unified, jumpIndex, loopHeadProgramCounter);
            reason = string.Empty;
            return true;
        }

        if (!TryCompileTarget(
                targetIndex,
                instructions,
                slotLayout,
                activeScopes,
                instructionPcMap,
                activeInstructions,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                scopeDescriptors,
                ref maxStackDepth,
                out reason))
        {
            return false;
        }

        PatchOperand(unified, jumpIndex, instructionPcMap[targetIndex]);
        reason = string.Empty;
        return true;
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
            if (!IsSupportedLoopBackEdgeTarget(sourceInstructionIndex, targetIndex, instructions) ||
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

    private static bool IsSupportedLoopBackEdgeTarget(
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

        if (instructions[sourceInstructionIndex] is ContinueInstruction { TargetIndex: var continueTargetIndex } &&
            continueTargetIndex == targetIndex)
        {
            return HasLoopContinueTarget(targetIndex, branch.AlternateIndex, instructions);
        }

        if (instructions[sourceInstructionIndex] is not AssignmentSlotInstruction and not CompoundAssignmentSlotInstruction)
        {
            return false;
        }

        return TryIsLinearCanonicalWhileBody(branch.ConsequentIndex, sourceInstructionIndex, instructions) &&
               !HasExplicitJumpIntoLoopBackEdgeSource(sourceInstructionIndex, instructions);
    }

    private static bool IsSupportedBranchConsequentBackEdge(
        int branchInstructionIndex,
        BranchInstruction branch,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        return branch.ConsequentIndex != branchInstructionIndex &&
               HasLoopContinueTarget(branchInstructionIndex, branch.AlternateIndex, instructions) &&
               ReachesInstructionLinearly(branch.ConsequentIndex, branchInstructionIndex, instructions);
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
                case ContinueInstruction continueInstruction
                    when continueInstruction.TargetIndex == endInstructionIndex:
                    current = continueInstruction.TargetIndex;
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

    private static bool HasLoopContinueTarget(
        int loopContinueInstructionIndex,
        int loopBreakInstructionIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction is BreakableEnterInstruction
                {
                    Label: null,
                    ContinueTarget: var continueTarget,
                    BreakTarget: var breakTarget
                } &&
                continueTarget == loopContinueInstructionIndex &&
                breakTarget == loopBreakInstructionIndex)
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
                ContinueInstruction continueInstruction
                    when continueInstruction.TargetIndex == targetInstructionIndex => continueInstruction.TargetIndex,
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
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
        if (TryAppendFirstBoundaryCallTargetPreparation(
                expressionProgram,
                slotLayout,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

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

                case ExpressionOpKind.LoadThis:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadThis));
                    break;

                case ExpressionOpKind.LoadNewTarget:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadNewTarget));
                    break;

                case ExpressionOpKind.LoadLiteral:
                    var literal = operation.GetLiteral(expressionProgram.LiteralConstants.AsSpan());
                    var literalIndex = literalConstants.Count;
                    literalConstants.Add(literal);
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, literalIndex));
                    break;

                case ExpressionOpKind.TypeOf:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.TypeOf));
                    break;

                case ExpressionOpKind.TypeOfIdentifier:
                    if (!TryResolveTypeOfIdentifierSlot(operation, expressionProgram, activationSlots, out var typeOfSlot, out reason))
                    {
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.TypeOfIdentifier, typeOfSlot));
                    break;

                case ExpressionOpKind.UnaryPlus:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.UnaryPlus));
                    break;

                case ExpressionOpKind.UnaryMinus:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.UnaryMinus));
                    break;

                case ExpressionOpKind.UnaryLogicalNot:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.UnaryLogicalNot));
                    break;

                case ExpressionOpKind.UnaryBitwiseNot:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.UnaryBitwiseNot));
                    break;

                case ExpressionOpKind.UnaryVoid:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.UnaryVoid));
                    break;

                case ExpressionOpKind.ToString:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ToString));
                    break;

                case ExpressionOpKind.Pop:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
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

    private static bool TryAppendFirstBoundaryCallTargetPreparation(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
        if (expressionProgram.OperationCount < 2)
        {
            reason = string.Empty;
            return false;
        }

        var call = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);
        if (call.Kind != ExpressionOpKind.Call)
        {
            reason = string.Empty;
            return false;
        }

        if (!call.HasExplicitThis)
        {
            reason = "Only direct identifier and member calls with explicit receiver records are supported.";
            return false;
        }

        if (call.SpreadMaskConstantIndex >= 0)
        {
            reason = "Spread call arguments are outside the call-target preparation boundary.";
            return false;
        }

        if (call.IsDirectEval)
        {
            reason = "Direct eval invocation semantics are outside the call-target preparation boundary.";
            return false;
        }

        if (TryAppendIdentifierCallTargetPreparation(
                expressionProgram,
                slotLayout,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                call,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

        if (TryAppendNamedMemberCallTargetPreparation(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                call,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

        if (TryAppendComputedMemberCallTargetPreparation(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                call,
                out reason))
        {
            return true;
        }

        if (string.IsNullOrEmpty(reason))
        {
            reason = "Call target preparation is only supported for activation-resolved identifier and direct member calls.";
        }

        return false;
    }

    private static bool TryAppendIdentifierCallTargetPreparation(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        PackedExpressionOp call,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
        var callTarget = expressionProgram.GetOperation(0);
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget)
        {
            reason = string.Empty;
            return false;
        }

        if (callTarget.IsArguments)
        {
            reason = "arguments call targets are outside the call-target preparation boundary.";
            return false;
        }

        var identifier = callTarget.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        if (!TryResolveActivationCallTargetSlot(identifier, slotLayout, out var slotIndex))
        {
            reason = $"Unsupported identifier call target '{identifier.Name.Name}'.";
            return false;
        }

        var nameIndex = stringConstants.Count;
        stringConstants.Add(identifier.Name.Name ?? string.Empty);
        var callTargetIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(
            UnifiedBytecodeCallTargetKind.Identifier,
            slotIndex,
            nameIndex));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareIdentifierCallTarget,
            callTargetIndex));

        return TryAppendCallArguments(
            expressionProgram,
            activationSlots,
            unified,
            literalConstants,
            argsStartIndex: 1,
            call,
            out reason);
    }

    private static bool TryAppendNamedMemberCallTargetPreparation(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        PackedExpressionOp call,
        out string reason)
    {
        var callTargetIndexInProgram = FindFirstOperation(expressionProgram, ExpressionOpKind.LoadNamedCallTarget);
        if (callTargetIndexInProgram < 0)
        {
            reason = string.Empty;
            return false;
        }

        if (callTargetIndexInProgram == 0)
        {
            reason = "Named member call targets require a receiver expression.";
            return false;
        }

        if (!TryAppendNamedReceiverOperations(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                callTargetIndexInProgram,
                out reason))
        {
            return false;
        }

        var callTarget = expressionProgram.GetOperation(callTargetIndexInProgram);
        var propertyName = callTarget.GetString(expressionProgram.StringConstants.AsSpan());
        if (propertyName.IsPrivateName())
        {
            reason = "Private named member call targets are outside the call-target preparation boundary.";
            return false;
        }

        var nameIndex = stringConstants.Count;
        stringConstants.Add(propertyName);
        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(
            UnifiedBytecodeCallTargetKind.NamedMember,
            NameConstantIndex: nameIndex));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareNamedCallTarget,
            callTargetConstantIndex));

        return TryAppendCallArguments(
            expressionProgram,
            activationSlots,
            unified,
            literalConstants,
            callTargetIndexInProgram + 1,
            call,
            out reason);
    }

    private static bool TryAppendComputedMemberCallTargetPreparation(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        PackedExpressionOp call,
        out string reason)
    {
        var callTargetIndexInProgram = FindFirstOperation(expressionProgram, ExpressionOpKind.LoadComputedCallTarget);
        if (callTargetIndexInProgram < 0)
        {
            reason = string.Empty;
            return false;
        }

        if (callTargetIndexInProgram < 2)
        {
            reason = "Computed member call targets require receiver and key operands.";
            return false;
        }

        var keyIndex = callTargetIndexInProgram - 1;
        if (!TryAppendNamedReceiverOperations(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                keyIndex,
                out reason))
        {
            return false;
        }

        if (!TryAppendComputedPropertyKeyLoad(
                expressionProgram.GetOperation(keyIndex),
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return false;
        }

        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(UnifiedBytecodeCallTargetKind.ComputedMember));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareComputedCallTarget,
            callTargetConstantIndex));

        return TryAppendCallArguments(
            expressionProgram,
            activationSlots,
            unified,
            literalConstants,
            callTargetIndexInProgram + 1,
            call,
            out reason);
    }

    private static bool TryAppendNamedReceiverOperations(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants,
        int endExclusive,
        out string reason)
    {
        if (endExclusive is < 1 or > 3)
        {
            reason = "Member call receiver is outside the direct named-chain boundary.";
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

        for (var operationIndex = 1; operationIndex < endExclusive; operationIndex++)
        {
            var propertyRead = expressionProgram.GetOperation(operationIndex);
            if (propertyRead.Kind != ExpressionOpKind.GetNamedProperty)
            {
                reason = $"Unsupported member call receiver op '{propertyRead.Kind}'.";
                return false;
            }

            var propertyName = propertyRead.GetString(expressionProgram.StringConstants.AsSpan());
            if (propertyName.IsPrivateName())
            {
                reason = "Private named receiver properties are outside the call-target preparation boundary.";
                return false;
            }

            if (propertyRead.IsOptional || propertyRead.ShortCircuitOnNullishTarget)
            {
                reason = "Optional receiver properties are outside the call-target preparation boundary.";
                return false;
            }

            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(propertyName);
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryResolveTypeOfIdentifierSlot(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        out int slotIndex,
        out string reason)
    {
        if (operation.IsArguments)
        {
            slotIndex = -1;
            reason = "arguments typeof is not supported.";
            return false;
        }

        var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        if (!TryResolveActivationSlot(identifier, activationSlots, out slotIndex))
        {
            reason = $"Unsupported typeof identifier '{identifier.Name.Name}'.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryAppendCallArguments(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        int argsStartIndex,
        PackedExpressionOp call,
        out string reason)
    {
        var callIndex = expressionProgram.OperationCount - 1;
        if (callIndex - argsStartIndex != call.ArgumentCount)
        {
            reason = "Call arguments must be simple one-op operands in the call-target preparation boundary.";
            return false;
        }

        for (var operationIndex = argsStartIndex; operationIndex < callIndex; operationIndex++)
        {
            if (!TryAppendSimpleOperandLoad(
                    expressionProgram.GetOperation(operationIndex),
                    expressionProgram,
                    activationSlots,
                    unified,
                    literalConstants,
                    out reason))
            {
                return false;
            }
        }

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.CallInvocationBoundary,
            call.ArgumentCount));
        reason = string.Empty;
        return true;
    }

    private static int FindFirstOperation(ExpressionProgram expressionProgram, ExpressionOpKind kind)
    {
        for (var operationIndex = 0; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            if (expressionProgram.GetOperation(operationIndex).Kind == kind)
            {
                return operationIndex;
            }
        }

        return -1;
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

        if (!TryAppendActivationValueLoad(
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

        if (!TryAppendActivationValueLoad(
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

        if (!TryAppendActivationValueLoad(
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

        if (!TryAppendActivationValueLoad(
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

        if (!TryAppendActivationValueLoad(
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

        if (!TryAppendActivationValueLoad(
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

        if (!TryAppendActivationValueLoad(baseLoad, expressionProgram, activationSlots, unified, out reason))
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

        if (!TryAppendActivationValueLoad(
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
                return TryAppendActivationValueLoad(operation, expressionProgram, activationSlots, unified, out reason);

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
                return TryAppendActivationValueLoad(operation, expressionProgram, activationSlots, unified, out reason);

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

    private static bool TryAppendActivationValueLoad(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        out string reason)
    {
        switch (operation.Kind)
        {
            case ExpressionOpKind.LoadThis:
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadThis));
                reason = string.Empty;
                return true;

            case ExpressionOpKind.LoadNewTarget:
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadNewTarget));
                reason = string.Empty;
                return true;
        }

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
            BinaryOperator.StrictEqual or
            BinaryOperator.StrictNotEqual or
            BinaryOperator.LessThan or
            BinaryOperator.LessThanOrEqual or
            BinaryOperator.GreaterThan or
            BinaryOperator.GreaterThanOrEqual;

    private static bool TryResolveInstructionSlot(
        Symbol symbol,
        int flatSlotId,
        UnifiedBytecodeSlotLayout slotLayout,
        out int slotIndex)
    {
        if (flatSlotId >= 0)
        {
            slotIndex = flatSlotId;
            return true;
        }

        return TryResolveActivationSymbolSlot(symbol, slotLayout, out slotIndex);
    }

    private static bool TryResolveDeclarationSlot(
        Symbol symbol,
        VariableKind varKind,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        out int slotIndex)
    {
        if (varKind == VariableKind.Var)
        {
            return TryResolveActivationSymbolSlot(symbol, slotLayout, out slotIndex);
        }

        if (activeScopes.Count > 0)
        {
            var scope = activeScopes.Peek();
            if (scope.SlotMap.TryGetValue(symbol, out var scopedSlotIndex))
            {
                foreach (var (candidateSlotIndex, flatSlotId) in scope.FlatSlotMappings)
                {
                    if (candidateSlotIndex == scopedSlotIndex)
                    {
                        slotIndex = flatSlotId;
                        return true;
                    }
                }
            }
        }

        slotIndex = -1;
        return false;
    }

    private static bool TryResolveActivationSlot(IdentifierOperand identifier, ActivationSlotShape activationSlots, out int slotIndex)
    {
        if (identifier.FlatSlotId >= 0)
        {
            slotIndex = identifier.FlatSlotId;
            return true;
        }

        if (identifier.ScopeId == activationSlots.ScopeId && identifier.SlotIndex >= 0)
        {
            slotIndex = identifier.SlotIndex;
            return true;
        }

        if (identifier.ScopeId >= 0 && identifier.ScopeId != activationSlots.ScopeId)
        {
            slotIndex = -1;
            return false;
        }

        if (activationSlots.SlotMap.TryGetValue(identifier.Name, out var mappedSlot))
        {
            slotIndex = mappedSlot;
            return true;
        }

        slotIndex = -1;
        return false;
    }

    private static bool TryResolveActivationCallTargetSlot(
        IdentifierOperand identifier,
        UnifiedBytecodeSlotLayout slotLayout,
        out int slotIndex)
    {
        var activationSlots = slotLayout.ActivationSlots;
        if (identifier.ScopeId >= 0 && identifier.SlotIndex >= 0)
        {
            if (identifier.ScopeId == activationSlots.ScopeId &&
                TryMapSlot(identifier.ScopeId, identifier.SlotIndex, slotLayout.FlatSlotMappings, out slotIndex))
            {
                return true;
            }

            slotIndex = -1;
            return false;
        }

        if (activationSlots.SlotMap.TryGetValue(identifier.Name, out var mappedSlot) &&
            TryMapSlot(activationSlots.ScopeId, mappedSlot, slotLayout.FlatSlotMappings, out slotIndex))
        {
            return true;
        }

        if (identifier.ScopeId >= 0 && identifier.ScopeId != activationSlots.ScopeId)
        {
            slotIndex = -1;
            return false;
        }

        if (identifier.FlatSlotId >= 0)
        {
            slotIndex = identifier.FlatSlotId;
            return true;
        }

        slotIndex = -1;
        return false;
    }

    private static bool TryResolveActivationSymbolSlot(
        Symbol symbol,
        UnifiedBytecodeSlotLayout slotLayout,
        out int slotIndex)
    {
        if (slotLayout.ActivationSlots.SlotMap.TryGetValue(symbol, out var activationSlotIndex) &&
            TryMapSlot(
                slotLayout.ActivationSlots.ScopeId,
                activationSlotIndex,
                slotLayout.FlatSlotMappings,
                out slotIndex))
        {
            return true;
        }

        slotIndex = -1;
        return false;
    }
}
