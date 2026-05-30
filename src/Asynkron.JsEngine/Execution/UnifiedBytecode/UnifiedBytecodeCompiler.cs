using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeCompiler
{
    private const int UpdateIncrementFlag = 1;
    private const int UpdatePrefixFlag = 2;
    private const int DynamicStoreAllowNameInferenceFlag = 1;

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
        ImmutableArray<string?> SlotNames)
    {
        // Spread-call masks discovered while compiling synchronous spread invocations
        // (gh2676). Each entry holds the spread argument positions for one
        // CallInvocationBoundary; the boundary operand references it by index+1.
        public List<ImmutableArray<int>> CallSpreadMasks { get; } = [];

        public int RegisterSpreadMask(ImmutableArray<int> spreadIndices)
        {
            var index = CallSpreadMasks.Count;
            CallSpreadMasks.Add(spreadIndices);
            return index;
        }
    }

    // CallInvocationBoundary operand packing for spread calls (gh2676):
    // low 16 bits hold the pushed argument value count, the high bits hold
    // spreadMaskIndex + 1 (0 means "no spread").
    private const int CallBoundaryArgumentMask = 0xFFFF;
    private const int CallBoundarySpreadShift = 16;

    private static int EncodeCallBoundaryOperand(int argumentValueCount, int spreadMaskIndex) =>
        spreadMaskIndex < 0
            ? argumentValueCount
            : (argumentValueCount & CallBoundaryArgumentMask) | ((spreadMaskIndex + 1) << CallBoundarySpreadShift);

    public static bool TryCompile(
        ExecutionPlan plan,
        bool isAsync,
        bool isGenerator,
        out UnifiedBytecodeProgram program,
        out string reason)
    {
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
        if (!UnifiedBytecodeWithDepthAnalysis.TryBuildActiveWithDepths(
                instructions,
                plan.EntryPoint,
                out var activeWithDepths,
                out reason))
        {
            program = EmptyProgram();
            return false;
        }

        var slotLayout = BuildSlotLayout(plan);

        var unified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        var literalConstants = ImmutableArray.CreateBuilder<JsValue>();
        var stringConstants = ImmutableArray.CreateBuilder<string>();
        var callTargetConstants = ImmutableArray.CreateBuilder<UnifiedBytecodeCallTarget>();
        var functionLiteralConstants = ImmutableArray.CreateBuilder<FunctionLiteralDescriptor>();
        var scopeDescriptors = ImmutableArray.CreateBuilder<UnifiedBytecodeScopeDescriptor>();
        var tryDescriptors = ImmutableArray.CreateBuilder<UnifiedBytecodeTryDescriptor>();
        var catchDescriptors = ImmutableArray.CreateBuilder<UnifiedBytecodeCatchDescriptor>();
        var driverDescriptors = ImmutableArray.CreateBuilder<UnifiedBytecodeDriverDescriptor>();
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
                activeWithDepths,
                slotLayout,
                activeScopes,
                instructionPcMap,
                activeInstructions,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                functionLiteralConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
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
            scopeDescriptors.ToImmutable(),
            tryDescriptors.ToImmutable(),
            catchDescriptors.ToImmutable(),
            driverDescriptors.ToImmutable(),
            slotLayout.CallSpreadMasks.Count == 0
                ? ImmutableArray<ImmutableArray<int>>.Empty
                : [.. slotLayout.CallSpreadMasks],
            functionLiteralConstants.Count == 0
                ? ImmutableArray<FunctionLiteralDescriptor>.Empty
                : functionLiteralConstants.ToImmutable());
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
            ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            ImmutableArray<UnifiedBytecodeDriverDescriptor>.Empty);

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
            switch (instruction)
            {
                case PushEnvironmentInstruction push:
                    if (!push.SlotNames.IsDefaultOrEmpty)
                    {
                        foreach (var (name, slotIndex) in push.SlotNames)
                        {
                            SetMappedSlotName(names, flatSlotMappings, push.ScopeId, slotIndex, name);
                        }

                        break;
                    }

                    foreach (var (name, slotIndex) in push.SlotMap)
                    {
                        SetMappedSlotName(names, flatSlotMappings, push.ScopeId, slotIndex, name);
                    }

                    break;

                case EnterCatchInstruction enterCatch:
                    foreach (var (name, slotIndex) in enterCatch.SlotMap)
                    {
                        SetMappedSlotName(names, flatSlotMappings, enterCatch.ScopeId, slotIndex, name);
                    }

                    if (enterCatch.CatchBindingProgram is IdentifierBindingTargetProgram identifier)
                    {
                        if (identifier.FlatSlotId >= 0)
                        {
                            SetSlotName(names, identifier.FlatSlotId, identifier.Name);
                        }
                        else
                        {
                            SetMappedSlotName(
                                names,
                                flatSlotMappings,
                                enterCatch.ScopeId,
                                identifier.SlotIndex,
                                identifier.Name);
                        }
                    }

                    break;
            }
        }

        return names.ToImmutableArray();
    }

    private static void SetMappedSlotName(
        string?[] names,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> flatSlotMappings,
        int scopeId,
        int slotIndex,
        Symbol name)
    {
        if (TryMapSlot(scopeId, slotIndex, flatSlotMappings, out var flatSlotId))
        {
            SetSlotName(names, flatSlotId, name);
        }
    }

    private static void SetSlotName(string?[] names, int flatSlotId, Symbol name)
    {
        if ((uint)flatSlotId < (uint)names.Length)
        {
            names[flatSlotId] = name.Name;
        }
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
        int[] activeWithDepths,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<FunctionLiteralDescriptor>.Builder functionLiteralConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ImmutableArray<UnifiedBytecodeTryDescriptor>.Builder tryDescriptors,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
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
                var allowsDynamicIdentifiers = activeWithDepths[instructionIndex] > 0;

                switch (instructions[instructionIndex])
                {
                    case SimpleVariableDeclarationInstruction
                        {
                            InitializerProgram: null,
                            AwaitedProgram: null,
                            TargetSymbol: { } declarationTargetSymbol
                        } declaration:
                        if (!TryResolveDeclarationSlot(
                                declarationTargetSymbol,
                                declaration.VarKind,
                                slotLayout,
                                activeScopes,
                                out var emptyDeclarationSlot))
                        {
                            reason =
                                $"Declaration target '{declarationTargetSymbol.Name}' is not eligible for unified bytecode storage.";
                            return false;
                        }

                        var emptyDeclarationLiteralIndex = literalConstants.Count;
                        literalConstants.Add(JsValue.Undefined);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.LoadLiteral,
                            emptyDeclarationLiteralIndex));
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.InitializeSlot, emptyDeclarationSlot));
                        maxStackDepth = Math.Max(maxStackDepth, 1);
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

                    case SimpleVariableDeclarationInstruction
                        {
                            InitializerProgram: { } initializerProgram,
                            AwaitedProgram: null,
                            TargetSymbol: { } declarationTargetSymbol
                        } declaration:
                        if (!TryResolveDeclarationSlot(declarationTargetSymbol, declaration.VarKind, slotLayout, activeScopes, out var storeSlot))
                        {
                            if (!TryAppendDynamicVarDeclaration(
                                    declaration,
                                    initializerProgram,
                                    allowsDynamicIdentifiers,
                                    slotLayout,
                                    unified,
                                    literalConstants,
                                    stringConstants,
                                    callTargetConstants,
                                    functionLiteralConstants,
                                    out reason))
                            {
                                return false;
                            }

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
                        }

                        if (!TryAppendExpressionProgramOps(
                                initializerProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        if (declaration.AllowNameInference)
                        {
                            var nameInferenceIndex = stringConstants.Count;
                            stringConstants.Add(declarationTargetSymbol.Name);
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.EnsureHasName, nameInferenceIndex));
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.InitializeSlot, storeSlot));
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
                            TargetSymbol: { } assignmentTargetSymbol
                        } assignment:
                        if (!TryResolveInstructionSlot(assignmentTargetSymbol, assignment.FlatSlotId, slotLayout, out var assignmentSlot))
                        {
                            if (!allowsDynamicIdentifiers)
                            {
                                reason = $"Unsupported assignment target '{assignmentTargetSymbol.Name}'.";
                                return false;
                            }

                            if (!TryAppendExpressionProgramOps(
                                    valueProgram,
                                    slotLayout,
                                    allowsDynamicIdentifiers,
                                    unified,
                                    literalConstants,
                                    stringConstants,
                                    callTargetConstants,
                                    functionLiteralConstants,
                                    out reason))
                            {
                                return false;
                            }

                            AppendDynamicStoreInstruction(
                                assignmentTargetSymbol,
                                assignment.AllowNameInference,
                                unified,
                                stringConstants);
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
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
                        }

                        if (!TryAppendExpressionProgramOps(
                                valueProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        if (assignment.AllowNameInference)
                        {
                            var nameInferenceIndex = stringConstants.Count;
                            stringConstants.Add(assignmentTargetSymbol.Name);
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.EnsureHasName, nameInferenceIndex));
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
                            TargetSymbol: { } compoundTargetSymbol
                        } compoundAssignment
                        when IsSupportedBinaryOperator(compoundAssignment.Operator):
                        if (!TryResolveInstructionSlot(compoundTargetSymbol, compoundAssignment.FlatSlotId, slotLayout, out var compoundSlot))
                        {
                            if (!allowsDynamicIdentifiers)
                            {
                                reason = $"Unsupported compound assignment target '{compoundTargetSymbol.Name}'.";
                                return false;
                            }

                            var dynamicTargetNameIndex = stringConstants.Count;
                            stringConstants.Add(compoundTargetSymbol.Name);
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.LoadDynamicIdentifier,
                                dynamicTargetNameIndex));
                            if (!TryAppendExpressionProgramOps(
                                    rhsProgram,
                                    slotLayout,
                                    allowsDynamicIdentifiers,
                                    unified,
                                    literalConstants,
                                    stringConstants,
                                    callTargetConstants,
                                    functionLiteralConstants,
                                    out reason))
                            {
                                return false;
                            }

                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.Binary,
                                (int)compoundAssignment.Operator));
                            AppendDynamicStoreInstruction(
                                compoundTargetSymbol,
                                allowNameInference: false,
                                unified,
                                stringConstants);
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
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
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, compoundSlot));
                        if (!TryAppendExpressionProgramOps(
                                rhsProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
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

                    case IncrementSlotInstruction
                        {
                            TargetSymbol: { } incrementTargetSymbol
                        } increment:
                        if (!allowsDynamicIdentifiers)
                        {
                            reason =
                                $"Unsupported instruction in unified bytecode plan: {nameof(IncrementSlotInstruction)}.";
                            return false;
                        }

                        var dynamicUpdateNameIndex = stringConstants.Count;
                        stringConstants.Add(incrementTargetSymbol.Name);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.UpdateDynamicIdentifier,
                            EncodeUpdateOperand(
                                dynamicUpdateNameIndex,
                                increment.IsIncrement,
                                increment.IsPrefix)));
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
                        maxStackDepth = Math.Max(maxStackDepth, 1);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                increment.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = increment.Next;
                        continue;

                    case PushEnvironmentInstruction pushEnvironment:
                        // Per-iteration binding environments (for (const/let x in/of ...)) are compiled
                        // as ordinary PushEnvironment instructions. The rebinding semantics are handled
                        // by the move-next instruction writing to the synthetic value slot, the
                        // PushEnvironment resetting the per-iteration lexical slot to Uninitialized, and
                        // the binding statement assigning the value slot to the per-iteration slot.
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

                    case EnterWithInstruction { ObjectProgram: { } enterWithObjectProgram, AwaitedProgram: null } enterWith:
                        if (!TryAppendExpressionProgramOps(
                                enterWithObjectProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.EnterWith));
                        maxStackDepth = Math.Max(maxStackDepth, enterWithObjectProgram.MaxStackDepth);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                enterWith.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = enterWith.Next;
                        continue;

                    case LeaveWithInstruction leaveWith:
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LeaveWith));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                leaveWith.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = leaveWith.Next;
                        continue;

                    case IteratorInitInstruction
                        {
                            IteratorKind: IteratorDriverKind.Sync,
                            IterableProgram: { } iterableProgram,
                            AwaitedProgram: null
                        } iteratorInit:
                        if (!TryResolveDriverSlot(
                                iteratorInit.IteratorSlot,
                                iteratorInit.IteratorSlotIndex,
                                slotLayout,
                                out var iteratorStateSlot))
                        {
                            reason = $"Unsupported iterator state slot '{iteratorInit.IteratorSlot.Name}'.";
                            return false;
                        }

                        // Slice A (#2678): resolve the loop-head TDZ bindings to flat slots and
                        // mark them uninitialized BEFORE the iterable source is evaluated, so a
                        // read of the head binding inside the source (e.g. `for (const x of [x])`)
                        // throws a ReferenceError on the production path.
                        if (!TryEmitTdzHeadInit(
                                iteratorInit.TdzBindings,
                                iteratorInit.TdzIsConst,
                                iteratorInit.TdzScopeId,
                                iteratorInit.TdzSlotIndices,
                                slotLayout,
                                unified,
                                driverDescriptors,
                                out reason))
                        {
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                iterableProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.IteratorInit,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(
                                    iteratorStateSlot,
                                    IteratorKind: iteratorInit.IteratorKind))));
                        maxStackDepth = Math.Max(maxStackDepth, iterableProgram.MaxStackDepth);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                iteratorInit.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = iteratorInit.Next;
                        continue;

                    case IteratorMoveNextInstruction iteratorMoveNext:
                        return TryAppendDriverMoveNext(
                            instructionIndex,
                            iteratorMoveNext.Next,
                            iteratorMoveNext.BreakIndex,
                            iteratorMoveNext.IteratorSlot,
                            iteratorMoveNext.IteratorSlotIndex,
                            iteratorMoveNext.ValueSlot,
                            iteratorMoveNext.ValueSlotIndex,
                            UnifiedBytecodeOpCode.IteratorMoveNext,
                            instructions,
                            activeWithDepths,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            functionLiteralConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            ref maxStackDepth,
                            out reason);

                    case IteratorCloseInstruction iteratorClose:
                        if (!TryResolveActivationSymbolSlot(iteratorClose.IteratorSlot, slotLayout, out var closeStateSlot))
                        {
                            reason = $"Unsupported iterator close state slot '{iteratorClose.IteratorSlot.Name}'.";
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.IteratorClose,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(closeStateSlot))));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                iteratorClose.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = iteratorClose.Next;
                        continue;

                    case ForInInitInstruction
                        {
                            ObjectProgram: { } objectProgram,
                            AwaitedProgram: null
                        } forInInit:
                        if (!TryResolveDriverSlot(
                                forInInit.StateSlot,
                                forInInit.StateSlotIndex,
                                slotLayout,
                                out var forInStateSlot))
                        {
                            reason = $"Unsupported for-in state slot '{forInInit.StateSlot.Name}'.";
                            return false;
                        }

                        // Slice A (#2678): resolve the loop-head TDZ bindings to flat slots and
                        // mark them uninitialized BEFORE the source object is evaluated, so a
                        // read of the head binding inside the source (e.g. `for (const k in k)`)
                        // throws a ReferenceError on the production path.
                        if (!TryEmitTdzHeadInit(
                                forInInit.TdzBindings,
                                forInInit.TdzIsConst,
                                forInInit.TdzScopeId,
                                forInInit.TdzSlotIndices,
                                slotLayout,
                                unified,
                                driverDescriptors,
                                out reason))
                        {
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                objectProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ForInInit,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(forInStateSlot))));
                        maxStackDepth = Math.Max(maxStackDepth, objectProgram.MaxStackDepth);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                forInInit.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = forInInit.Next;
                        continue;

                    case ForInMoveNextInstruction forInMoveNext:
                        return TryAppendDriverMoveNext(
                            instructionIndex,
                            forInMoveNext.Next,
                            forInMoveNext.BreakIndex,
                            forInMoveNext.StateSlot,
                            forInMoveNext.StateSlotIndex,
                            forInMoveNext.ValueSlot,
                            forInMoveNext.ValueSlotIndex,
                            UnifiedBytecodeOpCode.ForInMoveNext,
                            instructions,
                            activeWithDepths,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            functionLiteralConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            ref maxStackDepth,
                            out reason);

                    case ArrayDestructuringInitInstruction arrayDestructuringInit:
                        if (!TryResolveDriverSlot(
                                arrayDestructuringInit.IteratorSlot,
                                arrayDestructuringInit.IteratorSlotIndex,
                                slotLayout,
                                out var destructuringStateSlot))
                        {
                            reason =
                                $"Unsupported array destructuring state slot '{arrayDestructuringInit.IteratorSlot.Name}'.";
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                arrayDestructuringInit.SourceProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ArrayDestructuringInit,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(destructuringStateSlot))));
                        maxStackDepth = Math.Max(maxStackDepth, arrayDestructuringInit.SourceProgram.MaxStackDepth);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                arrayDestructuringInit.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = arrayDestructuringInit.Next;
                        continue;

                    case ArrayDestructuringElementInstruction arrayDestructuringElement:
                        if (!TryResolveDriverSlot(
                                arrayDestructuringElement.IteratorSlot,
                                arrayDestructuringElement.IteratorSlotIndex,
                                slotLayout,
                                out var elementStateSlot))
                        {
                            reason =
                                $"Unsupported array destructuring state slot '{arrayDestructuringElement.IteratorSlot.Name}'.";
                            return false;
                        }

                        var targetSlot = -1;
                        if (arrayDestructuringElement.TargetSymbol is { } targetSymbol &&
                            !TryResolveDeclarationSlot(
                                targetSymbol,
                                arrayDestructuringElement.VarKind,
                                slotLayout,
                                activeScopes,
                                out targetSlot))
                        {
                            reason = $"Unsupported array destructuring target '{targetSymbol.Name}'.";
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ArrayDestructuringElement,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(
                                    elementStateSlot,
                                    TargetSlot: targetSlot))));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                arrayDestructuringElement.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = arrayDestructuringElement.Next;
                        continue;

                    case ArrayDestructuringRestInstruction arrayDestructuringRest:
                        if (!TryResolveDriverSlot(
                                arrayDestructuringRest.IteratorSlot,
                                arrayDestructuringRest.IteratorSlotIndex,
                                slotLayout,
                                out var restStateSlot))
                        {
                            reason =
                                $"Unsupported array destructuring state slot '{arrayDestructuringRest.IteratorSlot.Name}'.";
                            return false;
                        }

                        if (!TryResolveDeclarationSlot(
                                arrayDestructuringRest.RestSymbol,
                                arrayDestructuringRest.VarKind,
                                slotLayout,
                                activeScopes,
                                out var restTargetSlot))
                        {
                            reason = $"Unsupported array destructuring rest target '{arrayDestructuringRest.RestSymbol.Name}'.";
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ArrayDestructuringRest,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(
                                    restStateSlot,
                                    TargetSlot: restTargetSlot))));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                arrayDestructuringRest.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = arrayDestructuringRest.Next;
                        continue;

                    case ArrayDestructuringCloseInstruction arrayDestructuringClose:
                        if (!TryResolveDriverSlot(
                                arrayDestructuringClose.IteratorSlot,
                                arrayDestructuringClose.IteratorSlotIndex,
                                slotLayout,
                                out var closeDestructuringStateSlot))
                        {
                            reason =
                                $"Unsupported array destructuring state slot '{arrayDestructuringClose.IteratorSlot.Name}'.";
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ArrayDestructuringClose,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(closeDestructuringStateSlot))));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                arrayDestructuringClose.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = arrayDestructuringClose.Next;
                        continue;

                    case ObjectDestructuringInitInstruction objectDestructuringInit:
                        if (!TryResolveDriverSlot(
                                objectDestructuringInit.SourceSlot,
                                objectDestructuringInit.SourceSlotIndex,
                                slotLayout,
                                out var objectDestructuringStateSlot))
                        {
                            reason =
                                $"Unsupported object destructuring state slot '{objectDestructuringInit.SourceSlot.Name}'.";
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                objectDestructuringInit.SourceProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ObjectDestructuringInit,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(objectDestructuringStateSlot))));
                        maxStackDepth = Math.Max(maxStackDepth, objectDestructuringInit.SourceProgram.MaxStackDepth);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                objectDestructuringInit.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = objectDestructuringInit.Next;
                        continue;

                    case ObjectDestructuringPropertyInstruction objectDestructuringProperty:
                        if (!TryResolveDriverSlot(
                                objectDestructuringProperty.SourceSlot,
                                objectDestructuringProperty.SourceSlotIndex,
                                slotLayout,
                                out var objectPropertyStateSlot))
                        {
                            reason =
                                $"Unsupported object destructuring state slot '{objectDestructuringProperty.SourceSlot.Name}'.";
                            return false;
                        }

                        if (!TryResolveDeclarationSlot(
                                objectDestructuringProperty.TargetSymbol,
                                objectDestructuringProperty.VarKind,
                                slotLayout,
                                activeScopes,
                                out var objectPropertyTargetSlot))
                        {
                            reason =
                                $"Unsupported object destructuring target '{objectDestructuringProperty.TargetSymbol.Name}'.";
                            return false;
                        }

                        var objectPropertyNameIndex = stringConstants.Count;
                        stringConstants.Add(objectDestructuringProperty.PropertyName);

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ObjectDestructuringProperty,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(
                                    objectPropertyStateSlot,
                                    TargetSlot: objectPropertyTargetSlot,
                                    NameConstantIndex: objectPropertyNameIndex))));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                objectDestructuringProperty.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = objectDestructuringProperty.Next;
                        continue;

                    case ObjectDestructuringRestInstruction objectDestructuringRest:
                        if (!TryResolveDriverSlot(
                                objectDestructuringRest.SourceSlot,
                                objectDestructuringRest.SourceSlotIndex,
                                slotLayout,
                                out var objectRestStateSlot))
                        {
                            reason =
                                $"Unsupported object destructuring state slot '{objectDestructuringRest.SourceSlot.Name}'.";
                            return false;
                        }

                        if (!TryResolveDeclarationSlot(
                                objectDestructuringRest.RestSymbol,
                                objectDestructuringRest.VarKind,
                                slotLayout,
                                activeScopes,
                                out var objectRestTargetSlot))
                        {
                            reason =
                                $"Unsupported object destructuring rest target '{objectDestructuringRest.RestSymbol.Name}'.";
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ObjectDestructuringRest,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(
                                    objectRestStateSlot,
                                    TargetSlot: objectRestTargetSlot))));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                objectDestructuringRest.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = objectDestructuringRest.Next;
                        continue;

                    case ObjectDestructuringCloseInstruction objectDestructuringClose:
                        if (!TryResolveDriverSlot(
                                objectDestructuringClose.SourceSlot,
                                objectDestructuringClose.SourceSlotIndex,
                                slotLayout,
                                out var objectCloseStateSlot))
                        {
                            reason =
                                $"Unsupported object destructuring state slot '{objectDestructuringClose.SourceSlot.Name}'.";
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ObjectDestructuringClose,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(objectCloseStateSlot))));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                objectDestructuringClose.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = objectDestructuringClose.Next;
                        continue;

                    case JumpInstruction jump:
                        return TryAppendResolvedJump(
                            instructionIndex,
                            jump.TargetIndex,
                            instructions,
                            activeWithDepths,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            functionLiteralConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            UnifiedBytecodeOpCode.Jump,
                            ref maxStackDepth,
                            out reason);

                    case BreakInstruction breakInstruction:
                        return TryAppendResolvedJump(
                            instructionIndex,
                            breakInstruction.TargetIndex,
                            instructions,
                            activeWithDepths,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            functionLiteralConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            UnifiedBytecodeOpCode.Break,
                            ref maxStackDepth,
                            out reason);

                    case ContinueInstruction continueInstruction:
                        return TryAppendResolvedJump(
                            instructionIndex,
                            continueInstruction.TargetIndex,
                            instructions,
                            activeWithDepths,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            functionLiteralConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            UnifiedBytecodeOpCode.Continue,
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

                    case EnterTryInstruction enterTry:
                        return TryAppendTryRegion(
                            enterTry,
                            instructions,
                            activeWithDepths,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            functionLiteralConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            ref maxStackDepth,
                            out reason);

                    case EnterCatchInstruction enterCatch:
                        if (!TryAppendCatchDescriptor(enterCatch, slotLayout, catchDescriptors, out var catchDescriptorIndex, out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.EnterCatch,
                            catchDescriptorIndex));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                enterCatch.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = enterCatch.Next;
                        continue;

                    case LeaveTryInstruction leaveTry:
                        return TryAppendResolvedJump(
                            instructionIndex,
                            leaveTry.Next,
                            instructions,
                            activeWithDepths,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            functionLiteralConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            UnifiedBytecodeOpCode.LeaveTry,
                            ref maxStackDepth,
                            out reason);

                    case EndFinallyInstruction endFinally:
                        return TryAppendResolvedJump(
                            instructionIndex,
                            endFinally.Next,
                            instructions,
                            activeWithDepths,
                            slotLayout,
                            activeScopes,
                            instructionPcMap,
                            activeInstructions,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants,
                            functionLiteralConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            UnifiedBytecodeOpCode.EndFinally,
                            ref maxStackDepth,
                            out reason);

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
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
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
                                     activeWithDepths,
                                     slotLayout,
                                     activeScopes,
                                     instructionPcMap,
                                     activeInstructions,
                                     unified,
                                     literalConstants,
                                     stringConstants,
                                     callTargetConstants,
                                     functionLiteralConstants,
                                     scopeDescriptors,
                                     tryDescriptors,
                                     catchDescriptors,
                                     driverDescriptors,
                                     ref maxStackDepth,
                                     out reason))
                        {
                            return false;
                        }

                        if (!TryCompileTarget(
                                branch.AlternateIndex,
                                instructions,
                                activeWithDepths,
                                slotLayout,
                                activeScopes,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                scopeDescriptors,
                                tryDescriptors,
                                catchDescriptors,
                                driverDescriptors,
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
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
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

                    case ReturnInstruction { AwaitedProgram: { } awaitedReturnProgram }:
                        if (!TryAppendExpressionProgramOps(
                                awaitedReturnProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitedReturn));
                        maxStackDepth = Math.Max(maxStackDepth, awaitedReturnProgram.MaxStackDepth);
                        reason = string.Empty;
                        return true;

                    case AwaitAndDiscardInstruction awaitAndDiscard:
                        if (!TryAppendExpressionProgramOps(
                                awaitAndDiscard.AwaitedProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitAndDiscard));
                        maxStackDepth = Math.Max(maxStackDepth, awaitAndDiscard.AwaitedProgram.MaxStackDepth);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                awaitAndDiscard.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = awaitAndDiscard.Next;
                        continue;

                    case YieldInstruction { AwaitedProgram: null, YieldProgram: { } yieldProgram } yield:
                        if (!TryAppendExpressionProgramOps(
                                yieldProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Yield));
                        maxStackDepth = Math.Max(maxStackDepth, yieldProgram.MaxStackDepth);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                yield.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = yield.Next;
                        continue;

                    case YieldInstruction { AwaitedProgram: null, YieldProgram: null } yield:
                        var undefinedLiteralIndex = literalConstants.Count;
                        literalConstants.Add(JsValue.Undefined);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.LoadLiteral,
                            undefinedLiteralIndex));
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Yield));
                        maxStackDepth = Math.Max(maxStackDepth, 1);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                yield.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = yield.Next;
                        continue;

                    case YieldStarInstruction { AwaitedProgram: null, IterableProgram: { } iterableProgram } yieldStar:
                        if (yieldStar.StateSlotSymbol is null)
                        {
                            reason = "yield* requires a state slot for resumable unified bytecode routing.";
                            return false;
                        }

                        if (!TryResolveYieldStarStateSlot(
                                yieldStar.StateSlotSymbol,
                                iterableProgram,
                                slotLayout,
                                activeScopes,
                                out var yieldStarStateSlot))
                        {
                            reason = $"yield* state slot '{yieldStar.StateSlotSymbol.Name}' is not in the activation slot layout.";
                            return false;
                        }

                        var yieldStarResultSlot = -1;
                        if (yieldStar.ResultSlotSymbol is { } resultSymbol &&
                            !TryResolveVisibleSymbolSlot(resultSymbol, slotLayout, activeScopes, out yieldStarResultSlot))
                        {
                            yieldStarResultSlot = yieldStarStateSlot;
                        }

                        if (!TryAppendExpressionProgramOps(
                                iterableProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                out reason))
                        {
                            return false;
                        }

                        var yieldStarDescriptorIndex = driverDescriptors.Count;
                        driverDescriptors.Add(new UnifiedBytecodeDriverDescriptor(
                            StateSlot: yieldStarStateSlot,
                            ValueSlot: yieldStarResultSlot));
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.YieldStar,
                            yieldStarDescriptorIndex));
                        maxStackDepth = Math.Max(maxStackDepth, iterableProgram.MaxStackDepth);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                yieldStar.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = yieldStar.Next;
                        continue;

                    case StoreResumeValueInstruction storeResume:
                        var resumeSlot = -1;
                        if (storeResume.TargetSymbol is { } resumeSymbol &&
                            !TryResolveActivationSymbolSlot(resumeSymbol, slotLayout, out resumeSlot))
                        {
                            reason = $"Resume target '{resumeSymbol.Name}' is not in the activation slot layout.";
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.StoreResumeValue, resumeSlot));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                storeResume.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = storeResume.Next;
                        continue;

                    case ThrowInstruction { ThrowProgram: { } throwProgram, AwaitedProgram: null }:
                        if (!TryAppendExpressionProgramOps(
                                throwProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
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
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
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
        int[] activeWithDepths,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<FunctionLiteralDescriptor>.Builder functionLiteralConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ImmutableArray<UnifiedBytecodeTryDescriptor>.Builder tryDescriptors,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
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
            activeWithDepths,
            slotLayout,
            activeScopes,
            instructionPcMap,
            activeInstructions,
            unified,
            literalConstants,
            stringConstants,
            callTargetConstants,
            functionLiteralConstants,
            scopeDescriptors,
            tryDescriptors,
            catchDescriptors,
            driverDescriptors,
            ref maxStackDepth,
            out reason);
    }

    private static bool TryAppendTryRegion(
        EnterTryInstruction enterTry,
        ImmutableArray<ExecutionInstruction> instructions,
        int[] activeWithDepths,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<FunctionLiteralDescriptor>.Builder functionLiteralConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ImmutableArray<UnifiedBytecodeTryDescriptor>.Builder tryDescriptors,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
        ref int maxStackDepth,
        out string reason)
    {
        var descriptorIndex = tryDescriptors.Count;
        tryDescriptors.Add(new UnifiedBytecodeTryDescriptor(-1, -1, -1, -1));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.EnterTry, descriptorIndex));

        if (!TryCompileTarget(
                enterTry.Next,
                instructions,
                activeWithDepths,
                slotLayout,
                activeScopes,
                instructionPcMap,
                activeInstructions,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                functionLiteralConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                ref maxStackDepth,
                out reason))
        {
            return false;
        }

        if (enterTry.HandlerIndex >= 0 &&
            !TryCompileTarget(
                enterTry.HandlerIndex,
                instructions,
                activeWithDepths,
                slotLayout,
                activeScopes,
                instructionPcMap,
                activeInstructions,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                functionLiteralConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                ref maxStackDepth,
                out reason))
        {
            return false;
        }

        if (enterTry.FinallyIndex >= 0 &&
            !TryCompileTarget(
                enterTry.FinallyIndex,
                instructions,
                activeWithDepths,
                slotLayout,
                activeScopes,
                instructionPcMap,
                activeInstructions,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                functionLiteralConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                ref maxStackDepth,
                out reason))
        {
            return false;
        }

        tryDescriptors[descriptorIndex] = new UnifiedBytecodeTryDescriptor(
            GetMappedTarget(enterTry.HandlerIndex, instructionPcMap),
            GetMappedTarget(enterTry.FinallyIndex, instructionPcMap),
            GetMappedTarget(enterTry.EndFinallyIndex, instructionPcMap),
            GetMappedTarget(enterTry.LeaveTryIndex, instructionPcMap),
            GetMappedTarget(enterTry.LoopContinueTarget, instructionPcMap),
            GetMappedTarget(enterTry.LoopBreakTarget, instructionPcMap));

        reason = string.Empty;
        return true;
    }

    private static int GetMappedTarget(int instructionIndex, Dictionary<int, int> instructionPcMap) =>
        instructionIndex >= 0 && instructionPcMap.TryGetValue(instructionIndex, out var programCounter)
            ? programCounter
            : -1;

    private static bool TryAppendCatchDescriptor(
        EnterCatchInstruction enterCatch,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        out int descriptorIndex,
        out string reason)
    {
        if (enterCatch.CatchBindingProgram is not null and not IdentifierBindingTargetProgram)
        {
            descriptorIndex = -1;
            reason = "Only optional and simple identifier catch bindings are eligible for unified bytecode compilation.";
            return false;
        }

        var slotIndices = ImmutableArray.CreateBuilder<int>(enterCatch.SlotMap.Count);
        foreach (var slotIndex in enterCatch.SlotMap.Values)
        {
            if (TryMapSlot(enterCatch.ScopeId, slotIndex, slotLayout.FlatSlotMappings, out var flatSlotId))
            {
                slotIndices.Add(flatSlotId);
            }
        }

        var bindingName = default(Symbol);
        var bindingSlot = -1;
        if (enterCatch.CatchBindingProgram is IdentifierBindingTargetProgram identifier)
        {
            bindingName = identifier.Name;
            bindingSlot = identifier.FlatSlotId >= 0
                ? identifier.FlatSlotId
                : TryMapSlot(enterCatch.ScopeId, identifier.SlotIndex, slotLayout.FlatSlotMappings, out var flatSlotId)
                    ? flatSlotId
                    : -1;
            if (bindingSlot < 0)
            {
                descriptorIndex = -1;
                reason = $"Unsupported catch binding slot '{identifier.Name.Name}'.";
                return false;
            }

            if (!slotIndices.Contains(bindingSlot))
            {
                slotIndices.Add(bindingSlot);
            }
        }

        descriptorIndex = catchDescriptors.Count;
        catchDescriptors.Add(new UnifiedBytecodeCatchDescriptor(
            enterCatch.ScopeId,
            slotIndices.ToImmutable(),
            bindingName,
            bindingSlot));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendDriverMoveNext(
        int instructionIndex,
        int nextIndex,
        int breakIndex,
        Symbol stateSymbol,
        int stateSlotIndex,
        Symbol valueSymbol,
        int valueSlotIndex,
        UnifiedBytecodeOpCode opCode,
        ImmutableArray<ExecutionInstruction> instructions,
        int[] activeWithDepths,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<FunctionLiteralDescriptor>.Builder functionLiteralConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ImmutableArray<UnifiedBytecodeTryDescriptor>.Builder tryDescriptors,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
        ref int maxStackDepth,
        out string reason)
    {
        if (!TryResolveDriverSlot(stateSymbol, stateSlotIndex, slotLayout, out var stateSlot))
        {
            reason = $"Unsupported driver state slot '{stateSymbol.Name}'.";
            return false;
        }

        if (!TryResolveDriverSlot(valueSymbol, valueSlotIndex, slotLayout, out var valueSlot))
        {
            reason = $"Unsupported driver value slot '{valueSymbol.Name}'.";
            return false;
        }

        var descriptorIndex = AddDriverDescriptor(
            driverDescriptors,
            new UnifiedBytecodeDriverDescriptor(
                stateSlot,
                ValueSlot: valueSlot,
                NextTarget: unified.Count + 1));
        unified.Add(new UnifiedBytecodeInstruction(opCode, descriptorIndex));

        if (!TryCompileTarget(
                nextIndex,
                instructions,
                activeWithDepths,
                slotLayout,
                activeScopes,
                instructionPcMap,
                activeInstructions,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                functionLiteralConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                ref maxStackDepth,
                out reason))
        {
            return false;
        }

        if (!TryCompileTarget(
                breakIndex,
                instructions,
                activeWithDepths,
                slotLayout,
                activeScopes,
                instructionPcMap,
                activeInstructions,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                functionLiteralConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                ref maxStackDepth,
                out reason))
        {
            return false;
        }

        PatchDriverDescriptorBreakTarget(driverDescriptors, descriptorIndex, instructionPcMap[breakIndex]);
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendResolvedJump(
        int sourceInstructionIndex,
        int targetIndex,
        ImmutableArray<ExecutionInstruction> instructions,
        int[] activeWithDepths,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        Dictionary<int, int> instructionPcMap,
        HashSet<int> activeInstructions,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<FunctionLiteralDescriptor>.Builder functionLiteralConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ImmutableArray<UnifiedBytecodeTryDescriptor>.Builder tryDescriptors,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
        UnifiedBytecodeOpCode jumpOpCode,
        ref int maxStackDepth,
        out string reason)
    {
        if ((uint)targetIndex >= (uint)instructions.Length)
        {
            reason = "Instruction flow reached an invalid target index.";
            return false;
        }

        var jumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(jumpOpCode));

        if (activeInstructions.Contains(targetIndex))
        {
            if (IsSupportedActiveAbruptControlTarget(sourceInstructionIndex, targetIndex, instructions) &&
                instructionPcMap.TryGetValue(targetIndex, out var activeTargetProgramCounter))
            {
                PatchOperand(unified, jumpIndex, activeTargetProgramCounter);
                reason = string.Empty;
                return true;
            }

            if (IsSupportedActiveTryCompletionTarget(sourceInstructionIndex, targetIndex, instructions) &&
                instructionPcMap.TryGetValue(targetIndex, out var tryCompletionProgramCounter))
            {
                PatchOperand(unified, jumpIndex, tryCompletionProgramCounter);
                reason = string.Empty;
                return true;
            }

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
                activeWithDepths,
                slotLayout,
                activeScopes,
                instructionPcMap,
                activeInstructions,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                functionLiteralConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                ref maxStackDepth,
                out reason))
        {
            return false;
        }

        PatchOperand(unified, jumpIndex, instructionPcMap[targetIndex]);
        reason = string.Empty;
        return true;
    }

    private static bool IsSupportedActiveAbruptControlTarget(
        int sourceInstructionIndex,
        int targetIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        return (uint)sourceInstructionIndex < (uint)instructions.Length &&
               (instructions[sourceInstructionIndex] switch
               {
                   BreakInstruction breakInstruction => breakInstruction.TargetIndex == targetIndex,
                   ContinueInstruction continueInstruction => continueInstruction.TargetIndex == targetIndex,
                   _ => false
               });
    }

    private static bool IsSupportedActiveTryCompletionTarget(
        int sourceInstructionIndex,
        int targetIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        if ((uint)sourceInstructionIndex >= (uint)instructions.Length ||
            (uint)targetIndex >= (uint)instructions.Length ||
            instructions[targetIndex] is not BranchInstruction branch ||
            !HasLoopContinueTarget(targetIndex, branch.AlternateIndex, instructions))
        {
            return false;
        }

        return instructions[sourceInstructionIndex] switch
        {
            LeaveTryInstruction leaveTry => leaveTry.Next == targetIndex,
            EndFinallyInstruction endFinally => endFinally.Next == targetIndex,
            _ => false
        };
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

        if (instructions[targetIndex] is ForInMoveNextInstruction forInMoveNext)
        {
            return IsSupportedDriverLoopBackEdgeTarget(
                sourceInstructionIndex,
                targetIndex,
                forInMoveNext.Next,
                instructions);
        }

        if (instructions[targetIndex] is IteratorMoveNextInstruction iteratorMoveNext)
        {
            return IsSupportedDriverLoopBackEdgeTarget(
                sourceInstructionIndex,
                targetIndex,
                iteratorMoveNext.Next,
                instructions);
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

    private static bool IsSupportedDriverLoopBackEdgeTarget(
        int sourceInstructionIndex,
        int targetIndex,
        int bodyStartIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        if (sourceInstructionIndex == targetIndex)
        {
            return false;
        }

        if (instructions[sourceInstructionIndex] is ContinueInstruction { TargetIndex: var continueTargetIndex } &&
            continueTargetIndex == targetIndex)
        {
            return true;
        }

        // A per-iteration lexical head (for (const/let x in/of ...)) closes its environment with a
        // PopEnvironment immediately before looping back to the driver's MoveNext. That PopEnvironment
        // is a valid back-edge source: the canonical-body walk below still requires the body between
        // the MoveNext and this Pop to be linear, so no branching control flow is admitted.
        if (instructions[sourceInstructionIndex] is not AssignmentSlotInstruction and not
            CompoundAssignmentSlotInstruction and not JumpInstruction and not PopEnvironmentInstruction)
        {
            return false;
        }

        return TryIsLinearCanonicalWhileBody(bodyStartIndex, sourceInstructionIndex, instructions) &&
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
                // Per-iteration lexical heads (for (const/let x in/of ...)) open and close a fresh
                // binding environment inside the loop body. On the flat-slot production path these
                // resolve to slot-reset/no-op opcodes, so they are linear pass-through steps here.
                case PushEnvironmentInstruction pushEnvironment:
                    current = pushEnvironment.Next;
                    break;
                case PopEnvironmentInstruction popEnvironment:
                    current = popEnvironment.Next;
                    break;
                case ContinueInstruction continueInstruction
                    when continueInstruction.TargetIndex == endInstructionIndex:
                    current = continueInstruction.TargetIndex;
                    break;
                case JumpInstruction jumpInstruction
                    when jumpInstruction.TargetIndex == endInstructionIndex:
                    current = jumpInstruction.TargetIndex;
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
        // Labeled breakable regions are admitted: loop-control targets are compiler-owned
        // (ADR 0253), and a labeled break/continue resolves to a numeric target through the
        // same resolved-jump path as the unlabeled case. The driver-crossing safety check
        // (a labeled abrupt that exits an enclosing iterator/for-in/destructuring driver loop)
        // is enforced conservatively during production eligibility, not here.
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

    private static int AddDriverDescriptor(
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder descriptors,
        UnifiedBytecodeDriverDescriptor descriptor)
    {
        var index = descriptors.Count;
        descriptors.Add(descriptor);
        return index;
    }

    /// <summary>
    ///     Slice A (#2678): emits a <see cref="UnifiedBytecodeOpCode.TdzHeadInit" /> instruction that
    ///     marks the loop-head lexical bindings (for example <c>for (const x of ...)</c>) uninitialized
    ///     before the iterator/for-in source expression is evaluated, establishing the temporal dead
    ///     zone on the production VM path. Returns <see langword="true" /> (emitting nothing) when there
    ///     are no TDZ bindings. Declines when a head binding cannot be resolved to a flat activation
    ///     slot, so an incompletely modeled head environment is never admitted.
    /// </summary>
    private static bool TryEmitTdzHeadInit(
        ImmutableArray<Symbol> tdzBindings,
        bool tdzIsConst,
        int tdzScopeId,
        ImmutableArray<int> tdzSlotIndices,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
        out string reason)
    {
        if (tdzBindings.IsDefaultOrEmpty)
        {
            reason = string.Empty;
            return true;
        }

        var headSlots = ImmutableArray.CreateBuilder<int>(tdzBindings.Length);
        for (var i = 0; i < tdzBindings.Length; i++)
        {
            var binding = tdzBindings[i];
            if (TryResolveActivationSymbolSlot(binding, slotLayout, out var headSlot))
            {
                headSlots.Add(headSlot);
                continue;
            }

            // Per-iteration binding symbols (for (const/let x in/of ...)) live in the
            // per-iteration scope, not the activation scope. Resolve via FlatSlotMappings
            // using the per-iteration scope ID and the pre-resolved slot index.
            var slotIndex = !tdzSlotIndices.IsDefaultOrEmpty && i < tdzSlotIndices.Length
                ? tdzSlotIndices[i]
                : -1;
            if (tdzScopeId >= 0 && slotIndex >= 0 &&
                TryMapSlot(tdzScopeId, slotIndex, slotLayout.FlatSlotMappings, out headSlot))
            {
                headSlots.Add(headSlot);
                continue;
            }

            reason =
                $"Iterator/for-in driver TDZ head binding '{binding.Name}' could not be resolved to a flat activation slot.";
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.TdzHeadInit,
            AddDriverDescriptor(
                driverDescriptors,
                new UnifiedBytecodeDriverDescriptor(
                    StateSlot: -1,
                    TdzHeadSlots: headSlots.ToImmutable(),
                    TdzHeadIsConst: tdzIsConst))));

        reason = string.Empty;
        return true;
    }

    private static void PatchDriverDescriptorBreakTarget(
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder descriptors,
        int descriptorIndex,
        int breakTarget)
    {
        var descriptor = descriptors[descriptorIndex];
        descriptors[descriptorIndex] = descriptor with { BreakTarget = breakTarget };
    }

    private static bool TryAppendExpressionProgramOps(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<FunctionLiteralDescriptor>.Builder functionLiteralConstants,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
        if (TryAppendFirstBoundaryCallTargetPreparation(
                expressionProgram,
                slotLayout,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                functionLiteralConstants,
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

        if (TryAppendFirstBoundaryPropertyReadBinaryExpression(
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

        if (TryAppendFirstBoundaryPropertyReadShortCircuitExpression(
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

        var exprPcToUnifiedPc = new int[expressionProgram.OperationCount + 1];
        List<(int UnifiedIndex, int ExprTarget)>? patches = null;

        for (var exprPc = 0; exprPc < expressionProgram.OperationCount; exprPc++)
        {
            exprPcToUnifiedPc[exprPc] = unified.Count;
            var operation = expressionProgram.GetOperation(exprPc);
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadIdentifier:
                    if (operation.IsArguments)
                    {
                        reason = "arguments is not supported.";
                        return false;
                    }

                    var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (!TryResolveActivationSlot(identifier, slotLayout, out var slotIndex))
                    {
                        if (!allowsDynamicIdentifiers &&
                            !CanUseMaterializedActivationDynamicLookup(identifier, activationSlots))
                        {
                            reason =
                                $"Identifier '{identifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                            return false;
                        }

                        var identifierNameIndex = stringConstants.Count;
                        stringConstants.Add(identifier.Name.Name ?? string.Empty);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.LoadDynamicIdentifier,
                            identifierNameIndex));
                        break;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, slotIndex));
                    break;

                case ExpressionOpKind.ResolveIdentifierReference:
                    if (operation.IsArguments)
                    {
                        reason = "arguments assignment references are not supported.";
                        return false;
                    }

                    var referenceIdentifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (!allowsDynamicIdentifiers)
                    {
                        reason =
                            $"Identifier assignment reference '{referenceIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                        return false;
                    }

                    var referenceNameIndex = stringConstants.Count;
                    stringConstants.Add(referenceIdentifier.Name.Name ?? string.Empty);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference,
                        referenceNameIndex));
                    break;

                case ExpressionOpKind.LoadResolvedIdentifierValue:
                    if (!allowsDynamicIdentifiers)
                    {
                        reason =
                            "Dynamic identifier assignment references are not eligible outside an active with environment.";
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadDynamicIdentifierReference));
                    break;

                case ExpressionOpKind.StoreResolvedIdentifier:
                    var storeReferenceIdentifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (!allowsDynamicIdentifiers)
                    {
                        reason =
                            $"Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                        return false;
                    }

                    var storeReferenceNameIndex = stringConstants.Count;
                    stringConstants.Add(storeReferenceIdentifier.Name.Name ?? string.Empty);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.StoreDynamicIdentifierReference,
                        EncodeDynamicStoreOperand(storeReferenceNameIndex, operation)));
                    break;

                case ExpressionOpKind.PopResolvedIdentifierReference:
                    if (!allowsDynamicIdentifiers)
                    {
                        reason =
                            "Dynamic identifier assignment references are not eligible outside an active with environment.";
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.PopDynamicIdentifierReference));
                    break;

                case ExpressionOpKind.StoreIdentifier:
                    var storeIdentifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (!allowsDynamicIdentifiers)
                    {
                        reason =
                            $"Identifier '{storeIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                        return false;
                    }

                    var storeIdentifierNameIndex = stringConstants.Count;
                    stringConstants.Add(storeIdentifier.Name.Name ?? string.Empty);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.StoreDynamicIdentifier,
                        EncodeDynamicStoreOperand(storeIdentifierNameIndex, operation)));
                    break;

                case ExpressionOpKind.LoadThis:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadThis));
                    break;

                case ExpressionOpKind.GetNamedProperty:
                    if (operation.IsOptional || operation.ShortCircuitOnNullishTarget)
                    {
                        reason = "Optional named property reads are not supported in the general expression loop.";
                        return false;
                    }

                    if (operation.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
                    {
                        reason = "Private named property reads are not supported in the general expression loop.";
                        return false;
                    }

                    var namedPropNameIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, namedPropNameIndex));
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
                    if (!TryResolveTypeOfIdentifierSlot(operation, expressionProgram, slotLayout, out var typeOfSlot, out reason))
                    {
                        if (operation.IsArguments)
                        {
                            return false;
                        }

                        var typeOfIdentifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                        if (!allowsDynamicIdentifiers)
                        {
                            reason =
                                $"typeof identifier '{typeOfIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                            return false;
                        }

                        var typeOfNameIndex = stringConstants.Count;
                        stringConstants.Add(typeOfIdentifier.Name.Name ?? string.Empty);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.TypeOfDynamicIdentifier,
                            typeOfNameIndex));
                        break;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.TypeOfIdentifier, typeOfSlot));
                    break;

                case ExpressionOpKind.DeleteIdentifier:
                    if (operation.IsArguments)
                    {
                        reason = "arguments delete is not supported.";
                        return false;
                    }

                    var deleteIdentifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (!allowsDynamicIdentifiers)
                    {
                        reason =
                            $"delete identifier '{deleteIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                        return false;
                    }

                    var deleteNameIndex = stringConstants.Count;
                    stringConstants.Add(deleteIdentifier.Name.Name ?? string.Empty);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.DeleteDynamicIdentifier,
                        deleteNameIndex));
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

                case ExpressionOpKind.UpdateIdentifier:
                    if (operation.IsArguments)
                    {
                        reason = "arguments update is not supported.";
                        return false;
                    }

                    var updateIdentifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (!allowsDynamicIdentifiers)
                    {
                        reason =
                            $"Update target '{updateIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                        return false;
                    }

                    var updateNameIndex = stringConstants.Count;
                    stringConstants.Add(updateIdentifier.Name.Name ?? string.Empty);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.UpdateDynamicIdentifier,
                        EncodeUpdateOperand(updateNameIndex, operation)));
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

                case ExpressionOpKind.ArraySpread:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ArraySpread));
                    break;

                case ExpressionOpKind.CreateObject:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.CreateObject));
                    break;

                case ExpressionOpKind.DefineObjectProperty:
                    var propertyNameIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.DefineObjectProperty,
                        EncodeDefineObjectPropertyOperand(propertyNameIndex, operation)));
                    break;

                case ExpressionOpKind.DefineComputedObjectProperty:
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.DefineComputedObjectProperty,
                        operation.AllowNameInference ? DefineObjectPropertyAllowNameInferenceFlag : 0));
                    break;

                case ExpressionOpKind.Construct:
                    // Synchronous non-spread construct calls (`new F(...)`, gh2690). The
                    // constructor value and each simple-operand argument are lowered by their
                    // own preceding ops in source order; this boundary opcode pops them and
                    // invokes [[Construct]] with the constructor as new.target. Spread-onto-
                    // construct is declined by eligibility, so guard defensively here too.
                    if (operation.SpreadMaskConstantIndex >= 0)
                    {
                        reason = "Spread construct arguments are outside the construct invocation boundary.";
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.ConstructInvocationBoundary,
                        operation.ArgumentCount));
                    break;

                case ExpressionOpKind.LoadFunctionLiteral:
                    var functionLiteralIndex = functionLiteralConstants.Count;
                    functionLiteralConstants.Add(
                        operation.GetObject<FunctionLiteralDescriptor>(expressionProgram.ObjectConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.LoadFunctionLiteral,
                        EncodeLoadFunctionLiteralOperand(functionLiteralIndex, operation.IsConstructorFunction)));
                    break;

                case ExpressionOpKind.JumpIfFalse:
                    patches ??= [];
                    patches.Add((unified.Count, operation.Target));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfShortCircuitFalse, 0));
                    break;

                case ExpressionOpKind.JumpIfTrue:
                    patches ??= [];
                    patches.Add((unified.Count, operation.Target));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfShortCircuitTrue, 0));
                    break;

                case ExpressionOpKind.JumpIfNotNullish:
                    patches ??= [];
                    patches.Add((unified.Count, operation.Target));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish, 0));
                    break;

                default:
                    reason = $"Unsupported expression op '{operation.Kind}'.";
                    return false;
            }
        }

        exprPcToUnifiedPc[expressionProgram.OperationCount] = unified.Count;

        if (patches is not null)
        {
            foreach (var (unifiedIndex, exprTarget) in patches)
            {
                unified[unifiedIndex] = new UnifiedBytecodeInstruction(
                    unified[unifiedIndex].OpCode,
                    exprPcToUnifiedPc[exprTarget]);
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryCallTargetPreparation(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<FunctionLiteralDescriptor>.Builder functionLiteralConstants,
        out string reason)
    {
        reason = string.Empty;
        if (expressionProgram.OperationCount < 2)
        {
            return false;
        }

        var lastOp = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);

        // Standard path: last op is Call (covers all non-optional and receiver-optional calls).
        if (lastOp.Kind == ExpressionOpKind.Call)
        {
            var call = lastOp;
            if (!call.HasExplicitThis)
            {
                reason = "Only direct identifier and member calls with explicit receiver records are supported.";
                return false;
            }

            // Synchronous spread calls are admitted (gh2676); spread flattening happens
            // at the invocation boundary using the registered spread mask. Direct eval
            // stays out of scope.
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
                    allowsDynamicIdentifiers,
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
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    call,
                    expressionProgram.OperationCount - 1,
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
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    call,
                    expressionProgram.OperationCount - 1,
                    out reason))
            {
                return true;
            }
        }
        else if (lastOp.Kind == ExpressionOpKind.Pop)
        {
            // Callee-optional path: trailing structure is ..., Call, Jump, SwapTopTwo, Pop.
            // The Call is at OperationCount - 4.
            var callIndex = expressionProgram.OperationCount - 4;
            if (callIndex < 1)
            {
                reason = string.Empty;
                return false;
            }

            var call = expressionProgram.GetOperation(callIndex);
            if (call.Kind != ExpressionOpKind.Call || !call.HasExplicitThis || call.IsDirectEval)
            {
                reason = string.Empty;
                return false;
            }

            if (TryAppendNamedMemberCallTargetPreparation(
                    expressionProgram,
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    call,
                    callIndex,
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
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    call,
                    callIndex,
                    out reason))
            {
                return true;
            }
        }
        else
        {
            // Last op is neither Call nor Pop — not a call-target preparation candidate.
            return false;
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
        bool allowsDynamicIdentifiers,
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
            if (!allowsDynamicIdentifiers &&
                !CanUseMaterializedActivationDynamicLookup(identifier, activationSlots))
            {
                reason =
                    $"Identifier call target '{identifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                return false;
            }

            var dynamicNameIndex = stringConstants.Count;
            stringConstants.Add(identifier.Name.Name ?? string.Empty);
            unified.Add(new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget,
                dynamicNameIndex));

            return TryAppendCallArguments(
                expressionProgram,
                slotLayout,
                unified,
                literalConstants,
                stringConstants,
                argsStartIndex: 1,
                call,
                out reason);
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
            slotLayout,
            unified,
            literalConstants,
            stringConstants,
            argsStartIndex: 1,
            call,
            out reason);
    }

    private static bool TryAppendNamedMemberCallTargetPreparation(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        PackedExpressionOp call,
        int callIndex,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
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

        // Case 1: receiver-optional named call — box?.read(args)
        // Pattern: [Receiver..., JumpIfNullish, LoadNamedCallTarget, args..., Call]
        if (callTargetIndexInProgram >= 2)
        {
            var maybeReceiverJump = expressionProgram.GetOperation(callTargetIndexInProgram - 1);
            if (maybeReceiverJump is { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true })
            {
                return TryAppendReceiverOptionalNamedCallTarget(
                    expressionProgram,
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    call,
                    callIndex,
                    callTargetIndexInProgram,
                    out reason);
            }
        }

        // Case 2: callee-optional named call — box.read?.()
        // Pattern: [Receiver..., LoadNamedCallTarget, JumpIfNullish, args..., Call, Jump, SwapTopTwo, Pop]
        if (callTargetIndexInProgram + 1 < callIndex)
        {
            var maybeCalleeJump = expressionProgram.GetOperation(callTargetIndexInProgram + 1);
            if (maybeCalleeJump is { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true } &&
                callIndex == expressionProgram.OperationCount - 4)
            {
                return TryAppendCalleeOptionalNamedCallTarget(
                    expressionProgram,
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    call,
                    callIndex,
                    callTargetIndexInProgram,
                    out reason);
            }
        }

        if (!TryAppendNamedReceiverOperations(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                callTargetIndexInProgram,
                allowDeepChain: true,
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
            slotLayout,
            unified,
            literalConstants,
            stringConstants,
            callTargetIndexInProgram + 1,
            call,
            callIndex,
            out reason);
    }

    private static bool TryAppendReceiverOptionalNamedCallTarget(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        PackedExpressionOp call,
        int callIndex,
        int callTargetIndexInProgram,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
        // Receiver chain ends just before the JumpIfNullish.
        var receiverEnd = callTargetIndexInProgram - 1;
        if (!TryAppendNamedReceiverOperations(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                receiverEnd,
                allowDeepChain: true,
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
            NameConstantIndex: nameIndex,
            IsOptionalReceiverCheck: true));

        // Operand packs: lower 16 bits = callTargetConstantIndex, upper 16 bits = jump target PC.
        // Backpatch: record the index, emit placeholder, then fix after arguments are compiled
        // so that multi-op literal arguments (gh2705) are counted correctly.
        var prepareIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget,
            callTargetConstantIndex));

        if (!TryAppendCallArguments(
                expressionProgram,
                slotLayout,
                unified,
                literalConstants,
                stringConstants,
                callTargetIndexInProgram + 1,
                call,
                callIndex,
                out reason))
        {
            return false;
        }

        unified[prepareIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget,
            callTargetConstantIndex | (unified.Count << 16));
        return true;
    }

    private static bool TryAppendCalleeOptionalNamedCallTarget(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        PackedExpressionOp call,
        int callIndex,
        int callTargetIndexInProgram,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
        if (!TryAppendNamedReceiverOperations(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                callTargetIndexInProgram,
                allowDeepChain: true,
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
            NameConstantIndex: nameIndex,
            IsOptionalReceiverCheck: false));

        var prepareIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget,
            callTargetConstantIndex));

        // Args start after the JumpIfNullish (at callTargetIndexInProgram + 2).
        if (!TryAppendCallArguments(
                expressionProgram,
                slotLayout,
                unified,
                literalConstants,
                stringConstants,
                callTargetIndexInProgram + 2,
                call,
                callIndex,
                out reason))
        {
            return false;
        }

        unified[prepareIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget,
            callTargetConstantIndex | (unified.Count << 16));
        return true;
    }

    private static bool TryAppendComputedMemberCallTargetPreparation(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        PackedExpressionOp call,
        int callIndex,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
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

        // Case 3: callee-optional computed call — box[key]?.()
        // Pattern: [Receiver, Key, LoadComputedCallTarget, JumpIfNullish, args..., Call, Jump, SwapTopTwo, Pop]
        if (callTargetIndexInProgram + 1 < callIndex &&
            callIndex == expressionProgram.OperationCount - 4)
        {
            var maybeCalleeJump = expressionProgram.GetOperation(callTargetIndexInProgram + 1);
            if (maybeCalleeJump is { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true })
            {
                return TryAppendCalleeOptionalComputedCallTarget(
                    expressionProgram,
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    call,
                    callIndex,
                    callTargetIndexInProgram,
                    out reason);
            }
        }

        var keyIndex = callTargetIndexInProgram - 1;
        if (!TryAppendNamedReceiverOperations(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                keyIndex,
                allowDeepChain: false,
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
            slotLayout,
            unified,
            literalConstants,
            stringConstants,
            callTargetIndexInProgram + 1,
            call,
            callIndex,
            out reason);
    }

    private static bool TryAppendCalleeOptionalComputedCallTarget(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        PackedExpressionOp call,
        int callIndex,
        int callTargetIndexInProgram,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
        var keyIndex = callTargetIndexInProgram - 1;

        if (!TryAppendNamedReceiverOperations(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                keyIndex,
                allowDeepChain: false,
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
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(
            UnifiedBytecodeCallTargetKind.ComputedMember,
            IsOptionalReceiverCheck: false));

        var prepareIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget,
            callTargetConstantIndex));

        if (!TryAppendCallArguments(
                expressionProgram,
                slotLayout,
                unified,
                literalConstants,
                stringConstants,
                callTargetIndexInProgram + 2,
                call,
                callIndex,
                out reason))
        {
            return false;
        }

        unified[prepareIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget,
            callTargetConstantIndex | (unified.Count << 16));
        return true;
    }

    private static bool TryAppendNamedReceiverOperations(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants,
        int endExclusive,
        bool allowDeepChain,
        out string reason)
    {
        if (endExclusive < 1 || (!allowDeepChain && endExclusive > 3))
        {
            reason = "Member call receiver is outside the direct named-chain boundary.";
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
        UnifiedBytecodeSlotLayout slotLayout,
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
        if (!TryResolveActivationSlot(identifier, slotLayout, out slotIndex))
        {
            reason = $"Unsupported typeof identifier '{identifier.Name.Name}'.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryAppendCallArguments(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        int argsStartIndex,
        PackedExpressionOp call,
        out string reason)
    {
        return TryAppendCallArguments(
            expressionProgram,
            slotLayout,
            unified,
            literalConstants,
            stringConstants,
            argsStartIndex,
            call,
            expressionProgram.OperationCount - 1,
            out reason);
    }

    private static bool TryAppendCallArguments(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        int argsStartIndex,
        PackedExpressionOp call,
        int callIndex,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;

        // Span-walk: each logical argument is a single simple operand or a multi-op
        // array/object literal span. Validate argument count via span-walk (gh2705).
        var argCount = 0;
        var operationIndex = argsStartIndex;
        while (operationIndex < callIndex)
        {
            var op = expressionProgram.GetOperation(operationIndex);
            if (op.Kind == ExpressionOpKind.CreateArray)
            {
                if (!TryAppendSimpleArrayLiteralSpan(
                        expressionProgram, operationIndex, activationSlots,
                        unified, literalConstants, out var arraySpanLen, out reason))
                {
                    return false;
                }

                operationIndex += arraySpanLen;
            }
            else if (op.Kind == ExpressionOpKind.CreateObject)
            {
                if (!TryAppendSimpleObjectLiteralSpan(
                        expressionProgram, operationIndex, activationSlots,
                        unified, literalConstants, stringConstants, out var objSpanLen, out reason))
                {
                    return false;
                }

                operationIndex += objSpanLen;
            }
            else if (op.Kind == ExpressionOpKind.LoadLiteral)
            {
                // A LoadLiteral may be the seed of a multi-op template literal span.
                // TryAppendSimpleTemplateLiteralSpan always emits at least the seed and returns spanLength >= 1.
                if (!TryAppendSimpleTemplateLiteralSpan(
                        expressionProgram, operationIndex, activationSlots,
                        unified, literalConstants, out var templateSpanLen, out reason))
                {
                    return false;
                }

                operationIndex += templateSpanLen;
            }
            else
            {
                // Spread arguments push the iterable value; flattening happens at the
                // invocation boundary using the registered spread mask (gh2676).
                if (!TryAppendSimpleOperandLoad(op, expressionProgram, activationSlots, unified, literalConstants, out reason))
                {
                    return false;
                }

                operationIndex++;
            }

            argCount++;
        }

        if (argCount != call.ArgumentCount)
        {
            reason = "Logical argument count does not match call.ArgumentCount in the call-target preparation boundary.";
            return false;
        }

        var spreadIndices = call.GetSpreadIndices(expressionProgram.SpreadMaskConstants.AsSpan());
        var spreadMaskIndex = spreadIndices.IsDefaultOrEmpty
            ? -1
            : slotLayout.RegisterSpreadMask(spreadIndices);

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.CallInvocationBoundary,
            EncodeCallBoundaryOperand(call.ArgumentCount, spreadMaskIndex)));
        reason = string.Empty;
        return true;
    }

    // Compiles a simple array literal span starting at startIndex in the expression program.
    // Emits: CreateArray, then N elements where each element is one of:
    //   - [simple-operand-load, ArrayPush]   — normal element
    //   - [simple-operand-load, ArraySpread] — spread element
    //   - ArrayPushHole                      — hole element
    private static bool TryAppendSimpleArrayLiteralSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        out int spanLength,
        out string reason)
    {
        if (expressionProgram.GetOperation(startIndex).Kind != ExpressionOpKind.CreateArray)
        {
            spanLength = 0;
            reason = $"Expected CreateArray at index {startIndex}.";
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.CreateArray));

        var i = startIndex + 1;
        while (i < expressionProgram.OperationCount)
        {
            var elementOp = expressionProgram.GetOperation(i);

            if (elementOp.Kind == ExpressionOpKind.ArrayPushHole)
            {
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ArrayPushHole));
                i++;
                continue;
            }

            if (!TryAppendSimpleOperandLoad(elementOp, expressionProgram, activationSlots, unified, literalConstants, out reason))
            {
                // Non-simple op — element scan is done; the array literal ends here.
                // Undo the failed load (TryAppendSimpleOperandLoad adds nothing on failure).
                break;
            }

            i++;
            if (i >= expressionProgram.OperationCount)
            {
                spanLength = 0;
                reason = "Expected ArrayPush or ArraySpread after element.";
                return false;
            }

            var pushOp = expressionProgram.GetOperation(i);
            if (pushOp.Kind == ExpressionOpKind.ArrayPush)
            {
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ArrayPush));
            }
            else if (pushOp.Kind == ExpressionOpKind.ArraySpread)
            {
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ArraySpread));
            }
            else
            {
                spanLength = 0;
                reason = "Expected ArrayPush or ArraySpread after array element operand.";
                return false;
            }

            i++;
        }

        spanLength = i - startIndex;
        reason = string.Empty;
        return true;
    }

    // Compiles a simple object literal span starting at startIndex in the expression program.
    // Emits: CreateObject, then N property triples:
    //   Static:   [simple-value-load, DefineObjectProperty(non-private, no name inference)]
    //   Computed: [simple-key-load, ResolvePropertyKey, simple-value-load, DefineComputedObjectProperty(no name inference)]
    private static bool TryAppendSimpleObjectLiteralSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out int spanLength,
        out string reason)
    {
        if (expressionProgram.GetOperation(startIndex).Kind != ExpressionOpKind.CreateObject)
        {
            spanLength = 0;
            reason = $"Expected CreateObject at index {startIndex}.";
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.CreateObject));

        var exprStringConstants = expressionProgram.StringConstants.AsSpan();
        var i = startIndex + 1;
        while (i < expressionProgram.OperationCount)
        {
            var firstOp = expressionProgram.GetOperation(i);
            if (!TryAppendSimpleOperandLoad(firstOp, expressionProgram, activationSlots, unified, literalConstants, out reason))
            {
                // Non-simple first op — property scan is done; the object literal ends here.
                break;
            }

            i++;
            if (i >= expressionProgram.OperationCount)
            {
                spanLength = 0;
                reason = "Expected DefineObjectProperty or value operand after first operand.";
                return false;
            }

            var secondOp = expressionProgram.GetOperation(i);
            if (secondOp.Kind == ExpressionOpKind.DefineObjectProperty)
            {
                // Static property: firstOp = value (already loaded), secondOp = DefineObjectProperty.
                if (secondOp.GetString(exprStringConstants).IsPrivateName() || secondOp.AllowNameInference)
                {
                    spanLength = 0;
                    reason = "Private names and name inference are not admitted in simple object literals.";
                    return false;
                }

                var propertyNameIndex = stringConstants.Count;
                stringConstants.Add(secondOp.GetString(exprStringConstants));
                unified.Add(new UnifiedBytecodeInstruction(
                    UnifiedBytecodeOpCode.DefineObjectProperty,
                    EncodeDefineObjectPropertyOperand(propertyNameIndex, secondOp)));
                i++;
            }
            else if (secondOp.Kind == ExpressionOpKind.ResolvePropertyKey)
            {
                // Computed property: firstOp = key (already loaded), secondOp = ResolvePropertyKey; load value then define.
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
                i++;
                if (i >= expressionProgram.OperationCount)
                {
                    spanLength = 0;
                    reason = "Expected value operand after ResolvePropertyKey.";
                    return false;
                }

                var valueOp = expressionProgram.GetOperation(i);
                if (!TryAppendSimpleOperandLoad(valueOp, expressionProgram, activationSlots, unified, literalConstants, out reason))
                {
                    spanLength = 0;
                    reason = "Complex value expressions are not admitted in simple computed object properties.";
                    return false;
                }

                i++;
                if (i >= expressionProgram.OperationCount)
                {
                    spanLength = 0;
                    reason = "Expected DefineComputedObjectProperty after key, ResolvePropertyKey, and value.";
                    return false;
                }

                var computedDefineOp = expressionProgram.GetOperation(i);
                if (computedDefineOp.Kind != ExpressionOpKind.DefineComputedObjectProperty ||
                    computedDefineOp.AllowNameInference)
                {
                    spanLength = 0;
                    reason = "Complex computed keys and name-inferred computed properties are not admitted in simple object literals.";
                    return false;
                }

                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DefineComputedObjectProperty, 0));
                i++;
            }
            else
            {
                spanLength = 0;
                reason = "Computed keys, private names, and name inference are not admitted in simple object literals.";
                return false;
            }
        }

        spanLength = i - startIndex;
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
        // Shape: [base, DuplicateTop, GetNamedProperty, rhs..., Binary, SetNamedProperty]
        // Minimum: 6 ops (rhs is a single simple operand).
        if (expressionProgram.OperationCount < 6)
        {
            reason = string.Empty;
            return false;
        }

        var duplicateTarget = expressionProgram.GetOperation(1);
        var propertyRead = expressionProgram.GetOperation(2);
        var binary = expressionProgram.GetOperation(expressionProgram.OperationCount - 2);
        var propertySet = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);
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

        var rhsStart = 3;
        var rhsEnd = expressionProgram.OperationCount - 3;

        if (rhsStart == rhsEnd)
        {
            if (!TryAppendSimpleOperandLoad(
                    expressionProgram.GetOperation(rhsStart),
                    expressionProgram,
                    activationSlots,
                    unified,
                    literalConstants,
                    out reason))
            {
                return false;
            }
        }
        else
        {
            var rhsOp = expressionProgram.GetOperation(rhsStart);
            if (rhsOp.Kind != ExpressionOpKind.LoadLiteral)
            {
                reason = string.Empty;
                return false;
            }

            if (!TryAppendSimpleTemplateLiteralSpan(
                    expressionProgram, rhsStart, activationSlots,
                    unified, literalConstants, out var spanLen, out reason))
            {
                return false;
            }

            if (rhsStart + spanLen - 1 != rhsEnd)
            {
                reason = "Template literal RHS span does not match expected boundary.";
                return false;
            }
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
        if (expressionProgram.OperationCount < 3)
        {
            reason = string.Empty;
            return false;
        }

        var propertySet = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);
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

        var rhsStart = 1;
        var rhsEnd = expressionProgram.OperationCount - 2;

        if (rhsStart == rhsEnd)
        {
            if (!TryAppendSimpleOperandLoad(
                    expressionProgram.GetOperation(rhsStart),
                    expressionProgram,
                    activationSlots,
                    unified,
                    literalConstants,
                    out reason))
            {
                return false;
            }
        }
        else
        {
            var rhsOp = expressionProgram.GetOperation(rhsStart);
            if (rhsOp.Kind != ExpressionOpKind.LoadLiteral)
            {
                reason = string.Empty;
                return false;
            }

            if (!TryAppendSimpleTemplateLiteralSpan(
                    expressionProgram, rhsStart, activationSlots,
                    unified, literalConstants, out var spanLen, out reason))
            {
                return false;
            }

            if (rhsStart + spanLen - 1 != rhsEnd)
            {
                reason = "Template literal RHS span does not match expected boundary.";
                return false;
            }
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
        if (expressionProgram.OperationCount < 2)
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

    private static bool TryAppendFirstBoundaryPropertyReadBinaryExpression(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        // Handles: [ActivationResolvedValue, GetNamedProperty+, RHS, ProductionBinary]
        // RHS may be a single simple operand or a simple array/object literal span (gh2705).
        // Validate the entire shape before emitting anything (mirrors the eligibility checker).
        if (expressionProgram.OperationCount < 4)
        {
            reason = string.Empty;
            return false;
        }

        var lastOp = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);
        if (lastOp.Kind != ExpressionOpKind.Binary || !IsSupportedBinaryOperator(lastOp.Operator))
        {
            reason = string.Empty;
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();

        // Validation pass — find the GetNamedProperty chain end and the RHS start.
        var rhsStart = -1;
        for (var i = 1; i < expressionProgram.OperationCount - 1; i++)
        {
            var op = expressionProgram.GetOperation(i);
            if (op.Kind == ExpressionOpKind.GetNamedProperty &&
                !op.GetString(expressionStringConstants).IsPrivateName() &&
                !op.IsOptional &&
                !op.ShortCircuitOnNullishTarget)
            {
                continue;
            }

            if (i < 2)
            {
                reason = string.Empty;
                return false;
            }

            rhsStart = i;
            break;
        }

        if (rhsStart < 0)
        {
            reason = string.Empty;
            return false;
        }

        var rhsEnd = expressionProgram.OperationCount - 2;
        if (rhsStart > rhsEnd)
        {
            reason = string.Empty;
            return false;
        }

        // Validate base (read-only, mirrors TryAppendActivationValueLoad without emitting).
        var baseOp = expressionProgram.GetOperation(0);
        if (baseOp.Kind != ExpressionOpKind.LoadThis && baseOp.Kind != ExpressionOpKind.LoadNewTarget)
        {
            if (baseOp.Kind != ExpressionOpKind.LoadIdentifier || baseOp.IsArguments)
            {
                reason = string.Empty;
                return false;
            }

            var baseIdentifier = baseOp.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
            if (!TryResolveActivationSlot(baseIdentifier, activationSlots, out _))
            {
                reason = string.Empty;
                return false;
            }
        }

        // Emission pass — only reached when all validation passes.
        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        for (var i = 1; i < rhsStart; i++)
        {
            var propertyRead = expressionProgram.GetOperation(i);
            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(propertyRead.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
        }

        if (rhsStart == rhsEnd)
        {
            // Single-op RHS.
            if (!TryAppendSimpleOperandLoad(
                    expressionProgram.GetOperation(rhsStart),
                    expressionProgram,
                    activationSlots,
                    unified,
                    literalConstants,
                    out reason))
            {
                return false;
            }
        }
        else
        {
            // Multi-op RHS — simple array or object literal span.
            var rhsOp = expressionProgram.GetOperation(rhsStart);
            if (rhsOp.Kind == ExpressionOpKind.CreateArray)
            {
                if (!TryAppendSimpleArrayLiteralSpan(
                        expressionProgram, rhsStart, activationSlots, unified, literalConstants,
                        out var arraySpanLen, out reason) ||
                    rhsStart + arraySpanLen - 1 != rhsEnd)
                {
                    reason = reason.Length == 0 ? "Array literal RHS span does not match expected boundary." : reason;
                    return false;
                }
            }
            else if (rhsOp.Kind == ExpressionOpKind.CreateObject)
            {
                if (!TryAppendSimpleObjectLiteralSpan(
                        expressionProgram, rhsStart, activationSlots, unified, literalConstants, stringConstants,
                        out var objSpanLen, out reason) ||
                    rhsStart + objSpanLen - 1 != rhsEnd)
                {
                    reason = reason.Length == 0 ? "Object literal RHS span does not match expected boundary." : reason;
                    return false;
                }
            }
            else if (rhsOp.Kind == ExpressionOpKind.LoadLiteral)
            {
                if (!TryAppendSimpleTemplateLiteralSpan(
                        expressionProgram, rhsStart, activationSlots, unified, literalConstants,
                        out var templateSpanLen, out reason) ||
                    rhsStart + templateSpanLen - 1 != rhsEnd)
                {
                    reason = reason.Length == 0 ? "Template literal RHS span does not match expected boundary." : reason;
                    return false;
                }
            }
            else
            {
                reason = string.Empty;
                return false;
            }
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)lastOp.Operator));
        reason = string.Empty;
        return true;
    }

    // Handles: [ActivationResolvedValue, GetNamedProperty+, JumpIfFalse|JumpIfTrue|JumpIfNotNullish, Pop, simple-rhs]
    // Mirrors TryIsFirstBoundaryPropertyReadShortCircuitExpressionCandidate in the eligibility checker.
    private static bool TryAppendFirstBoundaryPropertyReadShortCircuitExpression(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 5)
        {
            reason = string.Empty;
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();

        var shortCircuitStart = -1;
        for (var i = 1; i < expressionProgram.OperationCount - 1; i++)
        {
            var op = expressionProgram.GetOperation(i);
            if (op.Kind == ExpressionOpKind.GetNamedProperty &&
                !op.GetString(expressionStringConstants).IsPrivateName() &&
                !op.IsOptional &&
                !op.ShortCircuitOnNullishTarget)
            {
                continue;
            }

            if (i < 2)
            {
                reason = string.Empty;
                return false;
            }

            shortCircuitStart = i;
            break;
        }

        if (shortCircuitStart < 0)
        {
            reason = string.Empty;
            return false;
        }

        var jumpOp = expressionProgram.GetOperation(shortCircuitStart);
        if (jumpOp.Kind is not (ExpressionOpKind.JumpIfFalse or ExpressionOpKind.JumpIfTrue or ExpressionOpKind.JumpIfNotNullish))
        {
            reason = string.Empty;
            return false;
        }

        var popIndex = shortCircuitStart + 1;
        var rhsStart = shortCircuitStart + 2;

        if (rhsStart >= expressionProgram.OperationCount ||
            expressionProgram.GetOperation(popIndex).Kind != ExpressionOpKind.Pop ||
            jumpOp.Target != expressionProgram.OperationCount ||
            rhsStart != expressionProgram.OperationCount - 1)
        {
            reason = string.Empty;
            return false;
        }

        var baseOp = expressionProgram.GetOperation(0);
        if (!TryAppendActivationValueLoad(baseOp, expressionProgram, activationSlots, unified, out reason))
        {
            return false;
        }

        for (var i = 1; i < shortCircuitStart; i++)
        {
            var propertyRead = expressionProgram.GetOperation(i);
            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(propertyRead.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
        }

        var jumpOpCode = jumpOp.Kind switch
        {
            ExpressionOpKind.JumpIfFalse => UnifiedBytecodeOpCode.JumpIfShortCircuitFalse,
            ExpressionOpKind.JumpIfTrue => UnifiedBytecodeOpCode.JumpIfShortCircuitTrue,
            _ => UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish
        };

        var jumpUnifiedIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(jumpOpCode, 0));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));

        if (!TryAppendSimpleOperandLoad(
                expressionProgram.GetOperation(rhsStart),
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return false;
        }

        unified[jumpUnifiedIndex] = new UnifiedBytecodeInstruction(jumpOpCode, unified.Count);
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

    // Compiles a simple untagged template literal span starting at startIndex.
    // Shape: LoadLiteral (seed), then any number of text parts (LoadLiteral, Binary(Add))
    // or substitution parts (simple-operand, ToString, Binary(Add)).
    // Returns spanLength=1 when only the seed was emitted (standalone literal — no template cycles).
    // Returns false only if startIndex does not point to LoadLiteral.
    private static bool TryAppendSimpleTemplateLiteralSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        out int spanLength,
        out string reason)
    {
        if (expressionProgram.GetOperation(startIndex).Kind != ExpressionOpKind.LoadLiteral)
        {
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        var seedLiteral = expressionProgram.GetOperation(startIndex).GetLiteral(expressionProgram.LiteralConstants.AsSpan());
        var seedIndex = literalConstants.Count;
        literalConstants.Add(seedLiteral);
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, seedIndex));

        var i = startIndex + 1;
        while (i < expressionProgram.OperationCount)
        {
            var op = expressionProgram.GetOperation(i);

            // Text part: LoadLiteral followed by Binary(Add)
            if (op.Kind == ExpressionOpKind.LoadLiteral)
            {
                if (i + 1 >= expressionProgram.OperationCount)
                    break;
                var next = expressionProgram.GetOperation(i + 1);
                if (next.Kind != ExpressionOpKind.Binary || next.Operator != BinaryOperator.Add)
                    break;

                var textLiteral = op.GetLiteral(expressionProgram.LiteralConstants.AsSpan());
                var textIndex = literalConstants.Count;
                literalConstants.Add(textLiteral);
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, textIndex));
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)BinaryOperator.Add));
                i += 2;
                continue;
            }

            // Substitution part: simple-operand, ToString, Binary(Add)
            if (i + 2 < expressionProgram.OperationCount)
            {
                var toString = expressionProgram.GetOperation(i + 1);
                var add = expressionProgram.GetOperation(i + 2);
                if (toString.Kind == ExpressionOpKind.ToString &&
                    add.Kind == ExpressionOpKind.Binary && add.Operator == BinaryOperator.Add)
                {
                    if (TryAppendSimpleOperandLoad(op, expressionProgram, activationSlots, unified, literalConstants, out _))
                    {
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ToString));
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)BinaryOperator.Add));
                        i += 3;
                        continue;
                    }
                }
            }

            break;
        }

        spanLength = i - startIndex;
        reason = string.Empty;
        return true;
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

            case ExpressionOpKind.LoadThis:
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadThis));
                reason = string.Empty;
                return true;

            case ExpressionOpKind.LoadNewTarget:
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadNewTarget));
                reason = string.Empty;
                return true;

            default:
                reason = $"Unsupported simple operand op '{operation.Kind}'.";
                return false;
        }
    }

    private static int EncodeUpdateOperand(int stringConstantIndex, PackedExpressionOp update) =>
        (stringConstantIndex << 2) | EncodeUpdateFlags(update);

    private static int EncodeUpdateOperand(int stringConstantIndex, bool isIncrement, bool isPrefix)
    {
        var flags = 0;
        if (isIncrement)
        {
            flags |= UpdateIncrementFlag;
        }

        if (isPrefix)
        {
            flags |= UpdatePrefixFlag;
        }

        return (stringConstantIndex << 2) | flags;
    }

    private static int EncodeDynamicStoreOperand(int stringConstantIndex, PackedExpressionOp store)
    {
        var flags = store.AllowNameInference ? DynamicStoreAllowNameInferenceFlag : 0;
        return (stringConstantIndex << 1) | flags;
    }

    private static int EncodeDynamicStoreOperand(int stringConstantIndex, bool allowNameInference)
    {
        var flags = allowNameInference ? DynamicStoreAllowNameInferenceFlag : 0;
        return (stringConstantIndex << 1) | flags;
    }

    private static bool TryAppendDynamicVarDeclaration(
        SimpleVariableDeclarationInstruction declaration,
        ExpressionProgram initializerProgram,
        bool allowsDynamicIdentifiers,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        ImmutableArray<FunctionLiteralDescriptor>.Builder functionLiteralConstants,
        out string reason)
    {
        if (declaration.VarKind != VariableKind.Var ||
            declaration.TargetSymbol is not { } targetSymbol ||
            !slotLayout.ActivationSlots.MaterializedBindingNames.Contains(targetSymbol))
        {
            reason = $"Unsupported declaration target '{declaration.TargetSymbol?.Name}'.";
            return false;
        }

        var targetNameIndex = stringConstants.Count;
        stringConstants.Add(targetSymbol.Name);
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DeclareDynamicVar, targetNameIndex));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference, targetNameIndex));
        if (!TryAppendExpressionProgramOps(
                initializerProgram,
                slotLayout,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                functionLiteralConstants,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.StoreDynamicIdentifierReference,
            EncodeDynamicStoreOperand(targetNameIndex, declaration.AllowNameInference)));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
        reason = string.Empty;
        return true;
    }

    private static void AppendDynamicStoreInstruction(
        Symbol targetSymbol,
        bool allowNameInference,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants)
    {
        var targetNameIndex = stringConstants.Count;
        stringConstants.Add(targetSymbol.Name);
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.StoreDynamicIdentifier,
            EncodeDynamicStoreOperand(targetNameIndex, allowNameInference)));
    }

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
            reason = $"Unsupported identifier '{identifier.Name.Name}' at dynamic property-read boundary.";
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
            BinaryOperator.Power or
            BinaryOperator.Equal or
            BinaryOperator.NotEqual or
            BinaryOperator.StrictEqual or
            BinaryOperator.StrictNotEqual or
            BinaryOperator.LessThan or
            BinaryOperator.LessThanOrEqual or
            BinaryOperator.GreaterThan or
            BinaryOperator.GreaterThanOrEqual or
            BinaryOperator.BitwiseAnd or
            BinaryOperator.BitwiseOr or
            BinaryOperator.BitwiseXor or
            BinaryOperator.LeftShift or
            BinaryOperator.RightShift or
            BinaryOperator.UnsignedRightShift or
            BinaryOperator.In or
            BinaryOperator.InstanceOf;

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

    private static bool TryResolveDriverSlot(
        Symbol symbol,
        int slotIndexOrFlatSlotId,
        UnifiedBytecodeSlotLayout slotLayout,
        out int slotIndex)
    {
        if (TryResolveActivationSymbolSlot(symbol, slotLayout, out slotIndex))
        {
            return true;
        }

        if (slotIndexOrFlatSlotId >= 0)
        {
            if (TryMapSlot(
                    slotLayout.ActivationSlots.ScopeId,
                    slotIndexOrFlatSlotId,
                    slotLayout.FlatSlotMappings,
                    out slotIndex))
            {
                return true;
            }

            slotIndex = slotIndexOrFlatSlotId;
            return (uint)slotIndex < (uint)slotLayout.SlotCount;
        }

        slotIndex = -1;
        return false;
    }

    private static bool TryResolveVisibleSymbolSlot(
        Symbol symbol,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        out int slotIndex)
    {
        foreach (var scope in activeScopes)
        {
            if (!scope.SlotMap.TryGetValue(symbol, out var scopedSlotIndex))
            {
                continue;
            }

            foreach (var (candidateSlotIndex, flatSlotId) in scope.FlatSlotMappings)
            {
                if (candidateSlotIndex == scopedSlotIndex)
                {
                    slotIndex = flatSlotId;
                    return true;
                }
            }
        }

        return TryResolveActivationSymbolSlot(symbol, slotLayout, out slotIndex);
    }

    private static bool TryResolveYieldStarStateSlot(
        Symbol symbol,
        ExpressionProgram iterableProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        out int slotIndex)
    {
        if (TryResolveVisibleSymbolSlot(symbol, slotLayout, activeScopes, out slotIndex))
        {
            return true;
        }

        if (iterableProgram.OperationCount == 1)
        {
            var operation = iterableProgram.GetOperation(0);
            if (operation.Kind == ExpressionOpKind.LoadIdentifier && !operation.IsArguments)
            {
                var identifier = operation.GetIdentifier(iterableProgram.IdentifierConstants.AsSpan());
                return TryResolveActivationSlot(identifier, slotLayout.ActivationSlots, out slotIndex);
            }
        }

        slotIndex = -1;
        return false;
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

    private static bool TryResolveActivationSlot(
        IdentifierOperand identifier,
        UnifiedBytecodeSlotLayout slotLayout,
        out int slotIndex)
    {
        var activationSlots = slotLayout.ActivationSlots;
        if (identifier.FlatSlotId >= 0)
        {
            if (SlotNameMatches(slotLayout, identifier.FlatSlotId, identifier.Name))
            {
                slotIndex = identifier.FlatSlotId;
                return true;
            }

            if ((identifier.ScopeId < 0 || identifier.ScopeId == activationSlots.ScopeId) &&
                activationSlots.SlotMap.TryGetValue(identifier.Name, out var mappedSlot) &&
                TryMapSlot(activationSlots.ScopeId, mappedSlot, slotLayout.FlatSlotMappings, out var mappedFlatSlot))
            {
                slotIndex = mappedFlatSlot;
                return true;
            }

            slotIndex = identifier.FlatSlotId;
            return true;
        }

        if (identifier.ScopeId == activationSlots.ScopeId && identifier.SlotIndex >= 0)
        {
            if (TryMapSlot(identifier.ScopeId, identifier.SlotIndex, slotLayout.FlatSlotMappings, out var mappedFlatSlot))
            {
                slotIndex = mappedFlatSlot;
                return true;
            }

            slotIndex = identifier.SlotIndex;
            return true;
        }

        if (identifier.ScopeId >= 0 && identifier.ScopeId != activationSlots.ScopeId)
        {
            slotIndex = -1;
            return false;
        }

        if (activationSlots.SlotMap.TryGetValue(identifier.Name, out var mappedSlotByName))
        {
            if (TryMapSlot(activationSlots.ScopeId, mappedSlotByName, slotLayout.FlatSlotMappings, out var mappedFlatSlot))
            {
                slotIndex = mappedFlatSlot;
                return true;
            }

            slotIndex = mappedSlotByName;
            return true;
        }

        if (IsYieldStarSyntheticResult(identifier.Name) &&
            slotLayout.ParameterSlotIndices is { IsDefaultOrEmpty: false } parameterSlots &&
            parameterSlots[0] >= 0)
        {
            slotIndex = parameterSlots[0];
            return true;
        }

        slotIndex = -1;
        return false;
    }

    private static bool IsYieldStarSyntheticResult(Symbol symbol) =>
        symbol.Name.StartsWith("__yield_lower_resume", StringComparison.Ordinal);

    private static bool SlotNameMatches(UnifiedBytecodeSlotLayout slotLayout, int slotIndex, Symbol name) =>
        (uint)slotIndex < (uint)slotLayout.SlotNames.Length &&
        string.Equals(slotLayout.SlotNames[slotIndex], name.Name, StringComparison.Ordinal);

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

    private static bool CanUseMaterializedActivationDynamicLookup(
        IdentifierOperand identifier,
        ActivationSlotShape activationSlots) =>
        identifier.ScopeId < 0 &&
        activationSlots.MaterializedBindingNames.Contains(identifier.Name);

    private const int LoadFunctionLiteralIsConstructorFlag = 1;

    private static int EncodeLoadFunctionLiteralOperand(int constantIndex, bool isConstructorFunction) =>
        (constantIndex << 1) | (isConstructorFunction ? LoadFunctionLiteralIsConstructorFlag : 0);

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
