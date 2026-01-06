#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Scope-aware collector that builds slot maps for each scope in an execution plan.
/// - Tracks scope stack using Push/Pop environment IR instructions
/// - Allocates slots for variable declarations within each scope
/// - Preserves compiler-generated identifiers (prefix '\u0001') in the root scope
/// - Produces immutable slot maps used to stamp IdentifierExpressions with ScopeId/SlotIndex
/// </summary>
internal sealed class ScopeSlotCollector : AstVisitor
{
    private const int FirstBlockScopeId = 1000; // Reserve lower IDs for IR-generated scopes

    private readonly int _rootScopeId;
    private readonly BlockStatement? _functionBody;
    private readonly Func<Symbol, int> _allocateRootSlot;

    private readonly Dictionary<Symbol, int> _bindingScopeHints =
        new(ReferenceEqualityComparer<Symbol>.Instance);

    private readonly Dictionary<BlockStatement, int> _blockScopeIds =
        new(ReferenceEqualityComparer<BlockStatement>.Instance);

    private readonly int _entryIndex;
    private readonly List<ExecutionInstruction> _instructions;
    private readonly ImmutableArray<Symbol> _parameterSymbols;
    private readonly Dictionary<int, ScopeSlotInfo> _scopes = new();
    private readonly Stack<int> _scopeStack = new();
    private readonly bool[] _visited;
    private int _nextBlockScopeId = FirstBlockScopeId;

    public ScopeSlotCollector(IEnumerable<ExecutionInstruction> instructions,
        IReadOnlyList<Symbol> existingRootSlots,
        Func<Symbol, int> allocateRootSlot,
        int entryIndex,
        FunctionExpression? function = null)
    {
        _rootScopeId = function?.ScopeId >= 0 ? function.ScopeId : 0;
        _functionBody = function?.Body;
        _allocateRootSlot = allocateRootSlot;
        _instructions = instructions.ToList();
        _entryIndex = entryIndex;
        _visited = new bool[_instructions.Count];
        _parameterSymbols = CollectParameterSymbols(function);
        BuildBindingScopeHints();
        SeedRootScope(existingRootSlots);
        _scopeStack.Push(_rootScopeId);
    }

    private int CurrentScopeId => _scopeStack.TryPeek(out var id) ? id : _rootScopeId;

    public ScopeSlotAnalysis Collect()
    {
        TraverseFrom(_entryIndex);

        var immutableSlotMaps = new Dictionary<int, ImmutableDictionary<Symbol, int>>(
            _scopes.Count);
        var lexicalBindings = new Dictionary<int, ImmutableHashSet<Symbol>>(_scopes.Count);

        foreach (var (scopeId, info) in _scopes)
        {
            immutableSlotMaps[scopeId] = info.ToImmutableSlotMap();
            lexicalBindings[scopeId] =
                info.LexicalBindings.ToImmutableHashSet(ReferenceEqualityComparer<Symbol>.Instance);
        }

        return new ScopeSlotAnalysis(_scopes, immutableSlotMaps, lexicalBindings, _blockScopeIds);
    }

    private void TraverseFrom(int index)
    {
        if (index < 0 || index >= _instructions.Count || _visited[index])
        {
            return;
        }

        VisitInstruction(_instructions[index]);
        _visited[index] = true;

        var scopeSnapshot = _scopeStack.ToArray();
        foreach (var successor in _instructions[index].GetSuccessors())
        {
            RestoreStack(scopeSnapshot);
            TraverseFrom(successor);
        }
    }

    private void RestoreStack(int[] scopeSnapshot)
    {
        _scopeStack.Clear();
        for (var i = scopeSnapshot.Length - 1; i >= 0; i--)
        {
            _scopeStack.Push(scopeSnapshot[i]);
        }
    }

    private void BuildBindingScopeHints()
    {
        foreach (var instruction in _instructions)
        {
            if (instruction is PushEnvironmentInstruction push &&
                !push.PerIterationBindings.IsDefaultOrEmpty)
            {
                foreach (var binding in push.PerIterationBindings)
                {
                    if (!_bindingScopeHints.ContainsKey(binding))
                    {
                        _bindingScopeHints[binding] = push.ScopeId;
                    }

                    GetOrCreateScopeInfo(push.ScopeId).LexicalBindings.Add(binding);
                }
            }
        }
    }

