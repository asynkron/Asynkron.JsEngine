using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Rewriter that stamps IdentifierExpression nodes with ScopeId/SlotIndex using
/// scope-aware slot analysis. Also updates IR instructions to carry finalized
/// slot maps and counts for each scope.
/// </summary>
internal sealed class SlotAssignmentRewriter : AstRewriter
{
    private const int RootScopeId = 0;
    private static readonly bool SlotDebugEnabled = Environment.GetEnvironmentVariable("DEBUG_SLOT") == "1";

    private readonly Dictionary<int, ScopeSlotInfo> _scopes;
    private readonly Dictionary<int, ImmutableDictionary<Symbol, int>> _immutableSlotMaps;
    private readonly Dictionary<int, ImmutableHashSet<Symbol>> _lexicalBindings;
    private readonly Stack<int> _scopeStack = new();
    private readonly Stack<int> _catchScopeStack = new();
    private readonly Dictionary<int, int> _scopeIdRemap = new();
    private readonly Dictionary<int, int> _reverseScopeIdRemap = new();
    private int _nextSyntheticScopeId = 1;

    public SlotAssignmentRewriter(ScopeSlotAnalysis analysis)
    {
        _scopes = analysis.Scopes;
        _immutableSlotMaps = analysis.ImmutableSlotMaps;
        _lexicalBindings = analysis.LexicalBindings;
        _scopeStack.Push(RootScopeId);
    }

    public void RewriteInstructions(IList<ExecutionInstruction> instructions, int entryIndex)
    {
        _scopeStack.Clear();
        _catchScopeStack.Clear();
        _scopeStack.Push(RootScopeId);

        var visited = new bool[instructions.Count];
        RewriteFrom(entryIndex, instructions, visited);
    }

    private void RewriteFrom(int index, IList<ExecutionInstruction> instructions, bool[] visited)
    {
        if (index < 0 || index >= instructions.Count || visited[index])
        {
            return;
        }

        var scopeSnapshot = _scopeStack.ToArray();
        var catchSnapshot = _catchScopeStack.ToArray();

        instructions[index] = RewriteInstruction(instructions[index]);
        visited[index] = true;

        foreach (var successor in GetSuccessors(instructions[index]))
        {
            RestoreStack(scopeSnapshot, catchSnapshot);
            RewriteFrom(successor, instructions, visited);
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

    private void RestoreStack(int[] scopeSnapshot, int[] catchSnapshot)
    {
        _scopeStack.Clear();
        for (var i = scopeSnapshot.Length - 1; i >= 0; i--)
        {
            _scopeStack.Push(scopeSnapshot[i]);
        }

        _catchScopeStack.Clear();
        for (var i = catchSnapshot.Length - 1; i >= 0; i--)
        {
            _catchScopeStack.Push(catchSnapshot[i]);
        }
    }

    public ExecutionInstruction RewriteInstruction(ExecutionInstruction instruction)
    {
        switch (instruction)
        {
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
                _catchScopeStack.Push(mappedCatchScope);
                return updatedCatch;

            case EnterCatchWithDestructuringInstruction enterCatchDestructure:
                var mappedDestructureScope = RemapScopeId(enterCatchDestructure.ScopeId);
                _scopeStack.Push(mappedDestructureScope);
                _catchScopeStack.Push(mappedDestructureScope);
                var updatedDestructure = enterCatchDestructure with
                {
                    ScopeId = mappedDestructureScope,
                    SlotCount = GetSlotCount(mappedDestructureScope),
                    SlotMap = GetSlotMap(mappedDestructureScope)
                };
                return updatedDestructure;

            case LeaveTryInstruction:
                if (_catchScopeStack.Count > 0)
                {
                    var catchScopeId = _catchScopeStack.Pop();
                    LeaveScope(catchScopeId);
                }
                return instruction;

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
                if (SlotDebugEnabled)
                {
                    File.AppendAllText("/tmp/slotdebug.txt",
                        $"BRANCH stack=[{string.Join(",", _scopeStack)}]{Environment.NewLine}");
                }
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
                return compoundAssign with { RhsExpression = Rewrite(compoundAssign.RhsExpression) };

            default:
                return instruction;
        }
    }

    protected override IdentifierExpression RewriteIdentifier(IdentifierExpression node)
    {
        if (TryResolve(node.Name, out var resolution))
        {
            return node with
            {
                ScopeId = resolution.scopeId,
                SlotIndex = resolution.slotIndex
            };
        }

        return node;
    }

    protected override AssignmentExpression RewriteAssignment(AssignmentExpression node)
    {
        if (TryResolve(node.Target, out var resolution))
        {
            return node with
            {
                ScopeDepth = 0,
                ScopeId = resolution.scopeId,
                SlotIndex = resolution.slotIndex,
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

    private int CurrentScopeId => _scopeStack.TryPeek(out var id) ? id : RootScopeId;

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

        while (_scopeStack.Count > 1)
        {
            var popped = _scopeStack.Pop();
            if (popped == scopeId)
            {
                break;
            }
        }
    }

    private int RemapScopeId(int scopeId)
    {
        if (scopeId >= 0)
        {
            return scopeId;
        }

        if (_scopeIdRemap.TryGetValue(scopeId, out var mapped))
        {
            return mapped;
        }

        var newId = _nextSyntheticScopeId++;
        _scopeIdRemap[scopeId] = newId;
        _reverseScopeIdRemap[newId] = scopeId;
        return newId;
    }
}
