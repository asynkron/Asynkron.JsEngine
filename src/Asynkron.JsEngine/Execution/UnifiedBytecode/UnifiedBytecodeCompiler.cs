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
    private const int DeclarationBindingTargetHasInitializerFlag = 8;
    private const int DeclarationBindingTargetShift = 4;

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
        ImmutableArray<string?> SlotNames,
        bool AllowsOrdinaryDynamicIdentifiers)
    {
        // Spread masks discovered while compiling synchronous spread invocations.
        // Each entry holds the spread argument positions for one invocation
        // boundary; the boundary operand references it by index+1.
        public List<ImmutableArray<int>> CallSpreadMasks { get; } = [];

        public int RegisterSpreadMask(ImmutableArray<int> spreadIndices)
        {
            var index = CallSpreadMasks.Count;
            CallSpreadMasks.Add(spreadIndices);
            return index;
        }
    }

    // CallInvocationBoundary operand packing for spread calls (gh2676) and direct eval:
    // low 16 bits hold the pushed argument value count, the high bits hold
    // spreadMaskIndex + 1 (0 means "no spread"). Bit 30 marks syntactic direct eval.
    private const int CallBoundaryArgumentMask = 0xFFFF;
    private const int CallBoundarySpreadShift = 16;
    private const int CallBoundarySpreadMask = 0x3FFF;
    private const int CallBoundaryDirectEvalFlag = 1 << 30;
    private const int FunctionDeclarationIndexMask = 0xFFFF;
    private const int FunctionDeclarationNameIndexShift = 16;

    private static int EncodeCallBoundaryOperand(int argumentValueCount, int spreadMaskIndex, bool isDirectEval)
    {
        var operand = argumentValueCount & CallBoundaryArgumentMask;
        if (spreadMaskIndex >= 0)
        {
            var encodedSpreadMask = spreadMaskIndex + 1;
            if ((encodedSpreadMask & ~CallBoundarySpreadMask) != 0)
            {
                throw new InvalidOperationException("Call spread mask index exceeds the call boundary operand capacity.");
            }

            operand |= encodedSpreadMask << CallBoundarySpreadShift;
        }

        return isDirectEval ? operand | CallBoundaryDirectEvalFlag : operand;
    }

    private static int EncodeFunctionDeclarationOperand(int functionConstantIndex, int nameConstantIndex)
    {
        if ((functionConstantIndex & ~FunctionDeclarationIndexMask) != 0 ||
            (nameConstantIndex & ~FunctionDeclarationIndexMask) != 0)
        {
            throw new InvalidOperationException("Function declaration constant index exceeds operand capacity.");
        }

        return functionConstantIndex | (nameConstantIndex << FunctionDeclarationNameIndexShift);
    }

    public static bool TryCompile(
        ExecutionPlan plan,
        bool isAsync,
        bool isGenerator,
        out UnifiedBytecodeProgram program,
        out string reason,
        bool allowsOrdinaryDynamicIdentifiers = false)
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

        var slotLayout = BuildSlotLayout(plan, allowsOrdinaryDynamicIdentifiers);

        var unified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        var literalConstants = ImmutableArray.CreateBuilder<JsValue>();
        var stringConstants = ImmutableArray.CreateBuilder<string>();
        var callTargetConstants = ImmutableArray.CreateBuilder<UnifiedBytecodeCallTarget>();
        var functionLiteralConstants = ImmutableArray.CreateBuilder<FunctionLiteralDescriptor>();
        var classLiteralConstants = ImmutableArray.CreateBuilder<ClassExpression>();
        var classDeclarationConstants = ImmutableArray.CreateBuilder<ClassDeclarationDescriptor>();
        var templateObjectConstants = ImmutableArray.CreateBuilder<TaggedTemplateDescriptor>();
        var scopeDescriptors = ImmutableArray.CreateBuilder<UnifiedBytecodeScopeDescriptor>();
        var tryDescriptors = ImmutableArray.CreateBuilder<UnifiedBytecodeTryDescriptor>();
        var catchDescriptors = ImmutableArray.CreateBuilder<UnifiedBytecodeCatchDescriptor>();
        var driverDescriptors = ImmutableArray.CreateBuilder<UnifiedBytecodeDriverDescriptor>();
        var bindingTargetConstants = ImmutableArray.CreateBuilder<BindingTargetProgram>();
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
                classLiteralConstants,
                classDeclarationConstants,
                templateObjectConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                bindingTargetConstants,
                ref maxStackDepth,
                out reason))
        {
            program = EmptyProgram();
            return false;
        }

        var compiledInstructions = unified.ToImmutable();
        program = new UnifiedBytecodeProgram(
            compiledInstructions,
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
                : functionLiteralConstants.ToImmutable(),
            classLiteralConstants.Count == 0
                ? ImmutableArray<ClassExpression>.Empty
                : classLiteralConstants.ToImmutable(),
            classDeclarationConstants.Count == 0
                ? ImmutableArray<ClassDeclarationDescriptor>.Empty
                : classDeclarationConstants.ToImmutable(),
            bindingTargetConstants.Count == 0
                ? ImmutableArray<BindingTargetProgram>.Empty
                : bindingTargetConstants.ToImmutable(),
            templateObjectConstants.Count == 0
                ? ImmutableArray<TaggedTemplateDescriptor>.Empty
                : templateObjectConstants.ToImmutable(),
            RequiresShortCircuitStackFlags(compiledInstructions));
        reason = string.Empty;
        return true;
    }

    private static bool RequiresShortCircuitStackFlags(
        ImmutableArray<UnifiedBytecodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.OpCode == UnifiedBytecodeOpCode.JumpIfShortCircuited)
            {
                return true;
            }
        }

        return false;
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

    private static int GetCompiledExpressionMaxStackDepth(ExpressionProgram expressionProgram)
    {
        var maxStackDepth = expressionProgram.MaxStackDepth;
        if (RequiresNestedNamedPropertyReceiverStack(expressionProgram))
        {
            maxStackDepth = Math.Max(maxStackDepth, 3);
        }

        return maxStackDepth;
    }

    private static bool RequiresNestedNamedPropertyReceiverStack(ExpressionProgram expressionProgram)
    {
        if (expressionProgram.OperationCount < 3 ||
            expressionProgram.GetOperation(1).Kind != ExpressionOpKind.GetNamedProperty)
        {
            return false;
        }

        var lastOp = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);
        return lastOp.Kind is ExpressionOpKind.SetNamedProperty or ExpressionOpKind.UpdateNamedProperty;
    }

    private static UnifiedBytecodeSlotLayout BuildSlotLayout(
        ExecutionPlan plan,
        bool allowsOrdinaryDynamicIdentifiers)
    {
        var activationSlots = AddSyntheticResumeSlots(plan.ActivationSlots!, plan.Instructions);
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
            names,
            allowsOrdinaryDynamicIdentifiers);
    }

    private static ActivationSlotShape AddSyntheticResumeSlots(
        ActivationSlotShape activationSlots,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        ImmutableArray<Symbol>.Builder? missingSymbols = null;
        HashSet<Symbol>? seenSymbols = null;
        foreach (var instruction in instructions)
        {
            switch (instruction)
            {
                case StoreResumeValueInstruction { TargetSymbol: { } targetSymbol }:
                    AddIfMissing(targetSymbol);
                    break;

                case YieldStarInstruction { StateSlotSymbol: { } stateSymbol, ResultSlotSymbol: { } resultSymbol }:
                    AddIfMissing(stateSymbol);
                    AddIfMissing(resultSymbol);
                    break;

                case YieldStarInstruction { StateSlotSymbol: { } stateSymbol }:
                    AddIfMissing(stateSymbol);
                    break;

                case YieldStarInstruction { ResultSlotSymbol: { } resultSymbol }:
                    AddIfMissing(resultSymbol);
                    break;
            }
        }

        void AddIfMissing(Symbol symbol)
        {
            if (!IsCompilerSyntheticResumableSlot(symbol) ||
                activationSlots.SlotMap.ContainsKey(symbol))
            {
                return;
            }

            seenSymbols ??= new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
            if (!seenSymbols.Add(symbol))
            {
                return;
            }

            missingSymbols ??= ImmutableArray.CreateBuilder<Symbol>();
            missingSymbols.Add(symbol);
        }

        if (missingSymbols is null || missingSymbols.Count == 0)
        {
            return activationSlots;
        }

        var slotMap = activationSlots.SlotMap.ToBuilder();
        var slotNames = activationSlots.SlotNames.ToBuilder();
        var nextSlotIndex = activationSlots.SlotCount;
        foreach (var symbol in missingSymbols)
        {
            slotMap[symbol] = nextSlotIndex;
            slotNames.Add((symbol, nextSlotIndex));
            nextSlotIndex++;
        }

        return activationSlots with
        {
            SlotCount = nextSlotIndex,
            SlotMap = slotMap.ToImmutable(),
            SlotNames = slotNames.ToImmutable()
        };
    }

    private static bool IsCompilerSyntheticResumableSlot(Symbol symbol) =>
        IsYieldStarSyntheticResult(symbol) ||
        symbol.Name.StartsWith("\u0001_yieldstar", StringComparison.Ordinal);

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
        ImmutableArray<ClassExpression>.Builder classLiteralConstants,
        ImmutableArray<ClassDeclarationDescriptor>.Builder classDeclarationConstants,
        ImmutableArray<TaggedTemplateDescriptor>.Builder templateObjectConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ImmutableArray<UnifiedBytecodeTryDescriptor>.Builder tryDescriptors,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
        ImmutableArray<BindingTargetProgram>.Builder bindingTargetConstants,
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

                if (instructionPcMap.TryGetValue(instructionIndex, out var existingProgramCounter))
                {
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, existingProgramCounter));
                    reason = string.Empty;
                    return true;
                }

                if (activeInstructions.Contains(instructionIndex))
                {
                    reason = $"Loop-shaped unified bytecode plan detected at instruction {instructionIndex}.";
                    return false;
                }

                activeInstructions.Add(instructionIndex);
                activated.Add(instructionIndex);
                instructionPcMap[instructionIndex] = unified.Count;
                var allowsDynamicIdentifiers = activeWithDepths[instructionIndex] > 0 ||
                                               slotLayout.AllowsOrdinaryDynamicIdentifiers;

                switch (instructions[instructionIndex])
                {
                    case FunctionDeclarationInstruction { Descriptor: null } functionDeclaration:
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                functionDeclaration.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = functionDeclaration.Next;
                        continue;

                    case FunctionDeclarationInstruction { Descriptor: { } functionDeclarationDescriptor } functionDeclaration:
                        var functionDeclarationFunctionIndex = functionLiteralConstants.Count;
                        functionLiteralConstants.Add(new FunctionLiteralDescriptor(
                            functionDeclarationDescriptor.Function,
                            functionDeclarationDescriptor.PlanSeed));
                        var functionDeclarationNameIndex = stringConstants.Count;
                        stringConstants.Add(functionDeclarationDescriptor.Name.Name);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.DeclareFunction,
                            EncodeFunctionDeclarationOperand(
                                functionDeclarationFunctionIndex,
                                functionDeclarationNameIndex)));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                functionDeclaration.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = functionDeclaration.Next;
                        continue;

                    case ClassDeclarationInstruction classDeclaration:
                        var classDeclarationIndex = classDeclarationConstants.Count;
                        classDeclarationConstants.Add(classDeclaration.Descriptor);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.DeclareClass,
                            classDeclarationIndex));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                classDeclaration.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = classDeclaration.Next;
                        continue;

                    case SimpleVariableDeclarationInstruction
                        {
                            VarKind: VariableKind.Var,
                            InitializerProgram: null,
                            AwaitedProgram: null
                        } varDeclaration:
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                varDeclaration.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = varDeclaration.Next;
                        continue;

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
                                    classLiteralConstants,
                                    templateObjectConstants,
                                    out reason,
                                    bindingTargetConstants))
                            {
                                return false;
                            }

                            maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(initializerProgram));
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
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
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(initializerProgram));
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

                    case BindingVariableDeclarationInstruction
                        {
                            AwaitedProgram: null
                        } declaration:
                        var hasBindingInitializer = declaration.InitializerProgram is not null;
                        if (declaration.InitializerProgram is { } bindingInitializerProgram)
                        {
                            if (!TryAppendExpressionProgramOps(
                                    bindingInitializerProgram,
                                    slotLayout,
                                    allowsDynamicIdentifiers,
                                    unified,
                                    literalConstants,
                                    stringConstants,
                                    callTargetConstants,
                                    functionLiteralConstants,
                                    classLiteralConstants,
                                    templateObjectConstants,
                                    out reason,
                                    bindingTargetConstants))
                            {
                                return false;
                            }

                            maxStackDepth = Math.Max(
                                maxStackDepth,
                                GetCompiledExpressionMaxStackDepth(bindingInitializerProgram));
                        }
                        else
                        {
                            var bindingUndefinedIndex = literalConstants.Count;
                            literalConstants.Add(JsValue.Undefined);
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.LoadLiteral,
                                bindingUndefinedIndex));
                            maxStackDepth = Math.Max(maxStackDepth, 1);
                        }

                        var declarationBindingTargetIndex = bindingTargetConstants.Count;
                        bindingTargetConstants.Add(declaration.TargetProgram);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget,
                            EncodeDeclarationBindingTargetOperand(
                                declarationBindingTargetIndex,
                                declaration.VarKind,
                                hasBindingInitializer)));

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
                                    classLiteralConstants,
                                    templateObjectConstants,
                                    out reason,
                                    bindingTargetConstants))
                            {
                                return false;
                            }

                            AppendDynamicStoreInstruction(
                                assignmentTargetSymbol,
                                assignment.AllowNameInference,
                                unified,
                                stringConstants);
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
                            maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(valueProgram));
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
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
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(valueProgram));
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
                                    classLiteralConstants,
                                    templateObjectConstants,
                                    out reason,
                                    bindingTargetConstants))
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
                            maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(rhsProgram) + 1);
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.Binary,
                            (int)compoundAssignment.Operator));
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.StoreSlot, compoundSlot));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(rhsProgram) + 1);
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

                    case LogicalCompoundAssignmentSlotInstruction
                        {
                            RhsProgram: { } logicalRhsProgram,
                            AwaitedProgram: null,
                            TargetSymbol: { } logicalTargetSymbol
                        } logicalAssignment:
                        if (!TryResolveInstructionSlot(logicalTargetSymbol, logicalAssignment.FlatSlotId, slotLayout, out var logicalSlot))
                        {
                            reason = $"Unsupported logical assignment target '{logicalTargetSymbol.Name}'.";
                            return false;
                        }

                        var scJumpOpCode = logicalAssignment.Operator switch
                        {
                            BinaryOperator.LogicalAnd => UnifiedBytecodeOpCode.JumpIfShortCircuitFalse,
                            BinaryOperator.LogicalOr => UnifiedBytecodeOpCode.JumpIfShortCircuitTrue,
                            _ => UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish
                        };
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, logicalSlot));
                        var scJumpIndex = unified.Count;
                        unified.Add(new UnifiedBytecodeInstruction(scJumpOpCode, 0));
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
                        if (!TryAppendExpressionProgramOps(
                                logicalRhsProgram,
                                slotLayout,
                                allowsDynamicIdentifiers,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                functionLiteralConstants,
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.StoreSlot, logicalSlot));
                        var skipScPopIndex = unified.Count;
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));
                        PatchOperand(unified, scJumpIndex, unified.Count);
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
                        PatchOperand(unified, skipScPopIndex, unified.Count);
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(logicalRhsProgram) + 1);
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                logicalAssignment.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = logicalAssignment.Next;
                        continue;

                    case IncrementSlotInstruction
                        {
                            TargetSymbol: { } incrementTargetSymbol
                        } increment:
                        if (TryResolveInstructionSlot(
                                incrementTargetSymbol,
                                increment.FlatSlotId,
                                slotLayout,
                                out var incrementSlot))
                        {
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.UpdateSlot,
                                EncodeUpdateOperand(
                                    incrementSlot,
                                    increment.IsIncrement,
                                    increment.IsPrefix)));
                        }
                        else
                        {
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
                        }

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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.EnterWith));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(enterWithObjectProgram));
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
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
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(iterableProgram));
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
                            classLiteralConstants,
                            classDeclarationConstants,
                            templateObjectConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            bindingTargetConstants,
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ForInInit,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(forInStateSlot))));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(objectProgram));
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
                            classLiteralConstants,
                            classDeclarationConstants,
                            templateObjectConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            bindingTargetConstants,
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ArrayDestructuringInit,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(destructuringStateSlot))));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(arrayDestructuringInit.SourceProgram));
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ObjectDestructuringInit,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(objectDestructuringStateSlot))));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(objectDestructuringInit.SourceProgram));
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
                            classLiteralConstants,
                            classDeclarationConstants,
                            templateObjectConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            bindingTargetConstants,
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
                            classLiteralConstants,
                            classDeclarationConstants,
                            templateObjectConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            bindingTargetConstants,
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
                            classLiteralConstants,
                            classDeclarationConstants,
                            templateObjectConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            bindingTargetConstants,
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
                            classLiteralConstants,
                            classDeclarationConstants,
                            templateObjectConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            bindingTargetConstants,
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
                            classLiteralConstants,
                            classDeclarationConstants,
                            templateObjectConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            bindingTargetConstants,
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
                            classLiteralConstants,
                            classDeclarationConstants,
                            templateObjectConstants,
                            scopeDescriptors,
                            tryDescriptors,
                            catchDescriptors,
                            driverDescriptors,
                            bindingTargetConstants,
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(branch.ConditionProgram));
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
                                     classLiteralConstants,
                                     classDeclarationConstants,
                                     templateObjectConstants,
                                     scopeDescriptors,
                                     tryDescriptors,
                                     catchDescriptors,
                                     driverDescriptors,
                                     bindingTargetConstants,
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
                                classLiteralConstants,
                                classDeclarationConstants,
                                templateObjectConstants,
                                scopeDescriptors,
                                tryDescriptors,
                                catchDescriptors,
                                driverDescriptors,
                                bindingTargetConstants,
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(returnProgram));
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitedReturn));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(awaitedReturnProgram));
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitAndDiscard));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(awaitAndDiscard.AwaitedProgram));
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Yield));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(yieldProgram));
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
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
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(iterableProgram));
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Throw));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(throwProgram));
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
                                classLiteralConstants,
                                templateObjectConstants,
                                out reason,
                                bindingTargetConstants))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(discardedProgram));
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
        ImmutableArray<ClassExpression>.Builder classLiteralConstants,
        ImmutableArray<ClassDeclarationDescriptor>.Builder classDeclarationConstants,
        ImmutableArray<TaggedTemplateDescriptor>.Builder templateObjectConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ImmutableArray<UnifiedBytecodeTryDescriptor>.Builder tryDescriptors,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
        ImmutableArray<BindingTargetProgram>.Builder bindingTargetConstants,
        ref int maxStackDepth,
        out string reason)
    {
        if ((uint)targetIndex >= (uint)instructions.Length)
        {
            reason = "Instruction flow reached an invalid target index.";
            return false;
        }

        if (instructionPcMap.ContainsKey(targetIndex))
        {
            reason = string.Empty;
            return true;
        }

        if (activeInstructions.Contains(targetIndex))
        {
            reason = $"Loop-shaped unified bytecode plan detected at instruction {targetIndex}.";
            return false;
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
            classLiteralConstants,
            classDeclarationConstants,
            templateObjectConstants,
            scopeDescriptors,
            tryDescriptors,
            catchDescriptors,
            driverDescriptors,
            bindingTargetConstants,
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
        ImmutableArray<ClassExpression>.Builder classLiteralConstants,
        ImmutableArray<ClassDeclarationDescriptor>.Builder classDeclarationConstants,
        ImmutableArray<TaggedTemplateDescriptor>.Builder templateObjectConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ImmutableArray<UnifiedBytecodeTryDescriptor>.Builder tryDescriptors,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
        ImmutableArray<BindingTargetProgram>.Builder bindingTargetConstants,
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
                classLiteralConstants,
                classDeclarationConstants,
                templateObjectConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                bindingTargetConstants,
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
                classLiteralConstants,
                classDeclarationConstants,
                templateObjectConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                bindingTargetConstants,
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
                classLiteralConstants,
                classDeclarationConstants,
                templateObjectConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                bindingTargetConstants,
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
        ImmutableArray<ClassExpression>.Builder classLiteralConstants,
        ImmutableArray<ClassDeclarationDescriptor>.Builder classDeclarationConstants,
        ImmutableArray<TaggedTemplateDescriptor>.Builder templateObjectConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ImmutableArray<UnifiedBytecodeTryDescriptor>.Builder tryDescriptors,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
        ImmutableArray<BindingTargetProgram>.Builder bindingTargetConstants,
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
                NextTarget: unified.Count + 1,
                ContinueTarget: unified.Count,
                MoveNextTarget: unified.Count));
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
                classLiteralConstants,
                classDeclarationConstants,
                templateObjectConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                bindingTargetConstants,
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
                classLiteralConstants,
                classDeclarationConstants,
                templateObjectConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                bindingTargetConstants,
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
        ImmutableArray<ClassExpression>.Builder classLiteralConstants,
        ImmutableArray<ClassDeclarationDescriptor>.Builder classDeclarationConstants,
        ImmutableArray<TaggedTemplateDescriptor>.Builder templateObjectConstants,
        ImmutableArray<UnifiedBytecodeScopeDescriptor>.Builder scopeDescriptors,
        ImmutableArray<UnifiedBytecodeTryDescriptor>.Builder tryDescriptors,
        ImmutableArray<UnifiedBytecodeCatchDescriptor>.Builder catchDescriptors,
        ImmutableArray<UnifiedBytecodeDriverDescriptor>.Builder driverDescriptors,
        ImmutableArray<BindingTargetProgram>.Builder bindingTargetConstants,
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
                classLiteralConstants,
                classDeclarationConstants,
                templateObjectConstants,
                scopeDescriptors,
                tryDescriptors,
                catchDescriptors,
                driverDescriptors,
                bindingTargetConstants,
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
            !IsSupportedLoopContinueTarget(targetIndex, instructions))
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

    private static bool IsSupportedLoopContinueTarget(
        int targetIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        return instructions[targetIndex] switch
        {
            BranchInstruction branch => HasLoopContinueTarget(targetIndex, branch.AlternateIndex, instructions),
            IteratorMoveNextInstruction iteratorMoveNext => HasLoopContinueTarget(
                targetIndex,
                iteratorMoveNext.BreakIndex,
                instructions),
            ForInMoveNextInstruction forInMoveNext => HasLoopContinueTarget(
                targetIndex,
                forInMoveNext.BreakIndex,
                instructions),
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

        if (instructions[sourceInstructionIndex] is JumpInstruction { TargetIndex: var jumpTargetIndex } &&
            jumpTargetIndex == targetIndex)
        {
            return true;
        }

        if (instructions[sourceInstructionIndex] is SetCompletionValueInstruction { Next: var completionNext } &&
            completionNext == targetIndex)
        {
            return true;
        }

        if (instructions[sourceInstructionIndex] is LeaveTryInstruction { Next: var leaveTryNext } &&
            leaveTryNext == targetIndex)
        {
            return true;
        }

        if (instructions[sourceInstructionIndex] is EndFinallyInstruction { Next: var endFinallyNext } &&
            endFinallyNext == targetIndex)
        {
            return true;
        }

        if (IsSupportedAbruptCleanupDriverLoopBackEdge(sourceInstructionIndex, targetIndex, instructions))
        {
            return true;
        }

        // A per-iteration lexical head (for (const/let x in/of ...)) closes its environment with a
        // PopEnvironment immediately before looping back to the driver's MoveNext. That PopEnvironment
        // is a valid back-edge source: the canonical-body walk below still requires the body between
        // the MoveNext and this Pop to be linear, so no branching control flow is admitted.
        if (instructions[sourceInstructionIndex] is not AssignmentSlotInstruction and not
            CompoundAssignmentSlotInstruction and not JumpInstruction and not PopEnvironmentInstruction and not
            SetCompletionValueInstruction)
        {
            return false;
        }

        return TryIsLinearCanonicalWhileBody(bodyStartIndex, sourceInstructionIndex, instructions) &&
               !HasExplicitJumpIntoLoopBackEdgeSource(sourceInstructionIndex, instructions);
    }

    private static bool IsSupportedAbruptCleanupDriverLoopBackEdge(
        int sourceInstructionIndex,
        int targetIndex,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        if (instructions[sourceInstructionIndex] is not PopEnvironmentInstruction popEnvironment ||
            popEnvironment.Next != targetIndex)
        {
            return false;
        }

        foreach (var instruction in instructions)
        {
            if (instruction is ContinueInstruction { TargetIndex: var continueTarget } &&
                continueTarget == sourceInstructionIndex)
            {
                return true;
            }
        }

        return false;
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
        ImmutableArray<ClassExpression>.Builder classLiteralConstants,
        ImmutableArray<TaggedTemplateDescriptor>.Builder templateObjectConstants,
        out string reason,
        ImmutableArray<BindingTargetProgram>.Builder? bindingTargetConstants = null)
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
                classLiteralConstants,
                templateObjectConstants,
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
                allowsDynamicIdentifiers,
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

        if (TryAppendFirstBoundaryNamedLogicalPropertySet(
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers,
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

        if (TryAppendFirstBoundaryComputedLogicalPropertySet(
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
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

        if (TryAppendFirstBoundaryNamedPropertySet(
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
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

        if (TryAppendFirstBoundaryNestedNamedPropertySet(
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
                allowsDynamicIdentifiers,
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

        if (TryAppendFirstBoundaryNamedPropertyUpdate(
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
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

        if (TryAppendFirstBoundaryNestedNamedPropertyUpdate(
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
                allowsDynamicIdentifiers,
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

        if (TryAppendFirstBoundaryOptionalNamedPropertyDelete(
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

        if (TryAppendFirstBoundaryOptionalComputedPropertyDelete(
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

        if (TryAppendFirstBoundaryNamedPropertyReadChain(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                allowsDynamicIdentifiers,
                out reason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            return false;
        }

        if (TryAppendFirstBoundaryOptionalNamedPropertyRead(
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

        if (TryAppendFirstBoundaryOptionalNamedPropertyReadChain(
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

        if (TryAppendFirstBoundaryOptionalNamedThenComputed(
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

        if (TryAppendFirstBoundaryOptionalComputedPropertyReadChain(
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
                allowsDynamicIdentifiers,
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

        var exprPcToUnifiedPc = new int[expressionProgram.OperationCount + 1];
        List<(int UnifiedIndex, int ExprTarget)>? patches = null;

        for (var exprPc = 0; exprPc < expressionProgram.OperationCount; exprPc++)
        {
            exprPcToUnifiedPc[exprPc] = unified.Count;
            var operation = expressionProgram.GetOperation(exprPc);
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadIdentifier:
                    var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (TryResolveActivationSlot(identifier, slotLayout, out var slotIndex))
                    {
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, slotIndex));
                        break;
                    }

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

                case ExpressionOpKind.ResolveIdentifierReference:
                    var referenceIdentifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (IsImplicitArgumentsIdentifier(referenceIdentifier, slotLayout))
                    {
                        reason = "arguments assignment references are not supported.";
                        return false;
                    }

                    if (TryResolveActivationSlot(referenceIdentifier, slotLayout, out _))
                    {
                        reason =
                            $"Identifier assignment reference '{referenceIdentifier.Name.Name}' resolves to an activation slot and is not eligible for dynamic unified bytecode assignment references.";
                        return false;
                    }

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
                    if (IsImplicitArgumentsIdentifier(storeReferenceIdentifier, slotLayout))
                    {
                        reason = "arguments assignment references are not supported.";
                        return false;
                    }

                    if (TryResolveActivationSlot(storeReferenceIdentifier, slotLayout, out _))
                    {
                        reason =
                            $"Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' resolves to an activation slot and is not eligible for dynamic unified bytecode assignment references.";
                        return false;
                    }

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
                    if (IsImplicitArgumentsIdentifier(storeIdentifier, slotLayout))
                    {
                        reason = "arguments assignment references are not supported.";
                        return false;
                    }

                    if (TryResolveActivationSlot(storeIdentifier, slotLayout, out _))
                    {
                        reason =
                            $"Identifier '{storeIdentifier.Name.Name}' resolves to an activation slot and is not eligible for dynamic unified bytecode assignment references.";
                        return false;
                    }

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

                case ExpressionOpKind.EnsureSuperReference:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.EnsureSuperReference));
                    break;

                case ExpressionOpKind.GetNamedProperty:
                    if (operation.ShortCircuitOnNullishTarget)
                    {
                        reason = "Optional named property reads with short-circuit target are not supported in the general expression loop.";
                        return false;
                    }

                    var namedPropNameIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        operation.IsOptional
                            ? UnifiedBytecodeOpCode.GetNamedPropertyOptional
                            : UnifiedBytecodeOpCode.GetNamedProperty,
                        namedPropNameIndex));
                    break;

                case ExpressionOpKind.GetNamedSuperProperty:
                    var namedSuperPropNameIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.GetNamedSuperProperty,
                        namedSuperPropNameIndex));
                    break;

                case ExpressionOpKind.LoadNewTarget:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadNewTarget));
                    break;

                case ExpressionOpKind.LoadImportMeta:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadImportMeta));
                    break;

                case ExpressionOpKind.LoadIdentifierCallTarget:
                    var identifierCallTarget = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (!TryResolveActivationCallTargetSlot(identifierCallTarget, slotLayout, out var identifierCallTargetSlot))
                    {
                        if (!allowsDynamicIdentifiers &&
                            !CanUseMaterializedActivationDynamicLookup(identifierCallTarget, activationSlots))
                        {
                            reason =
                                $"Identifier call target '{identifierCallTarget.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                            return false;
                        }

                        var dynamicCallTargetNameIndex = stringConstants.Count;
                        stringConstants.Add(identifierCallTarget.Name.Name ?? string.Empty);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget,
                            dynamicCallTargetNameIndex));
                        break;
                    }

                    var identifierCallTargetNameIndex = stringConstants.Count;
                    stringConstants.Add(identifierCallTarget.Name.Name ?? string.Empty);
                    var identifierCallTargetIndex = callTargetConstants.Count;
                    callTargetConstants.Add(new UnifiedBytecodeCallTarget(
                        UnifiedBytecodeCallTargetKind.Identifier,
                        identifierCallTargetSlot,
                        identifierCallTargetNameIndex));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrepareIdentifierCallTarget,
                        identifierCallTargetIndex));
                    break;

                case ExpressionOpKind.LoadNamedCallTarget:
                    var namedCallTargetName = operation.GetString(expressionProgram.StringConstants.AsSpan());
                    var namedCallTargetNameIndex = stringConstants.Count;
                    stringConstants.Add(namedCallTargetName);
                    var namedCallTargetIndex = callTargetConstants.Count;
                    callTargetConstants.Add(new UnifiedBytecodeCallTarget(
                        UnifiedBytecodeCallTargetKind.NamedMember,
                        NameConstantIndex: namedCallTargetNameIndex));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrepareNamedCallTarget,
                        namedCallTargetIndex));
                    break;

                case ExpressionOpKind.LoadComputedCallTarget:
                    var computedCallTargetIndex = callTargetConstants.Count;
                    callTargetConstants.Add(new UnifiedBytecodeCallTarget(UnifiedBytecodeCallTargetKind.ComputedMember));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrepareComputedCallTarget,
                        computedCallTargetIndex));
                    break;

                case ExpressionOpKind.LoadNamedSuperCallTarget:
                    var namedSuperCallTargetName = operation.GetString(expressionProgram.StringConstants.AsSpan());
                    if (namedSuperCallTargetName.IsPrivateName())
                    {
                        reason = "Private named super call targets are outside the general expression loop boundary.";
                        return false;
                    }

                    var namedSuperCallTargetNameIndex = stringConstants.Count;
                    stringConstants.Add(namedSuperCallTargetName);
                    var namedSuperCallTargetIndex = callTargetConstants.Count;
                    callTargetConstants.Add(new UnifiedBytecodeCallTarget(
                        UnifiedBytecodeCallTargetKind.NamedSuperMember,
                        NameConstantIndex: namedSuperCallTargetNameIndex));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget,
                        namedSuperCallTargetIndex));
                    break;

                case ExpressionOpKind.LoadComputedSuperCallTarget:
                    var computedSuperCallTargetIndex = callTargetConstants.Count;
                    callTargetConstants.Add(new UnifiedBytecodeCallTarget(UnifiedBytecodeCallTargetKind.ComputedSuperMember));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget,
                        computedSuperCallTargetIndex));
                    break;

                case ExpressionOpKind.LoadTemplateObject:
                    var templateObjectIndex = templateObjectConstants.Count;
                    templateObjectConstants.Add(operation.GetObject<TaggedTemplateDescriptor>(
                        expressionProgram.ObjectConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.LoadTemplateObject,
                        templateObjectIndex));
                    break;

                case ExpressionOpKind.LoadLiteral:
                    var literal = operation.GetLiteral(expressionProgram.LiteralConstants.AsSpan());
                    var literalIndex = literalConstants.Count;
                    literalConstants.Add(literal);
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, literalIndex));
                    break;

                case ExpressionOpKind.LoadRegexLiteral:
                    var regexPatternIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.LoadRegexLiteral,
                        EncodeRegexLiteralOperand(regexPatternIndex, operation.EncodedRegexFlags)));
                    break;

                case ExpressionOpKind.TypeOf:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.TypeOf));
                    break;

                case ExpressionOpKind.ThrowReferenceError:
                    var referenceErrorMessageIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.ThrowReferenceError,
                        referenceErrorMessageIndex));
                    break;

                case ExpressionOpKind.TypeOfIdentifier:
                    if (!TryResolveTypeOfIdentifierSlot(operation, expressionProgram, slotLayout, out var typeOfSlot, out reason))
                    {
                        if (operation.IsArguments)
                        {
                            if (!allowsDynamicIdentifiers)
                            {
                                var argumentsTypeIndex = literalConstants.Count;
                                literalConstants.Add(new JsValue("object"));
                                unified.Add(new UnifiedBytecodeInstruction(
                                    UnifiedBytecodeOpCode.LoadLiteral,
                                    argumentsTypeIndex));
                                break;
                            }
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
                    var deleteIdentifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (IsImplicitArgumentsIdentifier(deleteIdentifier, slotLayout))
                    {
                        reason = "arguments delete is not supported.";
                        return false;
                    }

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

                case ExpressionOpKind.DeleteNamedProperty:
                    if (operation.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
                    {
                        reason = "Private named property deletes are not supported in the general expression loop.";
                        return false;
                    }

                    var deletePropertyNameIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.DeleteNamedProperty,
                        deletePropertyNameIndex));
                    break;

                case ExpressionOpKind.DeleteComputedProperty:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DeleteComputedProperty));
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

                case ExpressionOpKind.PrivateFieldIn:
                    var privateFieldNameIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrivateFieldIn,
                        privateFieldNameIndex));
                    break;

                case ExpressionOpKind.ToString:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ToString));
                    break;

                case ExpressionOpKind.Pop:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
                    break;

                case ExpressionOpKind.DuplicateTop:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DuplicateTop));
                    break;

                case ExpressionOpKind.DuplicateTopTwo:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DuplicateTopTwo));
                    break;

                case ExpressionOpKind.SwapTopTwo:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SwapTopTwo));
                    break;

                case ExpressionOpKind.RotateTopThreeRight:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.RotateTopThreeRight));
                    break;

                case ExpressionOpKind.ApplyBindingTarget:
                    if (bindingTargetConstants is null)
                    {
                        reason = "Binding-target expressions are not available in this unified bytecode compilation context.";
                        return false;
                    }

                    var bindingTargetIndex = bindingTargetConstants.Count;
                    bindingTargetConstants.Add(operation.GetObject<BindingTargetProgram>(
                        expressionProgram.ObjectConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.ApplyBindingTarget,
                        bindingTargetIndex));
                    break;

                case ExpressionOpKind.Binary when IsSupportedBinaryOperator(operation.Operator):
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)operation.Operator));
                    break;

                case ExpressionOpKind.UpdateIdentifier:
                    var updateIdentifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (TryResolveActivationSlot(updateIdentifier, slotLayout, out var updateSlot))
                    {
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.UpdateSlot,
                            EncodeUpdateOperand(updateSlot, operation)));
                        break;
                    }

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

                case ExpressionOpKind.SetNamedProperty:
                    var setNamedPropertyName = operation.GetString(expressionProgram.StringConstants.AsSpan());
                    if (operation.AllowNameInference)
                    {
                        reason = "Property writes with name inference are not supported in the general expression loop.";
                        return false;
                    }

                    var setNamedPropertyIndex = stringConstants.Count;
                    stringConstants.Add(setNamedPropertyName);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.SetNamedProperty,
                        setNamedPropertyIndex));
                    break;

                case ExpressionOpKind.SetComputedProperty:
                    if (operation.AllowNameInference)
                    {
                        reason = "Computed property writes with name inference are not supported in the general expression loop.";
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetComputedProperty));
                    break;

                case ExpressionOpKind.UpdateNamedProperty:
                    var updateNamedPropertyName = operation.GetString(expressionProgram.StringConstants.AsSpan());
                    var updateNamedPropertyIndex = stringConstants.Count;
                    stringConstants.Add(updateNamedPropertyName);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.UpdateNamedProperty,
                        EncodeUpdateOperand(updateNamedPropertyIndex, operation)));
                    break;

                case ExpressionOpKind.UpdateComputedProperty:
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.UpdateComputedProperty,
                        EncodeUpdateFlags(operation)));
                    break;

                case ExpressionOpKind.SetNamedSuperProperty:
                    var setNamedSuperPropertyIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.SetNamedSuperProperty,
                        EncodeDynamicStoreOperand(setNamedSuperPropertyIndex, operation.AllowNameInference)));
                    break;

                case ExpressionOpKind.SetComputedSuperProperty:
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.SetComputedSuperProperty,
                        operation.AllowNameInference ? DynamicStoreAllowNameInferenceFlag : 0));
                    break;

                case ExpressionOpKind.UpdateNamedSuperProperty:
                    var updateNamedSuperPropertyIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.UpdateNamedSuperProperty,
                        EncodeUpdateOperand(updateNamedSuperPropertyIndex, operation)));
                    break;

                case ExpressionOpKind.UpdateComputedSuperProperty:
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.UpdateComputedSuperProperty,
                        EncodeUpdateOperand(0, operation)));
                    break;

                case ExpressionOpKind.ResolvePropertyKey:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
                    break;

                case ExpressionOpKind.RequireObjectCoercible:
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.RequireObjectCoercible,
                        operation.Depth));
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

                case ExpressionOpKind.DefineObjectMethod:
                    var methodNameIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.DefineObjectMethod,
                        methodNameIndex));
                    break;

                case ExpressionOpKind.DefineComputedObjectMethod:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DefineComputedObjectMethod));
                    break;

                case ExpressionOpKind.DefineObjectAccessor:
                    var accessorNameIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.DefineObjectAccessor,
                        EncodeObjectAccessorOperand(accessorNameIndex, operation.AccessorKind)));
                    break;

                case ExpressionOpKind.DefineComputedObjectAccessor:
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.DefineComputedObjectAccessor,
                        EncodeObjectAccessorOperand(0, operation.AccessorKind)));
                    break;

                case ExpressionOpKind.ObjectSpread:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ObjectSpread));
                    break;

                case ExpressionOpKind.Construct:
                    // Synchronous construct calls. The constructor value and each logical
                    // argument are lowered by preceding ops in source order; spread positions
                    // hold their iterable value and are flattened by the construct boundary.
                    var constructSpreadIndices = operation.GetSpreadIndices(expressionProgram.SpreadMaskConstants.AsSpan());
                    var constructSpreadMaskIndex = constructSpreadIndices.IsDefaultOrEmpty
                        ? -1
                        : slotLayout.RegisterSpreadMask(constructSpreadIndices);

                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.ConstructInvocationBoundary,
                        EncodeCallBoundaryOperand(operation.ArgumentCount, constructSpreadMaskIndex, isDirectEval: false)));
                    break;

                case ExpressionOpKind.Call:
                    if (!operation.HasExplicitThis || operation.IsDirectEval)
                    {
                        reason = "Only ordinary explicit-this calls are supported in the general expression loop.";
                        return false;
                    }

                    var callSpreadIndices = operation.GetSpreadIndices(expressionProgram.SpreadMaskConstants.AsSpan());
                    var callSpreadMaskIndex = callSpreadIndices.IsDefaultOrEmpty
                        ? -1
                        : slotLayout.RegisterSpreadMask(callSpreadIndices);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.CallInvocationBoundary,
                        EncodeCallBoundaryOperand(operation.ArgumentCount, callSpreadMaskIndex, isDirectEval: false)));
                    break;

                case ExpressionOpKind.SuperConstruct:
                    var superConstructSpreadIndices =
                        operation.GetSpreadIndices(expressionProgram.SpreadMaskConstants.AsSpan());
                    var superConstructSpreadMaskIndex = superConstructSpreadIndices.IsDefaultOrEmpty
                        ? -1
                        : slotLayout.RegisterSpreadMask(superConstructSpreadIndices);

                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.SuperConstructInvocationBoundary,
                        EncodeCallBoundaryOperand(
                            operation.ArgumentCount,
                            superConstructSpreadMaskIndex,
                            isDirectEval: false)));
                    break;

                case ExpressionOpKind.LoadFunctionLiteral:
                    var functionLiteralIndex = functionLiteralConstants.Count;
                    functionLiteralConstants.Add(
                        operation.GetObject<FunctionLiteralDescriptor>(expressionProgram.ObjectConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.LoadFunctionLiteral,
                        EncodeLoadFunctionLiteralOperand(functionLiteralIndex, operation.IsConstructorFunction)));
                    break;

                case ExpressionOpKind.LoadClassLiteral:
                    var classLiteralIndex = classLiteralConstants.Count;
                    classLiteralConstants.Add(operation.GetObject<ClassExpression>(expressionProgram.ObjectConstants.AsSpan()));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadClassLiteral, classLiteralIndex));
                    break;

                case ExpressionOpKind.JumpIfFalse:
                    patches ??= [];
                    patches.Add((unified.Count, operation.Target));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfShortCircuitFalse, 0));
                    break;

                case ExpressionOpKind.JumpIfConditionalFalse:
                    patches ??= [];
                    patches.Add((unified.Count, operation.Target));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfFalse, 0));
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

                case ExpressionOpKind.JumpIfShortCircuited:
                    patches ??= [];
                    patches.Add((unified.Count, operation.Target));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfShortCircuited, 0));
                    break;

                case ExpressionOpKind.Jump:
                    patches ??= [];
                    patches.Add((unified.Count, operation.Target));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));
                    break;

                case ExpressionOpKind.JumpIfNullish when operation.ReplaceWithUndefined:
                    patches ??= [];
                    patches.Add((unified.Count, operation.Target));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));
                    break;

                case ExpressionOpKind.GetComputedProperty when !operation.ShortCircuitOnNullishTarget:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedProperty));
                    break;

                case ExpressionOpKind.GetComputedSuperProperty:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedSuperProperty));
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
        ImmutableArray<ClassExpression>.Builder classLiteralConstants,
        ImmutableArray<TaggedTemplateDescriptor>.Builder templateObjectConstants,
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
            if (!RequiresFirstBoundaryCallTargetPreparation(expressionProgram, call))
            {
                return false;
            }

            if (!call.HasExplicitThis && !call.IsDirectEval)
            {
                reason = "Only direct identifier and member calls with explicit receiver records are supported.";
                return false;
            }

            if (call.IsDirectEval &&
                (call.ArgumentCount != 1 || call.SpreadMaskConstantIndex >= 0))
            {
                reason = "Only one-argument non-spread direct eval is supported by the call-target preparation boundary.";
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

            if (!string.IsNullOrEmpty(reason))
            {
                return false;
            }

            if (TryAppendNamedSuperCallTargetPreparation(
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

            if (TryAppendComputedSuperCallTargetPreparation(
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

            if (TryAppendCalleeOptionalIdentifierCallTarget(
                    expressionProgram,
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    call,
                    callIndex,
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

    private static bool RequiresFirstBoundaryCallTargetPreparation(
        ExpressionProgram expressionProgram,
        PackedExpressionOp call)
    {
        if (call.IsDirectEval)
        {
            return true;
        }

        for (var operationIndex = 0; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            var operation = expressionProgram.GetOperation(operationIndex);
            if (operation.Kind == ExpressionOpKind.JumpIfShortCircuited ||
                operation is { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true } ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget)
            {
                return true;
            }
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

        var identifier = callTarget.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        var isDirectEval = call.IsDirectEval &&
                           string.Equals(identifier.Name.Name, "eval", StringComparison.Ordinal);
        if (call.IsDirectEval && !isDirectEval)
        {
            reason = "Direct eval call-target preparation requires an eval identifier target.";
            return false;
        }

        if (!TryResolveActivationCallTargetSlot(identifier, slotLayout, out var slotIndex))
        {
            if (!isDirectEval &&
                !allowsDynamicIdentifiers &&
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
                callTargetConstants,
                argsStartIndex: 1,
                call,
                allowsDynamicIdentifiers,
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
            callTargetConstants,
            argsStartIndex: 1,
            call,
            allowsDynamicIdentifiers,
            out reason);
    }

    private static bool TryAppendCalleeOptionalIdentifierCallTarget(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        PackedExpressionOp call,
        int callIndex,
        bool allowsDynamicIdentifiers,
        out string reason)
    {
        if (callIndex < 2)
        {
            reason = string.Empty;
            return false;
        }

        var callTarget = expressionProgram.GetOperation(0);
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget)
        {
            reason = string.Empty;
            return false;
        }

        var jumpOp = expressionProgram.GetOperation(1);
        if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish || !jumpOp.ReplaceWithUndefined)
        {
            reason = string.Empty;
            return false;
        }

        var identifier = callTarget.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        if (!TryResolveActivationCallTargetSlot(identifier, slotLayout, out var slotIndex))
        {
            if (callTarget.IsArguments ||
                !allowsDynamicIdentifiers ||
                identifier.FlatSlotId >= 0)
            {
                reason = callTarget.IsArguments
                    ? "arguments call targets are outside the optional identifier call-target preparation boundary."
                    : "Optional identifier call targets require an activation-resolved identifier slot or admitted dynamic identifier operations.";
                return false;
            }

            var dynamicNameIndex = stringConstants.Count;
            stringConstants.Add(identifier.Name.Name ?? string.Empty);
            var dynamicPrepareIndex = unified.Count;
            unified.Add(new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget,
                dynamicNameIndex));

            if (!TryAppendCallArguments(
                    expressionProgram,
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    argsStartIndex: 2,
                    call,
                    callIndex,
                    allowsDynamicIdentifiers,
                    out reason))
            {
                return false;
            }

            unified[dynamicPrepareIndex] = new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget,
                dynamicNameIndex | (unified.Count << 16));
            return true;
        }

        var nameIndex = stringConstants.Count;
        stringConstants.Add(identifier.Name.Name ?? string.Empty);
        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(
            UnifiedBytecodeCallTargetKind.Identifier,
            slotIndex,
            nameIndex));

        var prepareIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareIdentifierOptionalCallTarget,
            callTargetConstantIndex));

        if (!TryAppendCallArguments(
                expressionProgram,
                slotLayout,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                argsStartIndex: 2,
                call,
                callIndex,
                allowsDynamicIdentifiers,
                out reason))
        {
            return false;
        }

        unified[prepareIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareIdentifierOptionalCallTarget,
            callTargetConstantIndex | (unified.Count << 16));
        return true;
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

        // Case 4: optional-chain plain call — a?.b.c(args) / a.x?.b.c(args)
        // Pattern: [base/prefix..., GetNamedProperty(opt,b), JumpIfShortCircuited, LoadNamedCallTarget(c), args..., Call]
        if (callTargetIndexInProgram >= 3)
        {
            var maybeShortCircuit = expressionProgram.GetOperation(callTargetIndexInProgram - 1);
            if (maybeShortCircuit.Kind == ExpressionOpKind.JumpIfShortCircuited)
            {
                return TryAppendOptionalChainPlainCallTarget(
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

        // Case 5: optional-chain receiver-optional call — a?.b?.c(args)
        // Pattern: [base, GetNamedProperty(opt,b), JumpIfShortCircuited, JumpIfNullish(RWU), LoadNamedCallTarget(c), args..., Call]
        if (callTargetIndexInProgram == 4)
        {
            var maybeNullishJump = expressionProgram.GetOperation(callTargetIndexInProgram - 1);
            var maybeShortCircuit = expressionProgram.GetOperation(callTargetIndexInProgram - 2);
            if (maybeNullishJump is { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true } &&
                maybeShortCircuit.Kind == ExpressionOpKind.JumpIfShortCircuited)
            {
                return TryAppendOptionalChainReceiverOptionalCallTarget(
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
                callTargetConstants,
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
                callTargetConstants,
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
                callTargetConstants,
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

        // Case 6: optional-chain computed plain call — a?.b[k](args)
        // Pattern: [base, GetNamedProperty(opt,b), JumpIfShortCircuited, key, LoadComputedCallTarget, args..., Call]
        if (callTargetIndexInProgram == 4)
        {
            var maybeShortCircuit = expressionProgram.GetOperation(callTargetIndexInProgram - 2);
            if (maybeShortCircuit.Kind == ExpressionOpKind.JumpIfShortCircuited)
            {
                return TryAppendOptionalChainComputedPlainCallTarget(
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

        var keyStartIndex = FindComputedCallKeyStart(expressionProgram, callTargetIndexInProgram);
        if (!TryAppendNamedReceiverOperations(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                keyStartIndex,
                allowDeepChain: true,
                out reason))
        {
            return false;
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: keyStartIndex,
                endExclusive: callTargetIndexInProgram,
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
                callTargetConstants,
            callTargetIndexInProgram + 1,
            call,
            callIndex,
            out reason);
    }

    private static int FindComputedCallKeyStart(
        ExpressionProgram expressionProgram,
        int callTargetIndexInProgram)
    {
        var stringConstants = expressionProgram.StringConstants.AsSpan();
        var keyStartIndex = 1;
        while (keyStartIndex < callTargetIndexInProgram &&
               IsPlainNamedPropertyRead(expressionProgram.GetOperation(keyStartIndex), stringConstants))
        {
            keyStartIndex++;
        }

        return keyStartIndex;
    }

    private static bool IsPlainNamedPropertyRead(
        PackedExpressionOp operation,
        ReadOnlySpan<string> stringConstants)
    {
        return operation.Kind == ExpressionOpKind.GetNamedProperty &&
               !operation.IsOptional &&
               !operation.ShortCircuitOnNullishTarget &&
               !operation.GetString(stringConstants).IsPrivateName();
    }

    private static bool TryAppendNamedSuperCallTargetPreparation(
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
        var callTargetIndexInProgram = FindFirstOperation(expressionProgram, ExpressionOpKind.LoadNamedSuperCallTarget);
        if (callTargetIndexInProgram < 0)
        {
            reason = string.Empty;
            return false;
        }

        if (callTargetIndexInProgram != 0)
        {
            reason = "Named super call targets must be the first operation in the invocation boundary.";
            return false;
        }

        var callTarget = expressionProgram.GetOperation(callTargetIndexInProgram);
        var propertyName = callTarget.GetString(expressionProgram.StringConstants.AsSpan());
        if (propertyName.IsPrivateName())
        {
            reason = "Private named super call targets are outside the call-target preparation boundary.";
            return false;
        }

        var nameIndex = stringConstants.Count;
        stringConstants.Add(propertyName);
        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(
            UnifiedBytecodeCallTargetKind.NamedSuperMember,
            NameConstantIndex: nameIndex));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget,
            callTargetConstantIndex));

        return TryAppendCallArguments(
            expressionProgram,
            slotLayout,
            unified,
            literalConstants,
            stringConstants,
                callTargetConstants,
            callTargetIndexInProgram + 1,
            call,
            callIndex,
            out reason);
    }

    private static bool TryAppendComputedSuperCallTargetPreparation(
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
        var callTargetIndexInProgram = FindFirstOperation(expressionProgram, ExpressionOpKind.LoadComputedSuperCallTarget);
        if (callTargetIndexInProgram < 0)
        {
            reason = string.Empty;
            return false;
        }

        var keyEnd = callTargetIndexInProgram;
        if (expressionProgram.GetOperation(keyEnd - 1).Kind == ExpressionOpKind.EnsureSuperReference)
        {
            keyEnd--;
        }

        var hasResolvedKey = keyEnd == 2 &&
                             expressionProgram.GetOperation(1).Kind == ExpressionOpKind.ResolvePropertyKey;
        if (keyEnd is not 1 && !hasResolvedKey)
        {
            reason = "Computed super call targets require exactly one computed key operand.";
            return false;
        }

        if (!TryAppendComputedPropertyKeyLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return false;
        }

        if (hasResolvedKey)
        {
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
        }

        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(UnifiedBytecodeCallTargetKind.ComputedSuperMember));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget,
            callTargetConstantIndex));

        return TryAppendCallArguments(
            expressionProgram,
            slotLayout,
            unified,
            literalConstants,
            stringConstants,
                callTargetConstants,
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
                callTargetConstants,
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

    // Case 6: a?.b[k](args) — optional-start chain, computed plain non-optional call.
    // Lowers to: LoadSlot(base), JumpIfNullishReplaceUndefined(end), GetNamedProperty(b),
    //            key-load, PrepareComputedCallTarget, args, (end:)
    private static bool TryAppendOptionalChainComputedPlainCallTarget(
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
        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        var nullishJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        var firstHop = expressionProgram.GetOperation(1);
        var receiverName = firstHop.GetString(expressionStringConstants);
        if (receiverName.IsPrivateName())
        {
            reason = "Private named member call targets are outside the call-target preparation boundary.";
            return false;
        }

        var receiverNameIndex = stringConstants.Count;
        stringConstants.Add(receiverName);
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));

        var keyIndex = callTargetIndexInProgram - 1;
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

        if (!TryAppendCallArguments(
                expressionProgram,
                slotLayout,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                callTargetIndexInProgram + 1,
                call,
                callIndex,
                out reason))
        {
            return false;
        }

        unified[nullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            unified.Count);
        return true;
    }

    // Case 4: a?.b.c(args) / a.x?.b.c(args) — optional-start chain, plain non-optional call.
    // Lowers to: prefix-load, JumpIfNullishReplaceUndefined(end), GetNamedProperty(b),
    //            PrepareNamedCallTarget(c), args, (end:)
    private static bool TryAppendOptionalChainPlainCallTarget(
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
        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var shortCircuitIndex = callTargetIndexInProgram - 1;
        var optionalHopIndex = shortCircuitIndex - 1;

        if (optionalHopIndex < 1)
        {
            reason = string.Empty;
            return false;
        }

        if (!TryAppendNamedReceiverOperations(
                expressionProgram,
                activationSlots,
                unified,
                stringConstants,
                optionalHopIndex,
                allowDeepChain: true,
                out reason))
        {
            return false;
        }

        // Emit JumpIfNullishReplaceUndefined — backpatch after args
        var nullishJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        // Emit GetNamedProperty(b) — receiver for c()
        var firstHop = expressionProgram.GetOperation(optionalHopIndex);
        var receiverNameIndex = stringConstants.Count;
        stringConstants.Add(firstHop.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));

        // Emit PrepareNamedCallTarget(c)
        var calleeTarget = expressionProgram.GetOperation(callTargetIndexInProgram);
        var calleeName = calleeTarget.GetString(expressionStringConstants);
        if (calleeName.IsPrivateName())
        {
            reason = "Private named member call targets are outside the call-target preparation boundary.";
            return false;
        }

        var calleeNameIndex = stringConstants.Count;
        stringConstants.Add(calleeName);
        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(
            UnifiedBytecodeCallTargetKind.NamedMember,
            NameConstantIndex: calleeNameIndex));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.PrepareNamedCallTarget, callTargetConstantIndex));

        // Emit args
        if (!TryAppendCallArguments(
                expressionProgram,
                slotLayout,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                callTargetIndexInProgram + 1,
                call,
                callIndex,
                out reason))
        {
            return false;
        }

        // Backpatch JumpIfNullishReplaceUndefined to current position (after call)
        unified[nullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, unified.Count);
        return true;
    }

    // Case 5: a?.b?.c(args) — double-optional chain, receiver-optional call.
    // Lowers to: LoadSlot(base), JumpIfNullishReplaceUndefined(end), GetNamedProperty(b),
    //            PrepareNamedOptionalCallTarget(c, end), args, (end:)
    private static bool TryAppendOptionalChainReceiverOptionalCallTarget(
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
        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();

        // Emit base load
        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        // Emit JumpIfNullishReplaceUndefined — backpatch after args
        var nullishJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        // Emit GetNamedProperty(b) — receiver for ?.c()
        var firstHop = expressionProgram.GetOperation(1);
        var receiverNameIndex = stringConstants.Count;
        stringConstants.Add(firstHop.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));

        // Emit PrepareNamedOptionalCallTarget(c) with IsOptionalReceiverCheck:true
        var calleeTarget = expressionProgram.GetOperation(callTargetIndexInProgram);
        var calleeName = calleeTarget.GetString(expressionStringConstants);
        if (calleeName.IsPrivateName())
        {
            reason = "Private named member call targets are outside the call-target preparation boundary.";
            return false;
        }

        var calleeNameIndex = stringConstants.Count;
        stringConstants.Add(calleeName);
        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(
            UnifiedBytecodeCallTargetKind.NamedMember,
            NameConstantIndex: calleeNameIndex,
            IsOptionalReceiverCheck: true));

        var prepareIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget, callTargetConstantIndex));

        // Emit args
        if (!TryAppendCallArguments(
                expressionProgram,
                slotLayout,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                callTargetIndexInProgram + 1,
                call,
                callIndex,
                out reason))
        {
            return false;
        }

        // Backpatch both jumps to point past the call
        var chainEnd = unified.Count;
        unified[nullishJumpIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, chainEnd);
        unified[prepareIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget,
            callTargetConstantIndex | (chainEnd << 16));
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
        var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        if (!TryResolveActivationSlot(identifier, slotLayout, out slotIndex))
        {
            if (operation.IsArguments)
            {
                reason = "arguments typeof is not supported.";
                return false;
            }

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
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
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
            callTargetConstants,
            argsStartIndex,
            call,
            expressionProgram.OperationCount - 1,
            allowsDynamicIdentifiers: false,
            out reason);
    }

    private static bool TryAppendCallArguments(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        int argsStartIndex,
        PackedExpressionOp call,
        bool allowsDynamicIdentifiers,
        out string reason)
    {
        return TryAppendCallArguments(
            expressionProgram,
            slotLayout,
            unified,
            literalConstants,
            stringConstants,
            callTargetConstants,
            argsStartIndex,
            call,
            expressionProgram.OperationCount - 1,
            allowsDynamicIdentifiers,
            out reason);
    }

    private static bool TryAppendCallArguments(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        int argsStartIndex,
        PackedExpressionOp call,
        int callIndex,
        out string reason)
    {
        return TryAppendCallArguments(
            expressionProgram,
            slotLayout,
            unified,
            literalConstants,
            stringConstants,
            callTargetConstants,
            argsStartIndex,
            call,
            callIndex,
            allowsDynamicIdentifiers: false,
            out reason);
    }

    private static bool TryAppendCallArguments(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        int argsStartIndex,
        PackedExpressionOp call,
        int callIndex,
        bool allowsDynamicIdentifiers,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;

        // Span-walk: each logical argument is a single simple operand or a multi-op
        // binary/array/object/template literal span. Validate argument count via
        // span-walk (gh2705).
        var argCount = 0;
        var operationIndex = argsStartIndex;
        while (operationIndex < callIndex)
        {
            var op = expressionProgram.GetOperation(operationIndex);
            if (op.Kind == ExpressionOpKind.CreateArray)
            {
                if (!TryAppendSimpleArrayLiteralSpan(
                        expressionProgram, operationIndex, activationSlots,
                        unified, literalConstants, stringConstants, callTargetConstants, slotLayout, out var arraySpanLen, out reason,
                        allowsDynamicIdentifiers))
                {
                    return false;
                }

                operationIndex += arraySpanLen;
            }
            else if (op.Kind == ExpressionOpKind.CreateObject)
            {
                if (!TryAppendSimpleObjectLiteralSpan(
                        expressionProgram, operationIndex, activationSlots,
                        unified, literalConstants, stringConstants, callTargetConstants, slotLayout, out var objSpanLen, out reason,
                        allowsDynamicIdentifiers))
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
                        unified, literalConstants, out var templateSpanLen, out reason,
                        allowsDynamicIdentifiers,
                        stringConstants))
                {
                    return false;
                }

                operationIndex += templateSpanLen;
            }
            else if (TryAppendSimpleBinaryCallArgumentSpan(
                         expressionProgram,
                         operationIndex,
                         callIndex,
                         activationSlots,
                         allowsDynamicIdentifiers,
                         unified,
                         literalConstants,
                         stringConstants,
                         out var binarySpanLen,
                         out reason))
            {
                operationIndex += binarySpanLen;
            }
            else
            {
                // Spread arguments push the iterable value; flattening happens at the
                // invocation boundary using the registered spread mask (gh2676).
                var appendedArgument = call.IsDirectEval
                    ? TryAppendSimpleOperandLoad(op, expressionProgram, activationSlots, unified, literalConstants, out reason) ||
                      TryAppendDirectEvalArgumentLoad(op, expressionProgram, unified, stringConstants, out reason)
                    : TryAppendSimpleOperandLoadWithDynamic(
                        op,
                        expressionProgram,
                        activationSlots,
                        allowsDynamicIdentifiers,
                        unified,
                        literalConstants,
                        stringConstants,
                        out reason);

                if (!appendedArgument)
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
            EncodeCallBoundaryOperand(call.ArgumentCount, spreadMaskIndex, call.IsDirectEval)));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendSimpleBinaryCallArgumentSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        int endExclusive,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out int spanLength,
        out string reason)
    {
        if (startIndex + 2 >= endExclusive)
        {
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        var left = expressionProgram.GetOperation(startIndex);
        var right = expressionProgram.GetOperation(startIndex + 1);
        var binary = expressionProgram.GetOperation(startIndex + 2);
        if (binary.Kind != ExpressionOpKind.Binary)
        {
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        if (!IsSupportedBinaryOperator(binary.Operator))
        {
            spanLength = 0;
            reason = "Call arguments only admit supported simple binary operators.";
            return false;
        }

        if (!CanAppendSimpleOperandLoadWithDynamic(left, expressionProgram, activationSlots, allowsDynamicIdentifiers) ||
            !CanAppendSimpleOperandLoadWithDynamic(right, expressionProgram, activationSlots, allowsDynamicIdentifiers))
        {
            spanLength = 0;
            reason = "Call binary arguments require simple activation-resolved or admitted dynamic operands.";
            return false;
        }

        if (!TryAppendSimpleOperandLoadWithDynamic(
                left,
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason) ||
            !TryAppendSimpleOperandLoadWithDynamic(
                right,
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason))
        {
            spanLength = 0;
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)binary.Operator));
        spanLength = 3;
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendDirectEvalArgumentLoad(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (operation.Kind != ExpressionOpKind.LoadIdentifier || operation.IsArguments)
        {
            reason = $"Unsupported direct eval argument op '{operation.Kind}'.";
            return false;
        }

        var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        var nameIndex = stringConstants.Count;
        stringConstants.Add(identifier.Name.Name ?? string.Empty);
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadDynamicIdentifier, nameIndex));
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
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        out int spanLength,
        out string reason,
        bool allowsDynamicIdentifiers = false)
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

            if (slotLayout is not null &&
                callTargetConstants is not null &&
                TryMeasureSimpleDirectNamedCallOperandSpan(
                    expressionProgram,
                    i,
                    activationSlots,
                    out _,
                    out _,
                    out var callElementSpanLength))
            {
                if (!TryAppendSimpleDirectNamedCallOperandSpan(
                        expressionProgram,
                        i,
                        activationSlots,
                        slotLayout,
                        unified,
                        literalConstants,
                        stringConstants,
                        callTargetConstants,
                        out reason))
                {
                    spanLength = 0;
                    return false;
                }

                i += callElementSpanLength;
            }
            else if (TryAppendSimpleOperandLoadWithDynamic(
                         elementOp,
                         expressionProgram,
                         activationSlots,
                         allowsDynamicIdentifiers,
                         unified,
                         literalConstants,
                         stringConstants,
                         out reason))
            {
                i++;
            }
            else
            {
                // Non-simple op — element scan is done; the array literal ends here.
                // Undo the failed load (TryAppendSimpleOperandLoad adds nothing on failure).
                break;
            }

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
    // Emits: CreateObject, then N property triples/spreads:
    //   Static:   [simple-value-load, DefineObjectProperty(non-private, no name inference)]
    //   Computed: [simple-key-load or simple-binary-key-expression, ResolvePropertyKey,
    //              simple-value-load, DefineComputedObjectProperty(no name inference)]
    //   Spread:   [simple-spread-source-load, ObjectSpread]
    private static bool TryAppendSimpleObjectLiteralSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        out int spanLength,
        out string reason,
        bool allowsDynamicIdentifiers = false)
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
            if (slotLayout is not null &&
                callTargetConstants is not null &&
                TryMeasureSimpleIdentifierCallOperandSpan(
                    expressionProgram,
                    i,
                    slotLayout,
                    out var callKeySpanLength) &&
                i + callKeySpanLength < expressionProgram.OperationCount &&
                expressionProgram.GetOperation(i + callKeySpanLength).Kind == ExpressionOpKind.ResolvePropertyKey)
            {
                if (!TryAppendSimpleIdentifierCallOperandSpan(
                        expressionProgram,
                        i,
                        slotLayout,
                        unified,
                        stringConstants,
                        callTargetConstants,
                        out reason))
                {
                    spanLength = 0;
                    return false;
                }

                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
                i += callKeySpanLength + 1;

                if (i >= expressionProgram.OperationCount)
                {
                    spanLength = 0;
                    reason = "Expected value operand after ResolvePropertyKey.";
                    return false;
                }

                var valueOp = expressionProgram.GetOperation(i);
                if (!TryAppendSimpleOperandLoadWithDynamic(
                        valueOp,
                        expressionProgram,
                        activationSlots,
                        allowsDynamicIdentifiers,
                        unified,
                        literalConstants,
                        stringConstants,
                        out reason))
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
                continue;
            }

            if (TryMeasureSimpleBinaryOperandSpan(
                    expressionProgram,
                    i,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    out var keySpanLength,
                    out var keyOperator) &&
                i + keySpanLength < expressionProgram.OperationCount &&
                expressionProgram.GetOperation(i + keySpanLength).Kind == ExpressionOpKind.ResolvePropertyKey)
            {
                TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(i),
                    expressionProgram,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    out _);
                TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(i + 1),
                    expressionProgram,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    out _);
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)keyOperator));
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
                i += keySpanLength + 1;

                if (i >= expressionProgram.OperationCount)
                {
                    spanLength = 0;
                    reason = "Expected value operand after ResolvePropertyKey.";
                    return false;
                }

                var valueOp = expressionProgram.GetOperation(i);
                if (!TryAppendSimpleOperandLoadWithDynamic(
                        valueOp,
                        expressionProgram,
                        activationSlots,
                        allowsDynamicIdentifiers,
                        unified,
                        literalConstants,
                        stringConstants,
                        out reason))
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
                continue;
            }

            var firstOp = expressionProgram.GetOperation(i);
            if (slotLayout is not null &&
                callTargetConstants is not null &&
                TryMeasureSimpleDirectNamedCallOperandSpan(
                    expressionProgram,
                    i,
                    activationSlots,
                    out _,
                    out _,
                    out var spreadCallSpanLength) &&
                i + spreadCallSpanLength < expressionProgram.OperationCount &&
                expressionProgram.GetOperation(i + spreadCallSpanLength).Kind == ExpressionOpKind.ObjectSpread)
            {
                if (!TryAppendSimpleDirectNamedCallOperandSpan(
                        expressionProgram,
                        i,
                        activationSlots,
                        slotLayout,
                        unified,
                        literalConstants,
                        stringConstants,
                        callTargetConstants,
                        out reason))
                {
                    spanLength = 0;
                    return false;
                }

                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ObjectSpread));
                i += spreadCallSpanLength + 1;
                continue;
            }

            if (!TryAppendSimpleOperandLoadWithDynamic(
                    firstOp,
                    expressionProgram,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    out reason))
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
                if (!TryAppendSimpleOperandLoadWithDynamic(
                        valueOp,
                        expressionProgram,
                        activationSlots,
                        allowsDynamicIdentifiers,
                        unified,
                        literalConstants,
                        stringConstants,
                        out reason))
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
            else if (secondOp.Kind == ExpressionOpKind.ObjectSpread)
            {
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ObjectSpread));
                i++;
            }
            else
            {
                spanLength = 0;
                reason = "Object methods, object accessors, private names, and name inference are not admitted in simple object literals.";
                return false;
            }
        }

        spanLength = i - startIndex;
        reason = string.Empty;
        return true;
    }

    private static bool TryMeasureSimpleIdentifierCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        UnifiedBytecodeSlotLayout slotLayout,
        out int spanLength)
    {
        if (startIndex + 1 >= expressionProgram.OperationCount)
        {
            spanLength = 0;
            return false;
        }

        var callTarget = expressionProgram.GetOperation(startIndex);
        var call = expressionProgram.GetOperation(startIndex + 1);
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget ||
            callTarget.IsArguments ||
            call.Kind != ExpressionOpKind.Call ||
            !call.HasExplicitThis ||
            call.IsDirectEval ||
            call.ArgumentCount != 0 ||
            call.SpreadMaskConstantIndex >= 0)
        {
            spanLength = 0;
            return false;
        }

        var identifier = callTarget.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        if (!TryResolveActivationCallTargetSlot(identifier, slotLayout, out _))
        {
            spanLength = 0;
            return false;
        }

        spanLength = 2;
        return true;
    }

    private static bool TryAppendSimpleIdentifierCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        out string reason)
    {
        if (!TryMeasureSimpleIdentifierCallOperandSpan(
                expressionProgram,
                startIndex,
                slotLayout,
                out _))
        {
            reason = "Computed object keys only admit activation-resolved zero-argument identifier calls.";
            return false;
        }

        var callTarget = expressionProgram.GetOperation(startIndex);
        var identifier = callTarget.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        if (!TryResolveActivationCallTargetSlot(identifier, slotLayout, out var slotIndex))
        {
            reason = "Computed object key call target requires an activation-resolved identifier slot.";
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
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.CallInvocationBoundary,
            EncodeCallBoundaryOperand(0, spreadMaskIndex: -1, isDirectEval: false)));
        reason = string.Empty;
        return true;
    }

    private static bool TryMeasureSimpleDirectNamedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        out int callIndex,
        out int argumentCount,
        out int spanLength)
    {
        callIndex = -1;
        argumentCount = 0;
        spanLength = 0;
        if (startIndex + 2 >= expressionProgram.OperationCount)
        {
            return false;
        }

        if (!CanAppendSimpleOperandLoad(expressionProgram.GetOperation(startIndex), expressionProgram, activationSlots))
        {
            return false;
        }

        var callTarget = expressionProgram.GetOperation(startIndex + 1);
        if (callTarget.Kind != ExpressionOpKind.LoadNamedCallTarget ||
            callTarget.IsOptional ||
            callTarget.ShortCircuitOnNullishTarget ||
            callTarget.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
        {
            return false;
        }

        var operationIndex = startIndex + 2;
        while (operationIndex < expressionProgram.OperationCount &&
               CanAppendSimpleOperandLoad(expressionProgram.GetOperation(operationIndex), expressionProgram, activationSlots))
        {
            argumentCount++;
            operationIndex++;
        }

        if (operationIndex >= expressionProgram.OperationCount)
        {
            return false;
        }

        var call = expressionProgram.GetOperation(operationIndex);
        if (call.Kind != ExpressionOpKind.Call ||
            !call.HasExplicitThis ||
            call.IsDirectEval ||
            call.SpreadMaskConstantIndex >= 0 ||
            call.ArgumentCount != argumentCount)
        {
            argumentCount = 0;
            return false;
        }

        callIndex = operationIndex;
        spanLength = operationIndex - startIndex + 1;
        return true;
    }

    private static bool TryAppendSimpleDirectNamedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        out string reason)
    {
        if (!TryMeasureSimpleDirectNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                out var callIndex,
                out var argumentCount,
                out _))
        {
            reason = "Spread sources only admit direct named member calls with simple arguments.";
            return false;
        }

        if (!TryAppendSimpleOperandLoad(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return false;
        }

        var callTarget = expressionProgram.GetOperation(startIndex + 1);
        var callTargetNameIndex = stringConstants.Count;
        stringConstants.Add(callTarget.GetString(expressionProgram.StringConstants.AsSpan()));
        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(
            UnifiedBytecodeCallTargetKind.NamedMember,
            NameConstantIndex: callTargetNameIndex));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareNamedCallTarget,
            callTargetConstantIndex));

        for (var operationIndex = startIndex + 2; operationIndex < callIndex; operationIndex++)
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
            EncodeCallBoundaryOperand(argumentCount, spreadMaskIndex: -1, isDirectEval: false)));
        reason = string.Empty;
        return true;
    }

    private static bool TryMeasureSimpleObjectLiteralSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        out int spanLength)
    {
        if (expressionProgram.GetOperation(startIndex).Kind != ExpressionOpKind.CreateObject)
        {
            spanLength = 0;
            return false;
        }

        var stringConstants = expressionProgram.StringConstants.AsSpan();
        var i = startIndex + 1;
        while (i < expressionProgram.OperationCount)
        {
            if (TryMeasureSimpleBinaryOperandSpan(
                    expressionProgram,
                    i,
                    activationSlots,
                    out var keySpanLength,
                    out _) &&
                i + keySpanLength < expressionProgram.OperationCount &&
                expressionProgram.GetOperation(i + keySpanLength).Kind == ExpressionOpKind.ResolvePropertyKey)
            {
                i += keySpanLength + 1;
                if (i >= expressionProgram.OperationCount ||
                    !CanAppendSimpleOperandLoad(expressionProgram.GetOperation(i), expressionProgram, activationSlots))
                {
                    spanLength = 0;
                    return false;
                }

                i++;
                if (i >= expressionProgram.OperationCount)
                {
                    spanLength = 0;
                    return false;
                }

                var computedDefineOp = expressionProgram.GetOperation(i);
                if (computedDefineOp.Kind != ExpressionOpKind.DefineComputedObjectProperty ||
                    computedDefineOp.AllowNameInference)
                {
                    spanLength = 0;
                    return false;
                }

                i++;
                continue;
            }

            var firstOp = expressionProgram.GetOperation(i);
            if (!CanAppendSimpleOperandLoad(firstOp, expressionProgram, activationSlots))
            {
                break;
            }

            i++;
            if (i >= expressionProgram.OperationCount)
            {
                spanLength = 0;
                return false;
            }

            var secondOp = expressionProgram.GetOperation(i);
            if (secondOp.Kind == ExpressionOpKind.DefineObjectProperty)
            {
                if (secondOp.GetString(stringConstants).IsPrivateName() ||
                    secondOp.AllowNameInference)
                {
                    spanLength = 0;
                    return false;
                }

                i++;
            }
            else if (secondOp.Kind == ExpressionOpKind.ResolvePropertyKey)
            {
                i++;
                if (i >= expressionProgram.OperationCount ||
                    !CanAppendSimpleOperandLoad(expressionProgram.GetOperation(i), expressionProgram, activationSlots))
                {
                    spanLength = 0;
                    return false;
                }

                i++;
                if (i >= expressionProgram.OperationCount)
                {
                    spanLength = 0;
                    return false;
                }

                var computedDefineOp = expressionProgram.GetOperation(i);
                if (computedDefineOp.Kind != ExpressionOpKind.DefineComputedObjectProperty ||
                    computedDefineOp.AllowNameInference)
                {
                    spanLength = 0;
                    return false;
                }

                i++;
            }
            else if (secondOp.Kind == ExpressionOpKind.ObjectSpread)
            {
                i++;
            }
            else
            {
                spanLength = 0;
                return false;
            }
        }

        spanLength = i - startIndex;
        return true;
    }

    private static bool TryMeasureSimpleBinaryOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        out int spanLength,
        out BinaryOperator binaryOperator)
    {
        return TryMeasureSimpleBinaryOperandSpan(
            expressionProgram,
            startIndex,
            activationSlots,
            allowsDynamicIdentifiers: false,
            out spanLength,
            out binaryOperator);
    }

    private static bool TryMeasureSimpleBinaryOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int spanLength,
        out BinaryOperator binaryOperator)
    {
        if (startIndex + 2 >= expressionProgram.OperationCount)
        {
            spanLength = 0;
            binaryOperator = default;
            return false;
        }

        var left = expressionProgram.GetOperation(startIndex);
        var right = expressionProgram.GetOperation(startIndex + 1);
        var binary = expressionProgram.GetOperation(startIndex + 2);
        if (CanAppendSimpleOperandLoadWithDynamic(left, expressionProgram, activationSlots, allowsDynamicIdentifiers) &&
            CanAppendSimpleOperandLoadWithDynamic(right, expressionProgram, activationSlots, allowsDynamicIdentifiers) &&
            binary.Kind == ExpressionOpKind.Binary &&
            IsSupportedBinaryOperator(binary.Operator))
        {
            spanLength = 3;
            binaryOperator = binary.Operator;
            return true;
        }

        spanLength = 0;
        binaryOperator = default;
        return false;
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
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        // Shape: [base, GetNamedProperty(non-optional, non-private)*, DuplicateTop, GetNamedProperty, rhs..., Binary, SetNamedProperty]
        // The final target may be private; receiver-chain hops stay ordinary only.
        // Minimum: 6 ops (rhs is a single simple operand).
        if (expressionProgram.OperationCount < 6)
        {
            reason = string.Empty;
            return false;
        }

        var stringTable = expressionProgram.StringConstants.AsSpan();
        var duplicateIndex = 1;
        while (duplicateIndex < expressionProgram.OperationCount)
        {
            var receiverRead = expressionProgram.GetOperation(duplicateIndex);
            if (receiverRead.Kind != ExpressionOpKind.GetNamedProperty)
            {
                break;
            }

            if (receiverRead.GetString(stringTable).IsPrivateName())
            {
                reason = "Private named compound property receiver reads are not supported.";
                return false;
            }

            if (receiverRead.IsOptional || receiverRead.ShortCircuitOnNullishTarget)
            {
                reason = string.Empty;
                return false;
            }

            duplicateIndex++;
        }

        if (duplicateIndex + 4 >= expressionProgram.OperationCount)
        {
            reason = string.Empty;
            return false;
        }

        var duplicateTarget = expressionProgram.GetOperation(duplicateIndex);
        var propertyRead = expressionProgram.GetOperation(duplicateIndex + 1);
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

        if (propertyRead.GetString(stringTable) != propertySet.GetString(stringTable))
        {
            reason = "Mismatched named compound property read/write operands are not supported.";
            return false;
        }

        if (!IsSupportedBinaryOperator(binary.Operator))
        {
            reason = $"Unsupported compound property binary operator '{binary.Operator}'.";
            return false;
        }

        if (!TryAppendActivationOrPlainDynamicIdentifierReadValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                stringConstants,
                out reason))
        {
            return false;
        }

        for (var operationIndex = 1; operationIndex < duplicateIndex; operationIndex++)
        {
            var receiverRead = expressionProgram.GetOperation(operationIndex);
            var receiverNameIndex = stringConstants.Count;
            stringConstants.Add(receiverRead.GetString(stringTable));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));
        }

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(propertyRead.GetString(stringTable));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet,
            propertyNameIndex));

        var rhsStart = duplicateIndex + 2;
        var rhsEnd = expressionProgram.OperationCount - 3;

        if (rhsStart == rhsEnd)
        {
            if (!TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(rhsStart),
                    expressionProgram,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
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
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 9)
        {
            reason = string.Empty;
            return false;
        }

        var suffixStart = expressionProgram.OperationCount - 7;
        if (suffixStart <= 1)
        {
            reason = string.Empty;
            return false;
        }

        var requireObjectCoercible = expressionProgram.GetOperation(suffixStart);
        var resolvePropertyKey = expressionProgram.GetOperation(suffixStart + 1);
        var duplicateTargetAndKey = expressionProgram.GetOperation(suffixStart + 2);
        var propertyRead = expressionProgram.GetOperation(suffixStart + 3);
        var rhs = expressionProgram.GetOperation(suffixStart + 4);
        var binary = expressionProgram.GetOperation(suffixStart + 5);
        var propertySet = expressionProgram.GetOperation(suffixStart + 6);
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

        if (!IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: 1,
                endExclusive: suffixStart,
                allowsDynamicIdentifiers))
        {
            reason = "Unsupported computed property key span.";
            return false;
        }

        var stagedUnified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        stagedUnified.AddRange(unified);

        var stagedLiterals = ImmutableArray.CreateBuilder<JsValue>();
        stagedLiterals.AddRange(literalConstants);

        var stagedStrings = ImmutableArray.CreateBuilder<string>();
        stagedStrings.AddRange(stringConstants);

        if (!TryAppendActivationOrPlainDynamicIdentifierReadValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                stagedUnified,
                stagedStrings,
                out reason))
        {
            return false;
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                startInclusive: 1,
                endExclusive: suffixStart,
                out reason,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.RequireObjectCoercible, 1));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet));

        if (!TryAppendSimpleOperandLoadWithDynamic(
                rhs,
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                out reason))
        {
            return false;
        }

        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)binary.Operator));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetComputedProperty));
        unified.Clear();
        unified.AddRange(stagedUnified);
        literalConstants.Clear();
        literalConstants.AddRange(stagedLiterals);
        stringConstants.Clear();
        stringConstants.AddRange(stagedStrings);
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryNamedLogicalPropertySet(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 10)
        {
            reason = string.Empty;
            return false;
        }

        // The final target may be private; receiver-chain hops stay ordinary only.
        var stringTable = expressionProgram.StringConstants.AsSpan();
        var duplicateIndex = 1;
        while (duplicateIndex < expressionProgram.OperationCount)
        {
            var receiverRead = expressionProgram.GetOperation(duplicateIndex);
            if (receiverRead.Kind != ExpressionOpKind.GetNamedProperty)
            {
                break;
            }

            if (receiverRead.GetString(stringTable).IsPrivateName())
            {
                reason = "Private named logical property receiver reads are not supported.";
                return false;
            }

            if (receiverRead.IsOptional || receiverRead.ShortCircuitOnNullishTarget)
            {
                reason = string.Empty;
                return false;
            }

            duplicateIndex++;
        }

        if (expressionProgram.OperationCount != duplicateIndex + 9)
        {
            reason = string.Empty;
            return false;
        }

        var duplicateTarget = expressionProgram.GetOperation(duplicateIndex);
        var propertyRead = expressionProgram.GetOperation(duplicateIndex + 1);
        var jump = expressionProgram.GetOperation(duplicateIndex + 2);
        var pop = expressionProgram.GetOperation(duplicateIndex + 3);
        var rhs = expressionProgram.GetOperation(duplicateIndex + 4);
        var propertySet = expressionProgram.GetOperation(duplicateIndex + 5);
        var duplicateAssignedValue = expressionProgram.GetOperation(duplicateIndex + 6);
        var swap = expressionProgram.GetOperation(duplicateIndex + 7);
        var cleanupPop = expressionProgram.GetOperation(duplicateIndex + 8);
        if (duplicateTarget.Kind != ExpressionOpKind.DuplicateTop ||
            propertyRead.Kind != ExpressionOpKind.GetNamedProperty ||
            jump.Kind is not (ExpressionOpKind.JumpIfFalse or ExpressionOpKind.JumpIfTrue or ExpressionOpKind.JumpIfNotNullish) ||
            pop.Kind != ExpressionOpKind.Pop ||
            propertySet.Kind != ExpressionOpKind.SetNamedProperty ||
            duplicateAssignedValue.Kind != ExpressionOpKind.DuplicateTop ||
            swap.Kind != ExpressionOpKind.SwapTopTwo ||
            cleanupPop.Kind != ExpressionOpKind.Pop)
        {
            reason = string.Empty;
            return false;
        }

        if (propertyRead.IsOptional || propertyRead.ShortCircuitOnNullishTarget)
        {
            reason = "Optional named logical property writes are not supported.";
            return false;
        }

        if (propertySet.AllowNameInference)
        {
            reason = "Named logical property writes with name inference are not supported.";
            return false;
        }

        if (jump.Target != duplicateIndex + 7)
        {
            reason = "Logical named property writes require jump target at cleanup start.";
            return false;
        }

        var propertyName = propertyRead.GetString(stringTable);
        if (propertyName != propertySet.GetString(stringTable))
        {
            reason = "Mismatched named logical property read/write operands are not supported.";
            return false;
        }

        var stagedUnified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        stagedUnified.AddRange(unified);

        var stagedLiterals = ImmutableArray.CreateBuilder<JsValue>();
        stagedLiterals.AddRange(literalConstants);

        var stagedStrings = ImmutableArray.CreateBuilder<string>();
        stagedStrings.AddRange(stringConstants);

        if (!TryAppendActivationOrPlainDynamicIdentifierReadValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                stagedUnified,
                stagedStrings,
                out reason))
        {
            return false;
        }

        for (var operationIndex = 1; operationIndex < duplicateIndex; operationIndex++)
        {
            var receiverRead = expressionProgram.GetOperation(operationIndex);
            var receiverNameIndex = stagedStrings.Count;
            stagedStrings.Add(receiverRead.GetString(stringTable));
            stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));
        }

        var propertyNameIndex = stagedStrings.Count;
        stagedStrings.Add(propertyName);
        stagedUnified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet,
            propertyNameIndex));

        var jumpOpCode = jump.Kind switch
        {
            ExpressionOpKind.JumpIfFalse => UnifiedBytecodeOpCode.JumpIfShortCircuitFalse,
            ExpressionOpKind.JumpIfTrue => UnifiedBytecodeOpCode.JumpIfShortCircuitTrue,
            _ => UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish
        };
        var jumpUnifiedIndex = stagedUnified.Count;
        stagedUnified.Add(new UnifiedBytecodeInstruction(jumpOpCode, 0));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));

        if (!TryAppendSimpleOperandLoadWithDynamic(
                rhs,
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                out reason))
        {
            return false;
        }

        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetNamedProperty, propertyNameIndex));
        var endJumpIndex = stagedUnified.Count;
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));

        var cleanupIndex = stagedUnified.Count;
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SwapTopTwo));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));

        stagedUnified[jumpUnifiedIndex] = new UnifiedBytecodeInstruction(jumpOpCode, cleanupIndex);
        stagedUnified[endJumpIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, stagedUnified.Count);

        unified.Clear();
        unified.AddRange(stagedUnified);
        literalConstants.Clear();
        literalConstants.AddRange(stagedLiterals);
        stringConstants.Clear();
        stringConstants.AddRange(stagedStrings);
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryComputedLogicalPropertySet(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 15)
        {
            reason = string.Empty;
            return false;
        }

        var propertySetIndex = expressionProgram.OperationCount - 6;
        var suffixStart = -1;
        var matchedLayout = false;
        PackedExpressionOp propertyRead = default;
        PackedExpressionOp jump = default;
        PackedExpressionOp propertySet = default;
        for (var rhsLength = 1; rhsLength <= 3; rhsLength += 2)
        {
            var candidateSuffixStart = propertySetIndex - 6 - rhsLength;
            if (candidateSuffixStart <= 1 ||
                !IsSupportedComputedPropertyKeySpan(
                    expressionProgram,
                    activationSlots,
                    startInclusive: 1,
                    endExclusive: candidateSuffixStart,
                    allowsDynamicIdentifiers))
            {
                continue;
            }

            var requireObjectCoercible = expressionProgram.GetOperation(candidateSuffixStart);
            var resolvePropertyKey = expressionProgram.GetOperation(candidateSuffixStart + 1);
            var duplicateTargetAndKey = expressionProgram.GetOperation(candidateSuffixStart + 2);
            propertyRead = expressionProgram.GetOperation(candidateSuffixStart + 3);
            jump = expressionProgram.GetOperation(candidateSuffixStart + 4);
            var pop = expressionProgram.GetOperation(candidateSuffixStart + 5);
            propertySet = expressionProgram.GetOperation(propertySetIndex);
            var duplicateAssignedValue = expressionProgram.GetOperation(propertySetIndex + 1);
            var duplicateAssignedValueAgain = expressionProgram.GetOperation(propertySetIndex + 2);
            var rotateTopThreeRight = expressionProgram.GetOperation(propertySetIndex + 3);
            var cleanupPop = expressionProgram.GetOperation(propertySetIndex + 4);
            var cleanupPop2 = expressionProgram.GetOperation(propertySetIndex + 5);
            if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible ||
                requireObjectCoercible.Depth != 1 ||
                resolvePropertyKey.Kind != ExpressionOpKind.ResolvePropertyKey ||
                duplicateTargetAndKey.Kind != ExpressionOpKind.DuplicateTopTwo ||
                propertyRead.Kind != ExpressionOpKind.GetComputedProperty ||
                jump.Kind is not (ExpressionOpKind.JumpIfFalse or ExpressionOpKind.JumpIfTrue or ExpressionOpKind.JumpIfNotNullish) ||
                pop.Kind != ExpressionOpKind.Pop ||
                propertySet.Kind != ExpressionOpKind.SetComputedProperty ||
                duplicateAssignedValue.Kind != ExpressionOpKind.DuplicateTop ||
                duplicateAssignedValueAgain.Kind != ExpressionOpKind.DuplicateTop ||
                rotateTopThreeRight.Kind != ExpressionOpKind.RotateTopThreeRight ||
                cleanupPop.Kind != ExpressionOpKind.Pop ||
                cleanupPop2.Kind != ExpressionOpKind.Pop)
            {
                continue;
            }

            suffixStart = candidateSuffixStart;
            matchedLayout = true;
            break;
        }

        if (!matchedLayout)
        {
            reason = string.Empty;
            return false;
        }

        if (propertyRead.ShortCircuitOnNullishTarget)
        {
            reason = "Optional computed logical property writes are not supported.";
            return false;
        }

        if (propertySet.AllowNameInference)
        {
            reason = "Computed logical property writes with name inference are not supported.";
            return false;
        }

        if (jump.Target != propertySetIndex + 3)
        {
            reason = "Logical computed property writes require jump target at cleanup start.";
            return false;
        }

        var stagedUnified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        stagedUnified.AddRange(unified);

        var stagedLiterals = ImmutableArray.CreateBuilder<JsValue>();
        stagedLiterals.AddRange(literalConstants);

        var stagedStrings = ImmutableArray.CreateBuilder<string>();
        stagedStrings.AddRange(stringConstants);

        if (!TryAppendActivationOrPlainDynamicIdentifierReadValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                stagedUnified,
                stagedStrings,
                out reason))
        {
            return false;
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                startInclusive: 1,
                endExclusive: suffixStart,
                out reason,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.RequireObjectCoercible, 1));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet));

        var jumpOpCode = jump.Kind switch
        {
            ExpressionOpKind.JumpIfFalse => UnifiedBytecodeOpCode.JumpIfShortCircuitFalse,
            ExpressionOpKind.JumpIfTrue => UnifiedBytecodeOpCode.JumpIfShortCircuitTrue,
            _ => UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish
        };
        var jumpUnifiedIndex = stagedUnified.Count;
        stagedUnified.Add(new UnifiedBytecodeInstruction(jumpOpCode, 0));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));

        if (!TryAppendComputedLogicalAssignmentRhsSpan(
                expressionProgram,
                activationSlots,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                allowsDynamicIdentifiers,
                startInclusive: suffixStart + 6,
                endExclusive: propertySetIndex,
                out reason))
        {
            return false;
        }

        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetComputedProperty));
        var endJumpIndex = stagedUnified.Count;
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));

        var cleanupIndex = stagedUnified.Count;
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SwapTopTwo));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SwapTopTwo));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));

        stagedUnified[jumpUnifiedIndex] = new UnifiedBytecodeInstruction(jumpOpCode, cleanupIndex);
        stagedUnified[endJumpIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, stagedUnified.Count);

        unified.Clear();
        unified.AddRange(stagedUnified);
        literalConstants.Clear();
        literalConstants.AddRange(stagedLiterals);
        stringConstants.Clear();
        stringConstants.AddRange(stagedStrings);
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendComputedLogicalAssignmentRhsSpan(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        bool allowsDynamicIdentifiers,
        int startInclusive,
        int endExclusive,
        out string reason)
    {
        if (startInclusive >= endExclusive)
        {
            reason = "Computed logical property writes require an RHS expression.";
            return false;
        }

        if (endExclusive - startInclusive == 1)
        {
            return TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startInclusive),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason);
        }

        if (endExclusive - startInclusive != 3)
        {
            reason = "Computed logical property writes only admit simple or simple-binary RHS spans.";
            return false;
        }

        var left = expressionProgram.GetOperation(startInclusive);
        var right = expressionProgram.GetOperation(startInclusive + 1);
        var binary = expressionProgram.GetOperation(startInclusive + 2);
        if (binary.Kind != ExpressionOpKind.Binary ||
            !IsSupportedBinaryOperator(binary.Operator))
        {
            reason = "Computed logical property binary RHS uses an unsupported operator.";
            return false;
        }

        if (!TryAppendSimpleOperandLoadWithDynamic(
                left,
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason) ||
            !TryAppendSimpleOperandLoadWithDynamic(
                right,
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)binary.Operator));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryNamedPropertySet(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
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

        if (propertySet.AllowNameInference)
        {
            reason = "Property writes with name inference are not supported.";
            return false;
        }

        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason))
        {
            return false;
        }

        var rhsStart = 1;
        var rhsEnd = expressionProgram.OperationCount - 2;

        if (rhsStart == rhsEnd)
        {
            if (!TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(rhsStart),
                    expressionProgram,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
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
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 4)
        {
            reason = string.Empty;
            return false;
        }

        var propertySet = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);
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

        var valueIndex = expressionProgram.OperationCount - 2;
        if (!IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: 1,
                endExclusive: valueIndex,
                allowsDynamicIdentifiers))
        {
            reason = "Unsupported computed property key span.";
            return false;
        }

        var stagedUnified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        stagedUnified.AddRange(unified);

        var stagedLiterals = ImmutableArray.CreateBuilder<JsValue>();
        stagedLiterals.AddRange(literalConstants);

        var stagedStrings = ImmutableArray.CreateBuilder<string>();
        stagedStrings.AddRange(stringConstants);

        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                out reason))
        {
            return false;
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                startInclusive: 1,
                endExclusive: valueIndex,
                out reason,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(valueIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                out reason))
        {
            return false;
        }

        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetComputedProperty));
        unified.Clear();
        unified.AddRange(stagedUnified);
        literalConstants.Clear();
        literalConstants.AddRange(stagedLiterals);
        stringConstants.Clear();
        stringConstants.AddRange(stagedStrings);
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryNestedNamedPropertySet(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 4)
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

        var stringTable = expressionProgram.StringConstants.AsSpan();
        if (propertySet.GetString(stringTable).IsPrivateName())
        {
            reason = "Private nested named property writes are not supported.";
            return false;
        }

        if (propertySet.AllowNameInference)
        {
            reason = "Nested named property writes with name inference are not supported.";
            return false;
        }

        var rhsStart = 1;
        while (rhsStart < expressionProgram.OperationCount - 1)
        {
            var receiverRead = expressionProgram.GetOperation(rhsStart);
            if (receiverRead.Kind != ExpressionOpKind.GetNamedProperty)
            {
                break;
            }

            if (receiverRead.GetString(stringTable).IsPrivateName())
            {
                reason = "Private nested named property receiver reads are not supported.";
                return false;
            }

            if (receiverRead.IsOptional || receiverRead.ShortCircuitOnNullishTarget)
            {
                reason = string.Empty;
                return false;
            }

            rhsStart++;
        }

        if (rhsStart < 2)
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

        for (var operationIndex = 1; operationIndex < rhsStart; operationIndex++)
        {
            var receiverRead = expressionProgram.GetOperation(operationIndex);
            var receiverNameIndex = stringConstants.Count;
            stringConstants.Add(receiverRead.GetString(stringTable));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));
        }

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
                    expressionProgram, rhsStart, activationSlots, unified, literalConstants, out var spanLen, out reason))
            {
                return false;
            }

            if (rhsStart + spanLen - 1 != rhsEnd)
            {
                reason = "Template literal RHS span does not match expected nested property-write boundary.";
                return false;
            }
        }

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(propertySet.GetString(stringTable));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetNamedProperty, propertyNameIndex));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryNamedPropertyUpdate(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
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

        if (!TryAppendActivationOrPlainDynamicIdentifierReadValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                stringConstants,
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

    private static bool TryAppendFirstBoundaryNestedNamedPropertyUpdate(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 3)
        {
            reason = string.Empty;
            return false;
        }

        var propertyUpdate = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);
        if (propertyUpdate.Kind != ExpressionOpKind.UpdateNamedProperty)
        {
            reason = string.Empty;
            return false;
        }

        var stringTable = expressionProgram.StringConstants.AsSpan();
        if (propertyUpdate.GetString(stringTable).IsPrivateName())
        {
            reason = "Private nested named property updates are not supported.";
            return false;
        }

        for (var operationIndex = 1; operationIndex < expressionProgram.OperationCount - 1; operationIndex++)
        {
            var receiverRead = expressionProgram.GetOperation(operationIndex);
            if (receiverRead.Kind != ExpressionOpKind.GetNamedProperty)
            {
                reason = string.Empty;
                return false;
            }

            if (receiverRead.GetString(stringTable).IsPrivateName())
            {
                reason = "Private nested named property receiver reads are not supported.";
                return false;
            }

            if (receiverRead.IsOptional || receiverRead.ShortCircuitOnNullishTarget)
            {
                reason = string.Empty;
                return false;
            }
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

        for (var operationIndex = 1; operationIndex < expressionProgram.OperationCount - 1; operationIndex++)
        {
            var receiverRead = expressionProgram.GetOperation(operationIndex);
            var receiverNameIndex = stringConstants.Count;
            stringConstants.Add(receiverRead.GetString(stringTable));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));
        }

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(propertyUpdate.GetString(stringTable));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.UpdateNamedProperty,
            EncodeUpdateOperand(propertyNameIndex, propertyUpdate)));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryComputedPropertyUpdate(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
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

        var propertyUpdateIndex = expressionProgram.OperationCount - 1;
        var propertyUpdate = expressionProgram.GetOperation(propertyUpdateIndex);
        if (propertyUpdate.Kind != ExpressionOpKind.UpdateComputedProperty)
        {
            reason = string.Empty;
            return false;
        }

        if (!IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: 1,
                endExclusive: propertyUpdateIndex,
                allowsDynamicIdentifiers))
        {
            reason = "Unsupported computed property key span.";
            return false;
        }

        var stagedUnified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        stagedUnified.AddRange(unified);

        var stagedLiterals = ImmutableArray.CreateBuilder<JsValue>();
        stagedLiterals.AddRange(literalConstants);

        var stagedStrings = ImmutableArray.CreateBuilder<string>();
        stagedStrings.AddRange(stringConstants);

        if (!TryAppendActivationOrPlainDynamicIdentifierReadValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                stagedUnified,
                stagedStrings,
                out reason))
        {
            return false;
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                startInclusive: 1,
                endExclusive: propertyUpdateIndex,
                out reason,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        stagedUnified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.UpdateComputedProperty,
            EncodeUpdateFlags(propertyUpdate)));
        unified.Clear();
        unified.AddRange(stagedUnified);
        literalConstants.Clear();
        literalConstants.AddRange(stagedLiterals);
        stringConstants.Clear();
        stringConstants.AddRange(stagedStrings);
        reason = string.Empty;
        return true;
    }

    // Handles delete a?.b and delete a.b?.c, preserving the source program's nullish branch shape.
    private static bool TryAppendFirstBoundaryOptionalNamedPropertyDelete(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 6)
        {
            reason = string.Empty;
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var jumpIndex = 1;
        while (jumpIndex < expressionProgram.OperationCount)
        {
            var operation = expressionProgram.GetOperation(jumpIndex);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget ||
                operation.GetString(expressionStringConstants).IsPrivateName())
            {
                break;
            }

            jumpIndex++;
        }

        if (jumpIndex >= expressionProgram.OperationCount)
        {
            reason = string.Empty;
            return false;
        }

        var deleteIndex = expressionProgram.OperationCount - 4;
        var endJumpIndexInProgram = expressionProgram.OperationCount - 3;
        var popIndex = expressionProgram.OperationCount - 2;
        var trueIndex = expressionProgram.OperationCount - 1;
        var deleteProperty = expressionProgram.GetOperation(deleteIndex);
        if (deleteIndex != jumpIndex + 1 ||
            expressionProgram.GetOperation(jumpIndex) is not { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: false } jumpIfNullish ||
            jumpIfNullish.Target != popIndex ||
            deleteProperty.Kind != ExpressionOpKind.DeleteNamedProperty ||
            deleteProperty.GetString(expressionStringConstants).IsPrivateName() ||
            expressionProgram.GetOperation(endJumpIndexInProgram).Kind != ExpressionOpKind.Jump ||
            expressionProgram.GetOperation(endJumpIndexInProgram).Target != expressionProgram.OperationCount ||
            expressionProgram.GetOperation(popIndex).Kind != ExpressionOpKind.Pop ||
            !IsTrueLiteral(expressionProgram, trueIndex))
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

        for (var index = 1; index < jumpIndex; index++)
        {
            var propertyRead = expressionProgram.GetOperation(index);
            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(propertyRead.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
        }

        var nullishJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        var deleteNameIndex = stringConstants.Count;
        stringConstants.Add(deleteProperty.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DeleteNamedProperty, deleteNameIndex));
        var endJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));

        var shortCircuitIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
        AddTrueLiteral(unified, literalConstants);

        unified[nullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            shortCircuitIndex);
        unified[endJumpIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, unified.Count);

        reason = string.Empty;
        return true;
    }

    private static bool TryAppendFirstBoundaryOptionalComputedPropertyDelete(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (TryAppendFirstBoundaryOptionalNamedThenComputedPropertyDelete(
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

        if (TryAppendFirstBoundaryOptionalNamedThenOptionalComputedPropertyDelete(
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

        return TryAppendFirstBoundaryOptionalComputedPropertyDeleteWithJump(
            expressionProgram,
            activationSlots,
            unified,
            literalConstants,
            stringConstants,
            out reason);
    }

    // Handles delete a?.b[k]. The source expression program carries the optional named hop,
    // but delete must short-circuit to true before evaluating the computed key.
    private static bool TryAppendFirstBoundaryOptionalNamedThenComputedPropertyDelete(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 4 ||
            expressionProgram.GetOperation(expressionProgram.OperationCount - 1).Kind != ExpressionOpKind.DeleteComputedProperty)
        {
            reason = string.Empty;
            return false;
        }

        var firstHop = expressionProgram.GetOperation(1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
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

        var nullishJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(firstHop.GetString(expressionProgram.StringConstants.AsSpan()));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: 2,
                endExclusive: expressionProgram.OperationCount - 1,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DeleteComputedProperty));
        var endJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));

        var shortCircuitIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
        AddTrueLiteral(unified, literalConstants);

        unified[nullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            shortCircuitIndex);
        unified[endJumpIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, unified.Count);

        reason = string.Empty;
        return true;
    }

    // Handles delete a?.b?.[k], preserving the source program's nullish branch shape.
    private static bool TryAppendFirstBoundaryOptionalNamedThenOptionalComputedPropertyDelete(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 7)
        {
            reason = string.Empty;
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var firstHop = expressionProgram.GetOperation(1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(expressionStringConstants).IsPrivateName())
        {
            reason = string.Empty;
            return false;
        }

        var jumpIndex = 2;
        var deleteIndex = expressionProgram.OperationCount - 4;
        var endJumpIndexInProgram = expressionProgram.OperationCount - 3;
        var popIndex = expressionProgram.OperationCount - 2;
        var trueIndex = expressionProgram.OperationCount - 1;
        if (deleteIndex <= jumpIndex + 1 ||
            expressionProgram.GetOperation(jumpIndex) is not { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: false } jumpIfNullish ||
            jumpIfNullish.Target != popIndex ||
            expressionProgram.GetOperation(deleteIndex).Kind != ExpressionOpKind.DeleteComputedProperty ||
            expressionProgram.GetOperation(endJumpIndexInProgram).Kind != ExpressionOpKind.Jump ||
            expressionProgram.GetOperation(endJumpIndexInProgram).Target != expressionProgram.OperationCount ||
            expressionProgram.GetOperation(popIndex).Kind != ExpressionOpKind.Pop ||
            !IsTrueLiteral(expressionProgram, trueIndex))
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

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(firstHop.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedPropertyOptional, propertyNameIndex));

        var nullishJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: jumpIndex + 1,
                endExclusive: deleteIndex,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DeleteComputedProperty));
        var endJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));

        var shortCircuitIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
        AddTrueLiteral(unified, literalConstants);

        unified[nullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            shortCircuitIndex);
        unified[endJumpIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, unified.Count);

        reason = string.Empty;
        return true;
    }

    // Handles delete a?.[k] and delete a.b?.[k], preserving the source program's nullish branch shape.
    private static bool TryAppendFirstBoundaryOptionalComputedPropertyDeleteWithJump(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 7)
        {
            reason = string.Empty;
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var jumpIndex = 1;
        while (jumpIndex < expressionProgram.OperationCount)
        {
            var operation = expressionProgram.GetOperation(jumpIndex);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget ||
                operation.GetString(expressionStringConstants).IsPrivateName())
            {
                break;
            }

            jumpIndex++;
        }

        var deleteIndex = expressionProgram.OperationCount - 4;
        var endJumpIndexInProgram = expressionProgram.OperationCount - 3;
        var popIndex = expressionProgram.OperationCount - 2;
        var trueIndex = expressionProgram.OperationCount - 1;
        if (deleteIndex <= jumpIndex + 1 ||
            expressionProgram.GetOperation(jumpIndex) is not { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: false } jumpIfNullish ||
            jumpIfNullish.Target != popIndex ||
            expressionProgram.GetOperation(deleteIndex).Kind != ExpressionOpKind.DeleteComputedProperty ||
            expressionProgram.GetOperation(endJumpIndexInProgram).Kind != ExpressionOpKind.Jump ||
            expressionProgram.GetOperation(endJumpIndexInProgram).Target != expressionProgram.OperationCount ||
            expressionProgram.GetOperation(popIndex).Kind != ExpressionOpKind.Pop ||
            !IsTrueLiteral(expressionProgram, trueIndex))
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

        for (var index = 1; index < jumpIndex; index++)
        {
            var propertyRead = expressionProgram.GetOperation(index);
            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(propertyRead.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
        }

        var nullishJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: jumpIndex + 1,
                endExclusive: deleteIndex,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DeleteComputedProperty));
        var endJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));

        var shortCircuitIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
        AddTrueLiteral(unified, literalConstants);

        unified[nullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            shortCircuitIndex);
        unified[endJumpIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, unified.Count);

        reason = string.Empty;
        return true;
    }

    private static void AddTrueLiteral(
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants)
    {
        var trueIndex = literalConstants.Count;
        literalConstants.Add(JsValue.True);
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, trueIndex));
    }

    private static bool IsTrueLiteral(ExpressionProgram expressionProgram, int operationIndex)
    {
        var operation = expressionProgram.GetOperation(operationIndex);
        return operation.Kind == ExpressionOpKind.LoadLiteral &&
               operation.GetLiteral(expressionProgram.LiteralConstants.AsSpan()).Equals(JsValue.True);
    }

    private static bool TryAppendFirstBoundaryNamedPropertyReadChain(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        bool allowsDynamicIdentifiers,
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

            if (propertyRead.IsOptional || propertyRead.ShortCircuitOnNullishTarget)
            {
                reason = string.Empty;
                return false;
            }
        }

        if (!TryAppendSimpleOperandLoadWithDynamic(
                baseLoad,
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason))
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

    // Handles: [activation-resolved base, GetNamedProperty(IsOptional:true, !ShortCircuitOnNullishTarget, non-private)]
    // Emits: LoadSlot, GetNamedPropertyOptional
    private static bool TryAppendFirstBoundaryOptionalNamedPropertyRead(
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

        var baseLoad = expressionProgram.GetOperation(0);
        var getNamedOp = expressionProgram.GetOperation(1);

        if (getNamedOp.Kind != ExpressionOpKind.GetNamedProperty ||
            !getNamedOp.IsOptional ||
            getNamedOp.ShortCircuitOnNullishTarget ||
            getNamedOp.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
        {
            reason = string.Empty;
            return false;
        }

        if (!TryAppendActivationValueLoad(baseLoad, expressionProgram, activationSlots, unified, out reason))
        {
            return false;
        }

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(getNamedOp.GetString(expressionProgram.StringConstants.AsSpan()));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedPropertyOptional, propertyNameIndex));
        reason = string.Empty;
        return true;
    }

    // Handles multi-hop optional named chains a?.b.c and a?.b?.c:
    //   [activation-resolved base,
    //    GetNamedProperty(non-optional, non-private)*,
    //    GetNamedProperty(IsOptional:true, !ShortCircuitOnNullishTarget, non-private),
    //    GetNamedProperty(ShortCircuitOnNullishTarget:true, non-private)+]
    // Emits a jump-based lowering that keeps the operand stack a plain JsValue[]:
    //   LoadSlot,
    //   [GetNamedProperty,]                               // non-optional receiver prefix
    //   JumpIfNullishReplaceUndefined(END), GetNamedProperty,   // first optional hop
    //   [JumpIfNullishReplaceUndefined(END),] GetNamedProperty, // each subsequent hop (jump only when ?. optional)
    //   END:
    // Every optional hop's jump targets the same chain end, so a nullish base/intermediate
    // short-circuits the remainder of the chain to undefined while a real-undefined
    // intermediate (a = { b: undefined }) still throws on the following plain read.
    private static bool TryAppendFirstBoundaryOptionalNamedPropertyReadChain(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 3)
        {
            reason = string.Empty;
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var optionalStartIndex = 1;
        while (optionalStartIndex < expressionProgram.OperationCount)
        {
            var prefixOp = expressionProgram.GetOperation(optionalStartIndex);
            if (prefixOp.Kind != ExpressionOpKind.GetNamedProperty ||
                prefixOp.IsOptional ||
                prefixOp.ShortCircuitOnNullishTarget)
            {
                break;
            }

            if (prefixOp.GetString(expressionStringConstants).IsPrivateName())
            {
                reason = "Private named property reads are not supported.";
                return false;
            }

            optionalStartIndex++;
        }

        if (optionalStartIndex >= expressionProgram.OperationCount)
        {
            reason = string.Empty;
            return false;
        }

        var firstHop = expressionProgram.GetOperation(optionalStartIndex);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(expressionStringConstants).IsPrivateName())
        {
            reason = string.Empty;
            return false;
        }

        for (var operationIndex = optionalStartIndex + 1; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            var op = expressionProgram.GetOperation(operationIndex);
            if (op.Kind != ExpressionOpKind.GetNamedProperty ||
                !op.ShortCircuitOnNullishTarget)
            {
                reason = string.Empty;
                return false;
            }

            if (op.GetString(expressionStringConstants).IsPrivateName())
            {
                reason = "Private named property reads are not supported.";
                return false;
            }
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

        List<int>? boundaryJumpIndices = null;
        for (var operationIndex = 1; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            var op = expressionProgram.GetOperation(operationIndex);

            // Each optional hop (the leading ?.b or prefixed b?.c and any ?. continuation) emits a boundary jump
            // that short-circuits the rest of the chain to undefined when its target is nullish.
            if (op.IsOptional)
            {
                boundaryJumpIndices ??= [];
                boundaryJumpIndices.Add(unified.Count);
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));
            }

            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(op.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
        }

        var chainEnd = unified.Count;
        if (boundaryJumpIndices is not null)
        {
            foreach (var jumpIndex in boundaryJumpIndices)
            {
                unified[jumpIndex] = new UnifiedBytecodeInstruction(
                    UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
                    chainEnd);
            }
        }

        reason = string.Empty;
        return true;
    }

    // Handles: [activation-resolved base, GetNamedProperty(non-optional, non-private)*,
    // GetNamedProperty(IsOptional:true, !SC, non-private), key..., GetComputedProperty(SC:true), GetNamedProperty(SC:true)*]
    // Emits: LoadSlot, GetNamedProperty*, JumpIfNullishReplaceUndefined(end), GetNamedProperty(b),
    // key..., GetComputedProperty, GetNamedProperty*
    private static bool TryAppendFirstBoundaryOptionalNamedThenComputed(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 4)
        {
            reason = string.Empty;
            return false;
        }

        var baseLoad = expressionProgram.GetOperation(0);
        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var optionalStartIndex = 1;
        while (optionalStartIndex < expressionProgram.OperationCount)
        {
            var prefixOp = expressionProgram.GetOperation(optionalStartIndex);
            if (prefixOp.Kind != ExpressionOpKind.GetNamedProperty ||
                prefixOp.IsOptional ||
                prefixOp.ShortCircuitOnNullishTarget)
            {
                break;
            }

            if (prefixOp.GetString(expressionStringConstants).IsPrivateName())
            {
                reason = "Private named property reads are not supported.";
                return false;
            }

            optionalStartIndex++;
        }

        if (optionalStartIndex >= expressionProgram.OperationCount)
        {
            reason = string.Empty;
            return false;
        }

        var firstPropOp = expressionProgram.GetOperation(optionalStartIndex);
        var computedSuffixStart = expressionProgram.OperationCount;
        while (computedSuffixStart > optionalStartIndex + 3)
        {
            var suffixOp = expressionProgram.GetOperation(computedSuffixStart - 1);
            if (suffixOp.Kind != ExpressionOpKind.GetNamedProperty ||
                suffixOp.IsOptional ||
                !suffixOp.ShortCircuitOnNullishTarget)
            {
                break;
            }

            if (suffixOp.GetString(expressionStringConstants).IsPrivateName())
            {
                reason = "Private named property reads are not supported.";
                return false;
            }

            computedSuffixStart--;
        }

        var computedIndex = computedSuffixStart - 1;
        var computedOp = expressionProgram.GetOperation(computedIndex);

        if (firstPropOp.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstPropOp.IsOptional ||
            firstPropOp.ShortCircuitOnNullishTarget ||
            firstPropOp.GetString(expressionStringConstants).IsPrivateName())
        {
            reason = string.Empty;
            return false;
        }

        if (computedOp.Kind != ExpressionOpKind.GetComputedProperty || !computedOp.ShortCircuitOnNullishTarget)
        {
            reason = string.Empty;
            return false;
        }

        if (!TryAppendActivationValueLoad(baseLoad, expressionProgram, activationSlots, unified, out reason))
        {
            return false;
        }

        for (var operationIndex = 1; operationIndex < optionalStartIndex; operationIndex++)
        {
            var prefixOp = expressionProgram.GetOperation(operationIndex);
            var prefixNameIndex = stringConstants.Count;
            stringConstants.Add(prefixOp.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, prefixNameIndex));
        }

        var jumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        var propNameIndex = stringConstants.Count;
        stringConstants.Add(firstPropOp.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propNameIndex));

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: optionalStartIndex + 1,
                endExclusive: computedIndex,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedProperty));
        for (var operationIndex = computedIndex + 1; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            var continuationOp = expressionProgram.GetOperation(operationIndex);
            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(continuationOp.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
        }

        unified[jumpIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, unified.Count);
        reason = string.Empty;
        return true;
    }

    // Handles: [activation-resolved base, GetNamedProperty(non-optional, non-private)*,
    // JumpIfNullish(ReplaceWithUndefined:true), key..., GetComputedProperty(!SC),
    // GetNamedProperty(SC:true)*].
    private static bool TryAppendFirstBoundaryOptionalComputedPropertyReadChain(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 4)
        {
            reason = string.Empty;
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var jumpIndex = 1;
        while (jumpIndex < expressionProgram.OperationCount)
        {
            var prefixOp = expressionProgram.GetOperation(jumpIndex);
            if (prefixOp.Kind != ExpressionOpKind.GetNamedProperty ||
                prefixOp.IsOptional ||
                prefixOp.ShortCircuitOnNullishTarget)
            {
                break;
            }

            if (prefixOp.GetString(expressionStringConstants).IsPrivateName())
            {
                reason = "Private named property reads are not supported.";
                return false;
            }

            jumpIndex++;
        }

        if (jumpIndex >= expressionProgram.OperationCount)
        {
            reason = string.Empty;
            return false;
        }

        var jumpOp = expressionProgram.GetOperation(jumpIndex);
        if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish ||
            !jumpOp.ReplaceWithUndefined)
        {
            reason = string.Empty;
            return false;
        }

        var computedSuffixStart = expressionProgram.OperationCount;
        while (computedSuffixStart > jumpIndex + 2)
        {
            var suffixOp = expressionProgram.GetOperation(computedSuffixStart - 1);
            if (suffixOp.Kind != ExpressionOpKind.GetNamedProperty ||
                suffixOp.IsOptional ||
                !suffixOp.ShortCircuitOnNullishTarget)
            {
                break;
            }

            if (suffixOp.GetString(expressionStringConstants).IsPrivateName())
            {
                reason = "Private named property reads are not supported.";
                return false;
            }

            computedSuffixStart--;
        }

        var computedIndex = computedSuffixStart - 1;
        if (computedIndex <= jumpIndex + 1 ||
            jumpOp.Target != computedIndex + 1)
        {
            reason = string.Empty;
            return false;
        }

        var computedOp = expressionProgram.GetOperation(computedIndex);
        if (computedOp.Kind != ExpressionOpKind.GetComputedProperty ||
            computedOp.ShortCircuitOnNullishTarget)
        {
            reason = string.Empty;
            return false;
        }

        if (!IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: jumpIndex + 1,
                endExclusive: computedIndex))
        {
            reason = "Unsupported computed property key span.";
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

        for (var operationIndex = 1; operationIndex < jumpIndex; operationIndex++)
        {
            var prefixOp = expressionProgram.GetOperation(operationIndex);
            var prefixNameIndex = stringConstants.Count;
            stringConstants.Add(prefixOp.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, prefixNameIndex));
        }

        var unifiedJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: jumpIndex + 1,
                endExclusive: computedIndex,
                out reason))
        {
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedProperty));
        for (var operationIndex = computedIndex + 1; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            var suffixOp = expressionProgram.GetOperation(operationIndex);
            var suffixNameIndex = stringConstants.Count;
            stringConstants.Add(suffixOp.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, suffixNameIndex));
        }

        unified[unifiedJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            unified.Count);
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendComputedPropertyKeySpan(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        int startInclusive,
        int endExclusive,
        out string reason,
        bool allowsDynamicIdentifiers = false)
    {
        for (var index = startInclusive; index < endExclusive; index++)
        {
            var operation = expressionProgram.GetOperation(index);
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadIdentifier:
                case ExpressionOpKind.LoadLiteral:
                case ExpressionOpKind.LoadThis:
                case ExpressionOpKind.LoadNewTarget:
                    if (!TryAppendComputedPropertyKeyLoad(
                            operation,
                            expressionProgram,
                            activationSlots,
                            allowsDynamicIdentifiers,
                            unified,
                            literalConstants,
                            stringConstants,
                            out reason))
                    {
                        return false;
                    }

                    break;

                case ExpressionOpKind.CreateObject:
                    if (!TryAppendSimpleObjectLiteralSpan(
                            expressionProgram,
                            index,
                            activationSlots,
                            unified,
                            literalConstants,
                            stringConstants,
                            callTargetConstants: null,
                            slotLayout: null,
                            out var objectSpanLength,
                            out reason))
                    {
                        return false;
                    }

                    index += objectSpanLength - 1;
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

                case ExpressionOpKind.Binary when IsSupportedBinaryOperator(operation.Operator):
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)operation.Operator));
                    break;

                default:
                    reason = $"Unsupported computed property key op '{operation.Kind}'.";
                    return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsSupportedComputedPropertyKeySpan(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        int startInclusive,
        int endExclusive,
        bool allowsDynamicIdentifiers = false)
    {
        var stackDepth = 0;
        for (var index = startInclusive; index < endExclusive; index++)
        {
            var operation = expressionProgram.GetOperation(index);
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadLiteral:
                case ExpressionOpKind.LoadThis:
                case ExpressionOpKind.LoadNewTarget:
                    stackDepth++;
                    break;

                case ExpressionOpKind.LoadIdentifier:
                    if (!CanAppendSimpleOperandLoad(operation, expressionProgram, activationSlots) &&
                        !(allowsDynamicIdentifiers && !operation.IsArguments))
                    {
                        return false;
                    }

                    stackDepth++;
                    break;

                case ExpressionOpKind.CreateObject:
                    if (!TryMeasureSimpleObjectLiteralSpan(
                            expressionProgram,
                            index,
                            activationSlots,
                            out var objectSpanLength))
                    {
                        return false;
                    }

                    index += objectSpanLength - 1;
                    stackDepth++;
                    break;

                case ExpressionOpKind.UnaryPlus:
                case ExpressionOpKind.UnaryMinus:
                case ExpressionOpKind.UnaryLogicalNot:
                case ExpressionOpKind.UnaryBitwiseNot:
                case ExpressionOpKind.UnaryVoid:
                case ExpressionOpKind.ToString:
                    if (stackDepth < 1)
                    {
                        return false;
                    }

                    break;

                case ExpressionOpKind.Binary:
                    if (stackDepth < 2 || !IsSupportedBinaryOperator(operation.Operator))
                    {
                        return false;
                    }

                    stackDepth--;
                    break;

                default:
                    return false;
            }
        }

        return stackDepth == 1;
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
                        expressionProgram, rhsStart, activationSlots, unified, literalConstants, stringConstants,
                        callTargetConstants: null, slotLayout: null,
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
                        callTargetConstants: null, slotLayout: null,
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
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
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

        if (!TryAppendActivationOrImplicitArgumentsObjectReadValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                stringConstants,
                out reason))
        {
            return false;
        }

        if (!TryAppendComputedPropertyKeyLoad(
                expressionProgram.GetOperation(1),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
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
        return TryAppendComputedPropertyKeyLoad(
            operation,
            expressionProgram,
            activationSlots,
            allowsDynamicIdentifiers: false,
            unified,
            literalConstants,
            stringConstants: null,
            out reason);
    }

    private static bool TryAppendComputedPropertyKeyLoad(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder? stringConstants,
        out string reason)
    {
        switch (operation.Kind)
        {
            case ExpressionOpKind.LoadIdentifier:
                return TryAppendSimpleOperandLoadWithDynamic(
                    operation,
                    expressionProgram,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    out reason);

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
        out string reason,
        bool allowsDynamicIdentifiers = false,
        ImmutableArray<string>.Builder? stringConstants = null)
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

            // Substitution part: simple binary expression, ToString, Binary(Add)
            if (i + 4 < expressionProgram.OperationCount)
            {
                var rightOperand = expressionProgram.GetOperation(i + 1);
                var binary = expressionProgram.GetOperation(i + 2);
                var toString = expressionProgram.GetOperation(i + 3);
                var add = expressionProgram.GetOperation(i + 4);
                if (binary.Kind == ExpressionOpKind.Binary &&
                    IsSupportedBinaryOperator(binary.Operator) &&
                    toString.Kind == ExpressionOpKind.ToString &&
                    add.Kind == ExpressionOpKind.Binary &&
                    add.Operator == BinaryOperator.Add &&
                    CanAppendSimpleOperandLoadWithDynamic(op, expressionProgram, activationSlots, allowsDynamicIdentifiers) &&
                    CanAppendSimpleOperandLoadWithDynamic(rightOperand, expressionProgram, activationSlots, allowsDynamicIdentifiers))
                {
                    TryAppendSimpleOperandLoadWithDynamic(
                        op,
                        expressionProgram,
                        activationSlots,
                        allowsDynamicIdentifiers,
                        unified,
                        literalConstants,
                        stringConstants,
                        out _);
                    TryAppendSimpleOperandLoadWithDynamic(
                        rightOperand,
                        expressionProgram,
                        activationSlots,
                        allowsDynamicIdentifiers,
                        unified,
                        literalConstants,
                        stringConstants,
                        out _);
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)binary.Operator));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ToString));
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)BinaryOperator.Add));
                    i += 5;
                    continue;
                }
            }

            // Substitution part: simple operand, ToString, Binary(Add)
            if (i + 2 < expressionProgram.OperationCount)
            {
                var toString = expressionProgram.GetOperation(i + 1);
                var add = expressionProgram.GetOperation(i + 2);
                if (toString.Kind == ExpressionOpKind.ToString &&
                    add.Kind == ExpressionOpKind.Binary && add.Operator == BinaryOperator.Add)
                {
                    if (TryAppendSimpleOperandLoadWithDynamic(
                            op,
                            expressionProgram,
                            activationSlots,
                            allowsDynamicIdentifiers,
                            unified,
                            literalConstants,
                            stringConstants,
                            out _))
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

    private static bool CanAppendSimpleOperandLoad(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots)
    {
        return operation.Kind switch
        {
            ExpressionOpKind.LoadIdentifier => !operation.IsArguments &&
                TryResolveActivationSlot(
                    operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan()),
                    activationSlots,
                    out _),
            ExpressionOpKind.LoadLiteral or ExpressionOpKind.LoadThis or ExpressionOpKind.LoadNewTarget => true,
            _ => false
        };
    }

    private static bool CanAppendSimpleOperandLoadWithDynamic(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        return CanAppendSimpleOperandLoad(operation, expressionProgram, activationSlots) ||
               allowsDynamicIdentifiers &&
               operation.Kind == ExpressionOpKind.LoadIdentifier &&
               !operation.IsArguments;
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

    private static bool TryAppendSimpleOperandLoadWithDynamic(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder? stringConstants,
        out string reason)
    {
        if (TryAppendSimpleOperandLoad(
                operation,
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                out reason))
        {
            return true;
        }

        if (!allowsDynamicIdentifiers ||
            operation.Kind != ExpressionOpKind.LoadIdentifier ||
            operation.IsArguments ||
            stringConstants is null)
        {
            return false;
        }

        var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        var nameIndex = stringConstants.Count;
        stringConstants.Add(identifier.Name.Name ?? string.Empty);
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadDynamicIdentifier, nameIndex));
        reason = string.Empty;
        return true;
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

    private static int EncodeRegexLiteralOperand(int patternStringConstantIndex, byte encodedFlags) =>
        (patternStringConstantIndex << 8) | encodedFlags;

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

    private static int EncodeDeclarationBindingTargetOperand(
        int bindingTargetIndex,
        VariableKind varKind,
        bool hasInitializer)
    {
        var flags = hasInitializer ? DeclarationBindingTargetHasInitializerFlag : 0;
        return (bindingTargetIndex << DeclarationBindingTargetShift) | (int)varKind | flags;
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
        ImmutableArray<ClassExpression>.Builder classLiteralConstants,
        ImmutableArray<TaggedTemplateDescriptor>.Builder templateObjectConstants,
        out string reason,
        ImmutableArray<BindingTargetProgram>.Builder? bindingTargetConstants = null)
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
                classLiteralConstants,
                templateObjectConstants,
                out reason,
                bindingTargetConstants))
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

    private static bool IsSupportedDeclarationBindingTarget(BindingTargetProgram target)
    {
        switch (target)
        {
            case IdentifierBindingTargetProgram:
                return true;

            case ArrayBindingTargetProgram arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.DefaultProgram is not null ||
                        element.Target is { } elementTarget &&
                        !IsSupportedDeclarationBindingTarget(elementTarget))
                    {
                        return false;
                    }
                }

                return arrayBinding.RestElement is null ||
                       IsSupportedDeclarationBindingTarget(arrayBinding.RestElement);

            case ObjectBindingTargetProgram objectBinding:
                foreach (var property in objectBinding.Properties)
                {
                    if (property.DefaultProgram is not null ||
                        property.NameProgram is not null ||
                        !IsSupportedDeclarationBindingTarget(property.Target))
                    {
                        return false;
                    }
                }

                return objectBinding.RestElement is null ||
                       IsSupportedDeclarationBindingTarget(objectBinding.RestElement);

            default:
                return false;
        }
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

    private static int EncodeObjectAccessorOperand(int stringConstantIndex, ObjectAccessorKind accessorKind) =>
        (stringConstantIndex << 1) | (accessorKind == ObjectAccessorKind.Setter ? 1 : 0);

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

    private static bool TryAppendActivationOrImplicitArgumentsObjectReadValueLoad(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (operation.Kind == ExpressionOpKind.LoadIdentifier &&
            allowsDynamicIdentifiers)
        {
            var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
            if (operation.IsArguments ||
                !TryResolveActivationSlot(identifier, activationSlots, out _))
            {
                var identifierNameIndex = stringConstants.Count;
                stringConstants.Add(identifier.Name.Name ?? string.Empty);
                unified.Add(new UnifiedBytecodeInstruction(
                    UnifiedBytecodeOpCode.LoadDynamicIdentifier,
                    identifierNameIndex));
                reason = string.Empty;
                return true;
            }
        }

        return TryAppendActivationValueLoad(
            operation,
            expressionProgram,
            activationSlots,
            unified,
            out reason);
    }

    private static bool TryAppendActivationOrPlainDynamicIdentifierReadValueLoad(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (TryAppendActivationValueLoad(
                operation,
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return true;
        }

        if (!allowsDynamicIdentifiers ||
            operation.Kind != ExpressionOpKind.LoadIdentifier ||
            operation.IsArguments)
        {
            return false;
        }

        var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        if (identifier.FlatSlotId >= 0 ||
            TryResolveActivationSlot(identifier, activationSlots, out _))
        {
            return false;
        }

        var identifierNameIndex = stringConstants.Count;
        stringConstants.Add(identifier.Name.Name ?? string.Empty);
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.LoadDynamicIdentifier,
            identifierNameIndex));
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

    private static bool IsImplicitArgumentsIdentifier(
        IdentifierOperand identifier,
        UnifiedBytecodeSlotLayout slotLayout) =>
        ReferenceEquals(identifier.Name, Symbol.Arguments) &&
        !TryResolveActivationSlot(identifier, slotLayout, out _);

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
