using System.Collections.Immutable;
using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

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
    private const int RootScopeId = 0;

    private readonly Func<Symbol, int> _allocateRootSlot;
    private readonly ImmutableArray<Symbol> _parameterSymbols;
    private readonly Dictionary<Symbol, int> _bindingScopeHints =
        new(ReferenceEqualityComparer<Symbol>.Instance);
    private readonly List<ExecutionInstruction> _instructions;
    private readonly int _entryIndex;
    private readonly bool[] _visited;
    private readonly Dictionary<int, ScopeSlotInfo> _scopes = new();
    private readonly Stack<int> _scopeStack = new();

    public ScopeSlotCollector(IEnumerable<ExecutionInstruction> instructions,
        IReadOnlyList<Symbol> existingRootSlots,
        Func<Symbol, int> allocateRootSlot,
        int entryIndex,
        FunctionExpression? function = null)
    {
        _allocateRootSlot = allocateRootSlot;
        _instructions = instructions.ToList();
        _entryIndex = entryIndex;
        _visited = new bool[_instructions.Count];
        _parameterSymbols = CollectParameterSymbols(function);
        BuildBindingScopeHints();
        SeedRootScope(existingRootSlots);
        _scopeStack.Push(RootScopeId);
    }

    public ScopeSlotAnalysis Collect()
    {
        TraverseFrom(_entryIndex);

        var immutableSlotMaps = new Dictionary<int, ImmutableDictionary<Symbol, int>>(
            _scopes.Count);
        var lexicalBindings = new Dictionary<int, ImmutableHashSet<Symbol>>(_scopes.Count);

        foreach (var (scopeId, info) in _scopes)
        {
            immutableSlotMaps[scopeId] = info.ToImmutableSlotMap();
            lexicalBindings[scopeId] = info.LexicalBindings.ToImmutableHashSet(ReferenceEqualityComparer<Symbol>.Instance);
        }

        return new ScopeSlotAnalysis(_scopes, immutableSlotMaps, lexicalBindings);
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
        foreach (var successor in GetSuccessors(_instructions[index]))
        {
            RestoreStack(scopeSnapshot);
            TraverseFrom(successor);
        }
    }

    private static IEnumerable<int> GetSuccessors(ExecutionInstruction instruction)
    {
        switch (instruction)
        {
            case BranchInstruction branch:
                yield return branch.ConsequentIndex;
                yield return branch.AlternateIndex;
                yield break;
            case JumpInstruction jump:
                yield return jump.TargetIndex;
                yield break;
            default:
                if (instruction.Next >= 0)
                {
                    yield return instruction.Next;
                }

                yield break;
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
        var rootInfo = GetOrCreateScopeInfo(RootScopeId);
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

    private int CurrentScopeId => _scopeStack.TryPeek(out var id) ? id : RootScopeId;

    private int AllocateSlotInScope(int scopeId, Symbol symbol)
    {
        var scopeInfo = GetOrCreateScopeInfo(scopeId);
        if (scopeInfo.Slots.TryGetValue(symbol, out var existing))
        {
            return existing;
        }

        var slotIndex = scopeId == RootScopeId
            ? _allocateRootSlot(symbol)
            : scopeInfo.NextSlotIndex;

        scopeInfo.IncludeSlot(symbol, slotIndex);
        return slotIndex;
    }

    private void EnterScope(int scopeId,
        ImmutableDictionary<Symbol, int> slotMap,
        ImmutableArray<Symbol> perIterationBindings,
        int slotCount)
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
                EnterScope(push.ScopeId, push.SlotMap, push.PerIterationBindings, push.SlotCount);
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
            targetScope = RootScopeId;
        }
        else
        {
            // Prefer the current lexical scope; fall back to any binding hint we discovered.
            targetScope = CurrentScopeId;
            if (targetScope == RootScopeId &&
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

    protected override void VisitIdentifier(IdentifierExpression node)
    {
        // Compiler-generated identifiers (resume slots, iterator state, etc.) live in the root scope.
        if (node.Name.Name.StartsWith('\u0001'))
        {
            AllocateSlotInScope(RootScopeId, node.Name);
        }
    }

    protected override void VisitStatement(StatementNode statement)
    {
        if (statement is FunctionDeclaration funcDecl)
        {
            // Function declarations are hoisted to the function/global scope.
            AllocateSlotInScope(RootScopeId, funcDecl.Name);
            return;
        }

        base.VisitStatement(statement);
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

internal sealed class ScopeSlotInfo
{
    private int _maxSlotIndex = -1;

    public ScopeSlotInfo(int scopeId)
    {
        ScopeId = scopeId;
        Slots = new Dictionary<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
        LexicalBindings = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
    }

    public int ScopeId { get; }
    public Dictionary<Symbol, int> Slots { get; }
    public HashSet<Symbol> LexicalBindings { get; }
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

internal sealed class ScopeSlotAnalysis
{
    public ScopeSlotAnalysis(
        Dictionary<int, ScopeSlotInfo> scopes,
        Dictionary<int, ImmutableDictionary<Symbol, int>> immutableSlotMaps,
        Dictionary<int, ImmutableHashSet<Symbol>> lexicalBindings)
    {
        Scopes = scopes;
        ImmutableSlotMaps = immutableSlotMaps;
        LexicalBindings = lexicalBindings;
    }

    public Dictionary<int, ScopeSlotInfo> Scopes { get; }
    public Dictionary<int, ImmutableDictionary<Symbol, int>> ImmutableSlotMaps { get; }
    public Dictionary<int, ImmutableHashSet<Symbol>> LexicalBindings { get; }
}
