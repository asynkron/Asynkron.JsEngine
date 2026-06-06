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
        bool AllowsOrdinaryDynamicIdentifiers,
        ImmutableArray<int> ConstLexicalSlotIndices = default,
        int ScriptCompletionSlot = -1)
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
        bool allowsOrdinaryDynamicIdentifiers = false,
        bool isScript = false)
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
        if (isScript)
        {
            slotLayout = WithScriptCompletionSlot(slotLayout);
        }

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
            RequiresShortCircuitStackFlags(compiledInstructions),
            slotLayout.ScriptCompletionSlot,
            slotLayout.ConstLexicalSlotIndices.IsDefaultOrEmpty
                ? ImmutableArray<int>.Empty
                : slotLayout.ConstLexicalSlotIndices);
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

    private static UnifiedBytecodeSlotLayout WithScriptCompletionSlot(UnifiedBytecodeSlotLayout slotLayout)
    {
        var completionSlot = slotLayout.SlotCount;
        return slotLayout with
        {
            SlotCount = completionSlot + 1,
            SlotNames = slotLayout.SlotNames.Add(null),
            ScriptCompletionSlot = completionSlot
        };
    }

    private static int GetCompiledExpressionMaxStackDepth(ExpressionProgram expressionProgram)
    {
        var maxStackDepth = expressionProgram.MaxStackDepth;
        if (RequiresNestedNamedPropertyReceiverStack(expressionProgram))
        {
            maxStackDepth = Math.Max(maxStackDepth, 3);
        }

        if (RequiresValuePreservingIdentifierReferenceStoreStack(expressionProgram))
        {
            maxStackDepth++;
        }

        if (RequiresObjectLiteralFunctionMemberStack(expressionProgram))
        {
            maxStackDepth = Math.Max(maxStackDepth, 4);
        }

        return maxStackDepth;
    }

    private static bool RequiresObjectLiteralFunctionMemberStack(ExpressionProgram expressionProgram)
    {
        for (var i = 0; i < expressionProgram.OperationCount; i++)
        {
            if (expressionProgram.GetOperation(i).Kind is ExpressionOpKind.DefineObjectMethod
                or ExpressionOpKind.DefineComputedObjectMethod
                or ExpressionOpKind.DefineObjectAccessor
                or ExpressionOpKind.DefineComputedObjectAccessor)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresValuePreservingIdentifierReferenceStoreStack(ExpressionProgram expressionProgram)
    {
        for (var operationIndex = 0; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            if (expressionProgram.GetOperation(operationIndex).Kind == ExpressionOpKind.StoreResolvedIdentifier)
            {
                return true;
            }
        }

        return false;
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
        if (allowsOrdinaryDynamicIdentifiers)
        {
            // R7: at script scope the step-wise destructuring driver state symbols
            // (__objDestr_src / __arrDestr_iter) are not part of the activation slot
            // map, so they fail to resolve to a flat slot. The driver state is pure VM
            // scratch (never a JS-visible binding), so allocate synthetic activation
            // slots for it — mirroring AddSyntheticResumeSlots for generator state.
            activationSlots = AddSyntheticDestructuringStateSlots(activationSlots, plan.Instructions);
        }

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
        var constLexicalSlotIndices = activationSlots.ConstLexicalSlotIndices.IsDefaultOrEmpty
            ? ImmutableArray<int>.Empty
            : RemapSlotIndices(
                activationSlots.ScopeId,
                activationSlots.ConstLexicalSlotIndices,
                flatSlotMappings);

        return new UnifiedBytecodeSlotLayout(
            slotCount,
            activationSlots,
            flatSlotMappings,
            parameterSlotIndices,
            lexicalSlotIndices,
            names,
            allowsOrdinaryDynamicIdentifiers,
            constLexicalSlotIndices);
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

    private static ActivationSlotShape AddSyntheticDestructuringStateSlots(
        ActivationSlotShape activationSlots,
        ImmutableArray<ExecutionInstruction> instructions)
    {
        ImmutableArray<Symbol>.Builder? missingSymbols = null;
        HashSet<Symbol>? seenSymbols = null;
        foreach (var instruction in instructions)
        {
            switch (instruction)
            {
                case ArrayDestructuringInitInstruction { IteratorSlot: { } arrayInitSlot }:
                    AddIfMissing(arrayInitSlot);
                    break;

                case ArrayDestructuringElementInstruction { IteratorSlot: { } arrayElementSlot }:
                    AddIfMissing(arrayElementSlot);
                    break;

                case ArrayDestructuringRestInstruction { IteratorSlot: { } arrayRestSlot }:
                    AddIfMissing(arrayRestSlot);
                    break;

                case ArrayDestructuringCloseInstruction { IteratorSlot: { } arrayCloseSlot }:
                    AddIfMissing(arrayCloseSlot);
                    break;

                case ObjectDestructuringInitInstruction { SourceSlot: { } objectInitSlot }:
                    AddIfMissing(objectInitSlot);
                    break;

                case ObjectDestructuringPropertyInstruction { SourceSlot: { } objectPropertySlot }:
                    AddIfMissing(objectPropertySlot);
                    break;

                case ObjectDestructuringRestInstruction { SourceSlot: { } objectRestSlot }:
                    AddIfMissing(objectRestSlot);
                    break;

                case ObjectDestructuringCloseInstruction { SourceSlot: { } objectCloseSlot }:
                    AddIfMissing(objectCloseSlot);
                    break;
            }
        }

        void AddIfMissing(Symbol symbol)
        {
            if (activationSlots.SlotMap.ContainsKey(symbol))
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

    private static ImmutableArray<int> RemapPerIterationCopySlotIndices(
        int scopeId,
        ImmutableArray<Symbol> perIterationBindings,
        ImmutableDictionary<Symbol, int> slotMap,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> flatSlotMappings)
    {
        if (perIterationBindings.IsDefaultOrEmpty || slotMap.IsEmpty)
        {
            return ImmutableArray<int>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<int>(perIterationBindings.Length);
        foreach (var binding in perIterationBindings)
        {
            if (slotMap.TryGetValue(binding, out var slotIndex) &&
                TryMapSlot(scopeId, slotIndex, flatSlotMappings, out var flatSlotId))
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
                        var requiresDynamicDeclarationReference =
                            declaration.VarKind == VariableKind.Var && activeWithDepths[instructionIndex] > 0;
                        if (requiresDynamicDeclarationReference ||
                            !TryResolveDeclarationSlot(
                                declarationTargetSymbol,
                                declaration.VarKind,
                                slotLayout,
                                activeScopes,
                                out var storeSlot))
                        {
                            if (!TryAppendDynamicDeclaration(
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

                        if (declaration.VarKind == VariableKind.Using)
                        {
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DuplicateTop));
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.RegisterDisposable));
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

                    // `var x = await p` / `let y = await p` (B1). Mirrors the awaited IteratorInit / ForInInit
                    // lowering: evaluate the awaited operand, AwaitValue (suspends; pushes the settled value on
                    // resume), then InitializeSlot pops it into the declaration's flat slot. The store happens
                    // after the suspension completes so a later LoadSlot reads the correct value.
                    case SimpleVariableDeclarationInstruction
                        {
                            InitializerProgram: null,
                            AwaitedProgram: { } awaitedDeclarationInitializer,
                            TargetSymbol: { } awaitedDeclarationTargetSymbol
                        } awaitedDeclaration:
                        if (!TryResolveDeclarationSlot(
                                awaitedDeclarationTargetSymbol,
                                awaitedDeclaration.VarKind,
                                slotLayout,
                                activeScopes,
                                out var awaitedDeclarationSlot))
                        {
                            reason =
                                $"Awaited declaration target '{awaitedDeclarationTargetSymbol.Name}' is not eligible for unified bytecode storage.";
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                awaitedDeclarationInitializer,
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

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitValue));
                        if (awaitedDeclaration.AllowNameInference)
                        {
                            var awaitedNameInferenceIndex = stringConstants.Count;
                            stringConstants.Add(awaitedDeclarationTargetSymbol.Name);
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.EnsureHasName,
                                awaitedNameInferenceIndex));
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.InitializeSlot,
                            awaitedDeclarationSlot));
                        maxStackDepth = Math.Max(
                            maxStackDepth,
                            GetCompiledExpressionMaxStackDepth(awaitedDeclarationInitializer));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                awaitedDeclaration.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = awaitedDeclaration.Next;
                        continue;

                    // `let [a,b] = await p` / `const {x} = await p` (B44). Same await lowering family: evaluate
                    // the awaited operand, AwaitValue (suspends; pushes the settled source on resume), then
                    // ApplyDeclarationBindingTarget pops it and runs the synchronous destructuring of the
                    // lowered binding-target program, writing each binding into its slot.
                    case BindingVariableDeclarationInstruction
                        {
                            InitializerProgram: null,
                            AwaitedProgram: { } awaitedBindingInitializer
                        } awaitedBindingDeclaration:
                        if (!TryAppendExpressionProgramOps(
                                awaitedBindingInitializer,
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

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitValue));
                        var awaitedBindingTargetIndex = bindingTargetConstants.Count;
                        bindingTargetConstants.Add(awaitedBindingDeclaration.TargetProgram);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget,
                            EncodeDeclarationBindingTargetOperand(
                                awaitedBindingTargetIndex,
                                awaitedBindingDeclaration.VarKind,
                                hasInitializer: true)));
                        maxStackDepth = Math.Max(
                            maxStackDepth,
                            GetCompiledExpressionMaxStackDepth(awaitedBindingInitializer));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                awaitedBindingDeclaration.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = awaitedBindingDeclaration.Next;
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
                        if (declaration.VarKind == VariableKind.Using)
                        {
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DuplicateTop));
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.RegisterDisposable));
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget,
                            EncodeDeclarationBindingTargetOperand(
                                declarationBindingTargetIndex,
                                declaration.VarKind == VariableKind.Using ? VariableKind.Const : declaration.VarKind,
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

                            // §13.15.2: with no static slot resolution, resolve the LHS
                            // assignment reference BEFORE evaluating the RHS so that an RHS
                            // side effect (e.g. creating a matching global property) cannot
                            // change whether the LHS was originally resolvable. This mirrors
                            // ExecutionPlanRunner.HandleAssignmentSlot's pre-resolved-reference
                            // path and keeps strict-mode unresolved-reference ReferenceErrors.
                            var dynamicAssignmentNameIndex = stringConstants.Count;
                            stringConstants.Add(assignmentTargetSymbol.Name);
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference,
                                dynamicAssignmentNameIndex));

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

                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.StoreDynamicIdentifierReference,
                                EncodeDynamicStoreOperand(dynamicAssignmentNameIndex, assignment.AllowNameInference)));
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
                                UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference,
                                dynamicTargetNameIndex));
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.LoadDynamicIdentifierReference));
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
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.StoreDynamicIdentifierReference,
                                EncodeDynamicStoreOperand(dynamicTargetNameIndex, allowNameInference: false)));
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
                            if (!allowsDynamicIdentifiers)
                            {
                                reason = $"Unsupported logical assignment target '{logicalTargetSymbol.Name}'.";
                                return false;
                            }

                            var dynamicLogicalNameIndex = stringConstants.Count;
                            stringConstants.Add(logicalTargetSymbol.Name);
                            var dynamicScJumpOpCode = logicalAssignment.Operator switch
                            {
                                BinaryOperator.LogicalAnd => UnifiedBytecodeOpCode.JumpIfShortCircuitFalse,
                                BinaryOperator.LogicalOr => UnifiedBytecodeOpCode.JumpIfShortCircuitTrue,
                                _ => UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish
                            };

                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference,
                                dynamicLogicalNameIndex));
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.LoadDynamicIdentifierReference));
                            var dynamicScJumpIndex = unified.Count;
                            unified.Add(new UnifiedBytecodeInstruction(dynamicScJumpOpCode, 0));
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

                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.StoreDynamicIdentifierReference,
                                EncodeDynamicStoreOperand(dynamicLogicalNameIndex, logicalAssignment.AllowNameInference)));
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
                            var skipDynamicScPopIndex = unified.Count;
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));
                            PatchOperand(unified, dynamicScJumpIndex, unified.Count);
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.PopDynamicIdentifierReference));
                            PatchOperand(unified, skipDynamicScPopIndex, unified.Count);
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
                                reason = $"Unsupported update target '{incrementTargetSymbol.Name}'.";
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

                        // The update opcodes leave the result (new value for prefix, old
                        // numeric value for postfix) on the stack. When this update is the
                        // value of an expression statement, that result is the statement's
                        // completion value, so capture it into the script completion slot
                        // before discarding. Loop update expressions set SuppressCompletionValue
                        // and must not contribute to the completion value (per ES spec).
                        if (slotLayout.ScriptCompletionSlot >= 0 && !increment.SuppressCompletionValue)
                        {
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DuplicateTop));
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.StoreSlot,
                                slotLayout.ScriptCompletionSlot));
                            maxStackDepth = Math.Max(maxStackDepth, 2);
                        }
                        else
                        {
                            maxStackDepth = Math.Max(maxStackDepth, 1);
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
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
                        // as ordinary PushEnvironment instructions plus copy metadata for the slots that
                        // need CreatePerIterationEnvironment-style value carry-forward.
                        var lexicalSlotIndices = RemapSlotIndices(
                            pushEnvironment.ScopeId,
                            pushEnvironment.LexicalSlotIndices,
                            slotLayout.FlatSlotMappings);
                        var constSlotIndices = pushEnvironment.ConstLexicalSlotIndices.IsDefaultOrEmpty
                            ? ImmutableArray<int>.Empty
                            : RemapSlotIndices(
                                pushEnvironment.ScopeId,
                                pushEnvironment.ConstLexicalSlotIndices,
                                slotLayout.FlatSlotMappings);
                        var perIterationCopySlotIndices = RemapPerIterationCopySlotIndices(
                            pushEnvironment.ScopeId,
                            pushEnvironment.PerIterationBindings,
                            pushEnvironment.SlotMap,
                            slotLayout.FlatSlotMappings);
                        var scopeDescriptorIndex = scopeDescriptors.Count;
                        scopeDescriptors.Add(new UnifiedBytecodeScopeDescriptor(
                            pushEnvironment.ScopeId,
                            lexicalSlotIndices,
                            constSlotIndices,
                            perIterationCopySlotIndices));
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
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.PopEnvironment,
                            popEnvironment.ScopeId));
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

                        // Mirror HandleEnterWith in the AST runner: entering a `with` statement
                        // resets the script completion value to undefined so an empty/value-less
                        // body produces undefined rather than leaking the previous statement's value.
                        if (slotLayout.ScriptCompletionSlot >= 0)
                        {
                            var enterWithUndefinedIndex = literalConstants.Count;
                            literalConstants.Add(JsValue.Undefined);
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, enterWithUndefinedIndex));
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.StoreSlot,
                                slotLayout.ScriptCompletionSlot));
                            maxStackDepth = Math.Max(maxStackDepth, 1);
                        }

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

                    case IteratorInitInstruction
                        {
                            IterableProgram: null,
                            AwaitedProgram: { } awaitedIterableProgram
                        } awaitedIteratorInit:
                        if (!TryResolveDriverSlot(
                                awaitedIteratorInit.IteratorSlot,
                                awaitedIteratorInit.IteratorSlotIndex,
                                slotLayout,
                                out var awaitedIteratorStateSlot))
                        {
                            reason = $"Unsupported iterator state slot '{awaitedIteratorInit.IteratorSlot.Name}'.";
                            return false;
                        }

                        if (!TryEmitTdzHeadInit(
                                awaitedIteratorInit.TdzBindings,
                                awaitedIteratorInit.TdzIsConst,
                                awaitedIteratorInit.TdzScopeId,
                                awaitedIteratorInit.TdzSlotIndices,
                                slotLayout,
                                unified,
                                driverDescriptors,
                                out reason))
                        {
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                awaitedIterableProgram,
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

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitValue));
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.IteratorInit,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(
                                    awaitedIteratorStateSlot,
                                    IteratorKind: awaitedIteratorInit.IteratorKind))));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(awaitedIterableProgram));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                awaitedIteratorInit.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = awaitedIteratorInit.Next;
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
                            iteratorMoveNext.IteratorKind,
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

                    case ForInInitInstruction
                        {
                            ObjectProgram: null,
                            AwaitedProgram: { } awaitedObjectProgram
                        } awaitedForInInit:
                        if (!TryResolveDriverSlot(
                                awaitedForInInit.StateSlot,
                                awaitedForInInit.StateSlotIndex,
                                slotLayout,
                                out var awaitedForInStateSlot))
                        {
                            reason = $"Unsupported for-in state slot '{awaitedForInInit.StateSlot.Name}'.";
                            return false;
                        }

                        if (!TryEmitTdzHeadInit(
                                awaitedForInInit.TdzBindings,
                                awaitedForInInit.TdzIsConst,
                                awaitedForInInit.TdzScopeId,
                                awaitedForInInit.TdzSlotIndices,
                                slotLayout,
                                unified,
                                driverDescriptors,
                                out reason))
                        {
                            return false;
                        }

                        if (!TryAppendExpressionProgramOps(
                                awaitedObjectProgram,
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

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitValue));
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ForInInit,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(awaitedForInStateSlot))));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(awaitedObjectProgram));
                        if (TryAppendJumpToCompiledTarget(
                                instructionIndex,
                                awaitedForInInit.Next,
                                instructions,
                                instructionPcMap,
                                activeInstructions,
                                unified,
                                out reason))
                        {
                            return true;
                        }

                        instructionIndex = awaitedForInInit.Next;
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
                            IteratorDriverKind.Sync,
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
                        var elementDynamicNameIndex = -1;
                        if (arrayDestructuringElement.TargetSymbol is { } targetSymbol &&
                            !TryResolveDestructuringTarget(
                                targetSymbol,
                                arrayDestructuringElement.VarKind,
                                allowsDynamicIdentifiers,
                                slotLayout,
                                activeScopes,
                                stringConstants,
                                out targetSlot,
                                out elementDynamicNameIndex,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ArrayDestructuringElement,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(
                                    elementStateSlot,
                                    TargetSlot: targetSlot,
                                    TargetNameConstantIndex: elementDynamicNameIndex,
                                    TargetVariableKind: arrayDestructuringElement.VarKind))));
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

                        if (!TryResolveDestructuringTarget(
                                arrayDestructuringRest.RestSymbol,
                                arrayDestructuringRest.VarKind,
                                allowsDynamicIdentifiers,
                                slotLayout,
                                activeScopes,
                                stringConstants,
                                out var restTargetSlot,
                                out var restDynamicNameIndex,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ArrayDestructuringRest,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(
                                    restStateSlot,
                                    TargetSlot: restTargetSlot,
                                    TargetNameConstantIndex: restDynamicNameIndex,
                                    TargetVariableKind: arrayDestructuringRest.VarKind))));
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

                        if (!TryResolveDestructuringTarget(
                                objectDestructuringProperty.TargetSymbol,
                                objectDestructuringProperty.VarKind,
                                allowsDynamicIdentifiers,
                                slotLayout,
                                activeScopes,
                                stringConstants,
                                out var objectPropertyTargetSlot,
                                out var objectPropertyDynamicNameIndex,
                                out reason))
                        {
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
                                    NameConstantIndex: objectPropertyNameIndex,
                                    TargetNameConstantIndex: objectPropertyDynamicNameIndex,
                                    TargetVariableKind: objectDestructuringProperty.VarKind))));
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

                        if (!TryResolveDestructuringTarget(
                                objectDestructuringRest.RestSymbol,
                                objectDestructuringRest.VarKind,
                                allowsDynamicIdentifiers,
                                slotLayout,
                                activeScopes,
                                stringConstants,
                                out var objectRestTargetSlot,
                                out var objectRestDynamicNameIndex,
                                out reason))
                        {
                            return false;
                        }

                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.ObjectDestructuringRest,
                            AddDriverDescriptor(
                                driverDescriptors,
                                new UnifiedBytecodeDriverDescriptor(
                                    objectRestStateSlot,
                                    TargetSlot: objectRestTargetSlot,
                                    TargetNameConstantIndex: objectRestDynamicNameIndex,
                                    TargetVariableKind: objectDestructuringRest.VarKind))));
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
                        if (slotLayout.ScriptCompletionSlot >= 0)
                        {
                            var undefinedIndex = literalConstants.Count;
                            literalConstants.Add(JsValue.Undefined);
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, undefinedIndex));
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.StoreSlot,
                                slotLayout.ScriptCompletionSlot));
                            maxStackDepth = Math.Max(maxStackDepth, 1);
                        }

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
                        if (slotLayout.ScriptCompletionSlot >= 0)
                        {
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.LoadSlot,
                                slotLayout.ScriptCompletionSlot));
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return));
                            maxStackDepth = Math.Max(maxStackDepth, 1);
                        }
                        else
                        {
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ReturnUndefined));
                        }

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

                    case YieldStarInstruction { AwaitedProgram: { } awaitedProgram, IterableProgram: null } yieldStar:
                        if (yieldStar.StateSlotSymbol is null)
                        {
                            reason = "yield* requires a state slot for resumable unified bytecode routing.";
                            return false;
                        }

                        if (!TryResolveYieldStarStateSlot(
                                yieldStar.StateSlotSymbol,
                                awaitedProgram,
                                slotLayout,
                                activeScopes,
                                out var awaitedYieldStarStateSlot))
                        {
                            reason = $"yield* state slot '{yieldStar.StateSlotSymbol.Name}' is not in the activation slot layout.";
                            return false;
                        }

                        var awaitedYieldStarResultSlot = -1;
                        if (yieldStar.ResultSlotSymbol is { } awaitedResultSymbol &&
                            !TryResolveVisibleSymbolSlot(
                                awaitedResultSymbol,
                                slotLayout,
                                activeScopes,
                                out awaitedYieldStarResultSlot))
                        {
                            awaitedYieldStarResultSlot = awaitedYieldStarStateSlot;
                        }

                        if (!TryAppendExpressionProgramOps(
                                awaitedProgram,
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

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.AwaitValue));
                        var awaitedYieldStarDescriptorIndex = driverDescriptors.Count;
                        driverDescriptors.Add(new UnifiedBytecodeDriverDescriptor(
                            StateSlot: awaitedYieldStarStateSlot,
                            ValueSlot: awaitedYieldStarResultSlot));
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.YieldStar,
                            awaitedYieldStarDescriptorIndex));
                        maxStackDepth = Math.Max(maxStackDepth, GetCompiledExpressionMaxStackDepth(awaitedProgram));
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

                        if (slotLayout.ScriptCompletionSlot >= 0 && !discard.SuppressCompletionValue)
                        {
                            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DuplicateTop));
                            unified.Add(new UnifiedBytecodeInstruction(
                                UnifiedBytecodeOpCode.StoreSlot,
                                slotLayout.ScriptCompletionSlot));
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
                        var discardedMaxStackDepth = GetCompiledExpressionMaxStackDepth(discardedProgram);
                        if (slotLayout.ScriptCompletionSlot >= 0 && !discard.SuppressCompletionValue)
                        {
                            discardedMaxStackDepth++;
                        }

                        maxStackDepth = Math.Max(maxStackDepth, discardedMaxStackDepth);
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
        IteratorDriverKind iteratorKind,
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
                IteratorKind: iteratorKind,
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
                slotLayout,
                callTargetConstants,
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
                slotLayout,
                callTargetConstants,
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

        if (TryAppendFirstBoundaryComputedPrefixComputedPropertySet(
                expressionProgram,
                slotLayout,
                callTargetConstants,
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
                slotLayout,
                callTargetConstants,
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

        if (TryAppendFirstBoundaryNestedNamedComputedPropertySet(
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

        if (TryAppendFirstBoundaryComputedPropertySet(
                expressionProgram,
                slotLayout,
                callTargetConstants,
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

        if (TryAppendFirstBoundaryNestedNamedComputedPropertyUpdate(
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

        if (TryAppendFirstBoundaryComputedPrefixComputedPropertyUpdate(
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

        if (TryAppendFirstBoundaryOptionalNamedThenNamedPropertyDelete(
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

        if (TryAppendFirstBoundaryOptionalNamedThenOptionalNamedPropertyDelete(
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

        if (TryAppendSimplePropertyReadOperandSpan(
                expressionProgram,
                0,
                expressionProgram.OperationCount,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out var propertyReadSpanLength,
                out reason,
                allowPrivateNamedPrefix: true) &&
            propertyReadSpanLength == expressionProgram.OperationCount)
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

        if (TryAppendSimplePropertyReadBinaryExpression(
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

        if (TryAppendFirstBoundaryPropertyReadBinaryExpression(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                functionLiteralConstants,
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
        const int DynamicIdentifierReferenceSlot = -1;
        List<int>? identifierReferenceSlots = null;

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

                    if (TryResolveExplicitActivationSlot(referenceIdentifier, slotLayout, out var referenceSlotIndex))
                    {
                        identifierReferenceSlots ??= [];
                        identifierReferenceSlots.Add(referenceSlotIndex);
                        break;
                    }

                    if (TryResolveActivationSlot(referenceIdentifier, slotLayout, out _))
                    {
                        reason =
                            $"Identifier assignment reference '{referenceIdentifier.Name.Name}' resolves only by activation-slot name lookup and is not eligible for slot-reference unified bytecode assignment lowering.";
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
                    identifierReferenceSlots ??= [];
                    identifierReferenceSlots.Add(DynamicIdentifierReferenceSlot);
                    break;

                case ExpressionOpKind.LoadResolvedIdentifierValue:
                    if (identifierReferenceSlots is { Count: > 0 } &&
                        identifierReferenceSlots[^1] >= 0)
                    {
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.LoadSlot,
                            identifierReferenceSlots[^1]));
                        break;
                    }

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

                    if (TryResolveExplicitActivationSlot(
                            storeReferenceIdentifier,
                            slotLayout,
                            out var storeReferenceSlotIndex))
                    {
                        if (identifierReferenceSlots is not { Count: > 0 } ||
                            identifierReferenceSlots[^1] != storeReferenceSlotIndex)
                        {
                            reason =
                                $"Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' does not match the pending slot-reference target.";
                            return false;
                        }

                        identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DuplicateTop));
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.StoreSlot,
                            storeReferenceSlotIndex));
                        break;
                    }

                    if (TryResolveActivationSlot(storeReferenceIdentifier, slotLayout, out _))
                    {
                        reason =
                            $"Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' resolves only by activation-slot name lookup and is not eligible for slot-reference unified bytecode assignment lowering.";
                        return false;
                    }

                    if (!allowsDynamicIdentifiers)
                    {
                        reason =
                            $"Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                        return false;
                    }

                    if (identifierReferenceSlots is { Count: > 0 } &&
                        identifierReferenceSlots[^1] >= 0)
                    {
                        reason =
                            $"Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' cannot store through a pending slot-reference target using the dynamic-name path.";
                        return false;
                    }

                    var storeReferenceNameIndex = stringConstants.Count;
                    stringConstants.Add(storeReferenceIdentifier.Name.Name ?? string.Empty);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.StoreDynamicIdentifierReference,
                        EncodeDynamicStoreOperand(storeReferenceNameIndex, operation)));
                    if (identifierReferenceSlots is { Count: > 0 } &&
                        identifierReferenceSlots[^1] == DynamicIdentifierReferenceSlot)
                    {
                        identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                    }

                    break;

                case ExpressionOpKind.PopResolvedIdentifierReference:
                    if (identifierReferenceSlots is { Count: > 0 })
                    {
                        var pendingReferenceSlot = identifierReferenceSlots[^1];
                        identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                        if (pendingReferenceSlot >= 0)
                        {
                            break;
                        }
                    }

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
                        UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference,
                        storeIdentifierNameIndex));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.StoreDynamicIdentifierReference,
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
                    var typeOfIdentifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (ShouldUseDynamicTypeOfIdentifierForScriptBlockLexical(
                            typeOfIdentifier,
                            slotLayout,
                            allowsDynamicIdentifiers))
                    {
                        var typeOfBlockLexicalNameIndex = stringConstants.Count;
                        stringConstants.Add(typeOfIdentifier.Name.Name ?? string.Empty);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.TypeOfDynamicIdentifier,
                            typeOfBlockLexicalNameIndex));
                        break;
                    }

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

        if (identifierReferenceSlots is { Count: > 0 })
        {
            reason = "Identifier assignment references were left pending after unified bytecode expression lowering.";
            return false;
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

        // Tagged-template calls carry LoadTemplateObject as the first logical argument. The generic
        // expression-program loop already owns that opcode plus the prepared-call stack contract, so let
        // it emit the whole call in source order instead of forcing the specialized simple-argument splitter
        // to learn template-object constants as a second path.
        if (FindFirstOperation(expressionProgram, ExpressionOpKind.LoadTemplateObject) >= 0)
        {
            return false;
        }

        var lastOp = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);

        // A12: chained method/computed calls past the first invocation boundary —
        // `a.b().c()`, `o.m().n()`, `o.a()[k]()`. The final call's TARGET is a member access on
        // the RESULT of an earlier call, so an inner Call op appears before the final call-target
        // preparation. The specialized receiver-chain appenders below assume the receiver is a
        // plain identifier/property span and would mis-split the chain (FindFirstOperation latches
        // the INNER call target). The general per-op expression loop already lowers the whole chain
        // in source order onto the operand stack the VM maintains (the inner CallInvocationBoundary
        // leaves its result as the next call's receiver), so decline cleanly here and let it run.
        // Guard: this only applies when there are NO genuine optional-chain ops — real optional
        // chains stay owned by the dedicated optional measurers/appenders above.
        if (IsGeneralChainedCallProgram(expressionProgram))
        {
            return false;
        }

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
                    allowsDynamicIdentifiers,
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
                    allowsDynamicIdentifiers,
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
                    allowsDynamicIdentifiers,
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
                    allowsDynamicIdentifiers,
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

    // A12: a "general chained call" is a call whose TARGET is a member access on the result of an
    // earlier call (`a.b().c()`, `o.m().n()`, `o.a()[k]()`). Structurally: the LAST op is a Call,
    // and at least one EARLIER op is also a Call (the inner invocation whose result is the next
    // call's receiver). These shapes must be lowered by the general per-op expression loop, not the
    // specialized first-boundary receiver-chain appenders (which assume a plain identifier/property
    // receiver span and mis-split the chain). Genuine optional-chain ops (JumpIfShortCircuited,
    // JumpIfNullish-replace-undefined, or any ShortCircuitOnNullishTarget) disqualify the shape so
    // optional chains stay owned by the dedicated optional measurers/appenders.
    private static bool IsGeneralChainedCallProgram(ExpressionProgram expressionProgram)
    {
        var operationCount = expressionProgram.OperationCount;
        if (operationCount < 2)
        {
            return false;
        }

        if (expressionProgram.GetOperation(operationCount - 1).Kind != ExpressionOpKind.Call)
        {
            return false;
        }

        var innerCallSeen = false;
        for (var operationIndex = 0; operationIndex < operationCount - 1; operationIndex++)
        {
            var operation = expressionProgram.GetOperation(operationIndex);

            // Any genuine optional-chain structure keeps the program with the optional appenders.
            if (operation.Kind == ExpressionOpKind.JumpIfShortCircuited ||
                operation is { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true } ||
                operation.ShortCircuitOnNullishTarget ||
                (operation.Kind != ExpressionOpKind.Call && operation.IsOptional))
            {
                return false;
            }

            if (operation.Kind == ExpressionOpKind.Call)
            {
                innerCallSeen = true;
            }
        }

        return innerCallSeen;
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
        bool allowsDynamicIdentifiers,
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
                    allowsDynamicIdentifiers,
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
                    allowsDynamicIdentifiers,
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
                    allowsDynamicIdentifiers,
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
                    allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers: allowsDynamicIdentifiers,
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
            allowsDynamicIdentifiers,
            out reason);
    }

    private static bool TryAppendReceiverOptionalNamedCallTarget(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers: allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers,
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
        bool allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers: allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers,
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
        bool allowsDynamicIdentifiers,
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
                    allowsDynamicIdentifiers,
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

        // Case 7 (A30): optional-computed-START plain call — o?.[k](args)
        // Pattern: [base, JumpIfNullish(RWU,end), key, LoadComputedCallTarget, args..., Call]
        if (callTargetIndexInProgram == 3 &&
            expressionProgram.GetOperation(1) is
                { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true })
        {
            return TryAppendOptionalComputedStartPlainCallTarget(
                expressionProgram,
                slotLayout,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                call,
                callIndex,
                callTargetIndexInProgram,
                out reason);
        }

        // Case 8 (A30): double-optional named-then-computed plain call — a?.b?.[k](args)
        // Pattern: [base, GetNamedProperty(opt,b), JumpIfShortCircuited, JumpIfNullish(RWU,end),
        //           key, LoadComputedCallTarget, args..., Call]
        if (callTargetIndexInProgram == 5 &&
            expressionProgram.GetOperation(1) is
                { Kind: ExpressionOpKind.GetNamedProperty, IsOptional: true, ShortCircuitOnNullishTarget: false } &&
            expressionProgram.GetOperation(2).Kind == ExpressionOpKind.JumpIfShortCircuited &&
            expressionProgram.GetOperation(3) is
                { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true })
        {
            return TryAppendOptionalChainComputedReceiverOptionalCallTarget(
                expressionProgram,
                slotLayout,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                call,
                callIndex,
                callTargetIndexInProgram,
                out reason);
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
                    allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers: allowsDynamicIdentifiers,
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
            allowsDynamicIdentifiers,
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

    private static bool IsPlainNamedPropertyReadOperandPrefix(
        PackedExpressionOp operation,
        ReadOnlySpan<string> stringConstants,
        bool allowPrivateNamedPrefix)
    {
        return operation.Kind == ExpressionOpKind.GetNamedProperty &&
               !operation.IsOptional &&
               !operation.ShortCircuitOnNullishTarget &&
               (allowPrivateNamedPrefix || !operation.GetString(stringConstants).IsPrivateName());
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

        var keyStart = expressionProgram.GetOperation(0).Kind == ExpressionOpKind.EnsureSuperReference ? 1 : 0;
        var keyEnd = callTargetIndexInProgram;
        if (expressionProgram.GetOperation(keyEnd - 1).Kind == ExpressionOpKind.EnsureSuperReference)
        {
            keyEnd--;
        }

        var hasResolvedKey = keyEnd == keyStart + 2 &&
                             expressionProgram.GetOperation(keyStart + 1).Kind == ExpressionOpKind.ResolvePropertyKey;
        if (keyEnd != keyStart + 1 && !hasResolvedKey)
        {
            reason = "Computed super call targets require exactly one computed key operand.";
            return false;
        }

        if (keyStart == 1)
        {
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.EnsureSuperReference));
        }

        if (!TryAppendComputedPropertyKeyLoad(
                expressionProgram.GetOperation(keyStart),
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
        bool allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers: allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers,
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
        bool allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers,
                out reason))
        {
            return false;
        }

        unified[nullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            unified.Count);
        return true;
    }

    // Case 7 (A30): o?.[k](args) — optional-computed-START chain, plain non-optional computed call.
    // Lowers to: LoadSlot(base), JumpIfNullishReplaceUndefined(end), key-load,
    //            PrepareComputedCallTarget, args, (end:). A nullish receiver short-circuits the
    //            whole call to undefined (the call is never made).
    private static bool TryAppendOptionalComputedStartPlainCallTarget(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers,
                out reason))
        {
            return false;
        }

        unified[nullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            unified.Count);
        return true;
    }

    // Case 8 (A30): a?.b?.[k](args) — double-optional chain (optional-named start, optional-computed
    // continuation), plain non-optional call. Computed-key twin of Case 5's a?.b?.c(args).
    // Lowers to: LoadSlot(base), JumpIfNullishReplaceUndefined(end), GetNamedProperty(b),
    //            JumpIfNullishReplaceUndefined(end), key-load, PrepareComputedCallTarget, args, (end:).
    // Either nullish hop short-circuits the whole call to undefined (the call is never made).
    private static bool TryAppendOptionalChainComputedReceiverOptionalCallTarget(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers,
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

        // Emit base load.
        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            return false;
        }

        // Emit first-hop short-circuit (a?.) — backpatch after args.
        var firstNullishJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        // Emit GetNamedProperty(b) — receiver for ?.[k]().
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

        // Emit second-hop short-circuit (?.[k]) — backpatch after args.
        var secondNullishJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

        // Emit computed key.
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
                allowsDynamicIdentifiers,
                out reason))
        {
            return false;
        }

        var chainEnd = unified.Count;
        unified[firstNullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            chainEnd);
        unified[secondNullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            chainEnd);
        return true;
    }

    // Case 4: a?.b.c(args) / a.x?.b.c(args) — optional-start chain, plain non-optional call.
    // Lowers to: prefix-load, JumpIfNullishReplaceUndefined(end), GetNamedProperty(b),
    //            PrepareNamedCallTarget(c), args, (end:)
    private static bool TryAppendOptionalChainPlainCallTarget(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers: allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers,
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
        bool allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers,
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
        bool allowsDynamicIdentifiers,
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
            var receiverBase = expressionProgram.GetOperation(0);
            if (!allowsDynamicIdentifiers ||
                receiverBase.Kind != ExpressionOpKind.LoadIdentifier ||
                receiverBase.IsArguments)
            {
                return false;
            }

            var receiverIdentifier = receiverBase.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
            var receiverNameIndex = stringConstants.Count;
            stringConstants.Add(receiverIdentifier.Name.Name ?? string.Empty);
            unified.Add(new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.LoadDynamicIdentifier,
                receiverNameIndex));
            reason = string.Empty;
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

    private static bool ShouldUseDynamicTypeOfIdentifierForScriptBlockLexical(
        IdentifierOperand identifier,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers) =>
        allowsDynamicIdentifiers &&
        slotLayout.ScriptCompletionSlot >= 0 &&
        identifier.ScopeId >= 0 &&
        identifier.ScopeId != slotLayout.ActivationSlots.ScopeId;

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

        // Snapshot the builder lengths BEFORE any argument is emitted. The greedy per-argument
        // span appenders below can mis-split a complex argument (e.g. consume the receiver `o` of
        // `o.m(x)` as a standalone argument before reaching its call target). A11's region
        // fallback therefore rolls back to this point and re-lowers the WHOLE argument region with
        // the stack-discipline appender rather than from the mid-argument failure point.
        var preArgsUnifiedCount = unified.Count;
        var preArgsLiteralCount = literalConstants.Count;
        var preArgsStringCount = stringConstants.Count;
        var preArgsCallTargetCount = callTargetConstants.Count;

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
                // A LoadLiteral may be a real template literal seed. Preserve that
                // ownership before treating a standalone literal as a binary left operand.
                if (TryMeasureSimpleTemplateLiteralSpan(
                        expressionProgram,
                        operationIndex,
                        activationSlots,
                        out var templateSpanLen,
                        allowsDynamicIdentifiers) &&
                    templateSpanLen > 1)
                {
                    if (!TryAppendSimpleTemplateLiteralSpan(
                            expressionProgram, operationIndex, activationSlots,
                            unified, literalConstants, out _, out reason,
                            allowsDynamicIdentifiers,
                            stringConstants))
                    {
                        return false;
                    }

                    operationIndex += templateSpanLen;
                }
                else if (TryAppendSimpleBinaryOperandSpan(
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
                else if (TryAppendSimplePropertyReadOperandSpan(
                             expressionProgram,
                             operationIndex,
                             callIndex,
                             activationSlots,
                             allowsDynamicIdentifiers,
                             unified,
                             literalConstants,
                             stringConstants,
                             out var propertyReadSpanLen,
                             out reason))
                {
                    operationIndex += propertyReadSpanLen;
                }
                else if (TryAppendSimpleOperandLoadWithDynamic(
                             op,
                             expressionProgram,
                             activationSlots,
                             allowsDynamicIdentifiers,
                             unified,
                             literalConstants,
                             stringConstants,
                             out reason))
                {
                    operationIndex++;
                }
                else
                {
                    return false;
                }
            }
            else if (TryAppendSimpleBinaryOperandSpan(
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
            else if (TryAppendSimplePropertyReadOperandSpan(
                         expressionProgram,
                         operationIndex,
                         callIndex,
                         activationSlots,
                         allowsDynamicIdentifiers,
                         unified,
                         literalConstants,
                         stringConstants,
                         out var propertyReadSpanLen,
                         out reason))
            {
                operationIndex += propertyReadSpanLen;
            }
            else if (TryAppendSimpleOptionalNamedThenComputedReadOperandSpan(
                         expressionProgram,
                         operationIndex,
                         callIndex,
                         activationSlots,
                         allowsDynamicIdentifiers,
                         unified,
                         literalConstants,
                         stringConstants,
                         out var optionalNamedThenComputedSpanLen,
                         out reason))
            {
                operationIndex += optionalNamedThenComputedSpanLen;
            }
            else if (TryAppendSimpleOptionalNamedReadChainOperandSpan(
                         expressionProgram,
                         operationIndex,
                         callIndex,
                         activationSlots,
                         allowsDynamicIdentifiers,
                         unified,
                         literalConstants,
                         stringConstants,
                         out var optionalNamedChainSpanLen,
                         out reason))
            {
                operationIndex += optionalNamedChainSpanLen;
            }
            else if (TryAppendSimpleOptionalComputedPropertyReadOperandSpan(
                         expressionProgram,
                         operationIndex,
                         callIndex,
                         activationSlots,
                         allowsDynamicIdentifiers,
                         unified,
                         literalConstants,
                         stringConstants,
                         out var optionalComputedSpanLen,
                         out reason))
            {
                operationIndex += optionalComputedSpanLen;
            }
            else if (TryAppendSimpleOptionalNamedPropertyReadOperandSpan(
                         expressionProgram,
                         operationIndex,
                         callIndex,
                         activationSlots,
                         allowsDynamicIdentifiers,
                         unified,
                         literalConstants,
                         stringConstants,
                         out var optionalNamedReadSpanLen,
                         out reason))
            {
                operationIndex += optionalNamedReadSpanLen;
            }
            else if (TryAppendSimpleUnaryOperandSpan(
                         expressionProgram,
                         operationIndex,
                         callIndex,
                         activationSlots,
                         allowsDynamicIdentifiers,
                         unified,
                         literalConstants,
                         stringConstants,
                         out var unarySpanLen,
                         out reason))
            {
                operationIndex += unarySpanLen;
            }
            else if (TryAppendSimpleTypeOfOperandSpan(
                         expressionProgram,
                         operationIndex,
                         callIndex,
                         activationSlots,
                         allowsDynamicIdentifiers,
                         unified,
                         literalConstants,
                         stringConstants,
                         callTargetConstants,
                         slotLayout,
                         out var typeOfSpanLen,
                         out reason))
            {
                operationIndex += typeOfSpanLen;
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
                    // A11: complex call arguments. The flat span appenders above cover leaf operands,
                    // binaries/unaries of simple operands, member-read chains, literals, and member
                    // calls with simple args. A richer argument — a NESTED CALL (`g(h(x))`,
                    // `g(o.m(x))`), a binary whose operand is itself a call (`g(a + h(b))`), or any
                    // deeper composition of already-admitted value-producing ops — cannot be split
                    // per-argument by the greedy appenders (a postfix operator can read a value
                    // pushed several ops earlier). Lower the WHOLE remaining argument region with the
                    // general operand-stack appender, which mirrors the eligibility walker's stack
                    // discipline and emits each op's unified lowering in source (evaluation) order,
                    // preserving left-to-right argument evaluation.
                    if (!call.IsDirectEval)
                    {
                        // Roll back ANY arguments already emitted by the greedy span appenders
                        // (they may have mis-split this argument) and re-lower the entire region.
                        unified.Count = preArgsUnifiedCount;
                        literalConstants.Count = preArgsLiteralCount;
                        stringConstants.Count = preArgsStringCount;
                        callTargetConstants.Count = preArgsCallTargetCount;

                        if (TryAppendAdmittedComplexCallArgumentRegion(
                                expressionProgram,
                                slotLayout,
                                unified,
                                literalConstants,
                                stringConstants,
                                callTargetConstants,
                                argsStartIndex,
                                callIndex,
                                call.ArgumentCount,
                                allowsDynamicIdentifiers,
                                out reason))
                        {
                            // The region appender lowered every logical argument up to the call
                            // boundary; account for them and finish the span walk.
                            argCount = call.ArgumentCount;
                            operationIndex = callIndex;
                            break;
                        }
                    }

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

    // A11: lowers the remaining complex call-argument region [startIndex, callIndex) for a
    // non-optional, non-eval, non-spread call. This is the compiler twin of the eligibility
    // walker TryValidateAdmittedComplexCallArgumentRegion: it tracks the operand-stack depth the
    // production VM maintains and emits each op's unified lowering in source (evaluation) order,
    // so left-to-right argument evaluation is preserved and each argument is fully evaluated
    // before the next. Whole multi-op value spans (literals, templates, member-read chains,
    // control expressions, member calls with simple args) are emitted by the existing flat
    // appender; the remaining per-op cases (binary/unary/typeof, property reads, nested-call
    // targets and Call boundaries) are emitted inline here. Emission is all-or-nothing: on any
    // unsupported op or arity mismatch the builders are rolled back to their pre-region state and
    // the method returns false so the caller can decline cleanly.
    private static bool TryAppendAdmittedComplexCallArgumentRegion(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        int startIndex,
        int callIndex,
        int expectedArgumentCount,
        bool allowsDynamicIdentifiers,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();

        var startUnifiedCount = unified.Count;
        var startLiteralCount = literalConstants.Count;
        var startStringCount = stringConstants.Count;
        var startCallTargetCount = callTargetConstants.Count;

        void RollBack()
        {
            unified.Count = startUnifiedCount;
            literalConstants.Count = startLiteralCount;
            stringConstants.Count = startStringCount;
            callTargetConstants.Count = startCallTargetCount;
        }

        var depth = 0;
        const int DynamicIdentifierReferenceSlot = -1;
        List<int>? identifierReferenceSlots = null;
        var index = startIndex;
        while (index < callIndex)
        {
            // Whole value spans (leaf operand, array/object/template literal, member-read chain,
            // control expression, member call with simple args) each net +1 on the operand stack.
            if (TryAppendSimpleLiteralValueOperandSpan(
                    expressionProgram,
                    index,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    slotLayout,
                    out var literalSpan,
                    out _) &&
                literalSpan > 0 &&
                index + literalSpan <= callIndex)
            {
                index += literalSpan;
                depth++;
                continue;
            }

            if (TryAppendSimpleTypeOfOperandSpan(
                    expressionProgram,
                    index,
                    callIndex,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    slotLayout,
                    out var typeOfSpan,
                    out _) &&
                typeOfSpan > 0 &&
                index + typeOfSpan <= callIndex)
            {
                index += typeOfSpan;
                depth++;
                continue;
            }

            var op = expressionProgram.GetOperation(index);
            switch (op.Kind)
            {
                case ExpressionOpKind.LoadLiteral:
                case ExpressionOpKind.LoadThis:
                case ExpressionOpKind.LoadNewTarget:
                case ExpressionOpKind.LoadIdentifier:
                    if (!TryAppendSimpleOperandLoadWithDynamic(
                            op,
                            expressionProgram,
                            activationSlots,
                            allowsDynamicIdentifiers,
                            unified,
                            literalConstants,
                            stringConstants,
                            out var operandReason))
                    {
                        { RollBack(); reason = operandReason; return false; }
                    }

                    depth++;
                    break;

                case ExpressionOpKind.GetNamedProperty:
                    if (depth < 1 ||
                        op.IsOptional ||
                        op.ShortCircuitOnNullishTarget ||
                        op.GetString(expressionStringConstants).IsPrivateName())
                    {
                        { RollBack(); reason = "Unsupported named property read in complex call argument."; return false; }
                    }

                    var namedPropIndex = stringConstants.Count;
                    stringConstants.Add(op.GetString(expressionStringConstants));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.GetNamedProperty,
                        namedPropIndex));
                    break;

                case ExpressionOpKind.GetComputedProperty:
                    if (depth < 2 || op.IsOptional || op.ShortCircuitOnNullishTarget)
                    {
                        { RollBack(); reason = "Unsupported computed property read in complex call argument."; return false; }
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedProperty));
                    depth--;
                    break;

                case ExpressionOpKind.ResolvePropertyKey:
                    if (depth < 1)
                    {
                        { RollBack(); reason = "ResolvePropertyKey underflow in complex call argument."; return false; }
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
                    break;

                case ExpressionOpKind.Binary when IsSupportedBinaryOperator(op.Operator):
                    if (depth < 2)
                    {
                        { RollBack(); reason = "Binary underflow in complex call argument."; return false; }
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)op.Operator));
                    depth--;
                    break;

                case ExpressionOpKind.ResolveIdentifierReference:
                {
                    var referenceIdentifier = op.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (IsImplicitArgumentsIdentifier(referenceIdentifier, slotLayout))
                    {
                        { RollBack(); reason = "arguments assignment references are not supported in complex call arguments."; return false; }
                    }

                    if (TryResolveExplicitActivationSlot(referenceIdentifier, slotLayout, out var referenceSlotIndex))
                    {
                        identifierReferenceSlots ??= [];
                        identifierReferenceSlots.Add(referenceSlotIndex);
                        break;
                    }

                    if (TryResolveActivationSlot(referenceIdentifier, slotLayout, out _))
                    {
                        RollBack();
                        reason =
                            $"Identifier assignment reference '{referenceIdentifier.Name.Name}' resolves only by activation-slot name lookup and is not eligible for slot-reference unified bytecode assignment lowering.";
                        return false;
                    }

                    if (!allowsDynamicIdentifiers)
                    {
                        RollBack();
                        reason =
                            $"Identifier assignment reference '{referenceIdentifier.Name.Name}' requires dynamic lookup and is not eligible in complex call arguments.";
                        return false;
                    }

                    var referenceNameIndex = stringConstants.Count;
                    stringConstants.Add(referenceIdentifier.Name.Name ?? string.Empty);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference,
                        referenceNameIndex));
                    identifierReferenceSlots ??= [];
                    identifierReferenceSlots.Add(DynamicIdentifierReferenceSlot);
                    break;
                }

                case ExpressionOpKind.LoadResolvedIdentifierValue:
                    if (identifierReferenceSlots is not { Count: > 0 })
                    {
                        { RollBack(); reason = "Identifier reference load without a pending reference in complex call argument."; return false; }
                    }

                    if (identifierReferenceSlots[^1] >= 0)
                    {
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.LoadSlot,
                            identifierReferenceSlots[^1]));
                    }
                    else
                    {
                        if (!allowsDynamicIdentifiers)
                        {
                            { RollBack(); reason = "Dynamic identifier assignment references are not eligible in complex call arguments."; return false; }
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadDynamicIdentifierReference));
                    }

                    depth++;
                    break;

                case ExpressionOpKind.StoreResolvedIdentifier:
                {
                    if (depth < 1)
                    {
                        { RollBack(); reason = "Identifier reference store underflow in complex call argument."; return false; }
                    }

                    var storeReferenceIdentifier = op.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (IsImplicitArgumentsIdentifier(storeReferenceIdentifier, slotLayout))
                    {
                        { RollBack(); reason = "arguments assignment references are not supported in complex call arguments."; return false; }
                    }

                    if (TryResolveExplicitActivationSlot(
                            storeReferenceIdentifier,
                            slotLayout,
                            out var storeReferenceSlotIndex))
                    {
                        if (identifierReferenceSlots is not { Count: > 0 } ||
                            identifierReferenceSlots[^1] != storeReferenceSlotIndex)
                        {
                            { RollBack(); reason = "Identifier reference store target mismatch in complex call argument."; return false; }
                        }

                        identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DuplicateTop));
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.StoreSlot,
                            storeReferenceSlotIndex));
                        break;
                    }

                    if (TryResolveActivationSlot(storeReferenceIdentifier, slotLayout, out _))
                    {
                        RollBack();
                        reason =
                            $"Identifier assignment reference '{storeReferenceIdentifier.Name.Name}' resolves only by activation-slot name lookup and is not eligible for slot-reference unified bytecode assignment lowering.";
                        return false;
                    }

                    if (!allowsDynamicIdentifiers ||
                        identifierReferenceSlots is not { Count: > 0 } ||
                        identifierReferenceSlots[^1] != DynamicIdentifierReferenceSlot)
                    {
                        { RollBack(); reason = "Dynamic identifier reference store without a pending dynamic reference in complex call argument."; return false; }
                    }

                    var storeReferenceNameIndex = stringConstants.Count;
                    stringConstants.Add(storeReferenceIdentifier.Name.Name ?? string.Empty);
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.StoreDynamicIdentifierReference,
                        EncodeDynamicStoreOperand(storeReferenceNameIndex, op)));
                    identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                    break;
                }

                case ExpressionOpKind.PopResolvedIdentifierReference:
                    if (identifierReferenceSlots is not { Count: > 0 })
                    {
                        { RollBack(); reason = "Identifier reference pop without a pending reference in complex call argument."; return false; }
                    }

                    var pendingReferenceSlot = identifierReferenceSlots[^1];
                    identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                    if (pendingReferenceSlot < 0)
                    {
                        if (!allowsDynamicIdentifiers)
                        {
                            { RollBack(); reason = "Dynamic identifier assignment references are not eligible in complex call arguments."; return false; }
                        }

                        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.PopDynamicIdentifierReference));
                    }

                    break;

                case ExpressionOpKind.UnaryPlus:
                    if (depth < 1) { RollBack(); reason = "Unary underflow in complex call argument."; return false; }
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.UnaryPlus));
                    break;

                case ExpressionOpKind.UnaryMinus:
                    if (depth < 1) { RollBack(); reason = "Unary underflow in complex call argument."; return false; }
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.UnaryMinus));
                    break;

                case ExpressionOpKind.UnaryLogicalNot:
                    if (depth < 1) { RollBack(); reason = "Unary underflow in complex call argument."; return false; }
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.UnaryLogicalNot));
                    break;

                case ExpressionOpKind.UnaryBitwiseNot:
                    if (depth < 1) { RollBack(); reason = "Unary underflow in complex call argument."; return false; }
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.UnaryBitwiseNot));
                    break;

                case ExpressionOpKind.UnaryVoid:
                    if (depth < 1) { RollBack(); reason = "Unary underflow in complex call argument."; return false; }
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.UnaryVoid));
                    break;

                case ExpressionOpKind.TypeOf:
                    if (depth < 1) { RollBack(); reason = "TypeOf underflow in complex call argument."; return false; }
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.TypeOf));
                    break;

                case ExpressionOpKind.LoadIdentifierCallTarget:
                {
                    if (op.IsArguments)
                    {
                        { RollBack(); reason = "arguments call targets are not supported in complex call arguments."; return false; }
                    }

                    var callTargetIdentifier = op.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (string.Equals(callTargetIdentifier.Name.Name, "eval", StringComparison.Ordinal))
                    {
                        { RollBack(); reason = "eval call targets are not supported in complex call arguments."; return false; }
                    }

                    if (!TryResolveActivationCallTargetSlot(callTargetIdentifier, slotLayout, out var resolvedSlot))
                    {
                        if (!allowsDynamicIdentifiers &&
                            !CanUseMaterializedActivationDynamicLookup(callTargetIdentifier, activationSlots))
                        {
                            RollBack();
                            reason =
                                $"Identifier call target '{callTargetIdentifier.Name.Name}' requires dynamic lookup and is not eligible.";
                            return false;
                        }

                        var dynamicNameIdx = stringConstants.Count;
                        stringConstants.Add(callTargetIdentifier.Name.Name ?? string.Empty);
                        unified.Add(new UnifiedBytecodeInstruction(
                            UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget,
                            dynamicNameIdx));
                        depth += 2;
                        break;
                    }

                    var nameIdx = stringConstants.Count;
                    stringConstants.Add(callTargetIdentifier.Name.Name ?? string.Empty);
                    var ctIdx = callTargetConstants.Count;
                    callTargetConstants.Add(new UnifiedBytecodeCallTarget(
                        UnifiedBytecodeCallTargetKind.Identifier,
                        resolvedSlot,
                        nameIdx));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrepareIdentifierCallTarget,
                        ctIdx));
                    depth += 2;
                    break;
                }

                case ExpressionOpKind.LoadNamedCallTarget:
                {
                    if (depth < 1 ||
                        op.IsOptional ||
                        op.ShortCircuitOnNullishTarget)
                    {
                        RollBack();
                        reason = "Unsupported named call target in complex call argument.";
                        return false;
                    }

                    var namedCtName = op.GetString(expressionStringConstants);
                    var namedCtNameIdx = stringConstants.Count;
                    stringConstants.Add(namedCtName);
                    var namedCtIdx = callTargetConstants.Count;
                    callTargetConstants.Add(new UnifiedBytecodeCallTarget(
                        UnifiedBytecodeCallTargetKind.NamedMember,
                        NameConstantIndex: namedCtNameIdx));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrepareNamedCallTarget,
                        namedCtIdx));
                    depth++;
                    break;
                }

                case ExpressionOpKind.LoadComputedCallTarget:
                {
                    if (depth < 2 || op.IsOptional || op.ShortCircuitOnNullishTarget)
                    {
                        { RollBack(); reason = "Unsupported computed call target in complex call argument."; return false; }
                    }

                    var computedCtIdx = callTargetConstants.Count;
                    callTargetConstants.Add(new UnifiedBytecodeCallTarget(UnifiedBytecodeCallTargetKind.ComputedMember));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrepareComputedCallTarget,
                        computedCtIdx));
                    break;
                }

                case ExpressionOpKind.EnsureSuperReference:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.EnsureSuperReference));
                    break;

                case ExpressionOpKind.LoadNamedSuperCallTarget:
                {
                    var namedSuperCtName = op.GetString(expressionStringConstants);
                    if (namedSuperCtName.IsPrivateName())
                    {
                        RollBack();
                        reason = "Private named super call target in complex call argument.";
                        return false;
                    }

                    var namedSuperCtNameIdx = stringConstants.Count;
                    stringConstants.Add(namedSuperCtName);
                    var namedSuperCtIdx = callTargetConstants.Count;
                    callTargetConstants.Add(new UnifiedBytecodeCallTarget(
                        UnifiedBytecodeCallTargetKind.NamedSuperMember,
                        NameConstantIndex: namedSuperCtNameIdx));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget,
                        namedSuperCtIdx));
                    depth += 2;
                    break;
                }

                case ExpressionOpKind.LoadComputedSuperCallTarget:
                {
                    if (depth < 1)
                    {
                        RollBack();
                        reason = "Unsupported computed super call target in complex call argument.";
                        return false;
                    }

                    var computedSuperCtIdx = callTargetConstants.Count;
                    callTargetConstants.Add(new UnifiedBytecodeCallTarget(UnifiedBytecodeCallTargetKind.ComputedSuperMember));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget,
                        computedSuperCtIdx));
                    depth++;
                    break;
                }

                case ExpressionOpKind.Call:
                    if (!op.HasExplicitThis ||
                        op.IsDirectEval ||
                        op.SpreadMaskConstantIndex >= 0 ||
                        depth < op.ArgumentCount + 2)
                    {
                        { RollBack(); reason = "Unsupported nested call in complex call argument."; return false; }
                    }

                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.CallInvocationBoundary,
                        EncodeCallBoundaryOperand(op.ArgumentCount, -1, isDirectEval: false)));
                    depth -= op.ArgumentCount + 1;
                    break;

                default:
                    { RollBack(); reason = $"Unsupported op '{op.Kind}' in complex call argument."; return false; }
            }

            index++;
        }

        if (depth != expectedArgumentCount ||
            identifierReferenceSlots is { Count: > 0 })
        {
            { RollBack(); reason = "Complex call argument region did not produce the expected operand count."; return false; }
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryAppendSimpleBinaryOperandSpan(
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
            reason = "Only supported simple binary operators are admitted in this boundary.";
            return false;
        }

        if (!CanAppendSimpleOperandLoadWithDynamic(left, expressionProgram, activationSlots, allowsDynamicIdentifiers) ||
            !CanAppendSimpleOperandLoadWithDynamic(right, expressionProgram, activationSlots, allowsDynamicIdentifiers))
        {
            spanLength = 0;
            reason = "Simple binary spans require simple activation-resolved or admitted dynamic operands.";
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
    //   - [simple-literal-value-span, ArrayPush]   - normal element
    //   - [simple-literal-value-span, ArraySpread] - spread element
    //   - ArrayPushHole                            - hole element
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

            if (TryAppendSimpleLiteralValueOperandSpan(
                    expressionProgram,
                    i,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    slotLayout,
                    out var elementSpanLength,
                    out reason))
            {
                i += elementSpanLength;
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
    //   Static:   [simple-literal-value-span, DefineObjectProperty(non-private, no name inference)]
    //   Computed: [simple-key-span or simple-binary-key-expression, ResolvePropertyKey,
    //              simple-literal-value-span, DefineComputedObjectProperty(no name inference)]
    //   Spread:   [simple-spread-source-span, ObjectSpread]
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
        bool allowsDynamicIdentifiers = false,
        ImmutableArray<FunctionLiteralDescriptor>.Builder? functionLiteralConstants = null)
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

                if (!TryAppendSimpleLiteralValueOperandSpan(
                        expressionProgram,
                        i,
                        activationSlots,
                        allowsDynamicIdentifiers,
                        unified,
                        literalConstants,
                        stringConstants,
                        callTargetConstants,
                        slotLayout,
                        out var valueSpanLength,
                        out reason))
                {
                    spanLength = 0;
                    reason = "Complex value expressions are not admitted in simple computed object properties.";
                    return false;
                }

                i += valueSpanLength;
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

                if (!TryAppendSimpleLiteralValueOperandSpan(
                        expressionProgram,
                        i,
                        activationSlots,
                        allowsDynamicIdentifiers,
                        unified,
                        literalConstants,
                        stringConstants,
                        callTargetConstants,
                        slotLayout,
                        out var valueSpanLength,
                        out reason))
                {
                    spanLength = 0;
                    reason = "Complex value expressions are not admitted in simple computed object properties.";
                    return false;
                }

                i += valueSpanLength;
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

            if (slotLayout is not null &&
                callTargetConstants is not null &&
                TryMeasureSimpleMemberCallOperandSpan(
                    expressionProgram,
                    i,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    out var spreadCallSpanLength) &&
                i + spreadCallSpanLength < expressionProgram.OperationCount &&
                expressionProgram.GetOperation(i + spreadCallSpanLength).Kind == ExpressionOpKind.ObjectSpread)
            {
                if (!TryAppendSimpleMemberCallOperandSpan(
                        expressionProgram,
                        i,
                        activationSlots,
                        slotLayout,
                        allowsDynamicIdentifiers,
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

            if (TryAppendSimpleObjectLiteralMethodOrAccessorMemberSpan(
                    expressionProgram,
                    i,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    slotLayout,
                    functionLiteralConstants,
                    out var methodMemberSpanLength,
                    out reason))
            {
                i += methodMemberSpanLength;
                continue;
            }

            if (!TryAppendSimpleLiteralValueOperandSpan(
                    expressionProgram,
                    i,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    slotLayout,
                    out var firstSpanLength,
                    out reason))
            {
                // Non-simple first op — property scan is done; the object literal ends here.
                break;
            }

            i += firstSpanLength;
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

                if (!TryAppendSimpleLiteralValueOperandSpan(
                        expressionProgram,
                        i,
                        activationSlots,
                        allowsDynamicIdentifiers,
                        unified,
                        literalConstants,
                        stringConstants,
                        callTargetConstants,
                        slotLayout,
                        out var valueSpanLength,
                        out reason))
                {
                    spanLength = 0;
                    reason = "Complex value expressions are not admitted in simple computed object properties.";
                    return false;
                }

                i += valueSpanLength;
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

    private static bool TryAppendSimpleObjectLiteralMethodOrAccessorMemberSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        ImmutableArray<FunctionLiteralDescriptor>.Builder? functionLiteralConstants,
        out int spanLength,
        out string reason)
    {
        spanLength = 0;
        reason = string.Empty;

        if (functionLiteralConstants is null)
        {
            return false;
        }

        var operation = expressionProgram.GetOperation(startIndex);
        if (operation.Kind == ExpressionOpKind.LoadFunctionLiteral)
        {
            if (startIndex + 1 >= expressionProgram.OperationCount)
            {
                return false;
            }

            var defineOp = expressionProgram.GetOperation(startIndex + 1);
            if (defineOp.Kind is not (ExpressionOpKind.DefineObjectMethod or ExpressionOpKind.DefineObjectAccessor))
            {
                return false;
            }

            AppendLoadFunctionLiteral(operation, expressionProgram, unified, functionLiteralConstants);
            AppendStaticObjectMethodOrAccessorDefinition(defineOp, expressionProgram, unified, stringConstants);
            spanLength = 2;
            return true;
        }

        var startUnifiedCount = unified.Count;
        var startLiteralCount = literalConstants.Count;
        var startStringCount = stringConstants.Count;
        var startFunctionLiteralCount = functionLiteralConstants.Count;
        if (!TryAppendSimpleLiteralValueOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out var keySpanLength,
                out reason))
        {
            reason = string.Empty;
            return false;
        }

        var resolveIndex = startIndex + keySpanLength;
        if (resolveIndex + 2 >= expressionProgram.OperationCount ||
            expressionProgram.GetOperation(resolveIndex).Kind != ExpressionOpKind.ResolvePropertyKey ||
            expressionProgram.GetOperation(resolveIndex + 1).Kind != ExpressionOpKind.LoadFunctionLiteral)
        {
            RollBackUnifiedBuilder(unified, startUnifiedCount);
            RollBackUnifiedBuilder(literalConstants, startLiteralCount);
            RollBackUnifiedBuilder(stringConstants, startStringCount);
            functionLiteralConstants.Count = startFunctionLiteralCount;
            reason = string.Empty;
            return false;
        }

        var computedDefineOp = expressionProgram.GetOperation(resolveIndex + 2);
        if (computedDefineOp.Kind is not (ExpressionOpKind.DefineComputedObjectMethod or ExpressionOpKind.DefineComputedObjectAccessor))
        {
            RollBackUnifiedBuilder(unified, startUnifiedCount);
            RollBackUnifiedBuilder(literalConstants, startLiteralCount);
            RollBackUnifiedBuilder(stringConstants, startStringCount);
            functionLiteralConstants.Count = startFunctionLiteralCount;
            reason = string.Empty;
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
        AppendLoadFunctionLiteral(expressionProgram.GetOperation(resolveIndex + 1), expressionProgram, unified, functionLiteralConstants);
        AppendComputedObjectMethodOrAccessorDefinition(computedDefineOp, unified);
        spanLength = keySpanLength + 3;
        return true;
    }

    private static void AppendLoadFunctionLiteral(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<FunctionLiteralDescriptor>.Builder functionLiteralConstants)
    {
        var functionLiteralIndex = functionLiteralConstants.Count;
        functionLiteralConstants.Add(
            operation.GetObject<FunctionLiteralDescriptor>(expressionProgram.ObjectConstants.AsSpan()));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.LoadFunctionLiteral,
            EncodeLoadFunctionLiteralOperand(functionLiteralIndex, operation.IsConstructorFunction)));
    }

    private static void AppendStaticObjectMethodOrAccessorDefinition(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants)
    {
        var nameIndex = stringConstants.Count;
        stringConstants.Add(operation.GetString(expressionProgram.StringConstants.AsSpan()));
        if (operation.Kind == ExpressionOpKind.DefineObjectMethod)
        {
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DefineObjectMethod, nameIndex));
            return;
        }

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.DefineObjectAccessor,
            EncodeObjectAccessorOperand(nameIndex, operation.AccessorKind)));
    }

    private static void AppendComputedObjectMethodOrAccessorDefinition(
        PackedExpressionOp operation,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified)
    {
        if (operation.Kind == ExpressionOpKind.DefineComputedObjectMethod)
        {
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DefineComputedObjectMethod));
            return;
        }

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.DefineComputedObjectAccessor,
            EncodeObjectAccessorOperand(0, operation.AccessorKind)));
    }

    private static bool TryAppendSimpleLiteralValueOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        out int spanLength,
        out string reason)
    {
        return TryAppendSimpleLiteralValueOperandSpanCore(
            expressionProgram,
            startIndex,
            activationSlots,
            allowsDynamicIdentifiers,
            unified,
            literalConstants,
            stringConstants,
            callTargetConstants,
            slotLayout,
            out spanLength,
            out reason,
            allowControlExpressions: true);
    }

    private static bool TryAppendSimpleLiteralValueOperandSpanCore(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        out int spanLength,
        out string reason,
        bool allowControlExpressions)
    {
        if (slotLayout is not null &&
            callTargetConstants is not null &&
            TryMeasureSimpleMemberCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out spanLength))
        {
            if (TryAppendSimpleMemberCallOperandSpan(
                    expressionProgram,
                    startIndex,
                    activationSlots,
                    slotLayout,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    out reason))
            {
                return true;
            }

            spanLength = 0;
            return false;
        }

        var operation = expressionProgram.GetOperation(startIndex);
        if (operation.Kind == ExpressionOpKind.CreateArray)
        {
            return TryAppendSimpleArrayLiteralSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out spanLength,
                out reason,
                allowsDynamicIdentifiers);
        }

        if (operation.Kind == ExpressionOpKind.CreateObject)
        {
            return TryAppendSimpleObjectLiteralSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out spanLength,
                out reason,
                allowsDynamicIdentifiers);
        }

        if (operation.Kind == ExpressionOpKind.LoadLiteral &&
            TryMeasureSimpleTemplateLiteralSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                out var templateSpanLength,
                allowsDynamicIdentifiers) &&
            templateSpanLength > 1)
        {
            if (TryAppendSimpleTemplateLiteralSpan(
                    expressionProgram,
                    startIndex,
                    activationSlots,
                    unified,
                    literalConstants,
                    out spanLength,
                    out reason,
                    allowsDynamicIdentifiers,
                    stringConstants) &&
                spanLength == templateSpanLength)
            {
                return true;
            }

            spanLength = 0;
            return false;
        }

        if (TryAppendSimpleTypeOfOperandSpan(
                expressionProgram,
                startIndex,
                expressionProgram.OperationCount,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out spanLength,
                out reason))
        {
            return true;
        }

        if (TryAppendSimpleBinaryOperandSpan(
                expressionProgram,
                startIndex,
                expressionProgram.OperationCount,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out spanLength,
                out reason))
        {
            return true;
        }

        if (TryAppendSimpleUnaryOperandSpan(
                expressionProgram,
                startIndex,
                expressionProgram.OperationCount,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out spanLength,
                out reason))
        {
            return true;
        }

        if (TryAppendSimplePropertyReadOperandSpan(
                expressionProgram,
                startIndex,
                expressionProgram.OperationCount,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out spanLength,
                out reason))
        {
            return true;
        }

        if (allowControlExpressions &&
            TryAppendSimpleControlExpressionOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out spanLength,
                out reason))
        {
            return true;
        }

        if (TryAppendSimpleOperandLoadWithDynamic(
                operation,
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason))
        {
            spanLength = 1;
            return true;
        }

        spanLength = 0;
        return false;
    }

    private static bool TryAppendSimpleControlExpressionOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        out int spanLength,
        out string reason)
    {
        return TryAppendSimpleLogicalControlExpressionOperandSpan(
                   expressionProgram,
                   startIndex,
                   activationSlots,
                   allowsDynamicIdentifiers,
                   unified,
                   literalConstants,
                   stringConstants,
                   callTargetConstants,
                   slotLayout,
                   out spanLength,
                   out reason) ||
               TryAppendSimpleConditionalExpressionOperandSpan(
                   expressionProgram,
                   startIndex,
                   activationSlots,
                   allowsDynamicIdentifiers,
                   unified,
                   literalConstants,
                   stringConstants,
                   callTargetConstants,
                   slotLayout,
                   out spanLength,
                   out reason);
    }

    private static bool TryAppendSimpleLogicalControlExpressionOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        out int spanLength,
        out string reason)
    {
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        var callTargetCount = callTargetConstants?.Count ?? 0;

        if (!TryAppendSimpleLiteralValueOperandSpanCore(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out var leftSpanLength,
                out reason,
                allowControlExpressions: false))
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        var jumpIndex = startIndex + leftSpanLength;
        var popIndex = jumpIndex + 1;
        var rhsStartIndex = jumpIndex + 2;
        if (rhsStartIndex >= expressionProgram.OperationCount)
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        var jump = expressionProgram.GetOperation(jumpIndex);
        if (!TryGetSimpleLogicalJumpOpCode(jump.Kind, out var jumpOpCode) ||
            expressionProgram.GetOperation(popIndex).Kind != ExpressionOpKind.Pop)
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        var jumpUnifiedIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(jumpOpCode, 0));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));

        if (!TryAppendSimpleLiteralValueOperandSpanCore(
                expressionProgram,
                rhsStartIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out var rhsSpanLength,
                out reason,
                allowControlExpressions: true))
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = "Unsupported logical control-expression operand in simple literal span.";
            return false;
        }

        var endIndex = rhsStartIndex + rhsSpanLength;
        if (jump.Target != endIndex)
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        unified[jumpUnifiedIndex] = new UnifiedBytecodeInstruction(jumpOpCode, unified.Count);
        spanLength = endIndex - startIndex;
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendSimpleConditionalExpressionOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        out int spanLength,
        out string reason)
    {
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        var callTargetCount = callTargetConstants?.Count ?? 0;

        if (!TryAppendSimpleLiteralValueOperandSpanCore(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out var conditionSpanLength,
                out reason,
                allowControlExpressions: false))
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        var branchIndex = startIndex + conditionSpanLength;
        if (branchIndex >= expressionProgram.OperationCount ||
            expressionProgram.GetOperation(branchIndex).Kind != ExpressionOpKind.JumpIfConditionalFalse)
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        var branchJump = expressionProgram.GetOperation(branchIndex);
        var branchUnifiedIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfFalse, 0));

        var consequentStartIndex = branchIndex + 1;
        if (!TryAppendSimpleLiteralValueOperandSpanCore(
                expressionProgram,
                consequentStartIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out var consequentSpanLength,
                out reason,
                allowControlExpressions: true))
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = "Unsupported conditional consequent in simple literal span.";
            return false;
        }

        var jumpIndex = consequentStartIndex + consequentSpanLength;
        var alternateStartIndex = jumpIndex + 1;
        if (alternateStartIndex >= expressionProgram.OperationCount ||
            expressionProgram.GetOperation(jumpIndex).Kind != ExpressionOpKind.Jump ||
            branchJump.Target != alternateStartIndex)
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        var jump = expressionProgram.GetOperation(jumpIndex);
        var jumpUnifiedIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));
        unified[branchUnifiedIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfFalse,
            unified.Count);

        if (!TryAppendSimpleLiteralValueOperandSpanCore(
                expressionProgram,
                alternateStartIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out var alternateSpanLength,
                out reason,
                allowControlExpressions: true))
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = "Unsupported conditional alternate in simple literal span.";
            return false;
        }

        var endIndex = alternateStartIndex + alternateSpanLength;
        if (jump.Target != endIndex)
        {
            RollBackSimpleControlExpressionProbe(
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                unifiedCount,
                literalCount,
                stringCount,
                callTargetCount);
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        unified[jumpUnifiedIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, unified.Count);
        spanLength = endIndex - startIndex;
        reason = string.Empty;
        return true;
    }

    private static bool TryGetSimpleLogicalJumpOpCode(ExpressionOpKind expressionOpKind, out UnifiedBytecodeOpCode opCode)
    {
        switch (expressionOpKind)
        {
            case ExpressionOpKind.JumpIfFalse:
                opCode = UnifiedBytecodeOpCode.JumpIfShortCircuitFalse;
                return true;
            case ExpressionOpKind.JumpIfTrue:
                opCode = UnifiedBytecodeOpCode.JumpIfShortCircuitTrue;
                return true;
            case ExpressionOpKind.JumpIfNotNullish:
                opCode = UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish;
                return true;
            default:
                opCode = default;
                return false;
        }
    }

    private static void RollBackSimpleControlExpressionProbe(
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        int unifiedCount,
        int literalCount,
        int stringCount,
        int callTargetCount)
    {
        RollBackUnifiedBuilder(unified, unifiedCount);
        RollBackUnifiedBuilder(literalConstants, literalCount);
        RollBackUnifiedBuilder(stringConstants, stringCount);
        if (callTargetConstants is not null)
        {
            RollBackUnifiedBuilder(callTargetConstants, callTargetCount);
        }
    }

    private static bool TryAppendSimpleTypeOfOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        int endExclusive,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        out int spanLength,
        out string reason)
    {
        var operation = expressionProgram.GetOperation(startIndex);
        if (operation.Kind == ExpressionOpKind.TypeOfIdentifier)
        {
            return TryAppendSimpleTypeOfIdentifierOperand(
                operation,
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                slotLayout,
                out spanLength,
                out reason);
        }

        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        var callTargetCount = callTargetConstants?.Count ?? 0;
        if (!TryAppendSimpleTypeOfValueOperandSpan(
                expressionProgram,
                startIndex,
                endExclusive,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out var operandSpanLength,
                out reason) ||
            startIndex + operandSpanLength >= endExclusive ||
            expressionProgram.GetOperation(startIndex + operandSpanLength).Kind != ExpressionOpKind.TypeOf)
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            if (callTargetConstants is not null)
            {
                RollBackUnifiedBuilder(callTargetConstants, callTargetCount);
            }

            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.TypeOf));
        spanLength = operandSpanLength + 1;
        reason = string.Empty;
        return true;
    }

    private static void RollBackUnifiedBuilder<T>(ImmutableArray<T>.Builder builder, int count)
    {
        if (builder.Count > count)
        {
            builder.RemoveRange(count, builder.Count - count);
        }
    }

    private static bool TryAppendSimpleTypeOfIdentifierOperand(
        PackedExpressionOp operation,
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        out int spanLength,
        out string reason)
    {
        var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
        if (slotLayout is not null &&
            ShouldUseDynamicTypeOfIdentifierForScriptBlockLexical(
                identifier,
                slotLayout,
                allowsDynamicIdentifiers))
        {
            var blockLexicalNameIndex = stringConstants.Count;
            stringConstants.Add(identifier.Name.Name ?? string.Empty);
            unified.Add(new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.TypeOfDynamicIdentifier,
                blockLexicalNameIndex));
            spanLength = 1;
            reason = string.Empty;
            return true;
        }

        if (slotLayout is not null
                ? TryResolveActivationSlot(identifier, slotLayout, out var slotIndex)
                : TryResolveActivationSlot(identifier, activationSlots, out slotIndex))
        {
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.TypeOfIdentifier, slotIndex));
            spanLength = 1;
            reason = string.Empty;
            return true;
        }

        if (operation.IsArguments && !allowsDynamicIdentifiers)
        {
            var argumentsTypeIndex = literalConstants.Count;
            literalConstants.Add(new JsValue("object"));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, argumentsTypeIndex));
            spanLength = 1;
            reason = string.Empty;
            return true;
        }

        if (!allowsDynamicIdentifiers)
        {
            spanLength = 0;
            reason = $"typeof identifier '{identifier.Name.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
            return false;
        }

        var nameIndex = stringConstants.Count;
        stringConstants.Add(identifier.Name.Name ?? string.Empty);
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.TypeOfDynamicIdentifier, nameIndex));
        spanLength = 1;
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendSimpleTypeOfValueOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        int endExclusive,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder? callTargetConstants,
        UnifiedBytecodeSlotLayout? slotLayout,
        out int spanLength,
        out string reason)
    {
        if (slotLayout is not null &&
            callTargetConstants is not null &&
            TryMeasureSimpleMemberCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out spanLength))
        {
            if (TryAppendSimpleMemberCallOperandSpan(
                    expressionProgram,
                    startIndex,
                    activationSlots,
                    slotLayout,
                    allowsDynamicIdentifiers,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    out reason))
            {
                return true;
            }

            spanLength = 0;
            return false;
        }

        var operation = expressionProgram.GetOperation(startIndex);
        if (operation.Kind == ExpressionOpKind.CreateArray)
        {
            return TryAppendSimpleArrayLiteralSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out spanLength,
                out reason,
                allowsDynamicIdentifiers);
        }

        if (operation.Kind == ExpressionOpKind.CreateObject)
        {
            return TryAppendSimpleObjectLiteralSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                slotLayout,
                out spanLength,
                out reason,
                allowsDynamicIdentifiers);
        }

        if (operation.Kind == ExpressionOpKind.LoadLiteral &&
            TryMeasureSimpleTemplateLiteralSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                out var templateSpanLength,
                allowsDynamicIdentifiers) &&
            templateSpanLength > 1)
        {
            if (TryAppendSimpleTemplateLiteralSpan(
                    expressionProgram,
                    startIndex,
                    activationSlots,
                    unified,
                    literalConstants,
                    out spanLength,
                    out reason,
                    allowsDynamicIdentifiers,
                    stringConstants) &&
                spanLength == templateSpanLength)
            {
                return true;
            }

            spanLength = 0;
            return false;
        }

        if (TryAppendSimpleBinaryOperandSpan(
                expressionProgram,
                startIndex,
                endExclusive,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out spanLength,
                out reason))
        {
            return true;
        }

        if (TryAppendSimpleUnaryOperandSpan(
                expressionProgram,
                startIndex,
                endExclusive,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out spanLength,
                out reason))
        {
            return true;
        }

        if (TryAppendSimplePropertyReadOperandSpan(
                expressionProgram,
                startIndex,
                endExclusive,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out spanLength,
                out reason))
        {
            return true;
        }

        if (TryAppendSimpleOperandLoadWithDynamic(
                operation,
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason))
        {
            spanLength = 1;
            return true;
        }

        spanLength = 0;
        return false;
    }

    private static bool TryAppendSimplePropertyReadOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        int endExclusive,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out int spanLength,
        out string reason,
        bool allowPrivateNamedPrefix = true)
    {
        if (TryMeasureSimpleComputedPropertyReadOperandSpan(
                expressionProgram,
                startIndex,
                endExclusive,
                activationSlots,
                allowsDynamicIdentifiers,
                out var keyStart,
                out var keyEndExclusive,
                out spanLength,
                allowPrivateNamedPrefix))
        {
            return TryAppendMeasuredSimpleComputedPropertyReadOperandSpan(
                expressionProgram,
                startIndex,
                keyStart,
                keyEndExclusive,
                spanLength,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason);
        }

        if (TryMeasureSimpleNamedPropertyReadOperandSpan(
                expressionProgram,
                startIndex,
                endExclusive,
                activationSlots,
                allowsDynamicIdentifiers,
                out spanLength,
                allowPrivateNamedPrefix))
        {
            return TryAppendMeasuredSimpleNamedPropertyReadOperandSpan(
                expressionProgram,
                startIndex,
                spanLength,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                out reason);
        }

        spanLength = 0;
        reason = string.Empty;
        return false;
    }

    private static bool TryMeasureSimpleNamedPropertyReadOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        int endExclusive,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int spanLength,
        bool allowPrivateNamedPrefix = false)
    {
        spanLength = 0;
        if (startIndex + 1 >= endExclusive ||
            !CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var index = startIndex + 1;
        while (index < endExclusive &&
               IsPlainNamedPropertyReadOperandPrefix(
                   expressionProgram.GetOperation(index),
                   expressionProgram.StringConstants.AsSpan(),
                   allowPrivateNamedPrefix))
        {
            index++;
        }

        if (index == startIndex + 1)
        {
            return false;
        }

        spanLength = index - startIndex;
        return true;
    }

    private static bool TryMeasureSimpleComputedPropertyReadOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        int endExclusive,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int keyStart,
        out int keyEndExclusive,
        out int spanLength,
        bool allowPrivateNamedPrefix = false)
    {
        keyStart = 0;
        keyEndExclusive = 0;
        spanLength = 0;
        if (startIndex + 4 >= endExclusive ||
            !CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        keyStart = startIndex + 1;
        while (keyStart < endExclusive &&
               IsPlainNamedPropertyReadOperandPrefix(
                   expressionProgram.GetOperation(keyStart),
                   expressionProgram.StringConstants.AsSpan(),
                   allowPrivateNamedPrefix))
        {
            keyStart++;
        }

        for (var requireIndex = keyStart + 1; requireIndex + 2 < endExclusive; requireIndex++)
        {
            var requireObjectCoercible = expressionProgram.GetOperation(requireIndex);
            if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible ||
                requireObjectCoercible.Depth != 1)
            {
                continue;
            }

            var resolvePropertyKey = expressionProgram.GetOperation(requireIndex + 1);
            var getComputedProperty = expressionProgram.GetOperation(requireIndex + 2);
            if (resolvePropertyKey.Kind != ExpressionOpKind.ResolvePropertyKey ||
                getComputedProperty.Kind != ExpressionOpKind.GetComputedProperty ||
                getComputedProperty.ShortCircuitOnNullishTarget)
            {
                continue;
            }

            if (!IsSupportedComputedPropertyKeySpan(
                    expressionProgram,
                    activationSlots,
                    keyStart,
                    requireIndex,
                    allowsDynamicIdentifiers))
            {
                continue;
            }

            var end = requireIndex + 3;
            while (end < endExclusive &&
                   IsPlainNamedPropertyReadOperandPrefix(
                       expressionProgram.GetOperation(end),
                       expressionProgram.StringConstants.AsSpan(),
                       allowPrivateNamedPrefix))
            {
                end++;
            }

            keyEndExclusive = requireIndex;
            spanLength = end - startIndex;
            return true;
        }

        return false;
    }

    private static bool TryAppendMeasuredSimpleNamedPropertyReadOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        int spanLength,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        for (var index = startIndex + 1; index < startIndex + spanLength; index++)
        {
            AppendPlainNamedPropertyRead(
                expressionProgram.GetOperation(index),
                expressionStringConstants,
                unified,
                stringConstants);
        }

        if (unified.Count > unifiedCount &&
            literalConstants.Count >= literalCount &&
            stringConstants.Count >= stringCount)
        {
            reason = string.Empty;
            return true;
        }

        RollBackUnifiedBuilder(unified, unifiedCount);
        RollBackUnifiedBuilder(literalConstants, literalCount);
        RollBackUnifiedBuilder(stringConstants, stringCount);
        reason = "Failed to emit measured named property read span.";
        return false;
    }

    // Emits a baseline optional named property-read operand span (`box?.value`):
    // a simple base operand load followed by a single GetNamedPropertyOptional.
    // The optional opcode yields undefined for a nullish base, so no short-circuit
    // jump is needed. Chained optional reads carry a ShortCircuitOnNullishTarget
    // continuation hop and are not handled here.
    private static bool TryAppendSimpleOptionalNamedPropertyReadOperandSpan(
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
        spanLength = 0;
        reason = string.Empty;
        if (startIndex + 1 >= endExclusive ||
            !CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var namedRead = expressionProgram.GetOperation(startIndex + 1);
        if (namedRead.Kind != ExpressionOpKind.GetNamedProperty ||
            !namedRead.IsOptional ||
            namedRead.ShortCircuitOnNullishTarget ||
            namedRead.GetString(expressionStringConstants).IsPrivateName())
        {
            return false;
        }

        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(namedRead.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.GetNamedPropertyOptional,
            propertyNameIndex));

        if (unified.Count > unifiedCount)
        {
            spanLength = 2;
            reason = string.Empty;
            return true;
        }

        RollBackUnifiedBuilder(unified, unifiedCount);
        RollBackUnifiedBuilder(literalConstants, literalCount);
        RollBackUnifiedBuilder(stringConstants, stringCount);
        reason = "Failed to emit measured optional named property read span.";
        return false;
    }

    // Emits an optional-start named property-read operand span
    // (`box?.child.value`, `box?.child?.value`): a simple base load, one
    // JumpIfNullishReplaceUndefined per optional hop, the named reads, and any
    // plain continuation reads. Mirrors the proven
    // standalone optional-named-chain emission (TryAppendFirstBoundaryOptionalNamedPropertyReadChain).
    private static bool TryAppendSimpleOptionalNamedReadChainOperandSpan(
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
        spanLength = 0;
        reason = string.Empty;
        if (startIndex + 2 >= endExclusive ||
            !CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var firstHop = expressionProgram.GetOperation(startIndex + 1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(expressionStringConstants).IsPrivateName())
        {
            return false;
        }

        var index = startIndex + 2;
        while (index < endExclusive)
        {
            var continuation = expressionProgram.GetOperation(index);
            if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                !continuation.ShortCircuitOnNullishTarget ||
                continuation.GetString(expressionStringConstants).IsPrivateName())
            {
                break;
            }

            index++;
        }

        if (index == startIndex + 2)
        {
            return false;
        }

        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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

        List<int>? boundaryJumpIndices = null;
        for (var readIndex = startIndex + 1; readIndex < index; readIndex++)
        {
            var read = expressionProgram.GetOperation(readIndex);
            if (read.IsOptional)
            {
                boundaryJumpIndices ??= [];
                boundaryJumpIndices.Add(unified.Count);
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));
            }

            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(read.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.GetNamedProperty,
                propertyNameIndex));
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

        if (unified.Count > unifiedCount)
        {
            spanLength = index - startIndex;
            reason = string.Empty;
            return true;
        }

        RollBackUnifiedBuilder(unified, unifiedCount);
        RollBackUnifiedBuilder(literalConstants, literalCount);
        RollBackUnifiedBuilder(stringConstants, stringCount);
        reason = "Failed to emit measured optional named read chain span.";
        return false;
    }

    // Emits an optional-named-then-computed read operand span used as a call
    // argument (`box?.prop[key]`, `box?.prop?.[key]`, `box?.prop[a + b]`,
    // `box?.a.b[key]`): a simple base load, one JumpIfNullishReplaceUndefined per
    // optional named/computed hop, the named reads, the computed key span, a
    // GetComputedProperty, and trailing named reads. A nullish base/intermediate
    // receiver short-circuits the whole chain to undefined.
    private static bool TryAppendSimpleOptionalNamedThenComputedReadOperandSpan(
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
        spanLength = 0;
        reason = string.Empty;
        if (startIndex + 3 >= endExclusive ||
            !CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var firstHop = expressionProgram.GetOperation(startIndex + 1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(expressionStringConstants).IsPrivateName())
        {
            return false;
        }

        // Plain named continuations (`box?.a.b[key]`) before the computed read.
        var keyStart = startIndex + 2;
        while (keyStart < endExclusive)
        {
            var continuation = expressionProgram.GetOperation(keyStart);
            if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                continuation.IsOptional ||
                !continuation.ShortCircuitOnNullishTarget ||
                continuation.GetString(expressionStringConstants).IsPrivateName())
            {
                break;
            }

            keyStart++;
        }

        var optionalComputedJumpIndex = -1;
        if (keyStart < endExclusive)
        {
            var maybeJump = expressionProgram.GetOperation(keyStart);
            if (maybeJump.Kind == ExpressionOpKind.JumpIfNullish &&
                maybeJump.ReplaceWithUndefined)
            {
                optionalComputedJumpIndex = keyStart;
                keyStart++;
            }
        }

        // Locate the chain-short-circuit computed read after the key span.
        var computedIndex = -1;
        for (var candidate = keyStart + 1; candidate < endExclusive; candidate++)
        {
            var computedOp = expressionProgram.GetOperation(candidate);
            if (computedOp.Kind != ExpressionOpKind.GetComputedProperty ||
                computedOp.IsOptional ||
                !computedOp.ShortCircuitOnNullishTarget)
            {
                continue;
            }

            computedIndex = candidate;
            break;
        }

        if (computedIndex < 0 ||
            !IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: keyStart,
                endExclusive: computedIndex,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        if (optionalComputedJumpIndex >= 0 &&
            expressionProgram.GetOperation(optionalComputedJumpIndex).Target != computedIndex + 1)
        {
            return false;
        }

        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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

        var boundaryJumpIndices = new List<int>();

        // Optional hop read plus the plain named continuations up to the key span.
        for (var readIndex = startIndex + 1; readIndex < keyStart; readIndex++)
        {
            var read = expressionProgram.GetOperation(readIndex);
            if (read.Kind == ExpressionOpKind.JumpIfNullish)
            {
                continue;
            }

            if (read.IsOptional)
            {
                boundaryJumpIndices.Add(unified.Count);
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));
            }

            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(read.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.GetNamedProperty,
                propertyNameIndex));
        }

        if (optionalComputedJumpIndex >= 0)
        {
            boundaryJumpIndices.Add(unified.Count);
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: keyStart,
                endExclusive: computedIndex,
                out reason,
                allowsDynamicIdentifiers))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedProperty));

        // Trailing plain named continuation reads (`box?.prop[key].child`).
        var continuationIndex = computedIndex + 1;
        while (continuationIndex < endExclusive)
        {
            var continuation = expressionProgram.GetOperation(continuationIndex);
            if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                !continuation.ShortCircuitOnNullishTarget ||
                continuation.GetString(expressionStringConstants).IsPrivateName())
            {
                break;
            }

            if (continuation.IsOptional)
            {
                boundaryJumpIndices.Add(unified.Count);
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));
            }

            var continuationNameIndex = stringConstants.Count;
            stringConstants.Add(continuation.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, continuationNameIndex));
            continuationIndex++;
        }

        var chainEnd = unified.Count;
        foreach (var jumpIndex in boundaryJumpIndices)
        {
            unified[jumpIndex] = new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
                chainEnd);
        }

        spanLength = continuationIndex - startIndex;
        reason = string.Empty;
        return true;
    }

    // Emits an optional computed property-read operand span (`box?.[key]`,
    // `box?.[key]?.[key]`, `box?.[key]?.value`): a simple base load, one
    // JumpIfNullishReplaceUndefined per optional computed/named hop, computed key spans,
    // GetComputedProperty reads, and trailing named reads. A nullish base/intermediate
    // receiver short-circuits the whole chain to undefined.
    private static bool TryAppendSimpleOptionalComputedPropertyReadOperandSpan(
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
        spanLength = 0;
        reason = string.Empty;
        if (startIndex + 3 >= endExclusive ||
            !CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var hopComputedIndices = new List<int>();
        var index = startIndex + 1;
        while (index < endExclusive)
        {
            var jumpOp = expressionProgram.GetOperation(index);
            if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish ||
                !jumpOp.ReplaceWithUndefined)
            {
                break;
            }

            var keyStart = index + 1;
            var computedIndex = keyStart;
            while (computedIndex < endExclusive &&
                   expressionProgram.GetOperation(computedIndex).Kind != ExpressionOpKind.GetComputedProperty)
            {
                computedIndex++;
            }

            if (computedIndex <= keyStart ||
                computedIndex >= endExclusive ||
                jumpOp.Target != computedIndex + 1)
            {
                return false;
            }

            var computedOp = expressionProgram.GetOperation(computedIndex);
            var expectedShortCircuit = hopComputedIndices.Count > 0;
            if (computedOp.Kind != ExpressionOpKind.GetComputedProperty ||
                computedOp.IsOptional ||
                computedOp.ShortCircuitOnNullishTarget != expectedShortCircuit)
            {
                return false;
            }

            if (!IsSupportedComputedPropertyKeySpan(
                    expressionProgram,
                    activationSlots,
                    startInclusive: keyStart,
                    endExclusive: computedIndex,
                    allowsDynamicIdentifiers))
            {
                return false;
            }

            hopComputedIndices.Add(computedIndex);
            index = computedIndex + 1;
        }

        if (hopComputedIndices.Count == 0)
        {
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var continuationIndex = index;
        while (continuationIndex < endExclusive)
        {
            var continuation = expressionProgram.GetOperation(continuationIndex);
            if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                !continuation.ShortCircuitOnNullishTarget ||
                continuation.GetString(expressionStringConstants).IsPrivateName())
            {
                break;
            }

            continuationIndex++;
        }

        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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

        var boundaryJumpIndices = new List<int>();
        var keyStartIndex = startIndex + 2;
        foreach (var computedIndex in hopComputedIndices)
        {
            boundaryJumpIndices.Add(unified.Count);
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

            if (!TryAppendComputedPropertyKeySpan(
                    expressionProgram,
                    activationSlots,
                    unified,
                    literalConstants,
                    stringConstants,
                    startInclusive: keyStartIndex,
                    endExclusive: computedIndex,
                    out reason,
                    allowsDynamicIdentifiers))
            {
                RollBackUnifiedBuilder(unified, unifiedCount);
                RollBackUnifiedBuilder(literalConstants, literalCount);
                RollBackUnifiedBuilder(stringConstants, stringCount);
                return false;
            }

            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedProperty));
            keyStartIndex = computedIndex + 2;
        }

        // Emit named continuation reads (`box?.[key].value`, `box?.[key]?.value`).
        continuationIndex = hopComputedIndices[^1] + 1;
        while (continuationIndex < endExclusive)
        {
            var continuation = expressionProgram.GetOperation(continuationIndex);
            if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                !continuation.ShortCircuitOnNullishTarget ||
                continuation.GetString(expressionStringConstants).IsPrivateName())
            {
                break;
            }

            if (continuation.IsOptional)
            {
                boundaryJumpIndices.Add(unified.Count);
                unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));
            }

            var continuationNameIndex = stringConstants.Count;
            stringConstants.Add(continuation.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, continuationNameIndex));
            continuationIndex++;
        }

        var chainEnd = unified.Count;
        foreach (var jumpIndex in boundaryJumpIndices)
        {
            unified[jumpIndex] = new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
                chainEnd);
        }

        spanLength = continuationIndex - startIndex;
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendMeasuredSimpleComputedPropertyReadOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        int keyStart,
        int keyEndExclusive,
        int spanLength,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        for (var index = startIndex + 1; index < keyStart; index++)
        {
            AppendPlainNamedPropertyRead(
                expressionProgram.GetOperation(index),
                expressionStringConstants,
                unified,
                stringConstants);
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                keyStart,
                keyEndExclusive,
                out reason,
                allowsDynamicIdentifiers))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            reason = "Failed to emit measured computed property read span.";
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.RequireObjectCoercible, 1));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedProperty));

        for (var index = keyEndExclusive + 3; index < startIndex + spanLength; index++)
        {
            AppendPlainNamedPropertyRead(
                expressionProgram.GetOperation(index),
                expressionStringConstants,
                unified,
                stringConstants);
        }

        if (unified.Count > unifiedCount &&
            literalConstants.Count >= literalCount &&
            stringConstants.Count >= stringCount)
        {
            reason = string.Empty;
            return true;
        }

        RollBackUnifiedBuilder(unified, unifiedCount);
        RollBackUnifiedBuilder(literalConstants, literalCount);
        RollBackUnifiedBuilder(stringConstants, stringCount);
        reason = "Failed to emit measured computed property read span.";
        return false;
    }

    private static void AppendPlainNamedPropertyRead(
        PackedExpressionOp operation,
        ReadOnlySpan<string> expressionStringConstants,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<string>.Builder stringConstants)
    {
        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(operation.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
    }

    private static bool TryAppendSimpleUnaryOperandSpan(
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
        if (startIndex + 1 >= endExclusive)
        {
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        var operand = expressionProgram.GetOperation(startIndex);
        var unary = expressionProgram.GetOperation(startIndex + 1);
        if (!TryGetSimpleUnaryOpCode(unary.Kind, out var opCode))
        {
            spanLength = 0;
            reason = string.Empty;
            return false;
        }

        if (!CanAppendSimpleOperandLoadWithDynamic(operand, expressionProgram, activationSlots, allowsDynamicIdentifiers))
        {
            spanLength = 0;
            reason = "Simple unary spans require a simple activation-resolved or admitted dynamic operand.";
            return false;
        }

        if (!TryAppendSimpleOperandLoadWithDynamic(
                operand,
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

        unified.Add(new UnifiedBytecodeInstruction(opCode));
        spanLength = 2;
        reason = string.Empty;
        return true;
    }

    private static bool TryGetSimpleUnaryOpCode(ExpressionOpKind kind, out UnifiedBytecodeOpCode opCode)
    {
        opCode = kind switch
        {
            ExpressionOpKind.UnaryPlus => UnifiedBytecodeOpCode.UnaryPlus,
            ExpressionOpKind.UnaryMinus => UnifiedBytecodeOpCode.UnaryMinus,
            ExpressionOpKind.UnaryLogicalNot => UnifiedBytecodeOpCode.UnaryLogicalNot,
            ExpressionOpKind.UnaryBitwiseNot => UnifiedBytecodeOpCode.UnaryBitwiseNot,
            ExpressionOpKind.UnaryVoid => UnifiedBytecodeOpCode.UnaryVoid,
            _ => default
        };

        return kind is ExpressionOpKind.UnaryPlus or
            ExpressionOpKind.UnaryMinus or
            ExpressionOpKind.UnaryLogicalNot or
            ExpressionOpKind.UnaryBitwiseNot or
            ExpressionOpKind.UnaryVoid;
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

    private static bool TryMeasureSimpleDirectMemberCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int spanLength)
    {
        if (TryMeasureSimpleDirectNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out _,
                out _,
                out spanLength))
        {
            return true;
        }

        return TryMeasureSimpleDirectComputedCallOperandSpan(
            expressionProgram,
            startIndex,
            activationSlots,
            allowsDynamicIdentifiers,
            out _,
            out _,
            out spanLength);
    }

    private static bool TryMeasureSimpleMemberCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int spanLength)
    {
        if (TryMeasureSimpleDirectMemberCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out spanLength))
        {
            return true;
        }

        if (TryMeasureSimpleOptionalNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out _,
                out _,
                out spanLength))
        {
            return true;
        }

        return TryMeasureSimpleOptionalComputedCallOperandSpan(
            expressionProgram,
            startIndex,
            activationSlots,
            allowsDynamicIdentifiers,
            out _,
            out _,
            out _,
            out spanLength);
    }

    private static bool TryAppendSimpleDirectMemberCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        out string reason)
    {
        if (TryMeasureSimpleDirectNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out _,
                out _,
                out _))
        {
            return TryAppendSimpleDirectNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                slotLayout,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                out reason);
        }

        return TryAppendSimpleDirectComputedCallOperandSpan(
            expressionProgram,
            startIndex,
            activationSlots,
            slotLayout,
            allowsDynamicIdentifiers,
            unified,
            literalConstants,
            stringConstants,
            callTargetConstants,
            out reason);
    }

    private static bool TryAppendSimpleMemberCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        out string reason)
    {
        if (TryMeasureSimpleDirectMemberCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out _))
        {
            return TryAppendSimpleDirectMemberCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                slotLayout,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                out reason);
        }

        if (TryMeasureSimpleOptionalNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out _,
                out _,
                out _))
        {
            return TryAppendSimpleOptionalNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                out reason);
        }

        return TryAppendSimpleOptionalComputedCallOperandSpan(
            expressionProgram,
            startIndex,
            activationSlots,
            allowsDynamicIdentifiers,
            unified,
            literalConstants,
            stringConstants,
            callTargetConstants,
            out reason);
    }

    private static bool TryMeasureSimpleOptionalNamedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int callIndex,
        out int argumentCount,
        out int spanLength)
    {
        if (TryMeasureSimpleReceiverOptionalNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out callIndex,
                out argumentCount,
                out spanLength))
        {
            return true;
        }

        return TryMeasureSimpleCalleeOptionalNamedCallOperandSpan(
            expressionProgram,
            startIndex,
            activationSlots,
            allowsDynamicIdentifiers,
            out callIndex,
            out argumentCount,
            out spanLength);
    }

    private static bool TryMeasureSimpleReceiverOptionalNamedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int callIndex,
        out int argumentCount,
        out int spanLength)
    {
        callIndex = -1;
        argumentCount = 0;
        spanLength = 0;
        if (startIndex + 4 >= expressionProgram.OperationCount)
        {
            return false;
        }

        if (!CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var jump = expressionProgram.GetOperation(startIndex + 1);
        if (jump.Kind != ExpressionOpKind.JumpIfNullish || !jump.ReplaceWithUndefined)
        {
            return false;
        }

        var callTarget = expressionProgram.GetOperation(startIndex + 2);
        if (callTarget.Kind != ExpressionOpKind.LoadNamedCallTarget ||
            callTarget.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
        {
            return false;
        }

        var operationIndex = startIndex + 3;
        while (operationIndex < expressionProgram.OperationCount &&
               CanAppendSimpleOperandLoadWithDynamic(
                   expressionProgram.GetOperation(operationIndex),
                   expressionProgram,
                   activationSlots,
                   allowsDynamicIdentifiers))
        {
            argumentCount++;
            operationIndex++;
        }

        if (operationIndex >= expressionProgram.OperationCount)
        {
            argumentCount = 0;
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

    private static bool TryMeasureSimpleCalleeOptionalNamedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int callIndex,
        out int argumentCount,
        out int spanLength)
    {
        callIndex = -1;
        argumentCount = 0;
        spanLength = 0;
        if (startIndex + 6 >= expressionProgram.OperationCount)
        {
            return false;
        }

        if (!CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var callTarget = expressionProgram.GetOperation(startIndex + 1);
        if (callTarget.Kind != ExpressionOpKind.LoadNamedCallTarget ||
            callTarget.GetString(expressionProgram.StringConstants.AsSpan()).IsPrivateName())
        {
            return false;
        }

        var jump = expressionProgram.GetOperation(startIndex + 2);
        if (jump.Kind != ExpressionOpKind.JumpIfNullish || !jump.ReplaceWithUndefined)
        {
            return false;
        }

        var operationIndex = startIndex + 3;
        while (operationIndex < expressionProgram.OperationCount &&
               CanAppendSimpleOperandLoadWithDynamic(
                   expressionProgram.GetOperation(operationIndex),
                   expressionProgram,
                   activationSlots,
                   allowsDynamicIdentifiers))
        {
            argumentCount++;
            operationIndex++;
        }

        if (operationIndex + 3 >= expressionProgram.OperationCount)
        {
            argumentCount = 0;
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

        if (expressionProgram.GetOperation(operationIndex + 1).Kind != ExpressionOpKind.Jump ||
            expressionProgram.GetOperation(operationIndex + 2).Kind != ExpressionOpKind.SwapTopTwo ||
            expressionProgram.GetOperation(operationIndex + 3).Kind != ExpressionOpKind.Pop)
        {
            argumentCount = 0;
            return false;
        }

        callIndex = operationIndex;
        spanLength = operationIndex - startIndex + 4;
        return true;
    }

    private static bool TryAppendSimpleOptionalNamedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        out string reason)
    {
        if (TryMeasureSimpleReceiverOptionalNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out var receiverOptionalCallIndex,
                out var receiverOptionalArgumentCount,
                out _))
        {
            return TryAppendMeasuredSimpleOptionalNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                callTargetIndex: startIndex + 2,
                argsStartIndex: startIndex + 3,
                callIndex: receiverOptionalCallIndex,
                argumentCount: receiverOptionalArgumentCount,
                isOptionalReceiverCheck: true,
                out reason);
        }

        if (TryMeasureSimpleCalleeOptionalNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out var calleeOptionalCallIndex,
                out var calleeOptionalArgumentCount,
                out _))
        {
            return TryAppendMeasuredSimpleOptionalNamedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                callTargetIndex: startIndex + 1,
                argsStartIndex: startIndex + 3,
                callIndex: calleeOptionalCallIndex,
                argumentCount: calleeOptionalArgumentCount,
                isOptionalReceiverCheck: false,
                out reason);
        }

        reason = "Literal spans only admit optional named member calls with simple receiver and arguments.";
        return false;
    }

    private static bool TryAppendMeasuredSimpleOptionalNamedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        int callTargetIndex,
        int argsStartIndex,
        int callIndex,
        int argumentCount,
        bool isOptionalReceiverCheck,
        out string reason)
    {
        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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

        var callTarget = expressionProgram.GetOperation(callTargetIndex);
        var callTargetNameIndex = stringConstants.Count;
        stringConstants.Add(callTarget.GetString(expressionProgram.StringConstants.AsSpan()));
        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(
            UnifiedBytecodeCallTargetKind.NamedMember,
            NameConstantIndex: callTargetNameIndex,
            IsOptionalReceiverCheck: isOptionalReceiverCheck));

        var prepareIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget,
            callTargetConstantIndex));

        for (var operationIndex = argsStartIndex; operationIndex < callIndex; operationIndex++)
        {
            if (!TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(operationIndex),
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

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.CallInvocationBoundary,
            EncodeCallBoundaryOperand(argumentCount, spreadMaskIndex: -1, isDirectEval: false)));
        unified[prepareIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget,
            callTargetConstantIndex | (unified.Count << 16));
        reason = string.Empty;
        return true;
    }

    private static bool TryMeasureSimpleOptionalComputedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int callTargetIndex,
        out int callIndex,
        out int argumentCount,
        out int spanLength)
    {
        if (TryMeasureSimpleReceiverOptionalComputedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out callTargetIndex,
                out callIndex,
                out argumentCount,
                out spanLength))
        {
            return true;
        }

        return TryMeasureSimpleCalleeOptionalComputedCallOperandSpan(
            expressionProgram,
            startIndex,
            activationSlots,
            allowsDynamicIdentifiers,
            out callTargetIndex,
            out callIndex,
            out argumentCount,
            out spanLength);
    }

    private static bool TryMeasureSimpleReceiverOptionalComputedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int callTargetIndex,
        out int callIndex,
        out int argumentCount,
        out int spanLength)
    {
        callTargetIndex = -1;
        callIndex = -1;
        argumentCount = 0;
        spanLength = 0;
        if (startIndex + 5 >= expressionProgram.OperationCount)
        {
            return false;
        }

        if (!CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var jump = expressionProgram.GetOperation(startIndex + 1);
        if (jump.Kind != ExpressionOpKind.JumpIfNullish || !jump.ReplaceWithUndefined)
        {
            return false;
        }

        callTargetIndex = startIndex + 3;
        while (callTargetIndex < expressionProgram.OperationCount &&
               expressionProgram.GetOperation(callTargetIndex).Kind != ExpressionOpKind.LoadComputedCallTarget)
        {
            callTargetIndex++;
        }

        if (callTargetIndex >= expressionProgram.OperationCount)
        {
            callTargetIndex = -1;
            return false;
        }

        if (!IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: startIndex + 2,
                endExclusive: callTargetIndex,
                allowsDynamicIdentifiers))
        {
            callTargetIndex = -1;
            return false;
        }

        var callTarget = expressionProgram.GetOperation(callTargetIndex);
        if (callTarget.IsOptional || callTarget.ShortCircuitOnNullishTarget)
        {
            callTargetIndex = -1;
            return false;
        }

        var operationIndex = callTargetIndex + 1;
        while (operationIndex < expressionProgram.OperationCount &&
               CanAppendSimpleOperandLoadWithDynamic(
                   expressionProgram.GetOperation(operationIndex),
                   expressionProgram,
                   activationSlots,
                   allowsDynamicIdentifiers))
        {
            argumentCount++;
            operationIndex++;
        }

        if (operationIndex >= expressionProgram.OperationCount)
        {
            callTargetIndex = -1;
            argumentCount = 0;
            return false;
        }

        var call = expressionProgram.GetOperation(operationIndex);
        if (call.Kind != ExpressionOpKind.Call ||
            !call.HasExplicitThis ||
            call.IsDirectEval ||
            call.SpreadMaskConstantIndex >= 0 ||
            call.ArgumentCount != argumentCount)
        {
            callTargetIndex = -1;
            argumentCount = 0;
            return false;
        }

        callIndex = operationIndex;
        spanLength = operationIndex - startIndex + 1;
        return true;
    }

    private static bool TryMeasureSimpleCalleeOptionalComputedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int callTargetIndex,
        out int callIndex,
        out int argumentCount,
        out int spanLength)
    {
        callTargetIndex = -1;
        callIndex = -1;
        argumentCount = 0;
        spanLength = 0;
        if (startIndex + 7 >= expressionProgram.OperationCount)
        {
            return false;
        }

        if (!CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        callTargetIndex = startIndex + 2;
        while (callTargetIndex < expressionProgram.OperationCount &&
               expressionProgram.GetOperation(callTargetIndex).Kind != ExpressionOpKind.LoadComputedCallTarget)
        {
            callTargetIndex++;
        }

        if (callTargetIndex >= expressionProgram.OperationCount)
        {
            callTargetIndex = -1;
            return false;
        }

        if (!IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: startIndex + 1,
                endExclusive: callTargetIndex,
                allowsDynamicIdentifiers))
        {
            callTargetIndex = -1;
            return false;
        }

        var jump = expressionProgram.GetOperation(callTargetIndex + 1);
        if (jump.Kind != ExpressionOpKind.JumpIfNullish || !jump.ReplaceWithUndefined)
        {
            callTargetIndex = -1;
            return false;
        }

        var operationIndex = callTargetIndex + 2;
        while (operationIndex < expressionProgram.OperationCount &&
               CanAppendSimpleOperandLoadWithDynamic(
                   expressionProgram.GetOperation(operationIndex),
                   expressionProgram,
                   activationSlots,
                   allowsDynamicIdentifiers))
        {
            argumentCount++;
            operationIndex++;
        }

        if (operationIndex + 3 >= expressionProgram.OperationCount)
        {
            callTargetIndex = -1;
            argumentCount = 0;
            return false;
        }

        var call = expressionProgram.GetOperation(operationIndex);
        if (call.Kind != ExpressionOpKind.Call ||
            !call.HasExplicitThis ||
            call.IsDirectEval ||
            call.SpreadMaskConstantIndex >= 0 ||
            call.ArgumentCount != argumentCount)
        {
            callTargetIndex = -1;
            argumentCount = 0;
            return false;
        }

        if (expressionProgram.GetOperation(operationIndex + 1).Kind != ExpressionOpKind.Jump ||
            expressionProgram.GetOperation(operationIndex + 2).Kind != ExpressionOpKind.SwapTopTwo ||
            expressionProgram.GetOperation(operationIndex + 3).Kind != ExpressionOpKind.Pop)
        {
            callTargetIndex = -1;
            argumentCount = 0;
            return false;
        }

        callIndex = operationIndex;
        spanLength = operationIndex - startIndex + 4;
        return true;
    }

    private static bool TryAppendSimpleOptionalComputedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        out string reason)
    {
        if (TryMeasureSimpleReceiverOptionalComputedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out var receiverOptionalCallTargetIndex,
                out var receiverOptionalCallIndex,
                out var receiverOptionalArgumentCount,
                out _))
        {
            return TryAppendMeasuredSimpleReceiverOptionalComputedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                receiverOptionalCallTargetIndex,
                receiverOptionalCallIndex,
                receiverOptionalArgumentCount,
                out reason);
        }

        if (TryMeasureSimpleCalleeOptionalComputedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out var calleeOptionalCallTargetIndex,
                out var calleeOptionalCallIndex,
                out var calleeOptionalArgumentCount,
                out _))
        {
            return TryAppendMeasuredSimpleCalleeOptionalComputedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants,
                calleeOptionalCallTargetIndex,
                calleeOptionalCallIndex,
                calleeOptionalArgumentCount,
                out reason);
        }

        reason = "Literal spans only admit optional computed member calls with simple receiver, key, and arguments.";
        return false;
    }

    private static bool TryAppendMeasuredSimpleReceiverOptionalComputedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        int callTargetIndex,
        int callIndex,
        int argumentCount,
        out string reason)
    {
        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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

        var nullishJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));
        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: startIndex + 2,
                endExclusive: callTargetIndex,
                out reason,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(UnifiedBytecodeCallTargetKind.ComputedMember));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareComputedCallTarget,
            callTargetConstantIndex));

        for (var operationIndex = callTargetIndex + 1; operationIndex < callIndex; operationIndex++)
        {
            if (!TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(operationIndex),
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

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.CallInvocationBoundary,
            EncodeCallBoundaryOperand(argumentCount, spreadMaskIndex: -1, isDirectEval: false)));
        unified[nullishJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
            unified.Count);
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendMeasuredSimpleCalleeOptionalComputedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        int callTargetIndex,
        int callIndex,
        int argumentCount,
        out string reason)
    {
        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: startIndex + 1,
                endExclusive: callTargetIndex,
                out reason,
                allowsDynamicIdentifiers))
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

        for (var operationIndex = callTargetIndex + 2; operationIndex < callIndex; operationIndex++)
        {
            if (!TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(operationIndex),
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

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.CallInvocationBoundary,
            EncodeCallBoundaryOperand(argumentCount, spreadMaskIndex: -1, isDirectEval: false)));
        unified[prepareIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget,
            callTargetConstantIndex | (unified.Count << 16));
        reason = string.Empty;
        return true;
    }

    private static bool TryMeasureSimpleDirectNamedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
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

        if (!CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
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
               CanAppendSimpleOperandLoadWithDynamic(
                   expressionProgram.GetOperation(operationIndex),
                   expressionProgram,
                   activationSlots,
                   allowsDynamicIdentifiers))
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

    private static bool TryMeasureSimpleDirectComputedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out int callIndex,
        out int argumentCount,
        out int spanLength)
    {
        callIndex = -1;
        argumentCount = 0;
        spanLength = 0;
        if (startIndex + 3 >= expressionProgram.OperationCount)
        {
            return false;
        }

        if (!CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var callTargetIndex = startIndex + 2;
        while (callTargetIndex < expressionProgram.OperationCount &&
               expressionProgram.GetOperation(callTargetIndex).Kind != ExpressionOpKind.LoadComputedCallTarget)
        {
            callTargetIndex++;
        }

        if (callTargetIndex >= expressionProgram.OperationCount)
        {
            return false;
        }

        var callTarget = expressionProgram.GetOperation(callTargetIndex);
        if (callTarget.IsOptional || callTarget.ShortCircuitOnNullishTarget)
        {
            return false;
        }

        if (!IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: startIndex + 1,
                endExclusive: callTargetIndex,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var operationIndex = callTargetIndex + 1;
        while (operationIndex < expressionProgram.OperationCount &&
               CanAppendSimpleOperandLoadWithDynamic(
                   expressionProgram.GetOperation(operationIndex),
                   expressionProgram,
                   activationSlots,
                   allowsDynamicIdentifiers))
        {
            argumentCount++;
            operationIndex++;
        }

        if (operationIndex >= expressionProgram.OperationCount)
        {
            argumentCount = 0;
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

    private static bool TryAppendSimpleDirectComputedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        out string reason)
    {
        if (!TryMeasureSimpleDirectComputedCallOperandSpan(
                expressionProgram,
                startIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out var callIndex,
                out var argumentCount,
                out _))
        {
            reason = "Literal spans only admit direct computed member calls with simple receiver, key, and arguments.";
            return false;
        }

        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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

        var callTargetIndex = FindFirstOperation(
            expressionProgram,
            ExpressionOpKind.LoadComputedCallTarget,
            startIndex + 2);
        if (callTargetIndex < 0 ||
            !TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: startIndex + 1,
                endExclusive: callTargetIndex,
                out reason,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var callTargetConstantIndex = callTargetConstants.Count;
        callTargetConstants.Add(new UnifiedBytecodeCallTarget(UnifiedBytecodeCallTargetKind.ComputedMember));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.PrepareComputedCallTarget,
            callTargetConstantIndex));

        for (var operationIndex = callTargetIndex + 1; operationIndex < callIndex; operationIndex++)
        {
            if (!TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(operationIndex),
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

        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.CallInvocationBoundary,
            EncodeCallBoundaryOperand(argumentCount, spreadMaskIndex: -1, isDirectEval: false)));
        reason = string.Empty;
        return true;
    }

    private static bool TryAppendSimpleDirectNamedCallOperandSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        UnifiedBytecodeSlotLayout slotLayout,
        bool allowsDynamicIdentifiers,
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
                allowsDynamicIdentifiers,
                out var callIndex,
                out var argumentCount,
                out _))
        {
            reason = "Spread sources only admit direct named member calls with simple arguments.";
            return false;
        }

        if (!TryAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(startIndex),
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
            if (!TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(operationIndex),
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
        return FindFirstOperation(expressionProgram, kind, startIndex: 0);
    }

    private static int FindFirstOperation(ExpressionProgram expressionProgram, ExpressionOpKind kind, int startIndex)
    {
        for (var operationIndex = startIndex; operationIndex < expressionProgram.OperationCount; operationIndex++)
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
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
        // Shape: [base, GetNamedProperty(non-optional, non-private)*, DuplicateTop, GetNamedProperty, rhs..., Binary, SetNamedProperty]
        // The final target may be private; receiver-chain hops stay ordinary only.
        // Minimum: 6 ops (rhs is a single simple operand).
        if (expressionProgram.OperationCount < 6)
        {
            reason = string.Empty;
            return false;
        }

        var binary = expressionProgram.GetOperation(expressionProgram.OperationCount - 2);
        var propertySet = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);
        if (binary.Kind != ExpressionOpKind.Binary ||
            propertySet.Kind != ExpressionOpKind.SetNamedProperty)
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

        // Capture builder lengths before any emission so a later validation failure
        // rolls back and never leaks half-written operand loads the general loop would
        // re-emit (doubling them past MaxStackDepth -> VM stack overflow).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        var callTargetCount = callTargetConstants.Count;

        void RollBack()
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            RollBackUnifiedBuilder(callTargetConstants, callTargetCount);
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
            RollBack();
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
        var binaryIndex = expressionProgram.OperationCount - 2;

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
                RollBack();
                return false;
            }
        }
        else if (expressionProgram.GetOperation(rhsStart).Kind == ExpressionOpKind.LoadLiteral &&
                 TryMeasureSimpleTemplateLiteralSpan(
                     expressionProgram,
                     rhsStart,
                     activationSlots,
                     out var templateSpanProbe,
                     allowsDynamicIdentifiers) &&
                 templateSpanProbe > 1 &&
                 rhsStart + templateSpanProbe - 1 == rhsEnd)
        {
            // Simple template literal RHS span fast path.
            if (!TryAppendSimpleTemplateLiteralSpan(
                    expressionProgram, rhsStart, activationSlots,
                    unified, literalConstants, out var spanLen, out reason))
            {
                RollBack();
                return false;
            }

            if (rhsStart + spanLen - 1 != rhsEnd)
            {
                reason = "Template literal RHS span does not match expected boundary.";
                RollBack();
                return false;
            }
        }
        else
        {
            // Complex RHS region (mirrors the plain-write complex-RHS lowering): the old value is
            // already on the stack (GetNamedPropertyForCompoundSet above). Lower [rhsStart, Binary)
            // with the general operand-stack appender — it emits each op in source (evaluation)
            // order, leaving exactly one value (the RHS) on top of the old value, preserving the
            // read-old / evaluate-RHS / apply-op / store sequence exactly.
            if (!TryAppendAdmittedComplexCallArgumentRegion(
                    expressionProgram,
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    rhsStart,
                    binaryIndex,
                    expectedArgumentCount: 1,
                    allowsDynamicIdentifiers,
                    out reason))
            {
                RollBack();
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
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
        if (expressionProgram.OperationCount < 9)
        {
            reason = string.Empty;
            return false;
        }

        // Layout: [base, recv*, key..., RequireObjectCoercible, ResolvePropertyKey,
        // DuplicateTopTwo, GetComputedProperty (old-value read), RHS..., Binary,
        // SetComputedProperty]. Binary and SetComputedProperty are the last two ops; the 4-op read
        // prefix sits at [readStart, readStart+4). For a single-op RHS readStart = OperationCount-7
        // (the old shape); a complex multi-op RHS pushes the read prefix earlier and the RHS region
        // [readStart+4, Binary) is variable-length. We locate readStart, never reordering the op
        // stream — evaluation order (object, key, read old value, RHS, apply op, store) is fixed.
        var binaryIndex = expressionProgram.OperationCount - 2;
        var binary = expressionProgram.GetOperation(binaryIndex);
        var propertySet = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);
        if (binary.Kind != ExpressionOpKind.Binary ||
            propertySet.Kind != ExpressionOpKind.SetComputedProperty)
        {
            reason = string.Empty;
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

        var stringTable = expressionProgram.StringConstants.AsSpan();

        var readStart = -1;
        var keyStart = -1;
        var computedPrefixKeyStart = -1;
        var computedPrefixKeyEndExclusive = -1;
        var computedPrefixSpanLength = 0;
        for (var candidateReadStart = expressionProgram.OperationCount - 7;
             candidateReadStart >= 1;
             candidateReadStart--)
        {
            if (candidateReadStart + 4 > binaryIndex)
            {
                continue;
            }

            var requireObjectCoercible = expressionProgram.GetOperation(candidateReadStart);
            var resolvePropertyKey = expressionProgram.GetOperation(candidateReadStart + 1);
            var duplicateTargetAndKey = expressionProgram.GetOperation(candidateReadStart + 2);
            var propertyRead = expressionProgram.GetOperation(candidateReadStart + 3);
            if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible ||
                requireObjectCoercible.Depth != 1 ||
                resolvePropertyKey.Kind != ExpressionOpKind.ResolvePropertyKey ||
                duplicateTargetAndKey.Kind != ExpressionOpKind.DuplicateTopTwo ||
                propertyRead.Kind != ExpressionOpKind.GetComputedProperty)
            {
                continue;
            }

            if (propertyRead.ShortCircuitOnNullishTarget)
            {
                reason = "Optional computed compound property writes are not supported.";
                return false;
            }

            // Walk an optional named receiver-prefix chain (e.g. box.child[key] += value).
            var candidateKeyStart = 1;
            var receiverChainOk = true;
            var receiverChainPrivate = false;
            while (candidateKeyStart < candidateReadStart)
            {
                var receiverRead = expressionProgram.GetOperation(candidateKeyStart);
                if (receiverRead.Kind != ExpressionOpKind.GetNamedProperty)
                {
                    break;
                }

                if (receiverRead.GetString(stringTable).IsPrivateName())
                {
                    receiverChainPrivate = true;
                    break;
                }

                if (receiverRead.IsOptional || receiverRead.ShortCircuitOnNullishTarget)
                {
                    receiverChainOk = false;
                    break;
                }

                candidateKeyStart++;
            }

            if (receiverChainPrivate)
            {
                reason = "Private nested named property receiver reads are not supported.";
                return false;
            }

            if (!receiverChainOk ||
                candidateKeyStart >= candidateReadStart)
            {
                continue;
            }

            var candidateComputedPrefixKeyStart = -1;
            var candidateComputedPrefixKeyEndExclusive = -1;
            var candidateComputedPrefixSpanLength = 0;
            if (candidateKeyStart == 1 &&
                TryMeasureSimpleComputedPropertyReadOperandSpan(
                    expressionProgram,
                    0,
                    candidateReadStart,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    out candidateComputedPrefixKeyStart,
                    out candidateComputedPrefixKeyEndExclusive,
                    out candidateComputedPrefixSpanLength) &&
                candidateComputedPrefixSpanLength > 1 &&
                candidateComputedPrefixSpanLength < candidateReadStart)
            {
                candidateKeyStart = candidateComputedPrefixSpanLength;
            }

            if (candidateKeyStart >= candidateReadStart ||
                !IsSupportedComputedPropertyKeySpan(
                    expressionProgram,
                    activationSlots,
                    startInclusive: candidateKeyStart,
                    endExclusive: candidateReadStart,
                    allowsDynamicIdentifiers))
            {
                continue;
            }

            readStart = candidateReadStart;
            keyStart = candidateKeyStart;
            computedPrefixKeyStart = candidateComputedPrefixKeyStart;
            computedPrefixKeyEndExclusive = candidateComputedPrefixKeyEndExclusive;
            computedPrefixSpanLength = candidateComputedPrefixSpanLength;
            break;
        }

        if (readStart < 0)
        {
            reason = string.Empty;
            return false;
        }

        var rhsStart = readStart + 4;
        var rhsIsSimpleSingleOperand = rhsStart == binaryIndex - 1;

        var stagedUnified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        stagedUnified.AddRange(unified);

        var stagedLiterals = ImmutableArray.CreateBuilder<JsValue>();
        stagedLiterals.AddRange(literalConstants);

        var stagedStrings = ImmutableArray.CreateBuilder<string>();
        stagedStrings.AddRange(stringConstants);

        var stagedCallTargets = ImmutableArray.CreateBuilder<UnifiedBytecodeCallTarget>();
        stagedCallTargets.AddRange(callTargetConstants);

        if (computedPrefixSpanLength > 0)
        {
            if (!TryAppendMeasuredSimpleComputedPropertyReadOperandSpan(
                    expressionProgram,
                    0,
                    computedPrefixKeyStart,
                    computedPrefixKeyEndExclusive,
                    computedPrefixSpanLength,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    stagedUnified,
                    stagedLiterals,
                    stagedStrings,
                    out reason))
            {
                return false;
            }
        }
        else
        {
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

            for (var operationIndex = 1; operationIndex < keyStart; operationIndex++)
            {
                var receiverRead = expressionProgram.GetOperation(operationIndex);
                var receiverNameIndex = stagedStrings.Count;
                stagedStrings.Add(receiverRead.GetString(stringTable));
                stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));
            }
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                startInclusive: keyStart,
                endExclusive: readStart,
                out reason,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.RequireObjectCoercible, 1));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.ResolvePropertyKey));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet));

        if (rhsIsSimpleSingleOperand)
        {
            if (!TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(rhsStart),
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
        }
        else
        {
            // Complex RHS region: the old value is already on the stack
            // (GetComputedPropertyForCompoundSet). Lower [rhsStart, Binary) with the general
            // operand-stack appender, leaving exactly one value (the RHS) on top of the old value,
            // preserving the read-old / evaluate-RHS / apply-op / store sequence exactly.
            if (!TryAppendAdmittedComplexCallArgumentRegion(
                    expressionProgram,
                    slotLayout,
                    stagedUnified,
                    stagedLiterals,
                    stagedStrings,
                    stagedCallTargets,
                    rhsStart,
                    binaryIndex,
                    expectedArgumentCount: 1,
                    allowsDynamicIdentifiers,
                    out reason))
            {
                return false;
            }
        }

        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)binary.Operator));
        stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.SetComputedProperty));
        unified.Clear();
        unified.AddRange(stagedUnified);
        literalConstants.Clear();
        literalConstants.AddRange(stagedLiterals);
        stringConstants.Clear();
        stringConstants.AddRange(stagedStrings);
        callTargetConstants.Clear();
        callTargetConstants.AddRange(stagedCallTargets);
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

        // Walk an optional named receiver-prefix chain (e.g. box.child[key] &&= value).
        var stringTable = expressionProgram.StringConstants.AsSpan();
        var keyStart = 1;
        while (keyStart < propertySetIndex)
        {
            var receiverRead = expressionProgram.GetOperation(keyStart);
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

            keyStart++;
        }

        var suffixStart = -1;
        var matchedLayout = false;
        var computedPrefixKeyStart = -1;
        var computedPrefixKeyEndExclusive = -1;
        var computedPrefixSpanLength = 0;
        PackedExpressionOp propertyRead = default;
        PackedExpressionOp jump = default;
        PackedExpressionOp propertySet = default;
        for (var rhsLength = 1; rhsLength <= 3; rhsLength += 2)
        {
            var candidateSuffixStart = propertySetIndex - 6 - rhsLength;
            var candidateKeyStart = keyStart;
            var candidateComputedPrefixKeyStart = -1;
            var candidateComputedPrefixKeyEndExclusive = -1;
            var candidateComputedPrefixSpanLength = 0;
            if (candidateKeyStart == 1 &&
                TryMeasureSimpleComputedPropertyReadOperandSpan(
                    expressionProgram,
                    0,
                    candidateSuffixStart,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    out candidateComputedPrefixKeyStart,
                    out candidateComputedPrefixKeyEndExclusive,
                    out candidateComputedPrefixSpanLength) &&
                candidateComputedPrefixSpanLength > 1 &&
                candidateComputedPrefixSpanLength < candidateSuffixStart)
            {
                candidateKeyStart = candidateComputedPrefixSpanLength;
            }

            if (candidateSuffixStart <= candidateKeyStart ||
                !IsSupportedComputedPropertyKeySpan(
                    expressionProgram,
                    activationSlots,
                    startInclusive: candidateKeyStart,
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
            keyStart = candidateKeyStart;
            computedPrefixKeyStart = candidateComputedPrefixKeyStart;
            computedPrefixKeyEndExclusive = candidateComputedPrefixKeyEndExclusive;
            computedPrefixSpanLength = candidateComputedPrefixSpanLength;
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

        if (computedPrefixSpanLength > 0)
        {
            if (!TryAppendMeasuredSimpleComputedPropertyReadOperandSpan(
                    expressionProgram,
                    0,
                    computedPrefixKeyStart,
                    computedPrefixKeyEndExclusive,
                    computedPrefixSpanLength,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    stagedUnified,
                    stagedLiterals,
                    stagedStrings,
                    out reason))
            {
                return false;
            }
        }
        else
        {
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

            for (var operationIndex = 1; operationIndex < keyStart; operationIndex++)
            {
                var receiverRead = expressionProgram.GetOperation(operationIndex);
                var receiverNameIndex = stagedStrings.Count;
                stagedStrings.Add(receiverRead.GetString(stringTable));
                stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));
            }
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                startInclusive: keyStart,
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

    // Computed receiver-prefix followed by a computed property write (`box[k1].child[k2] = value`).
    // The prefix is emitted once through the shared computed-read span emitter, then the terminal
    // key and RHS are lowered in source order before the final SetComputedProperty.
    private static bool TryAppendFirstBoundaryComputedPrefixComputedPropertySet(
        ExpressionProgram expressionProgram,
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        reason = string.Empty;
        if (expressionProgram.OperationCount < 8)
        {
            return false;
        }

        var setComputedIndex = expressionProgram.OperationCount - 1;
        var propertySet = expressionProgram.GetOperation(setComputedIndex);
        if (propertySet.Kind != ExpressionOpKind.SetComputedProperty)
        {
            return false;
        }

        if (propertySet.AllowNameInference)
        {
            reason = "Computed-prefix computed property writes with name inference are not supported.";
            return false;
        }

        var activationSlots = slotLayout.ActivationSlots;
        if (!TryMeasureSimpleComputedPropertyReadOperandSpan(
                expressionProgram,
                0,
                setComputedIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out var receiverKeyStart,
                out var receiverKeyEndExclusive,
                out var receiverSpanLength))
        {
            return false;
        }

        if (receiverSpanLength <= 0 || receiverSpanLength >= setComputedIndex)
        {
            return false;
        }

        var valueStart = -1;
        var valueIsSimpleSingleOperand = false;
        var simpleValueIndex = setComputedIndex - 1;
        if (receiverSpanLength < simpleValueIndex &&
            IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: receiverSpanLength,
                endExclusive: simpleValueIndex,
                allowsDynamicIdentifiers) &&
            CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(simpleValueIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            valueStart = simpleValueIndex;
            valueIsSimpleSingleOperand = true;
        }
        else
        {
            for (var candidateValueStart = receiverSpanLength + 1;
                 candidateValueStart < setComputedIndex;
                 candidateValueStart++)
            {
                if (IsSupportedComputedPropertyKeySpan(
                        expressionProgram,
                        activationSlots,
                        startInclusive: receiverSpanLength,
                        endExclusive: candidateValueStart,
                        allowsDynamicIdentifiers) &&
                    UnifiedBytecodeProductionEligibility.TryValidateAdmittedComplexCallArgumentRegion(
                        expressionProgram,
                        argsStartIndex: candidateValueStart,
                        callIndex: setComputedIndex,
                        expectedArgumentCount: 1,
                        expressionProgram.IdentifierConstants.AsSpan(),
                        activationSlots,
                        allowsDynamicIdentifiers))
                {
                    valueStart = candidateValueStart;
                    break;
                }
            }
        }

        if (valueStart < 0)
        {
            return false;
        }

        var stagedUnified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        stagedUnified.AddRange(unified);

        var stagedLiterals = ImmutableArray.CreateBuilder<JsValue>();
        stagedLiterals.AddRange(literalConstants);

        var stagedStrings = ImmutableArray.CreateBuilder<string>();
        stagedStrings.AddRange(stringConstants);

        var stagedCallTargets = ImmutableArray.CreateBuilder<UnifiedBytecodeCallTarget>();
        stagedCallTargets.AddRange(callTargetConstants);

        if (!TryAppendMeasuredSimpleComputedPropertyReadOperandSpan(
                expressionProgram,
                0,
                receiverKeyStart,
                receiverKeyEndExclusive,
                receiverSpanLength,
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
                startInclusive: receiverSpanLength,
                endExclusive: valueStart,
                out reason,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        if (valueIsSimpleSingleOperand)
        {
            if (!TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(valueStart),
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
        }
        else if (!TryAppendAdmittedComplexCallArgumentRegion(
                     expressionProgram,
                     slotLayout,
                     stagedUnified,
                     stagedLiterals,
                     stagedStrings,
                     stagedCallTargets,
                     valueStart,
                     setComputedIndex,
                     expectedArgumentCount: 1,
                     allowsDynamicIdentifiers,
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
        callTargetConstants.Clear();
        callTargetConstants.AddRange(stagedCallTargets);
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
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
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

        // This handler owns the `base.name = rhs` shape: base at op 0, the RHS spanning the
        // rest. A receiver chain between the base and the write — e.g. a computed read prefix
        // `box[key].child = value` — is NOT this shape. We detect that by validating the RHS
        // region (everything after the base) up front via the eligibility walker BEFORE
        // emitting the base load, so a non-matching shape declines without leaving a stray
        // operand load for the general loop to double (overflowing MaxStackDepth).
        var rhsStart = 1;
        var rhsEnd = expressionProgram.OperationCount - 2;
        var setNamedIndex = expressionProgram.OperationCount - 1;
        var rhsIsSingleSimpleOperand = rhsStart == rhsEnd;

        // RHS region classification (in priority order):
        //   - single simple operand,
        //   - simple template literal span,
        //   - any already-admitted complex value region (binary, nested call, member/optional
        //     read span, composition thereof) — mirrors A11 call-arg admission.
        var rhsIsTemplateLiteral = false;
        var rhsIsComplexRegion = false;
        if (!rhsIsSingleSimpleOperand)
        {
            if (expressionProgram.GetOperation(rhsStart).Kind == ExpressionOpKind.LoadLiteral &&
                TryMeasureSimpleTemplateLiteralSpan(
                    expressionProgram,
                    rhsStart,
                    activationSlots,
                    out var templateSpanProbe,
                    allowsDynamicIdentifiers) &&
                templateSpanProbe > 1 &&
                rhsStart + templateSpanProbe - 1 == rhsEnd)
            {
                rhsIsTemplateLiteral = true;
            }
            else if (UnifiedBytecodeProductionEligibility.TryValidateAdmittedComplexCallArgumentRegion(
                         expressionProgram,
                         rhsStart,
                         setNamedIndex,
                         expectedArgumentCount: 1,
                         expressionProgram.IdentifierConstants.AsSpan(),
                         activationSlots,
                         allowsDynamicIdentifiers))
            {
                rhsIsComplexRegion = true;
            }
            else
            {
                reason = string.Empty;
                return false;
            }
        }

        // Capture builder lengths before emission so a later failure rolls back
        // instead of leaking half-written operand loads the general loop would
        // re-emit (doubling them past MaxStackDepth -> VM stack overflow).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;
        var callTargetCount = callTargetConstants.Count;

        void RollBack()
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            RollBackUnifiedBuilder(callTargetConstants, callTargetCount);
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
            RollBack();
            return false;
        }

        if (rhsIsSingleSimpleOperand)
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
                RollBack();
                return false;
            }
        }
        else if (rhsIsTemplateLiteral)
        {
            if (!TryAppendSimpleTemplateLiteralSpan(
                    expressionProgram, rhsStart, activationSlots,
                    unified, literalConstants, out var spanLen, out reason))
            {
                RollBack();
                return false;
            }

            if (rhsStart + spanLen - 1 != rhsEnd)
            {
                reason = "Template literal RHS span does not match expected boundary.";
                RollBack();
                return false;
            }
        }
        else
        {
            // Complex RHS region: lower [rhsStart, setNamedIndex) with the general
            // operand-stack appender (the compiler twin of the eligibility walker). It emits
            // each op's lowering in source (evaluation) order, leaving exactly one value on
            // the stack — the RHS — above the base, preserving base-then-RHS order.
            if (!rhsIsComplexRegion ||
                !TryAppendAdmittedComplexCallArgumentRegion(
                    expressionProgram,
                    slotLayout,
                    unified,
                    literalConstants,
                    stringConstants,
                    callTargetConstants,
                    rhsStart,
                    setNamedIndex,
                    expectedArgumentCount: 1,
                    allowsDynamicIdentifiers,
                    out reason))
            {
                RollBack();
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
        UnifiedBytecodeSlotLayout slotLayout,
        ImmutableArray<UnifiedBytecodeCallTarget>.Builder callTargetConstants,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        var activationSlots = slotLayout.ActivationSlots;
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

        var setComputedIndex = expressionProgram.OperationCount - 1;
        var identifierConstants = expressionProgram.IdentifierConstants.AsSpan();

        // Resolve the key/value split. Evaluation order (object, then key, then RHS) is fixed
        // by the op stream; we only locate where the key span ends and the value region begins,
        // we never reorder. Prefer the simple-value fast path (value is the single op before
        // SetComputedProperty); otherwise scan for the split where [1, valueStart) is a valid
        // key span and [valueStart, set) is a single-operand complex value region.
        var simpleValueIndex = setComputedIndex - 1;
        var valueStart = -1;
        var valueIsSimpleOperand = false;
        if (IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: 1,
                endExclusive: simpleValueIndex,
                allowsDynamicIdentifiers) &&
            CanAppendSimpleOperandLoadWithDynamic(
                expressionProgram.GetOperation(simpleValueIndex),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            valueStart = simpleValueIndex;
            valueIsSimpleOperand = true;
        }
        else
        {
            for (var candidate = 2; candidate < setComputedIndex; candidate++)
            {
                if (IsSupportedComputedPropertyKeySpan(
                        expressionProgram,
                        activationSlots,
                        startInclusive: 1,
                        endExclusive: candidate,
                        allowsDynamicIdentifiers) &&
                    UnifiedBytecodeProductionEligibility.TryValidateAdmittedComplexCallArgumentRegion(
                        expressionProgram,
                        candidate,
                        setComputedIndex,
                        expectedArgumentCount: 1,
                        identifierConstants,
                        activationSlots,
                        allowsDynamicIdentifiers))
                {
                    valueStart = candidate;
                    break;
                }
            }
        }

        if (valueStart < 0)
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

        var stagedCallTargets = ImmutableArray.CreateBuilder<UnifiedBytecodeCallTarget>();
        stagedCallTargets.AddRange(callTargetConstants);

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
                endExclusive: valueStart,
                out reason,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        if (valueIsSimpleOperand)
        {
            if (!TryAppendSimpleOperandLoadWithDynamic(
                    expressionProgram.GetOperation(valueStart),
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
        }
        else if (!TryAppendAdmittedComplexCallArgumentRegion(
                     expressionProgram,
                     slotLayout,
                     stagedUnified,
                     stagedLiterals,
                     stagedStrings,
                     stagedCallTargets,
                     valueStart,
                     setComputedIndex,
                     expectedArgumentCount: 1,
                     allowsDynamicIdentifiers,
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
        callTargetConstants.Clear();
        callTargetConstants.AddRange(stagedCallTargets);
        reason = string.Empty;
        return true;
    }

    // Nested named receiver prefix followed by a computed property write
    // (`box.child[key] = value`): emits the activation-resolved base, the named
    // receiver-prefix reads, the computed key span, the value, then SetComputedProperty.
    private static bool TryAppendFirstBoundaryNestedNamedComputedPropertySet(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
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

        var propertySet = expressionProgram.GetOperation(expressionProgram.OperationCount - 1);
        if (propertySet.Kind != ExpressionOpKind.SetComputedProperty)
        {
            reason = string.Empty;
            return false;
        }

        if (propertySet.AllowNameInference)
        {
            reason = "Nested named computed property writes with name inference are not supported.";
            return false;
        }

        var stringTable = expressionProgram.StringConstants.AsSpan();
        if (expressionProgram.GetOperation(1).Kind != ExpressionOpKind.GetNamedProperty)
        {
            reason = string.Empty;
            return false;
        }

        var keyStart = 1;
        while (keyStart < expressionProgram.OperationCount - 1)
        {
            var receiverRead = expressionProgram.GetOperation(keyStart);
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

            keyStart++;
        }

        if (keyStart < 2)
        {
            reason = string.Empty;
            return false;
        }

        var valueIndex = expressionProgram.OperationCount - 2;
        if (keyStart >= valueIndex)
        {
            reason = string.Empty;
            return false;
        }

        var stagedUnified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        stagedUnified.AddRange(unified);

        var stagedLiterals = ImmutableArray.CreateBuilder<JsValue>();
        stagedLiterals.AddRange(literalConstants);

        var stagedStrings = ImmutableArray.CreateBuilder<string>();
        stagedStrings.AddRange(stringConstants);

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                stagedUnified,
                out reason))
        {
            return false;
        }

        for (var operationIndex = 1; operationIndex < keyStart; operationIndex++)
        {
            var receiverRead = expressionProgram.GetOperation(operationIndex);
            var receiverNameIndex = stagedStrings.Count;
            stagedStrings.Add(receiverRead.GetString(stringTable));
            stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                startInclusive: keyStart,
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

        // Capture builder lengths before emission so a later failure rolls back
        // instead of leaking half-written operand loads the general loop would
        // re-emit (doubling them past MaxStackDepth -> VM stack overflow).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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
                RollBackUnifiedBuilder(unified, unifiedCount);
                RollBackUnifiedBuilder(literalConstants, literalCount);
                RollBackUnifiedBuilder(stringConstants, stringCount);
                return false;
            }
        }
        else
        {
            var rhsOp = expressionProgram.GetOperation(rhsStart);
            if (rhsOp.Kind != ExpressionOpKind.LoadLiteral)
            {
                reason = string.Empty;
                RollBackUnifiedBuilder(unified, unifiedCount);
                RollBackUnifiedBuilder(literalConstants, literalCount);
                RollBackUnifiedBuilder(stringConstants, stringCount);
                return false;
            }

            if (!TryAppendSimpleTemplateLiteralSpan(
                    expressionProgram, rhsStart, activationSlots, unified, literalConstants, out var spanLen, out reason))
            {
                RollBackUnifiedBuilder(unified, unifiedCount);
                RollBackUnifiedBuilder(literalConstants, literalCount);
                RollBackUnifiedBuilder(stringConstants, stringCount);
                return false;
            }

            if (rhsStart + spanLen - 1 != rhsEnd)
            {
                reason = "Template literal RHS span does not match expected nested property-write boundary.";
                RollBackUnifiedBuilder(unified, unifiedCount);
                RollBackUnifiedBuilder(literalConstants, literalCount);
                RollBackUnifiedBuilder(stringConstants, stringCount);
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

    // Nested named receiver-prefix computed property update (`box.child[key]++`).
    // The simple computed-update emitter below intercepts on a trailing UpdateComputedProperty
    // but treats index 1 onward as the key span, so the named receiver prefix would be rejected
    // ("Unsupported computed property key span"). This handler must run first and own the
    // named-prefix shape. The full shape is validated before any staging is committed so a
    // false path never leaves a partially emitted (overflowing) program.
    private static bool TryAppendFirstBoundaryNestedNamedComputedPropertyUpdate(
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

        var propertyUpdateIndex = expressionProgram.OperationCount - 1;
        var propertyUpdate = expressionProgram.GetOperation(propertyUpdateIndex);
        if (propertyUpdate.Kind != ExpressionOpKind.UpdateComputedProperty)
        {
            reason = string.Empty;
            return false;
        }

        var stringTable = expressionProgram.StringConstants.AsSpan();
        if (expressionProgram.GetOperation(1).Kind != ExpressionOpKind.GetNamedProperty)
        {
            // No named receiver prefix — let the simple computed-update emitter own it.
            reason = string.Empty;
            return false;
        }

        var keyStart = 1;
        while (keyStart < propertyUpdateIndex)
        {
            var receiverRead = expressionProgram.GetOperation(keyStart);
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

            keyStart++;
        }

        if (keyStart < 2 || keyStart >= propertyUpdateIndex)
        {
            reason = string.Empty;
            return false;
        }

        // Validate the computed key span fully before emitting anything so a decline
        // here cannot leave staged ops partially written.
        if (!IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: keyStart,
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

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                stagedUnified,
                out reason))
        {
            return false;
        }

        for (var operationIndex = 1; operationIndex < keyStart; operationIndex++)
        {
            var receiverRead = expressionProgram.GetOperation(operationIndex);
            var receiverNameIndex = stagedStrings.Count;
            stagedStrings.Add(receiverRead.GetString(stringTable));
            stagedUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, receiverNameIndex));
        }

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                stagedUnified,
                stagedLiterals,
                stagedStrings,
                startInclusive: keyStart,
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

    // A23: computed receiver-prefix property update with a COMPUTED-update terminal
    // (`box[k1].child[k2]++`, `--box[k1].child[k2]`). The receiver prefix is a simple computed
    // property read (`box[k1]`, optionally with trailing plain named reads such as `.child`) that
    // TryAppendFirstBoundaryNestedNamedComputedPropertyUpdate cannot own (op 1 is not a plain
    // GetNamedProperty) and that TryAppendFirstBoundaryComputedPropertyUpdate mis-reads as a single
    // key span (`Unsupported computed property key span`). This handler resolves the prefix ONCE via
    // the shared computed-read span emitter, then emits the trailing computed key span and the
    // update. The whole shape is validated before any staging is committed so a false path never
    // leaves a partially emitted program. The named-update terminal (`box[k1].child++`) is already
    // owned by the generic UpdateNamedProperty per-op path, so it is not handled here.
    private static bool TryAppendFirstBoundaryComputedPrefixComputedPropertyUpdate(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        reason = string.Empty;
        if (expressionProgram.OperationCount < 6)
        {
            return false;
        }

        var propertyUpdateIndex = expressionProgram.OperationCount - 1;
        var propertyUpdate = expressionProgram.GetOperation(propertyUpdateIndex);
        if (propertyUpdate.Kind != ExpressionOpKind.UpdateComputedProperty)
        {
            return false;
        }

        // The receiver prefix must be a simple computed property read (`box[k1]`, optionally with
        // trailing plain named reads) ending before the trailing computed key span.
        if (!TryMeasureSimpleComputedPropertyReadOperandSpan(
                expressionProgram,
                0,
                propertyUpdateIndex,
                activationSlots,
                allowsDynamicIdentifiers,
                out var keyStart,
                out var keyEndExclusive,
                out var receiverSpanLength))
        {
            return false;
        }

        if (receiverSpanLength <= 0 || receiverSpanLength >= propertyUpdateIndex)
        {
            return false;
        }

        // Validate the trailing computed key span fully before emitting anything.
        if (!IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: receiverSpanLength,
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

        if (!TryAppendMeasuredSimpleComputedPropertyReadOperandSpan(
                expressionProgram,
                0,
                keyStart,
                keyEndExclusive,
                receiverSpanLength,
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
                startInclusive: receiverSpanLength,
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

    // Handles delete a?.b.c and delete a.b?.c.d. The source expression program carries an optional
    // named receiver hop followed by a flag-only short-circuit guard before the terminal named delete.
    private static bool TryAppendFirstBoundaryOptionalNamedThenNamedPropertyDelete(
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
        var optionalHopIndex = 1;
        while (optionalHopIndex < expressionProgram.OperationCount)
        {
            var operation = expressionProgram.GetOperation(optionalHopIndex);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget ||
                operation.GetString(expressionStringConstants).IsPrivateName())
            {
                break;
            }

            optionalHopIndex++;
        }

        if (optionalHopIndex >= expressionProgram.OperationCount)
        {
            reason = string.Empty;
            return false;
        }

        var optionalHop = expressionProgram.GetOperation(optionalHopIndex);
        var jumpIndex = optionalHopIndex + 1;
        var deleteIndex = expressionProgram.OperationCount - 4;
        var endJumpIndexInProgram = expressionProgram.OperationCount - 3;
        var popIndex = expressionProgram.OperationCount - 2;
        var trueIndex = expressionProgram.OperationCount - 1;
        var deleteProperty = expressionProgram.GetOperation(deleteIndex);
        if (deleteIndex != jumpIndex + 1 ||
            optionalHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !optionalHop.IsOptional ||
            optionalHop.ShortCircuitOnNullishTarget ||
            optionalHop.GetString(expressionStringConstants).IsPrivateName() ||
            expressionProgram.GetOperation(jumpIndex) is not { Kind: ExpressionOpKind.JumpIfShortCircuited } jumpIfShortCircuited ||
            jumpIfShortCircuited.Target != popIndex ||
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

        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            return false;
        }

        for (var index = 1; index < optionalHopIndex; index++)
        {
            var propertyRead = expressionProgram.GetOperation(index);
            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(propertyRead.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
        }

        var optionalPropertyNameIndex = stringConstants.Count;
        stringConstants.Add(optionalHop.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.GetNamedPropertyOptional,
            optionalPropertyNameIndex));

        var shortCircuitJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfShortCircuited, 0));

        var deleteNameIndex = stringConstants.Count;
        stringConstants.Add(deleteProperty.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DeleteNamedProperty, deleteNameIndex));
        var endJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));

        var shortCircuitIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
        AddTrueLiteral(unified, literalConstants);

        unified[shortCircuitJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfShortCircuited,
            shortCircuitIndex);
        unified[endJumpIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, unified.Count);

        reason = string.Empty;
        return true;
    }

    // Handles delete a?.b?.c and delete a.b?.c?.d. The source expression program carries an optional
    // named receiver hop followed by the terminal optional-delete guard.
    private static bool TryAppendFirstBoundaryOptionalNamedThenOptionalNamedPropertyDelete(
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
        var optionalHopIndex = 1;
        while (optionalHopIndex < expressionProgram.OperationCount)
        {
            var operation = expressionProgram.GetOperation(optionalHopIndex);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget ||
                operation.GetString(expressionStringConstants).IsPrivateName())
            {
                break;
            }

            optionalHopIndex++;
        }

        if (optionalHopIndex >= expressionProgram.OperationCount)
        {
            reason = string.Empty;
            return false;
        }

        var optionalHop = expressionProgram.GetOperation(optionalHopIndex);
        var jumpIndex = optionalHopIndex + 1;
        var deleteIndex = expressionProgram.OperationCount - 4;
        var endJumpIndexInProgram = expressionProgram.OperationCount - 3;
        var popIndex = expressionProgram.OperationCount - 2;
        var trueIndex = expressionProgram.OperationCount - 1;
        var deleteProperty = expressionProgram.GetOperation(deleteIndex);
        if (deleteIndex != jumpIndex + 1 ||
            optionalHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !optionalHop.IsOptional ||
            optionalHop.ShortCircuitOnNullishTarget ||
            optionalHop.GetString(expressionStringConstants).IsPrivateName() ||
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

        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            return false;
        }

        for (var index = 1; index < optionalHopIndex; index++)
        {
            var propertyRead = expressionProgram.GetOperation(index);
            var propertyNameIndex = stringConstants.Count;
            stringConstants.Add(propertyRead.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, propertyNameIndex));
        }

        var optionalPropertyNameIndex = stringConstants.Count;
        stringConstants.Add(optionalHop.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.GetNamedPropertyOptional,
            optionalPropertyNameIndex));

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
        if (TryAppendFirstBoundaryOptionalComputedReadThenComputedPropertyDelete(
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

    // Handles delete a?.[k1][k2]. A nullish base skips both key spans and returns true; a present base
    // evaluates k1 before the receiver read, then k2 before the terminal delete.
    private static bool TryAppendFirstBoundaryOptionalComputedReadThenComputedPropertyDelete(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
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
        if (expressionProgram.GetOperation(jumpIndex) is not
                { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true } jumpIfNullish ||
            jumpIfNullish.Target <= jumpIndex + 1 ||
            jumpIfNullish.Target >= deleteIndex ||
            expressionProgram.GetOperation(jumpIfNullish.Target) is not { Kind: ExpressionOpKind.JumpIfShortCircuited } jumpIfShortCircuited ||
            jumpIfShortCircuited.Target != popIndex ||
            expressionProgram.GetOperation(deleteIndex).Kind != ExpressionOpKind.DeleteComputedProperty ||
            expressionProgram.GetOperation(endJumpIndexInProgram).Kind != ExpressionOpKind.Jump ||
            expressionProgram.GetOperation(endJumpIndexInProgram).Target != expressionProgram.OperationCount ||
            expressionProgram.GetOperation(popIndex).Kind != ExpressionOpKind.Pop ||
            !IsTrueLiteral(expressionProgram, trueIndex))
        {
            reason = string.Empty;
            return false;
        }

        var computedReadIndex = jumpIfNullish.Target - 1;
        if (computedReadIndex <= jumpIndex + 1 ||
            expressionProgram.GetOperation(computedReadIndex).Kind != ExpressionOpKind.GetComputedProperty ||
            !IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: jumpIndex + 1,
                endExclusive: computedReadIndex) ||
            !IsSupportedComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive: jumpIfNullish.Target + 1,
                endExclusive: deleteIndex))
        {
            reason = string.Empty;
            return false;
        }

        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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
                endExclusive: computedReadIndex,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedProperty));

        var shortCircuitJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfShortCircuited, 0));

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: jumpIfNullish.Target + 1,
                endExclusive: deleteIndex,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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
            shortCircuitJumpIndex);
        unified[shortCircuitJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfShortCircuited,
            shortCircuitIndex);
        unified[endJumpIndex] = new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, unified.Count);

        reason = string.Empty;
        return true;
    }

    // Handles delete a?.b[k]. The source expression program carries the optional named hop and a
    // flag-only short-circuit guard before the computed key, so a nullish base skips key evaluation
    // while an ordinary nullish `b` value still evaluates the key and then throws from delete.
    private static bool TryAppendFirstBoundaryOptionalNamedThenComputedPropertyDelete(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        out string reason)
    {
        if (expressionProgram.OperationCount < 8)
        {
            reason = string.Empty;
            return false;
        }

        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();
        var firstHop = expressionProgram.GetOperation(1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(expressionStringConstants).IsPrivateName() ||
            expressionProgram.GetOperation(2) is not { Kind: ExpressionOpKind.JumpIfShortCircuited } jumpIfShortCircuited ||
            jumpIfShortCircuited.Target != expressionProgram.OperationCount - 2 ||
            expressionProgram.GetOperation(expressionProgram.OperationCount - 4).Kind != ExpressionOpKind.DeleteComputedProperty ||
            expressionProgram.GetOperation(expressionProgram.OperationCount - 3).Kind != ExpressionOpKind.Jump ||
            expressionProgram.GetOperation(expressionProgram.OperationCount - 3).Target != expressionProgram.OperationCount ||
            expressionProgram.GetOperation(expressionProgram.OperationCount - 2).Kind != ExpressionOpKind.Pop ||
            !IsTrueLiteral(expressionProgram, expressionProgram.OperationCount - 1))
        {
            reason = string.Empty;
            return false;
        }

        // Capture builder lengths before emission so a later failure rolls back
        // instead of leaking half-written operand loads the general loop would
        // re-emit (doubling them past MaxStackDepth -> VM stack overflow).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            return false;
        }

        var propertyNameIndex = stringConstants.Count;
        stringConstants.Add(firstHop.GetString(expressionStringConstants));
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedPropertyOptional, propertyNameIndex));

        var shortCircuitJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfShortCircuited, 0));

        if (!TryAppendComputedPropertyKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive: 3,
                endExclusive: expressionProgram.OperationCount - 4,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            return false;
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.DeleteComputedProperty));
        var endJumpIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Jump, 0));

        var shortCircuitIndex = unified.Count;
        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Pop));
        AddTrueLiteral(unified, literalConstants);

        unified[shortCircuitJumpIndex] = new UnifiedBytecodeInstruction(
            UnifiedBytecodeOpCode.JumpIfShortCircuited,
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

        // Capture builder lengths before emission so a later failure rolls back
        // instead of leaking half-written operand loads the general loop would
        // re-emit (doubling them past MaxStackDepth -> VM stack overflow).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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

        // Capture builder lengths before emission so a later failure rolls back
        // instead of leaking half-written operand loads the general loop would
        // re-emit (doubling them past MaxStackDepth -> VM stack overflow).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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

        // Capture builder lengths before emission so a later failure rolls back
        // instead of leaking half-written operand loads the general loop would
        // re-emit (doubling them past MaxStackDepth -> VM stack overflow).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationValueLoad(baseLoad, expressionProgram, activationSlots, unified, out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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

        // A29: peel any trailing short-circuiting NAMED reads (`a?.[k].c`,
        // `a?.[k]?.[j].c`). The remaining span [jumpIndex, chainEnd) is exactly the
        // one-or-more optional computed hops.
        var chainEnd = expressionProgram.OperationCount;
        while (chainEnd > jumpIndex + 2)
        {
            var suffixOp = expressionProgram.GetOperation(chainEnd - 1);
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

            chainEnd--;
        }

        // Pre-pass: validate each optional computed hop and record its
        // GetComputedProperty index. Each hop is
        // [JumpIfNullish(ReplaceWithUndefined:true), key-span..., GetComputedProperty].
        // The first hop's read is the chain's first boundary (!ShortCircuitOnNullishTarget);
        // subsequent hops short-circuit on a nullish receiver.
        var hopComputedIndices = new List<int>();
        var walkIndex = jumpIndex;
        while (walkIndex < chainEnd)
        {
            var hopJump = expressionProgram.GetOperation(walkIndex);
            var keyStart = walkIndex + 1;
            var hopComputedIndex = keyStart;
            while (hopComputedIndex < chainEnd &&
                   expressionProgram.GetOperation(hopComputedIndex).Kind != ExpressionOpKind.GetComputedProperty)
            {
                hopComputedIndex++;
            }

            // Each hop's lowered boundary jump targets the operation immediately after its
            // OWN GetComputedProperty; short-circuit cascades hop-to-hop. The emitted unified
            // boundary jumps are later all backpatched to the single chain end.
            if (hopJump.Kind != ExpressionOpKind.JumpIfNullish ||
                !hopJump.ReplaceWithUndefined ||
                hopComputedIndex >= chainEnd ||
                hopComputedIndex <= keyStart ||
                hopJump.Target != hopComputedIndex + 1)
            {
                reason = string.Empty;
                return false;
            }

            var hopComputedOp = expressionProgram.GetOperation(hopComputedIndex);
            var expectedShortCircuit = hopComputedIndices.Count > 0;
            if (hopComputedOp.Kind != ExpressionOpKind.GetComputedProperty ||
                hopComputedOp.ShortCircuitOnNullishTarget != expectedShortCircuit)
            {
                reason = string.Empty;
                return false;
            }

            if (!IsSupportedComputedPropertyKeySpan(
                    expressionProgram,
                    activationSlots,
                    startInclusive: keyStart,
                    endExclusive: hopComputedIndex))
            {
                reason = "Unsupported computed property key span.";
                return false;
            }

            hopComputedIndices.Add(hopComputedIndex);
            walkIndex = hopComputedIndex + 1;
        }

        if (hopComputedIndices.Count == 0 || walkIndex != chainEnd)
        {
            reason = string.Empty;
            return false;
        }

        // Capture builder lengths before emission so a later failure rolls back
        // instead of leaking half-written operand loads the general loop would
        // re-emit (doubling them past MaxStackDepth -> VM stack overflow).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
            return false;
        }

        for (var operationIndex = 1; operationIndex < jumpIndex; operationIndex++)
        {
            var prefixOp = expressionProgram.GetOperation(operationIndex);
            var prefixNameIndex = stringConstants.Count;
            stringConstants.Add(prefixOp.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, prefixNameIndex));
        }

        // Emit each optional computed hop, recording its boundary-jump slot so every
        // jump can be backpatched to the same chain end (the post-tail instruction count).
        var unifiedJumpIndices = new List<int>(hopComputedIndices.Count);
        var hopKeyStart = jumpIndex + 1;
        foreach (var hopComputedIndex in hopComputedIndices)
        {
            var unifiedJumpIndex = unified.Count;
            unifiedJumpIndices.Add(unifiedJumpIndex);
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined, 0));

            if (!TryAppendComputedPropertyKeySpan(
                    expressionProgram,
                    activationSlots,
                    unified,
                    literalConstants,
                    stringConstants,
                    startInclusive: hopKeyStart,
                    endExclusive: hopComputedIndex,
                    out reason))
            {
                RollBackUnifiedBuilder(unified, unifiedCount);
                RollBackUnifiedBuilder(literalConstants, literalCount);
                RollBackUnifiedBuilder(stringConstants, stringCount);
                return false;
            }

            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetComputedProperty));
            hopKeyStart = hopComputedIndex + 2;
        }

        for (var operationIndex = chainEnd; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            var suffixOp = expressionProgram.GetOperation(operationIndex);
            var suffixNameIndex = stringConstants.Count;
            stringConstants.Add(suffixOp.GetString(expressionStringConstants));
            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.GetNamedProperty, suffixNameIndex));
        }

        foreach (var unifiedJumpIndex in unifiedJumpIndices)
        {
            unified[unifiedJumpIndex] = new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined,
                unified.Count);
        }

        reason = string.Empty;
        return true;
    }

    // Emits a control-expression computed key span (`box[cond ? a : b]`, `box[a && b]`,
    // `box[a ?? b]`) into <paramref name="unified"/> when the whole span is exactly one
    // admitted control-expression operand span, leaving a single key value on the stack.
    // Returns false (without partial output) when the span is not a whole control
    // expression so the caller can fall back to the stack-machine key emission.
    private static bool TryAppendControlExpressionComputedKeySpan(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
        ImmutableArray<string>.Builder stringConstants,
        int startInclusive,
        int endExclusive,
        bool allowsDynamicIdentifiers)
    {
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (TryAppendSimpleControlExpressionOperandSpan(
                expressionProgram,
                startInclusive,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                literalConstants,
                stringConstants,
                callTargetConstants: null,
                slotLayout: null,
                out var spanLength,
                out _) &&
            startInclusive + spanLength == endExclusive)
        {
            return true;
        }

        // Roll back any partial control-expression output so the stack-machine path
        // starts from a clean builder state.
        unified.Count = unifiedCount;
        literalConstants.Count = literalCount;
        stringConstants.Count = stringCount;
        return false;
    }

    // Validation-only probe: runs the control-expression key emitter against throwaway
    // builders so eligibility can confirm a whole-span control-expression key without
    // mutating live output.
    private static bool TryProbeControlExpressionComputedKeySpan(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        int startInclusive,
        int endExclusive,
        bool allowsDynamicIdentifiers)
    {
        var probeUnified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        var probeLiterals = ImmutableArray.CreateBuilder<JsValue>();
        var probeStrings = ImmutableArray.CreateBuilder<string>();
        return TryAppendControlExpressionComputedKeySpan(
            expressionProgram,
            activationSlots,
            probeUnified,
            probeLiterals,
            probeStrings,
            startInclusive,
            endExclusive,
            allowsDynamicIdentifiers);
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
        if (startInclusive < endExclusive &&
            TryAppendControlExpressionComputedKeySpan(
                expressionProgram,
                activationSlots,
                unified,
                literalConstants,
                stringConstants,
                startInclusive,
                endExclusive,
                allowsDynamicIdentifiers))
        {
            reason = string.Empty;
            return true;
        }

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
        if (startInclusive >= endExclusive)
        {
            return false;
        }

        // Control-expression computed keys (`box[cond ? a : b]`, `box[a && b]`,
        // `box[a ?? b]`) lower to JumpIfConditionalFalse/Jump/Pop control flow that the
        // stack-machine walker below cannot validate. Probe the dedicated control-flow
        // emitter against throwaway builders to confirm the whole span is an admitted
        // control expression; the live emission in TryAppendComputedPropertyKeySpan runs
        // the same path. Only a whole-span match is admitted.
        if (TryProbeControlExpressionComputedKeySpan(
                expressionProgram,
                activationSlots,
                startInclusive,
                endExclusive,
                allowsDynamicIdentifiers))
        {
            return true;
        }

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

    private static bool TryAppendSimplePropertyReadBinaryExpression(
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

        var containsPropertyRead = false;
        var containsBinary = false;
        var stackDepth = 0;
        var startCount = unified.Count;
        var startLiteralCount = literalConstants.Count;
        var startStringCount = stringConstants.Count;
        var expressionStringConstants = expressionProgram.StringConstants.AsSpan();

        void RollBack()
        {
            unified.Count = startCount;
            literalConstants.Count = startLiteralCount;
            stringConstants.Count = startStringCount;
        }

        for (var operationIndex = 0; operationIndex < expressionProgram.OperationCount; operationIndex++)
        {
            var operation = expressionProgram.GetOperation(operationIndex);
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadIdentifier:
                case ExpressionOpKind.LoadLiteral:
                case ExpressionOpKind.LoadThis:
                case ExpressionOpKind.LoadNewTarget:
                    if (!TryAppendSimpleOperandLoadWithDynamic(
                            operation,
                            expressionProgram,
                            activationSlots,
                            allowsDynamicIdentifiers,
                            unified,
                            literalConstants,
                            stringConstants,
                            out reason))
                    {
                        RollBack();
                        reason = string.Empty;
                        return false;
                    }

                    stackDepth++;
                    break;

                case ExpressionOpKind.GetNamedProperty:
                    if (stackDepth < 1 ||
                        operation.IsOptional ||
                        operation.ShortCircuitOnNullishTarget ||
                        operation.GetString(expressionStringConstants).IsPrivateName())
                    {
                        RollBack();
                        reason = string.Empty;
                        return false;
                    }

                    var propertyNameIndex = stringConstants.Count;
                    stringConstants.Add(operation.GetString(expressionStringConstants));
                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.GetNamedProperty,
                        propertyNameIndex));
                    containsPropertyRead = true;
                    break;

                case ExpressionOpKind.Binary:
                    if (stackDepth < 2 || !IsSupportedBinaryOperator(operation.Operator))
                    {
                        RollBack();
                        reason = string.Empty;
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(
                        UnifiedBytecodeOpCode.Binary,
                        (int)operation.Operator));
                    stackDepth--;
                    containsBinary = true;
                    break;

                default:
                    RollBack();
                    reason = string.Empty;
                    return false;
            }
        }

        if (!containsPropertyRead || !containsBinary || stackDepth != 1)
        {
            RollBack();
            reason = string.Empty;
            return false;
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
        ImmutableArray<FunctionLiteralDescriptor>.Builder functionLiteralConstants,
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

        // Validate the multi-op RHS shape before emitting anything. The emission pass below
        // only knows how to lower an array/object/template-literal span; any other multi-op
        // RHS (e.g. `typeof base.prop === literal`, where the op after the property chain is
        // `TypeOf`) is not this shape. Rejecting here keeps the contract "validate the entire
        // shape before emitting anything" — otherwise the base load + property reads would be
        // emitted and then left stranded on a non-rolled-back `false` return, doubling them
        // when the general loop re-emits the expression and overflowing MaxStackDepth.
        if (rhsStart != rhsEnd)
        {
            var rhsKind = expressionProgram.GetOperation(rhsStart).Kind;
            if (rhsKind is not (ExpressionOpKind.CreateArray
                or ExpressionOpKind.CreateObject
                or ExpressionOpKind.LoadLiteral))
            {
                reason = string.Empty;
                return false;
            }
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

        // Emission pass — only reached when all validation passes. Capture builder
        // lengths so any lowering helper that still returns false mid-emission rolls
        // back instead of leaving half-written operand loads the general loop would
        // re-emit (doubling them past MaxStackDepth).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                unified,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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
                RollBackUnifiedBuilder(unified, unifiedCount);
                RollBackUnifiedBuilder(literalConstants, literalCount);
                RollBackUnifiedBuilder(stringConstants, stringCount);
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
                    RollBackUnifiedBuilder(unified, unifiedCount);
                    RollBackUnifiedBuilder(literalConstants, literalCount);
                    RollBackUnifiedBuilder(stringConstants, stringCount);
                    return false;
                }
            }
            else if (rhsOp.Kind == ExpressionOpKind.CreateObject)
            {
                if (!TryAppendSimpleObjectLiteralSpan(
                        expressionProgram, rhsStart, activationSlots, unified, literalConstants, stringConstants,
                        callTargetConstants: null, slotLayout: null,
                        out var objSpanLen, out reason,
                        functionLiteralConstants: functionLiteralConstants) ||
                    rhsStart + objSpanLen - 1 != rhsEnd)
                {
                    reason = reason.Length == 0 ? "Object literal RHS span does not match expected boundary." : reason;
                    RollBackUnifiedBuilder(unified, unifiedCount);
                    RollBackUnifiedBuilder(literalConstants, literalCount);
                    RollBackUnifiedBuilder(stringConstants, stringCount);
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
                    RollBackUnifiedBuilder(unified, unifiedCount);
                    RollBackUnifiedBuilder(literalConstants, literalCount);
                    RollBackUnifiedBuilder(stringConstants, stringCount);
                    return false;
                }
            }
            else
            {
                reason = string.Empty;
                RollBackUnifiedBuilder(unified, unifiedCount);
                RollBackUnifiedBuilder(literalConstants, literalCount);
                RollBackUnifiedBuilder(stringConstants, stringCount);
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

        // Capture builder lengths before emission so a later operand-load failure
        // rolls back instead of leaking half-written loads the general loop would
        // re-emit (doubling them past MaxStackDepth -> VM stack overflow).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        var baseOp = expressionProgram.GetOperation(0);
        if (!TryAppendActivationValueLoad(baseOp, expressionProgram, activationSlots, unified, out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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

        // Capture builder lengths before emission so a later key-load failure rolls
        // back instead of leaking a half-written base load the general loop would
        // re-emit (doubling them past MaxStackDepth -> VM stack overflow).
        var unifiedCount = unified.Count;
        var literalCount = literalConstants.Count;
        var stringCount = stringConstants.Count;

        if (!TryAppendActivationOrImplicitArgumentsObjectReadValueLoad(
                expressionProgram.GetOperation(0),
                expressionProgram,
                activationSlots,
                allowsDynamicIdentifiers,
                unified,
                stringConstants,
                out reason))
        {
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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
            RollBackUnifiedBuilder(unified, unifiedCount);
            RollBackUnifiedBuilder(literalConstants, literalCount);
            RollBackUnifiedBuilder(stringConstants, stringCount);
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

    private static bool TryMeasureSimpleTemplateLiteralSpan(
        ExpressionProgram expressionProgram,
        int startIndex,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        if (expressionProgram.GetOperation(startIndex).Kind != ExpressionOpKind.LoadLiteral)
        {
            spanLength = 0;
            return false;
        }

        var i = startIndex + 1;
        while (i < expressionProgram.OperationCount)
        {
            var op = expressionProgram.GetOperation(i);

            if (op.Kind == ExpressionOpKind.LoadLiteral)
            {
                if (i + 1 >= expressionProgram.OperationCount)
                {
                    break;
                }

                var next = expressionProgram.GetOperation(i + 1);
                if (next.Kind != ExpressionOpKind.Binary || next.Operator != BinaryOperator.Add)
                {
                    break;
                }

                i += 2;
                continue;
            }

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
                    i += 5;
                    continue;
                }
            }

            if (i + 2 < expressionProgram.OperationCount)
            {
                var toString = expressionProgram.GetOperation(i + 1);
                var add = expressionProgram.GetOperation(i + 2);
                if (toString.Kind == ExpressionOpKind.ToString &&
                    add.Kind == ExpressionOpKind.Binary &&
                    add.Operator == BinaryOperator.Add &&
                    CanAppendSimpleOperandLoadWithDynamic(op, expressionProgram, activationSlots, allowsDynamicIdentifiers))
                {
                    i += 3;
                    continue;
                }
            }

            break;
        }

        spanLength = i - startIndex;
        return true;
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

    private static int EncodeDynamicLexicalDeclarationOperand(int stringConstantIndex, VariableKind varKind)
    {
        var flags = varKind == VariableKind.Const ? 1 : 0;
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

    private static bool TryAppendDynamicDeclaration(
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
        if (declaration.TargetSymbol is not { } targetSymbol)
        {
            reason = $"Unsupported declaration target '{declaration.TargetSymbol?.Name}'.";
            return false;
        }

        var targetNameIndex = stringConstants.Count;
        stringConstants.Add(targetSymbol.Name);

        if (declaration.VarKind is VariableKind.Let or VariableKind.Const)
        {
            if (!allowsDynamicIdentifiers)
            {
                reason = $"Lexical declaration target '{targetSymbol.Name}' requires dynamic identifier operations.";
                return false;
            }

            unified.Add(new UnifiedBytecodeInstruction(
                UnifiedBytecodeOpCode.DeclareDynamicLexical,
                EncodeDynamicLexicalDeclarationOperand(targetNameIndex, declaration.VarKind)));
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
                UnifiedBytecodeOpCode.InitializeDynamicLexical,
                EncodeDynamicStoreOperand(targetNameIndex, declaration.AllowNameInference)));
            reason = string.Empty;
            return true;
        }

        if (declaration.VarKind != VariableKind.Var ||
            !slotLayout.ActivationSlots.MaterializedBindingNames.Contains(targetSymbol))
        {
            reason = $"Unsupported declaration target '{targetSymbol.Name}'.";
            return false;
        }

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

    /// <summary>
    /// Resolves a step-wise destructuring target. Returns a flat activation slot when one
    /// exists. Otherwise, at script scope (<paramref name="allowsDynamicIdentifiers"/>), the
    /// target is stored dynamically by name. <see cref="VariableKind.Var"/> targets are already
    /// var-hoisted into the materialized script environment; <see cref="VariableKind.Let"/> and
    /// <see cref="VariableKind.Const"/> targets are top-level lexical bindings already placed in
    /// TDZ by script declaration instantiation before the VM runs.
    /// </summary>
    private static bool TryResolveDestructuringTarget(
        Symbol targetSymbol,
        VariableKind varKind,
        bool allowsDynamicIdentifiers,
        UnifiedBytecodeSlotLayout slotLayout,
        Stack<UnifiedBytecodeScopeFrame> activeScopes,
        ImmutableArray<string>.Builder stringConstants,
        out int slotIndex,
        out int dynamicNameIndex,
        out string reason)
    {
        dynamicNameIndex = -1;
        if (TryResolveDeclarationSlot(targetSymbol, varKind, slotLayout, activeScopes, out slotIndex))
        {
            reason = string.Empty;
            return true;
        }

        if (allowsDynamicIdentifiers && varKind is VariableKind.Var or VariableKind.Let or VariableKind.Const)
        {
            dynamicNameIndex = stringConstants.Count;
            stringConstants.Add(targetSymbol.Name);
            slotIndex = -1;
            reason = string.Empty;
            return true;
        }

        reason = $"Unsupported destructuring target '{targetSymbol.Name}'.";
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

        if (activationSlots.SlotMap.TryGetValue(identifier.Name, out var mappedSlot) ||
            TryResolveActivationSlotByUniqueName(identifier.Name, activationSlots, out mappedSlot))
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
            if (identifier.ScopeId >= 0 &&
                identifier.ScopeId != activationSlots.ScopeId)
            {
                if (identifier.SlotIndex >= 0 &&
                    TryMapSlot(identifier.ScopeId, identifier.SlotIndex, slotLayout.FlatSlotMappings, out var scopedFlatSlot))
                {
                    slotIndex = scopedFlatSlot;
                    return true;
                }

                slotIndex = -1;
                return false;
            }

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

        if (activationSlots.SlotMap.TryGetValue(identifier.Name, out var mappedSlotByName) ||
            TryResolveActivationSlotByUniqueName(identifier.Name, activationSlots, out mappedSlotByName))
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

    private static bool TryResolveActivationSlotByUniqueName(
        Symbol symbol,
        ActivationSlotShape activationSlots,
        out int slotIndex)
    {
        slotIndex = -1;
        var name = symbol.Name;
        if (string.IsNullOrEmpty(name) || activationSlots.SlotNames.IsDefaultOrEmpty)
        {
            return false;
        }

        var found = false;
        foreach (var (candidate, candidateSlotIndex) in activationSlots.SlotNames)
        {
            if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (found)
            {
                slotIndex = -1;
                return false;
            }

            found = true;
            slotIndex = candidateSlotIndex;
        }

        return found;
    }

    private static bool TryResolveExplicitActivationSlot(
        IdentifierOperand identifier,
        UnifiedBytecodeSlotLayout slotLayout,
        out int slotIndex)
    {
        var activationSlots = slotLayout.ActivationSlots;
        if (identifier.FlatSlotId >= 0)
        {
            if (identifier.ScopeId >= 0 &&
                identifier.ScopeId != activationSlots.ScopeId)
            {
                if (identifier.SlotIndex >= 0 &&
                    TryMapSlot(identifier.ScopeId, identifier.SlotIndex, slotLayout.FlatSlotMappings, out slotIndex))
                {
                    return true;
                }

                slotIndex = -1;
                return false;
            }

            slotIndex = identifier.FlatSlotId;
            return true;
        }

        if (identifier.ScopeId == activationSlots.ScopeId && identifier.SlotIndex >= 0)
        {
            if (TryMapSlot(identifier.ScopeId, identifier.SlotIndex, slotLayout.FlatSlotMappings, out slotIndex))
            {
                return true;
            }

            slotIndex = identifier.SlotIndex;
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
