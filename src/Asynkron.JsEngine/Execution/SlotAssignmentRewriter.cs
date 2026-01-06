#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Rewriter that stamps IdentifierExpression nodes with ScopeId/SlotIndex using
/// scope-aware slot analysis. Also updates IR instructions to carry finalized
/// slot maps and counts for each scope.
/// </summary>
internal sealed class SlotAssignmentRewriter : AstRewriter
{
    private readonly Dictionary<BlockStatement, int> _blockScopeIds;
    private readonly Stack<int> _tryFrameCatchScopes = new();
    private readonly Dictionary<int, ImmutableDictionary<Symbol, int>> _immutableSlotMaps;
    private readonly Dictionary<int, ImmutableHashSet<Symbol>> _lexicalBindings;
    private readonly Dictionary<int, int> _reverseScopeIdRemap = new();
    private readonly Dictionary<int, int> _scopeIdRemap = new();

    private readonly Dictionary<int, ScopeSlotInfo> _scopes;
    private readonly Stack<int> _scopeStack = new();
    private readonly int _analysisRootScopeId;
    private readonly int _targetRootScopeId;
    private readonly int _mappedRootScopeId;

    /// <summary>
    /// When true, we're re-stamping nested function instructions from an outer context.
    /// In this mode, we skip overwriting identifiers that are already resolved to scopes
    /// not on the current stack (i.e., inner function scopes).
    /// </summary>
    private bool _isRestampingNestedFunction;

    /// <summary>
    /// Maps (scopeId, slotIndex) pairs to flat slot IDs for O(1) variable access.
    /// </summary>
    private readonly Dictionary<(int scopeId, int slotIndex), int> _flatSlotMap = new();

    /// <summary>
    /// Total number of flat slots allocated during rewriting.
    /// </summary>
    public int FlatSlotCount => _flatSlotMap.Count;

    /// <summary>
    /// Builds the flat slot mappings grouped by scope ID for eager initialization.
    /// Returns a dictionary mapping scopeId to array of (slotIndex, flatSlotId) pairs.
    /// </summary>
    public ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> BuildFlatSlotMappings()
    {
        if (_flatSlotMap.Count == 0)
        {
            return ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>.Empty;
        }

        // Group by scopeId
        var grouped = _flatSlotMap
            .GroupBy(kv => kv.Key.scopeId)
            .ToImmutableDictionary(
                g => g.Key,
                g => g.Select(kv => (kv.Key.slotIndex, kv.Value)).ToImmutableArray());

        return grouped;
    }

    public SlotAssignmentRewriter(ScopeSlotAnalysis analysis, int targetRootScopeId, int analysisRootScopeId = 0)
    {
        _analysisRootScopeId = analysisRootScopeId;
        _targetRootScopeId = targetRootScopeId;
        _scopes = analysis.Scopes;
        _immutableSlotMaps = analysis.ImmutableSlotMaps;
        _lexicalBindings = analysis.LexicalBindings;
        _blockScopeIds = analysis.BlockScopeIds;
        _mappedRootScopeId = MapScopeId(_analysisRootScopeId);
        _scopeStack.Push(_mappedRootScopeId);
    }

    public void RewriteInstructions(IList<ExecutionInstruction> instructions, int entryIndex)
    {
        _scopeStack.Clear();
        _tryFrameCatchScopes.Clear();
        _scopeStack.Push(_mappedRootScopeId);

        var visited = new bool[instructions.Count];
        RewriteFrom(entryIndex, instructions, visited);
    }

    private void RewriteFrom(int index, IList<ExecutionInstruction> instructions, bool[] visited)
    {
        if (index < 0 || index >= instructions.Count || visited[index])
        {
            return;
        }

        instructions[index] = RewriteInstruction(instructions[index]);
        visited[index] = true;

        var scopeSnapshot = _scopeStack.ToArray();
        var tryFrameSnapshot = _tryFrameCatchScopes.ToArray();

        foreach (var successor in instructions[index].GetSuccessors())
        {
            RestoreStack(scopeSnapshot, tryFrameSnapshot);
            RewriteFrom(successor, instructions, visited);
        }
    }

    private void RestoreStack(int[] scopeSnapshot, int[] tryFrameSnapshot)
    {
        _scopeStack.Clear();
        for (var i = scopeSnapshot.Length - 1; i >= 0; i--)
        {
            _scopeStack.Push(scopeSnapshot[i]);
        }

        _tryFrameCatchScopes.Clear();
        for (var i = tryFrameSnapshot.Length - 1; i >= 0; i--)
        {
            _tryFrameCatchScopes.Push(tryFrameSnapshot[i]);
        }
    }