    private void SeedRootScope(IReadOnlyList<Symbol> existingSlots)
    {
        var rootInfo = GetOrCreateScopeInfo(_rootScopeId);
        var seen = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        var index = 0;

        void AppendOrdered(IEnumerable<Symbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                if (seen.Add(symbol))
                {
                    rootInfo.IncludeSlot(symbol, index++);
                }
            }
        }

        AppendOrdered(existingSlots);
        AppendOrdered(_parameterSymbols);

        rootInfo.SlotCountHint = Math.Max(rootInfo.SlotCountHint, index);
        rootInfo.NextSlotIndex = Math.Max(rootInfo.NextSlotIndex, index);
    }

    private ScopeSlotInfo GetOrCreateScopeInfo(int scopeId)
    {
        if (_scopes.TryGetValue(scopeId, out var existing))
        {
            return existing;
        }

        var info = new ScopeSlotInfo(scopeId);
        _scopes[scopeId] = info;
        return info;
    }

    private int AllocateSlotInScope(int scopeId, Symbol symbol)
    {
        var scopeInfo = GetOrCreateScopeInfo(scopeId);
        if (scopeInfo.Slots.TryGetValue(symbol, out var existing))
        {
            return existing;
        }

        var slotIndex = scopeId == _rootScopeId
            ? _allocateRootSlot(symbol)
            : scopeInfo.NextSlotIndex;

        scopeInfo.IncludeSlot(symbol, slotIndex);
        return slotIndex;
    }

    private void EnterScope(int scopeId,
        ImmutableDictionary<Symbol, int> slotMap,
        ImmutableArray<Symbol> perIterationBindings,
        int slotCount,
        BlockStatement? sourceBlock = null)
    {
        var info = GetOrCreateScopeInfo(scopeId);
        if (!slotMap.IsEmpty)
        {
            foreach (var symbol in slotMap.Keys)
            {
                AllocateSlotInScope(scopeId, symbol);
                // Slot maps originate from lexical scopes (loop/function bodies).
                info.LexicalBindings.Add(symbol);
            }
        }

        if (!perIterationBindings.IsDefaultOrEmpty)
        {
            foreach (var binding in perIterationBindings)
            {
                AllocateSlotInScope(scopeId, binding);
                info.LexicalBindings.Add(binding);
            }
        }

        // Register block-to-scopeId mapping if a source block was provided.
        // This enables SlotAssignmentRewriter to stamp the BlockStatement with scope metadata.
        if (sourceBlock is not null && !_blockScopeIds.ContainsKey(sourceBlock))
        {
            _blockScopeIds[sourceBlock] = scopeId;
        }

        if (slotCount > 0)
        {
            info.SlotCountHint = Math.Max(info.SlotCountHint, slotCount);
        }

        _scopeStack.Push(scopeId);
    }

    private void LeaveScope(int scopeId)
    {
        if (_scopeStack.Count <= 1)
        {
            return;
        }

        if (scopeId < 0)
        {
            _scopeStack.Pop();
            return;
        }

        if (_scopeStack.Peek() == scopeId)
        {
            _scopeStack.Pop();
            return;
        }

        // Best-effort cleanup if IR ordering is non-linear: pop until matching scope is removed.
        while (_scopeStack.Count > 1)
        {
            var popped = _scopeStack.Pop();
            if (popped == scopeId)
            {
                break;
            }
        }
    }

    private void CollectBindingTargetSlots(BindingTarget target, int scopeId)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    AllocateSlotInScope(scopeId, identifier.Name);
                    GetOrCreateScopeInfo(scopeId).LexicalBindings.Add(identifier.Name);
                    return;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectBindingTargetSlots(element.Target, scopeId);
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }

                    return;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        CollectBindingTargetSlots(property.Target, scopeId);
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }

                    return;
                default:
                    return;
            }
        }
    }

    public void VisitInstruction(ExecutionInstruction instruction)
    {
        switch (instruction)
        {
            case PushEnvironmentInstruction push:
                EnterScope(push.ScopeId, push.SlotMap, push.PerIterationBindings, push.SlotCount, push.SourceBlock);
                return;

            case PopEnvironmentInstruction pop:
                LeaveScope(pop.ScopeId);
                return;

            case EnterCatchInstruction enterCatch:
                EnterScope(enterCatch.ScopeId, enterCatch.SlotMap, ImmutableArray<Symbol>.Empty, enterCatch.SlotCount);
                if (enterCatch.CatchParameterSymbol is not null)
                {
                    AllocateSlotInScope(enterCatch.ScopeId, enterCatch.CatchParameterSymbol);
                    GetOrCreateScopeInfo(enterCatch.ScopeId).LexicalBindings.Add(enterCatch.CatchParameterSymbol);
                }

                return;

            case EnterCatchWithDestructuringInstruction enterCatchDestructure:
                EnterScope(enterCatchDestructure.ScopeId,
                    enterCatchDestructure.SlotMap,
                    ImmutableArray<Symbol>.Empty,
                    enterCatchDestructure.SlotCount);
                CollectBindingTargetSlots(enterCatchDestructure.BindingPattern, enterCatchDestructure.ScopeId);
                return;

            case StatementInstruction stmt:
                Visit(stmt.Statement);
                return;

            case ExpressionInstruction expr:
                Visit(expr.Expression);
                return;

            case EvaluateAndDiscardInstruction eval:
                Visit(eval.Expression);
                return;

            case YieldInstruction { YieldExpression: not null } yield:
                Visit(yield.YieldExpression);
                return;

            case ReturnInstruction { ReturnExpression: not null } ret:
                Visit(ret.ReturnExpression);
                return;

            case ThrowInstruction thr:
                Visit(thr.Expression);
                return;

            case BranchInstruction branch:
                Visit(branch.Condition);
                return;

            case SimpleVariableDeclarationInstruction { Initializer: not null } varDecl:
                RegisterDeclaration(varDecl);
                Visit(varDecl.Initializer);
                return;

            case SimpleVariableDeclarationInstruction varDecl:
                RegisterDeclaration(varDecl);
                return;

            case IteratorInitInstruction iterInit:
                Visit(iterInit.IterableExpression);
                return;

            case CompoundAssignmentSlotInstruction compoundAssign:
                Visit(compoundAssign.RhsExpression);
                return;

            case EnterWithInstruction enterWith:
                Visit(enterWith.ObjectExpression);
                return;

            case YieldStarInstruction yieldStar:
                Visit(yieldStar.IterableExpression);
                return;
        }
    }

    private void RegisterDeclaration(SimpleVariableDeclarationInstruction varDecl)
    {
        int targetScope;
        if (varDecl.VarKind == VariableKind.Var)
        {
            targetScope = _rootScopeId;
        }
        else
        {
            // Prefer the current lexical scope; fall back to any binding hint we discovered.
            targetScope = CurrentScopeId;
            if (targetScope == _rootScopeId &&
                _bindingScopeHints.TryGetValue(varDecl.TargetSymbol, out var hintedScope))
            {
                targetScope = hintedScope;
            }
        }

        var slotIndex = AllocateSlotInScope(targetScope, varDecl.TargetSymbol);
        if (varDecl.VarKind != VariableKind.Var)
        {
            GetOrCreateScopeInfo(targetScope).LexicalBindings.Add(varDecl.TargetSymbol);
        }
        else
        {
            // Ensure var slots are marked non-lexical
            GetOrCreateScopeInfo(targetScope).LexicalBindings.Remove(varDecl.TargetSymbol);
        }
    }

    protected override void VisitIdentifierExpression(IdentifierExpression node)
    {
        // Compiler-generated identifiers (resume slots, iterator state, etc.) live in the root scope.
        if (node.Name.Name.StartsWith('\u0001'))
        {
            AllocateSlotInScope(_rootScopeId, node.Name);
        }
    }

    protected override void VisitFunctionDeclaration(FunctionDeclaration funcDecl)
    {
        // Function declarations:
        // - If we're inside a nested block scope (stack has more than just root), allocate to block scope
        // - Otherwise, allocate to the function/global scope (hoisted function)
        // Note: _scopeStack always has at least the root scope id (pushed in constructor), so:
        // - Count == 1 means we're at function body level → root scope
        // - Count > 1 means we're in a nested block → block's scope
        var targetScope = _scopeStack.Count > 1 ? _scopeStack.Peek() : _rootScopeId;
        AllocateSlotInScope(targetScope, funcDecl.Name);
        // Do not traverse the nested function body here; nested functions are analyzed separately
    }

    protected override void VisitBlockStatement(BlockStatement block)
    {
        // The function body shares the function's root scope. Do not create a nested
        // block scope for it; allocate its lexical bindings directly in the root.
        if (_functionBody is not null && ReferenceEquals(block, _functionBody))
        {
            var functionBodyHoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();
            if (functionBodyHoistPlan.NeedsEnvironment)
            {
                var rootInfo = GetOrCreateScopeInfo(_rootScopeId);
                foreach (var lexName in functionBodyHoistPlan.TopLevelLexicalNames)
                {
                    AllocateSlotInScope(_rootScopeId, lexName);
                    rootInfo.LexicalBindings.Add(lexName);
                }

                rootInfo.SlotCountHint = Math.Max(rootInfo.SlotCountHint,
                    functionBodyHoistPlan.TopLevelLexicalNames.Count);
            }

            base.VisitBlockStatement(block);
            return;
        }

        // Check if this block needs its own lexical scope (has let/const declarations or functions)
        var hoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();
        if (!hoistPlan.NeedsEnvironment)
        {
            // No lexical bindings - just visit children without creating a scope
            base.VisitBlockStatement(block);
            return;
        }

        // Create a new scope for this nested block
        var blockScopeId = _nextBlockScopeId++;
        _blockScopeIds[block] = blockScopeId;

        // Enter the block scope
        var info = GetOrCreateScopeInfo(blockScopeId);

        // Pre-allocate slots for lexical bindings in this block (using TopLevelLexicalNames
        // to get only direct declarations, not nested ones)
        foreach (var lexName in hoistPlan.TopLevelLexicalNames)
        {
            var slotIndex = info.NextSlotIndex++;
            info.IncludeSlot(lexName, slotIndex);
            info.LexicalBindings.Add(lexName);
        }

        info.SlotCountHint = Math.Max(info.SlotCountHint, hoistPlan.TopLevelLexicalNames.Count);

        _scopeStack.Push(blockScopeId);
        try
        {
            base.VisitBlockStatement(block);
        }
        finally
        {
            _scopeStack.Pop();
        }
    }

    protected override void VisitFunctionExpression(FunctionExpression node)
    {
        // Do not descend into nested function bodies - they're analyzed separately with their own
        // ScopeSlotCollector. Visiting them here would incorrectly create block scopes for their
        // function bodies within the parent function's scope analysis.
    }

    private static ImmutableArray<Symbol> CollectParameterSymbols(FunctionExpression? function)
    {
        if (function is null)
        {
            return ImmutableArray<Symbol>.Empty;
        }

        var list = new List<Symbol>();
        function.CollectParameterNamesFromFunction(list);
        return list.Count == 0 ? ImmutableArray<Symbol>.Empty : [..list];
    }
}

