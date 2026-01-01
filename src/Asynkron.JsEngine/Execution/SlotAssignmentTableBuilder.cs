using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Builds lookup tables that map identifier/assignment nodes to slot metadata.
/// Traverses the IR control flow so scope pushes/pops match runtime behavior.
/// </summary>
internal sealed class SlotAssignmentTableBuilder : AstVisitor
{
    private static readonly ImmutableDictionary<Symbol, int> EmptySlotMap =
        ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);

    private readonly int _entryIndex;
    private readonly IReadOnlyList<ExecutionInstruction> _instructions;
    private readonly Dictionary<int, ImmutableDictionary<Symbol, int>> _scopeSlotMaps;
    private readonly Dictionary<Symbol, int> _planSlotIndices;
    private readonly HashSet<Symbol> _functionSymbols;
    private readonly Func<Symbol, int> _allocateSlot;
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
    private ImmutableArray<ScopeFrame> _currentScopes = ImmutableArray<ScopeFrame>.Empty;

    internal SlotAssignmentTableBuilder(
        int entryIndex,
        IReadOnlyList<ExecutionInstruction> instructions,
        Dictionary<int, ImmutableDictionary<Symbol, int>> scopeSlotMaps,
        Dictionary<Symbol, int> planSlotIndices,
        Func<Symbol, int> allocateSlot)
    {
        _entryIndex = entryIndex;
        _instructions = instructions;
        _scopeSlotMaps = scopeSlotMaps;
        _planSlotIndices = planSlotIndices;
        _allocateSlot = allocateSlot;
        _functionSymbols = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
    }

    internal Dictionary<IdentifierExpression, (int scopeId, int slotIndex)> IdentifierSlots { get; } =
        new(ReferenceEqualityComparer<IdentifierExpression>.Instance);

    internal Dictionary<AssignmentExpression, (int scopeId, int slotIndex)> AssignmentSlots { get; } =
        new(ReferenceEqualityComparer<AssignmentExpression>.Instance);

    internal void Build()
    {
        CollectFunctionScopeSymbols();
        _visited.Clear();
        Traverse(_entryIndex, ImmutableArray<ScopeFrame>.Empty);
    }

    internal static Dictionary<int, ImmutableDictionary<Symbol, int>> CollectScopeSlotMaps(
        IEnumerable<ExecutionInstruction> instructions)
    {
        var scopeSlotMaps = new Dictionary<int, ImmutableDictionary<Symbol, int>>();
        foreach (var instruction in instructions)
        {
            switch (instruction)
            {
                case PushEnvironmentInstruction pushEnv when pushEnv.ScopeId >= 0:
                    scopeSlotMaps[pushEnv.ScopeId] = pushEnv.SlotMap;
                    break;
                case EnterCatchInstruction enterCatch when enterCatch.ScopeId >= 0:
                    scopeSlotMaps[enterCatch.ScopeId] = enterCatch.SlotMap;
                    break;
                case EnterCatchWithDestructuringInstruction enterCatch when enterCatch.ScopeId >= 0:
                    scopeSlotMaps[enterCatch.ScopeId] = enterCatch.SlotMap;
                    break;
            }
        }

        return scopeSlotMaps;
    }

    protected override void VisitIdentifier(IdentifierExpression node)
    {
        var slotInfo = ResolveSymbol(node.Name);
        if (slotInfo.HasValue)
        {
            IdentifierSlots[node] = slotInfo.Value;
        }
    }

    protected override void VisitAssignment(AssignmentExpression node)
    {
        var slotInfo = ResolveSymbol(node.Target);
        if (slotInfo.HasValue)
        {
            AssignmentSlots[node] = slotInfo.Value;
        }
        base.VisitAssignment(node);
    }

    private void CollectFunctionScopeSymbols()
    {
        Traverse(
            _entryIndex,
            ImmutableArray<ScopeFrame>.Empty,
            instruction =>
            {
                if (instruction is SimpleVariableDeclarationInstruction varDecl)
                {
                    var isFunctionScope =
                        varDecl.VarKind == VariableKind.Var || _currentScopes.IsDefaultOrEmpty;
                    if (isFunctionScope)
                    {
                        _functionSymbols.Add(varDecl.TargetSymbol);
                    }
                }
                else if (instruction is StatementInstruction stmt)
                {
                    CollectFunctionScopeSymbolsFromStatement(stmt.Statement);
                }
            });
    }

    private void Traverse(int index, ImmutableArray<ScopeFrame> scopes, Action<ExecutionInstruction>? observer = null)
    {
        if (index < 0 || index >= _instructions.Count)
        {
            return;
        }

        var key = BuildVisitKey(index, scopes);
        if (!_visited.Add(key))
        {
            return;
        }

        _currentScopes = scopes;
        var instruction = _instructions[index];
        observer?.Invoke(instruction);

        switch (instruction)
        {
            case PushEnvironmentInstruction pushEnv:
            {
                var nextScopes = scopes;
                if (pushEnv.ScopeId >= 0)
                {
                    var slotMap = pushEnv.SlotMap.IsEmpty ? GetScopeSlotMap(pushEnv.ScopeId) : pushEnv.SlotMap;
                    nextScopes = nextScopes.Add(new ScopeFrame(pushEnv.ScopeId, slotMap));
                }
                Traverse(pushEnv.Next, nextScopes, observer);
                break;
            }
            case PopEnvironmentInstruction popEnv:
            {
                var nextScopes = PopScope(scopes, popEnv.ScopeId);
                Traverse(popEnv.Next, nextScopes, observer);
                break;
            }
            case EnterCatchInstruction enterCatch:
            {
                var nextScopes = scopes;
                if (enterCatch.ScopeId >= 0)
                {
                    nextScopes = nextScopes.Add(new ScopeFrame(enterCatch.ScopeId, enterCatch.SlotMap));
                }
                Traverse(enterCatch.Next, nextScopes, observer);
                break;
            }
            case EnterCatchWithDestructuringInstruction enterCatch:
            {
                var nextScopes = scopes;
                if (enterCatch.ScopeId >= 0)
                {
                    nextScopes = nextScopes.Add(new ScopeFrame(enterCatch.ScopeId, enterCatch.SlotMap));
                }
                Traverse(enterCatch.Next, nextScopes, observer);
                break;
            }
            case BranchInstruction branch:
                Visit(branch.Condition);
                Traverse(branch.ConsequentIndex, scopes, observer);
                Traverse(branch.AlternateIndex, scopes, observer);
                break;
            case JumpInstruction jump:
                Traverse(jump.TargetIndex, scopes, observer);
                break;
            case BreakInstruction brk:
            {
                var nextScopes = PopScope(scopes, brk.TargetScopeId);
                Traverse(brk.TargetIndex, nextScopes, observer);
                break;
            }
            case ContinueInstruction cont:
            {
                var nextScopes = PopScope(scopes, cont.TargetScopeId);
                Traverse(cont.TargetIndex, nextScopes, observer);
                break;
            }
            case StatementInstruction stmt:
                Visit(stmt.Statement);
                Traverse(stmt.Next, scopes, observer);
                break;
            case ExpressionInstruction expr:
                Visit(expr.Expression);
                Traverse(expr.Next, scopes, observer);
                break;
            case EvaluateAndDiscardInstruction eval:
                Visit(eval.Expression);
                Traverse(eval.Next, scopes, observer);
                break;
            case ReturnInstruction ret when ret.ReturnExpression is not null:
                Visit(ret.ReturnExpression);
                break;
            case ThrowInstruction thr:
                Visit(thr.Expression);
                break;
            case YieldInstruction yield when yield.YieldExpression is not null:
                Visit(yield.YieldExpression);
                Traverse(yield.Next, scopes, observer);
                break;
            case YieldStarInstruction yieldStar:
                Visit(yieldStar.IterableExpression);
                Traverse(yieldStar.Next, scopes, observer);
                break;
            case SimpleVariableDeclarationInstruction varDecl when varDecl.Initializer is not null:
                Visit(varDecl.Initializer);
                Traverse(varDecl.Next, scopes, observer);
                break;
            case IteratorInitInstruction iterInit:
                Visit(iterInit.IterableExpression);
                Traverse(iterInit.Next, scopes, observer);
                break;
            case EnterWithInstruction enterWith:
                Visit(enterWith.ObjectExpression);
                Traverse(enterWith.Next, scopes, observer);
                break;
            case CompoundAssignmentSlotInstruction compoundAssign:
                Visit(compoundAssign.RhsExpression);
                Traverse(compoundAssign.Next, scopes, observer);
                break;
            case BinaryOpInstruction binaryOp:
                Visit(binaryOp.Left);
                Visit(binaryOp.Right);
                Traverse(binaryOp.Next, scopes, observer);
                break;
            case IncrementSlotInstruction increment:
                Traverse(increment.Next, scopes, observer);
                break;
            case ReturnInstruction:
            case ThrowInstruction:
                break;
            case ExecutionInstruction nextInstruction:
                Traverse(nextInstruction.Next, scopes, observer);
                break;
        }
    }

    private ImmutableArray<ScopeFrame> PopScope(ImmutableArray<ScopeFrame> scopes, int scopeId)
    {
        if (scopes.IsDefaultOrEmpty)
        {
            return scopes;
        }

        if (scopeId < 0)
        {
            return scopes;
        }

        var index = scopes.Length - 1;
        while (index >= 0 && scopes[index].ScopeId != scopeId)
        {
            index--;
        }

        return index >= 0 ? scopes.RemoveRange(index, scopes.Length - index) : scopes;
    }

    private ImmutableDictionary<Symbol, int> GetScopeSlotMap(int scopeId)
    {
        return scopeId >= 0 && _scopeSlotMaps.TryGetValue(scopeId, out var slotMap)
            ? slotMap
            : EmptySlotMap;
    }

    private (int scopeId, int slotIndex)? ResolveSymbol(Symbol symbol)
    {
        for (var i = _currentScopes.Length - 1; i >= 0; i--)
        {
            var scope = _currentScopes[i];
            if (scope.SlotMap.TryGetValue(symbol, out var slotIndex))
            {
                return (scope.ScopeId, slotIndex);
            }
        }

        if (_planSlotIndices.TryGetValue(symbol, out var planSlot))
        {
            return (0, planSlot);
        }

        if (_functionSymbols.Contains(symbol))
        {
            var slotIndex = _allocateSlot(symbol);
            _planSlotIndices[symbol] = slotIndex;
            return (0, slotIndex);
        }

        return null;
    }

    private string BuildVisitKey(int index, ImmutableArray<ScopeFrame> scopes)
    {
        if (scopes.IsDefaultOrEmpty)
        {
            return $"{index}|";
        }

        var builder = new System.Text.StringBuilder();
        builder.Append(index);
        builder.Append('|');
        for (var i = 0; i < scopes.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }
            builder.Append(scopes[i].ScopeId);
        }
        return builder.ToString();
    }

    private void CollectFunctionScopeSymbolsFromStatement(StatementNode statement)
    {
        switch (statement)
        {
            case VariableDeclaration varDecl:
                foreach (var declarator in varDecl.Declarators)
                {
                    CollectFunctionScopeSymbolsFromBinding(declarator.Target, varDecl.Kind);
                }
                break;
            case BlockStatement block:
                foreach (var stmt in block.Statements)
                {
                    CollectFunctionScopeSymbolsFromStatement(stmt);
                }
                break;
            case ForStatement forStatement:
                if (forStatement.Initializer is VariableDeclaration forDecl)
                {
                    foreach (var declarator in forDecl.Declarators)
                    {
                        CollectFunctionScopeSymbolsFromBinding(declarator.Target, forDecl.Kind);
                    }
                }
                CollectFunctionScopeSymbolsFromStatement(forStatement.Body);
                break;
            case ForEachStatement forEachStatement:
                CollectFunctionScopeSymbolsFromStatement(forEachStatement.Body);
                break;
            case IfStatement ifStatement:
                CollectFunctionScopeSymbolsFromStatement(ifStatement.Then);
                if (ifStatement.Else is not null)
                {
                    CollectFunctionScopeSymbolsFromStatement(ifStatement.Else);
                }
                break;
            case WhileStatement whileStatement:
                CollectFunctionScopeSymbolsFromStatement(whileStatement.Body);
                break;
            case DoWhileStatement doWhileStatement:
                CollectFunctionScopeSymbolsFromStatement(doWhileStatement.Body);
                break;
            case TryStatement tryStatement:
                CollectFunctionScopeSymbolsFromStatement(tryStatement.TryBlock);
                if (tryStatement.Catch is not null)
                {
                    CollectFunctionScopeSymbolsFromStatement(tryStatement.Catch.Body);
                }
                if (tryStatement.Finally is not null)
                {
                    CollectFunctionScopeSymbolsFromStatement(tryStatement.Finally);
                }
                break;
            case SwitchStatement switchStatement:
                foreach (var caseNode in switchStatement.Cases)
                {
                    CollectFunctionScopeSymbolsFromStatement(caseNode.Body);
                }
                break;
            case LabeledStatement labeledStatement:
                CollectFunctionScopeSymbolsFromStatement(labeledStatement.Statement);
                break;
            case WithStatement withStatement:
                CollectFunctionScopeSymbolsFromStatement(withStatement.Body);
                break;
        }
    }

    private void CollectFunctionScopeSymbolsFromBinding(BindingTarget target, VariableKind kind)
    {
        var isFunctionScope = kind == VariableKind.Var || _currentScopes.IsDefaultOrEmpty;
        if (!isFunctionScope)
        {
            return;
        }

        switch (target)
        {
            case IdentifierBinding identifierBinding:
                _functionSymbols.Add(identifierBinding.Name);
                break;
            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.Target is not null)
                    {
                        CollectFunctionScopeSymbolsFromBinding(element.Target, kind);
                    }
                }
                if (arrayBinding.RestElement is not null)
                {
                    CollectFunctionScopeSymbolsFromBinding(arrayBinding.RestElement, kind);
                }
                break;
            case ObjectBinding objectBinding:
                foreach (var property in objectBinding.Properties)
                {
                    CollectFunctionScopeSymbolsFromBinding(property.Target, kind);
                }
                if (objectBinding.RestElement is not null)
                {
                    CollectFunctionScopeSymbolsFromBinding(objectBinding.RestElement, kind);
                }
                break;
        }
    }

    private readonly record struct ScopeFrame(int ScopeId, ImmutableDictionary<Symbol, int> SlotMap);
}