    /// <summary>
    /// Maps a (possibly negative) scope id to the rewritten id used in instructions.
    /// Exposed so we can stamp iterator plan bodies with consistent scope ids.
    /// </summary>
    public int MapScopeId(int scopeId)
    {
        return RemapScopeId(scopeId);
    }

    /// <summary>
    /// Stamps an arbitrary AST node with slot metadata using the current slot analysis.
    /// The node is visited in the context of the provided scope (pushed on top of the stack)
    /// and the rewritten, stamped node is returned.
    /// </summary>
    public T StampNodeInScope<T>(T node, int scopeId) where T : AstNode
    {
        var scopeSnapshot = _scopeStack.ToArray();
        var tryFrameSnapshot = _tryFrameCatchScopes.ToArray();
        _scopeStack.Clear();
        _scopeStack.Push(_mappedRootScopeId);
        if (scopeId != _mappedRootScopeId)
        {
            _scopeStack.Push(scopeId);
        }

        var rewritten = node switch
        {
            StatementNode stmt => (T)(AstNode)Rewrite(stmt),
            ExpressionNode expr => (T)(AstNode)Rewrite(expr),
            _ => node
        };
        RestoreStack(scopeSnapshot, tryFrameSnapshot);
        return rewritten;
    }

    public bool TryResolveSlot(Symbol symbol, int mappedScopeId, out int slotIndex)
    {
        var scopeSnapshot = _scopeStack.ToArray();
        var tryFrameSnapshot = _tryFrameCatchScopes.ToArray();
        _scopeStack.Clear();
        _scopeStack.Push(_mappedRootScopeId);
        if (mappedScopeId != _mappedRootScopeId)
        {
            _scopeStack.Push(mappedScopeId);
        }

        var found = TryResolve(symbol, out var resolution) && resolution.slotIndex >= 0;
        slotIndex = found ? resolution.slotIndex : -1;
        RestoreStack(scopeSnapshot, tryFrameSnapshot);
        return found;
    }

    /// <summary>
    /// Stamps an instruction with slot metadata from the enclosing scope.
    /// Used to stamp nested function execution plan instructions with outer scope slot info.
    /// </summary>
    public ExecutionInstruction StampInstructionInScope(ExecutionInstruction instruction, int enclosingScopeId)
    {
        var scopeSnapshot = _scopeStack.ToArray();
        var tryFrameSnapshot = _tryFrameCatchScopes.ToArray();
        _scopeStack.Clear();
        _scopeStack.Push(_mappedRootScopeId);
        if (enclosingScopeId != _mappedRootScopeId)
        {
            _scopeStack.Push(enclosingScopeId);
        }

        // Set the flag to indicate we're re-stamping nested function instructions.
        // This prevents overwriting identifiers already resolved to inner scopes.
        _isRestampingNestedFunction = true;
        try
        {
            var result = RewriteInstruction(instruction);
            return result;
        }
        finally
        {
            _isRestampingNestedFunction = false;
            RestoreStack(scopeSnapshot, tryFrameSnapshot);
        }
    }

    public int GetSlotCountForScope(int mappedScopeId)
    {
        return GetSlotCount(mappedScopeId);
    }