internal sealed class ScopeSlotInfo(int scopeId)
{
    private int _maxSlotIndex = -1;

    public int ScopeId { get; } = scopeId;
    public Dictionary<Symbol, int> Slots { get; } = new(ReferenceEqualityComparer<Symbol>.Instance);
    public HashSet<Symbol> LexicalBindings { get; } = new(ReferenceEqualityComparer<Symbol>.Instance);
    public int SlotCountHint { get; set; }
    public int NextSlotIndex { get; set; }

    public int SlotCount
    {
        get
        {
            var slotCount = _maxSlotIndex + 1;
            return Math.Max(slotCount, SlotCountHint);
        }
    }

    public void IncludeSlot(Symbol symbol, int slotIndex)
    {
        if (!Slots.ContainsKey(symbol))
        {
            Slots[symbol] = slotIndex;
        }

        _maxSlotIndex = Math.Max(_maxSlotIndex, slotIndex);
        NextSlotIndex = Math.Max(NextSlotIndex, slotIndex + 1);
    }

    public ImmutableDictionary<Symbol, int> ToImmutableSlotMap()
    {
        if (Slots.Count == 0)
        {
            return ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
        }

        var builder = ImmutableDictionary.CreateBuilder<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var (symbol, index) in Slots)
        {
            builder[symbol] = index;
        }

        return builder.ToImmutable();
    }
}

internal sealed class ScopeSlotAnalysis(
    Dictionary<int, ScopeSlotInfo> scopes,
    Dictionary<int, ImmutableDictionary<Symbol, int>> immutableSlotMaps,
    Dictionary<int, ImmutableHashSet<Symbol>> lexicalBindings,
    Dictionary<BlockStatement, int> blockScopeIds)
{
    public Dictionary<int, ScopeSlotInfo> Scopes { get; } = scopes;
    public Dictionary<int, ImmutableDictionary<Symbol, int>> ImmutableSlotMaps { get; } = immutableSlotMaps;
    public Dictionary<int, ImmutableHashSet<Symbol>> LexicalBindings { get; } = lexicalBindings;
    public Dictionary<BlockStatement, int> BlockScopeIds { get; } = blockScopeIds;
}
