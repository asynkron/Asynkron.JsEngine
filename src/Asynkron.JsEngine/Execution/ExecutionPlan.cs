#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Intermediate representation for pauseable functions (generators, async functions, async generators).
///     The plan contains a flat list of instructions that model sequential execution, branching, and yield/await points.
///     The interpreter maintains a program counter and executes the instructions synchronously, allowing
///     .next/.throw/.return to resume exactly where execution paused.
/// </summary>
/// <param name="Instructions">The instruction sequence.</param>
/// <param name="EntryPoint">Index of the first instruction to execute.</param>
/// <param name="SlotCount">Number of slots to allocate for internal variables (iterator states, values, etc.).</param>
/// <param name="SlotSymbols">Symbols mapped to slot indices for O(1) variable access.</param>
/// <param name="RootSlotCount">Slot count required for the root (function) scope user bindings.</param>
/// <param name="RootSlotMap">Slot map for the root (function) scope user bindings.</param>
/// <param name="RootLexicalBindings">Lexical bindings in the root scope (for TDZ).</param>
/// <param name="ScopeLexicalBindings">Lexical bindings per scope id.</param>
/// <param name="RootScopeId">Explicit root scope id for this plan (default 0 for compatibility).</param>
/// <param name="LayoutId">Stable identity for the expected slot layout, used to validate pooled environments.</param>
/// <param name="FlatSlotCount">Total number of flat slots needed for O(1) variable access across all scopes.</param>
/// <param name="FlatSlotMappings">Maps scopeId to array of (slotIndex, flatSlotId) for eager flat slot initialization.</param>
/// <param name="ActivationSlots">Precomputed slot-shape metadata for function activation setup.</param>
internal sealed record ExecutionPlan(
    ImmutableArray<ExecutionInstruction> Instructions,
    int EntryPoint,
    int SlotCount = 0,
    ImmutableArray<Symbol> SlotSymbols = default,
    int RootSlotCount = 0,
    ImmutableDictionary<Symbol, int>? RootSlotMap = null,
    ImmutableHashSet<Symbol>? RootLexicalBindings = null,
    ImmutableDictionary<int, ImmutableHashSet<Symbol>>? ScopeLexicalBindings = null,
    int RootScopeId = 0,
    int LayoutId = 0,
    int FlatSlotCount = 0,
    ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>? FlatSlotMappings = null,
    ActivationSlotShape? ActivationSlots = null,
    CompactStatementStorageBoundary? CompactStatementStorageBoundary = null)
{
    public ImmutableDictionary<Symbol, int> SafeRootSlotMap =>
        RootSlotMap ?? ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);

    public ImmutableHashSet<Symbol> SafeRootLexicalBindings =>
        RootLexicalBindings ?? ImmutableHashSet<Symbol>.Empty.WithComparer(ReferenceEqualityComparer<Symbol>.Instance);

    public ImmutableDictionary<int, ImmutableHashSet<Symbol>> SafeScopeLexicalBindings =>
        ScopeLexicalBindings ?? ImmutableDictionary<int, ImmutableHashSet<Symbol>>.Empty;

    public bool HasOnlyRootFlatSlotMappings { get; } =
        ComputeHasOnlyRootFlatSlotMappings(RootScopeId, FlatSlotMappings);

    public bool CanUseRawSyncReturn { get; } = ComputeCanUseRawSyncReturn(Instructions);

    public ExpressionProgram? SimpleReturnProgram { get; } = ComputeSimpleReturnProgram(Instructions, EntryPoint);

    public IrCallShape IrCallShape { get; } = ComputeIrCallShape(Instructions, EntryPoint);

    public CompactStatementStorageBoundary CreateCompactStatementStorageBoundary() =>
        CompactStatementStorageBoundary ?? CompactStatementStorage.CreateBoundary(
            Instructions,
            CompactStatementBoundaryMode.PureControlFlow);

    public CompactStatementStorageBoundary CreateDiagnosticCompactStatementStorageBoundary() =>
        CompactStatementStorage.CreateBoundary(Instructions, CompactStatementBoundaryMode.DiagnosticCoverage);

    private static bool ComputeHasOnlyRootFlatSlotMappings(
        int rootScopeId,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>? flatSlotMappings)
    {
        if (flatSlotMappings is null || flatSlotMappings.Count == 0)
        {
            return true;
        }

        foreach (var mapping in flatSlotMappings)
        {
            if (mapping.Key != rootScopeId)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ComputeCanUseRawSyncReturn(ImmutableArray<ExecutionInstruction> instructions)
    {
        if (instructions.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (var instruction in instructions)
        {
            if (BlocksRawSyncReturn(instruction))
            {
                return false;
            }
        }

        return true;
    }

    private static ExpressionProgram? ComputeSimpleReturnProgram(
        ImmutableArray<ExecutionInstruction> instructions,
        int entryPoint)
    {
        if ((uint)entryPoint >= (uint)instructions.Length ||
            !ComputeCanUseRawSyncReturn(instructions))
        {
            return null;
        }

        return instructions[entryPoint] is ReturnInstruction
        {
            ReturnProgram: { } returnProgram,
            AwaitedProgram: null
        }
            ? returnProgram
            : null;
    }

    private static IrCallShape ComputeIrCallShape(
        ImmutableArray<ExecutionInstruction> instructions,
        int entryPoint) =>
        ComputeSimpleReturnProgram(instructions, entryPoint) is not null
            ? IrCallShape.SimpleReturnExpression
            : IrCallShape.None;

    private static bool BlocksRawSyncReturn(ExecutionInstruction instruction) =>
        instruction.Kind is
            InstructionKind.AwaitAndDiscard or
            InstructionKind.Yield or
            InstructionKind.YieldStar or
            InstructionKind.EnterTry or
            InstructionKind.EnterCatch or
            InstructionKind.LeaveTry or
            InstructionKind.EndFinally or
            InstructionKind.IteratorInit or
            InstructionKind.IteratorMoveNext or
            InstructionKind.IteratorClose or
            InstructionKind.ForInInit or
            InstructionKind.ForInMoveNext or
            InstructionKind.ArrayDestructuringInit or
            InstructionKind.ArrayDestructuringElement or
            InstructionKind.ArrayDestructuringRest or
            InstructionKind.ArrayDestructuringClose ||
        MayCreateActiveIteratorState(instruction);

    private static bool MayCreateActiveIteratorState(ExecutionInstruction instruction)
    {
        if (instruction.Kind is
            InstructionKind.IteratorInit or
            InstructionKind.IteratorMoveNext or
            InstructionKind.IteratorClose or
            InstructionKind.ForInInit or
            InstructionKind.ForInMoveNext or
            InstructionKind.ArrayDestructuringInit or
            InstructionKind.ArrayDestructuringElement or
            InstructionKind.ArrayDestructuringRest or
            InstructionKind.ArrayDestructuringClose or
            InstructionKind.BindingVariableDeclaration)
        {
            return true;
        }

        return instruction switch
        {
            ThrowInstruction { ThrowProgram: { } program } => ContainsBindingTarget(program),
            EvaluateAndDiscardInstruction { ExpressionProgram: var program } => ContainsBindingTarget(program),
            AwaitAndDiscardInstruction { AwaitedProgram: var program } => ContainsBindingTarget(program),
            AssignmentSlotInstruction { ValueProgram: { } program } => ContainsBindingTarget(program),
            AssignmentSlotInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            LogicalCompoundAssignmentSlotInstruction { RhsProgram: { } program } => ContainsBindingTarget(program),
            LogicalCompoundAssignmentSlotInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            CompoundAssignmentSlotInstruction { RhsProgram: { } program } => ContainsBindingTarget(program),
            CompoundAssignmentSlotInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            SimpleVariableDeclarationInstruction { InitializerProgram: { } program } => ContainsBindingTarget(program),
            SimpleVariableDeclarationInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            BranchInstruction { ConditionProgram: var program } => ContainsBindingTarget(program),
            ReturnInstruction { ReturnProgram: { } program } => ContainsBindingTarget(program),
            ReturnInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            EnterWithInstruction { ObjectProgram: { } program } => ContainsBindingTarget(program),
            EnterWithInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            ForInInitInstruction { ObjectProgram: { } program } => ContainsBindingTarget(program),
            ForInInitInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            IteratorInitInstruction { IterableProgram: { } program } => ContainsBindingTarget(program),
            IteratorInitInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            _ => false
        };
    }

    private static bool ContainsBindingTarget(ExpressionProgram program)
    {
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind == ExpressionOpKind.ApplyBindingTarget)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record ActivationSlotShape(
    int ScopeId,
    int SlotCount,
    int LayoutId,
    ImmutableDictionary<Symbol, int> SlotMap,
    ImmutableArray<(Symbol Name, int SlotIndex)> SlotNames,
    ImmutableArray<int> ParameterSlotIndices);

internal enum IrCallShape
{
    None,
    SimpleReturnExpression
}