    public ExecutionInstruction RewriteInstruction(ExecutionInstruction instruction)
    {
        switch (instruction)
        {
            case EnterTryInstruction:
                _tryFrameCatchScopes.Push(-1);
                return instruction;

            case PushEnvironmentInstruction push:
                var mappedPushScope = RemapScopeId(push.ScopeId);
                var lexical = GetLexicalBindings(mappedPushScope);
                var updatedPush = push with
                {
                    ScopeId = mappedPushScope,
                    SlotCount = GetSlotCount(mappedPushScope),
                    SlotMap = GetSlotMap(mappedPushScope),
                    LexicalBindings = lexical
                };
                _scopeStack.Push(mappedPushScope);
                return updatedPush;

            case PopEnvironmentInstruction pop:
                var mappedPopScope = RemapScopeId(pop.ScopeId);
                LeaveScope(mappedPopScope);
                if (_tryFrameCatchScopes.Count > 0 && _tryFrameCatchScopes.Peek() == mappedPopScope)
                {
                    _tryFrameCatchScopes.Pop();
                    _tryFrameCatchScopes.Push(-1);
                }
                return pop with { ScopeId = mappedPopScope };

            case EnterCatchInstruction enterCatch:
                var mappedCatchScope = RemapScopeId(enterCatch.ScopeId);
                var updatedCatch = enterCatch with
                {
                    ScopeId = mappedCatchScope,
                    SlotCount = GetSlotCount(mappedCatchScope),
                    SlotMap = GetSlotMap(mappedCatchScope)
                };
                _scopeStack.Push(mappedCatchScope);
                if (_tryFrameCatchScopes.Count > 0)
                {
                    _tryFrameCatchScopes.Pop();
                    _tryFrameCatchScopes.Push(mappedCatchScope);
                }
                return updatedCatch;

            case EnterCatchWithDestructuringInstruction enterCatchDestructure:
                var mappedDestructureScope = RemapScopeId(enterCatchDestructure.ScopeId);
                _scopeStack.Push(mappedDestructureScope);
                if (_tryFrameCatchScopes.Count > 0)
                {
                    _tryFrameCatchScopes.Pop();
                    _tryFrameCatchScopes.Push(mappedDestructureScope);
                }
                var updatedDestructure = enterCatchDestructure with
                {
                    ScopeId = mappedDestructureScope,
                    SlotCount = GetSlotCount(mappedDestructureScope),
                    SlotMap = GetSlotMap(mappedDestructureScope)
                };
                return updatedDestructure;

            case LeaveTryInstruction:
                if (_tryFrameCatchScopes.Count > 0)
                {
                    var catchScopeId = _tryFrameCatchScopes.Pop();
                    var isOnStack = false;
                    foreach (var scopeId in _scopeStack)
                    {
                        if (scopeId == catchScopeId)
                        {
                            isOnStack = true;
                            break;
                        }
                    }

                    if (isOnStack)
                    {
                        LeaveScope(catchScopeId);
                    }
                }

                return instruction;

            case BreakInstruction breakInstruction:
                if (breakInstruction.TargetScopeId < 0)
                {
                    return breakInstruction;
                }

                var mappedBreakScope = RemapScopeId(breakInstruction.TargetScopeId);
                PopToScope(mappedBreakScope);
                return breakInstruction with { TargetScopeId = mappedBreakScope };

            case ContinueInstruction continueInstruction:
                if (continueInstruction.TargetScopeId < 0)
                {
                    return continueInstruction;
                }

                var mappedContinueScope = RemapScopeId(continueInstruction.TargetScopeId);
                PopToScope(mappedContinueScope);
                return continueInstruction with { TargetScopeId = mappedContinueScope };

            case StatementInstruction stmt:
                return stmt with { Statement = Rewrite(stmt.Statement) };

            case ExpressionInstruction expr:
                return expr with { Expression = Rewrite(expr.Expression) };

            case EvaluateAndDiscardInstruction eval:
                return eval with { Expression = Rewrite(eval.Expression) };

            case YieldInstruction { YieldExpression: not null } yield:
                return yield with { YieldExpression = Rewrite(yield.YieldExpression) };

            case ReturnInstruction { ReturnExpression: not null } ret:
                return ret with { ReturnExpression = Rewrite(ret.ReturnExpression) };

            case ThrowInstruction thr:
                return thr with { Expression = Rewrite(thr.Expression) };

            case BranchInstruction branch:
                return branch with { Condition = Rewrite(branch.Condition) };

            case SimpleVariableDeclarationInstruction { Initializer: not null } varDecl:
                return varDecl with { Initializer = Rewrite(varDecl.Initializer) };

            case IteratorInitInstruction iterInit:
                return iterInit with { IterableExpression = Rewrite(iterInit.IterableExpression) };

            case EnterWithInstruction enterWith:
                return enterWith with { ObjectExpression = Rewrite(enterWith.ObjectExpression) };

            case YieldStarInstruction yieldStar:
                return yieldStar with { IterableExpression = Rewrite(yieldStar.IterableExpression) };

            case CompoundAssignmentSlotInstruction compoundAssign:
                {
                    // Rewrite the RHS expression first to resolve any identifiers
                    var rewrittenRhs = Rewrite(compoundAssign.RhsExpression);
                    // Extract RhsFlatSlotId if RHS is an identifier with a flat slot
                    var rhsFlatSlotId = rewrittenRhs is IdentifierExpression { FlatSlotId: >= 0 } rhsIdent
                        ? rhsIdent.FlatSlotId
                        : -1;

                    // Resolve the target symbol to get scope/slot/flatSlot metadata
                    if (TryResolve(compoundAssign.TargetSymbol, out var compoundResolution))
                    {
                        var compoundFlatSlotId = GetOrCreateFlatSlotId(compoundResolution.scopeId, compoundResolution.slotIndex);
                        return compoundAssign with
                        {
                            RhsExpression = rewrittenRhs,
                            ScopeId = compoundResolution.scopeId,
                            SlotIndex = compoundResolution.slotIndex,
                            FlatSlotId = compoundFlatSlotId,
                            RhsFlatSlotId = rhsFlatSlotId
                        };
                    }
                    return compoundAssign with { RhsExpression = rewrittenRhs, RhsFlatSlotId = rhsFlatSlotId };
                }

            case IncrementSlotInstruction increment:
                {
                    // Resolve the target symbol to get scope/slot/flatSlot metadata
                    if (TryResolve(increment.TargetSymbol, out var incrementResolution))
                    {
                        var incrementFlatSlotId = GetOrCreateFlatSlotId(incrementResolution.scopeId, incrementResolution.slotIndex);
                        return increment with
                        {
                            ScopeId = incrementResolution.scopeId,
                            SlotIndex = incrementResolution.slotIndex,
                            FlatSlotId = incrementFlatSlotId
                        };
                    }
                    return increment;
                }

            default:
                return instruction;
        }
    }

    protected override StatementNode RewriteStatement(StatementNode statement)
    {
        if (statement is BlockStatement block)
        {
            var hoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();

            // Fallback: if scope analysis missed this block, synthesize a scope so shadowed lets
            // get their own slots instead of reusing the parent scope.
            if (!_blockScopeIds.ContainsKey(block) && hoistPlan.NeedsEnvironment)
            {
                var syntheticScopeId = SyntheticScopeIdAllocator.Next();
                var scopeInfo = new ScopeSlotInfo(syntheticScopeId);
                var slotIndex = 0;
                foreach (var lexName in hoistPlan.TopLevelLexicalNames)
                {
                    scopeInfo.IncludeSlot(lexName, slotIndex++);
                    scopeInfo.LexicalBindings.Add(lexName);
                }

                scopeInfo.SlotCountHint = Math.Max(scopeInfo.SlotCountHint, slotIndex);
                _blockScopeIds[block] = syntheticScopeId;
                _scopes[syntheticScopeId] = scopeInfo;
                _immutableSlotMaps[syntheticScopeId] = scopeInfo.ToImmutableSlotMap();
                _lexicalBindings[syntheticScopeId] = scopeInfo.LexicalBindings
                    .ToImmutableHashSet(ReferenceEqualityComparer<Symbol>.Instance);
            }

            if (_blockScopeIds.TryGetValue(block, out var scopeId))
            {
                var mappedScopeId = RemapScopeId(scopeId);
                // Push the block's scope onto the stack for rewriting children
                _scopeStack.Push(mappedScopeId);
                try
                {
                    // Rewrite children in this scope
                    var rewrittenStatements = RewriteStatementList(block.Statements);

                    // Stamp the block with its scope metadata
                    return block with
                    {
                        Statements = rewrittenStatements,
                        ScopeId = mappedScopeId,
                        SlotCount = GetSlotCount(mappedScopeId),
                        SlotMap = GetSlotMap(mappedScopeId)
                    };
                }
                finally
                {
                    _scopeStack.Pop();
                }
            }
        }

        // Handle BlockStatement specially to stamp with scope metadata
        return base.RewriteStatement(statement);
    }

    protected override IdentifierExpression RewriteIdentifier(IdentifierExpression node)
    {
        // When re-stamping nested function instructions, skip identifiers already resolved
        // to inner scopes (not on the current stack). This prevents outer scope re-stamping
        // from overwriting correctly resolved inner scope bindings.
        if (_isRestampingNestedFunction && node.ScopeId >= 0 && node.SlotIndex >= 0)
        {
            // Check if the identifier's scope is on the current stack
            var isOnStack = false;
            foreach (var scopeId in _scopeStack)
            {
                if (scopeId == node.ScopeId)
                {
                    isOnStack = true;
                    break;
                }
            }

            // If resolved to a scope not on our stack, it's an inner scope - leave it alone
            if (!isOnStack)
            {
                return node;
            }
        }

        if (TryResolve(node.Name, out var resolution))
        {
            var flatSlotId = GetOrCreateFlatSlotId(resolution.scopeId, resolution.slotIndex);
            return node with
            {
                ScopeId = resolution.scopeId,
                SlotIndex = resolution.slotIndex,
                FlatSlotId = flatSlotId
            };
        }

        return node;
    }

    /// <summary>
    /// Gets or creates a flat slot ID for the given (scopeId, slotIndex) pair.
    /// Flat slot IDs are assigned in order of first encounter during rewriting.
    /// </summary>
    private int GetOrCreateFlatSlotId(int scopeId, int slotIndex)
    {
        var key = (scopeId, slotIndex);
        if (_flatSlotMap.TryGetValue(key, out var flatSlotId))
        {
            return flatSlotId;
        }

        flatSlotId = _flatSlotMap.Count;
        _flatSlotMap[key] = flatSlotId;
        return flatSlotId;
    }

    protected override AssignmentExpression RewriteAssignment(AssignmentExpression node)
    {
        if (TryResolve(node.Target, out var resolution))
        {
            var flatSlotId = GetOrCreateFlatSlotId(resolution.scopeId, resolution.slotIndex);
            return node with
            {
                ScopeDepth = 0,
                ScopeId = resolution.scopeId,
                SlotIndex = resolution.slotIndex,
                FlatSlotId = flatSlotId,
                Value = RewriteExpression(node.Value)
            };
        }

        return base.RewriteAssignment(node);
    }

    private (int scopeId, int slotIndex) ResolveInScope(Symbol symbol, int scopeId)
    {
        var lookupScopeId = _reverseScopeIdRemap.TryGetValue(scopeId, out var original)
            ? original
            : scopeId;

        if (_scopes.TryGetValue(lookupScopeId, out var info) &&
            info.Slots.TryGetValue(symbol, out var index))
        {
            return (scopeId, index);
        }

        return (-1, -1);
    }

    private bool TryResolve(Symbol symbol, out (int scopeId, int slotIndex) resolution)
    {
        foreach (var scopeId in _scopeStack)
        {
            var candidate = ResolveInScope(symbol, scopeId);
            if (candidate.scopeId >= 0 && candidate.slotIndex >= 0)
            {
                resolution = candidate;
                return true;
            }
        }

        foreach (var scope in _scopes)
        {
            if (scope.Value.Slots.TryGetValue(symbol, out var slotIndex))
            {
                var mappedScope = RemapScopeId(scope.Key);
                resolution = (mappedScope, slotIndex);
                return true;
            }
        }

        resolution = default;
        return false;
    }

    private ImmutableDictionary<Symbol, int> GetSlotMap(int scopeId)
    {
        var lookupScopeId = _reverseScopeIdRemap.TryGetValue(scopeId, out var original)
            ? original
            : scopeId;

        return _immutableSlotMaps.TryGetValue(lookupScopeId, out var map)
            ? map
            : ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
    }

    private int GetSlotCount(int scopeId)
    {
        var lookupScopeId = _reverseScopeIdRemap.TryGetValue(scopeId, out var original)
            ? original
            : scopeId;

        return _scopes.TryGetValue(lookupScopeId, out var info) ? info.SlotCount : 0;
    }

    private ImmutableHashSet<Symbol> GetLexicalBindings(int scopeId)
    {
        var lookupScopeId = _reverseScopeIdRemap.TryGetValue(scopeId, out var original)
            ? original
            : scopeId;

        return _lexicalBindings.TryGetValue(lookupScopeId, out var set)
            ? set
            : ImmutableHashSet<Symbol>.Empty.WithComparer(ReferenceEqualityComparer<Symbol>.Instance);
    }

    private void LeaveScope(int scopeId)
    {
        if (_scopeStack.Count <= 1)
        {
            return;
        }

        if (_scopeStack.Peek() == scopeId)
        {
            _scopeStack.Pop();
            return;
        }

        while (_scopeStack.Count > 1)
        {
            var popped = _scopeStack.Pop();
            if (popped == scopeId)
            {
                break;
            }
        }
    }

    private void PopToScope(int targetScopeId)
    {
        if (targetScopeId < 0 || _scopeStack.Count <= 1)
        {
            return;
        }

        while (_scopeStack.Count > 1 && _scopeStack.Peek() != targetScopeId)
        {
            _scopeStack.Pop();
        }
    }

    private int RemapScopeId(int scopeId)
    {
        if (_scopeIdRemap.TryGetValue(scopeId, out var mapped))
        {
            return mapped;
        }

        var mappedId = scopeId == _analysisRootScopeId
            ? _targetRootScopeId
            : SyntheticScopeIdAllocator.Next();

        _scopeIdRemap[scopeId] = mappedId;
        _reverseScopeIdRemap[mappedId] = scopeId;
        return mappedId;
    }
}
