#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Provides context for IR emitters, encapsulating access to the instruction list,
/// loop scope stack, and helper methods for building execution plans.
/// </summary>
internal sealed class EmitContext(
    ExecutionPlanBuilder builder,
    List<ExecutionInstruction> instructions,
    Stack<ExecutionPlanBuilder.LoopScope> loopScopes)
{
    /// <summary>
    /// Current number of instructions (used for rollback on failure).
    /// </summary>
    public int InstructionCount => instructions.Count;

    /// <summary>
    /// When true, expression statements should suppress completion value updates.
    /// Per ES spec, for-loop update expressions don't contribute to the loop's completion value.
    /// </summary>
    public bool SuppressCompletionValue { get; set; }

    /// <summary>
    /// Whether this plan is being built for a top-level script (not a function body).
    /// Script-level var declarations must update the global object.
    /// </summary>
    public bool IsScriptLevel => builder.IsScriptLevel;

    /// <summary>
    /// Get the instruction list (for IteratorInstructionTemplate).
    /// </summary>
    public List<ExecutionInstruction> Instructions => builder.Instructions;

    /// <summary>
    /// Append an instruction and return its index.
    /// </summary>
    public int Append(ExecutionInstruction instruction)
    {
        var index = instructions.Count;
        instructions.Add(instruction);
        return index;
    }

    /// <summary>
    /// Patch an instruction at the given index with a new instruction.
    /// </summary>
    public void Patch(int index, ExecutionInstruction instruction)
    {
        instructions[index] = instruction;
    }

    /// <summary>
    /// Remove instructions from the given start index to the end.
    /// Used for rollback on failure.
    /// </summary>
    public void Rollback(int startIndex)
    {
        instructions.RemoveRange(startIndex, instructions.Count - startIndex);
    }

    /// <summary>
    /// Push a loop scope for break/continue resolution.
    /// </summary>
    public void PushLoopScope(Symbol? label, int continueTarget, int breakTarget, int targetScopeId)
    {
        loopScopes.Push(new ExecutionPlanBuilder.LoopScope(label, continueTarget, breakTarget, targetScopeId));
    }

    /// <summary>
    /// Pop a loop scope.
    /// </summary>
    public void PopLoopScope()
    {
        loopScopes.Pop();
    }

    /// <summary>
    /// Try to find a loop scope for a break statement.
    /// </summary>
    public bool TryFindBreakTarget(Symbol? label, out int target, out int scopeId)
    {
        foreach (var scope in loopScopes)
        {
            if (label is null || ReferenceEquals(scope.Label, label))
            {
                target = scope.BreakTarget;
                scopeId = scope.TargetScopeId;
                return true;
            }
        }

        target = -1;
        scopeId = -1;
        return false;
    }

    /// <summary>
    /// Try to find a loop scope for a continue statement.
    /// </summary>
    public bool TryFindContinueTarget(Symbol? label, out int target, out int scopeId)
    {
        foreach (var scope in loopScopes)
        {
            if (label is null || ReferenceEquals(scope.Label, label))
            {
                if (scope.ContinueTarget < 0)
                {
                    // This is a labeled non-loop statement - continue not valid
                    continue;
                }

                target = scope.ContinueTarget;
                scopeId = scope.TargetScopeId;
                return true;
            }
        }

        target = -1;
        scopeId = -1;
        return false;
    }

    /// <summary>
    /// Build a statement list, delegating to the builder.
    /// </summary>
    public bool TryBuildStatementList(ImmutableArray<StatementNode> statements, int nextIndex, out int entryIndex)
    {
        return builder.TryBuildStatementList(statements, nextIndex, out entryIndex);
    }

    /// <summary>
    /// Build a single statement by dispatching to the StatementEmitter.
    /// </summary>
    public bool TryBuildStatement(StatementNode statement, int nextIndex, out int entryIndex,
        Symbol? activeLabel = null)
    {
        return StatementEmitter.TryEmitStatement(this, statement, nextIndex, out entryIndex, activeLabel);
    }

    /// <summary>
    /// Set the failure reason on the builder.
    /// </summary>
    public void SetFailureReason(string reason)
    {
        builder.SetFailureReason(reason);
    }

    /// <summary>
    /// Create a catch slot symbol.
    /// </summary>
    public Symbol CreateCatchSlotSymbol()
    {
        return builder.CreateCatchSlotSymbol();
    }

    /// <summary>
    /// Build a catch block with the given symbol.
    /// </summary>
    public BlockStatement BuildCatchBlock(CatchClause catchClause, Symbol catchSlotSymbol)
    {
        return ExecutionPlanBuilder.BuildCatchBlock(catchClause, catchSlotSymbol);
    }

    /// <summary>
    /// Allocate a slot index for a generator-internal symbol.
    /// </summary>
    public int AllocateSlot(Symbol symbol)
    {
        return builder.AllocateSlot(symbol);
    }

    /// <summary>
    /// Allocate a scope ID for dynamic scopes (catch blocks, etc.).
    /// </summary>
    public int AllocateScopeId()
    {
        return builder.AllocateScopeId();
    }

    /// <summary>
    /// Create iterator binding statement.
    /// </summary>
    public static StatementNode CreateIteratorBindingStatement(IteratorDriverPlan plan, Symbol valueSymbol,
        int valueSlotIndex)
    {
        return ExecutionPlanBuilder.CreateIteratorBindingStatement(plan, valueSymbol, valueSlotIndex);
    }

    /// <summary>
    /// Check if a statement is a strict block.
    /// </summary>
    public static bool IsStrictBlock(StatementNode statement)
    {
        return ExecutionPlanBuilder.IsStrictBlock(statement);
    }

    /// <summary>
    /// Check for unlabeled break/continue in finally blocks.
    /// </summary>
    public static bool ContainsUnlabeledAbruptInFinally(StatementNode statement)
    {
        return ExecutionPlanBuilder.ContainsUnlabeledAbruptInFinally(statement);
    }

    /// <summary>
    /// Create a with scope slot symbol.
    /// </summary>
    public Symbol CreateWithScopeSlotSymbol()
    {
        return builder.CreateWithScopeSlotSymbol();
    }

    /// <summary>
    /// Create a resume slot symbol for yield expressions.
    /// </summary>
    public Symbol CreateResumeSlotSymbol()
    {
        return builder.CreateResumeSlotSymbol();
    }

    /// <summary>
    /// Append a yield sequence (yield instruction followed by store resume value).
    /// </summary>
    public int AppendYieldSequence(ExpressionNode? expression, int continuationIndex, Symbol? resumeSlot)
    {
        return builder.AppendYieldSequence(expression, continuationIndex, resumeSlot);
    }

    /// <summary>
    /// Append a yield* sequence (delegated yield instruction).
    /// </summary>
    public int AppendYieldStarSequence(YieldExpression expression, int continuationIndex, Symbol? resultSlot)
    {
        return builder.AppendYieldStarSequence(expression, continuationIndex, resultSlot);
    }

    /// <summary>
    /// Build a slot map from per-iteration bindings and their slot indices.
    /// Used by LoopEmitter and ForOfEmitter to create PushEnvironmentInstruction slot maps.
    /// </summary>
    public static ImmutableDictionary<Symbol, int> BuildSlotMap(
        ImmutableArray<Symbol> bindings,
        ImmutableArray<int> slotIndices)
    {
        var slotMapBuilder = ImmutableDictionary.CreateBuilder<Symbol, int>();
        var count = Math.Min(bindings.Length, slotIndices.IsDefaultOrEmpty ? 0 : slotIndices.Length);

        for (var i = 0; i < count; i++)
        {
            if (slotIndices[i] >= 0)
            {
                slotMapBuilder[bindings[i]] = slotIndices[i];
            }
        }

        return slotMapBuilder.ToImmutable();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared Yield Detection Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Check if a symbol is a lowerer-generated temporary variable.
    /// </summary>
    public static bool IsLowererTemp(Symbol symbol)
    {
        return symbol.Name?.StartsWith("__yield_lower_", StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// Check if a binding target is a lowerer-generated temporary variable.
    /// </summary>
    public static bool IsLowererTemp(BindingTarget target)
    {
        return target is IdentifierBinding { Name.Name: not null } identifier &&
               identifier.Name.Name.StartsWith("__yield_lower_", StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks if a binding target contains yields anywhere - either in default values
    /// or in assignment target expressions (like [ {}[ yield ] ]).
    /// </summary>
    public static bool BindingTargetContainsYieldAnywhere(BindingTarget target)
    {
        switch (target)
        {
            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(element.DefaultValue))
                    {
                        return true;
                    }

                    if (element.Target is not null && BindingTargetContainsYieldAnywhere(element.Target))
                    {
                        return true;
                    }
                }

                if (arrayBinding.RestElement is not null &&
                    BindingTargetContainsYieldAnywhere(arrayBinding.RestElement))
                {
                    return true;
                }

                return false;

            case ObjectBinding objectBinding:
                foreach (var prop in objectBinding.Properties)
                {
                    if (prop.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(prop.DefaultValue))
                    {
                        return true;
                    }

                    if (prop.NameExpression is not null && AstShapeAnalyzer.ContainsYield(prop.NameExpression))
                    {
                        return true;
                    }

                    if (BindingTargetContainsYieldAnywhere(prop.Target))
                    {
                        return true;
                    }
                }

                if (objectBinding.RestElement is not null &&
                    BindingTargetContainsYieldAnywhere(objectBinding.RestElement))
                {
                    return true;
                }

                return false;

            case AssignmentTargetBinding assignmentTarget:
                return AstShapeAnalyzer.ContainsYield(assignmentTarget.Expression);

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks if an expression contains a destructuring assignment with yields anywhere in
    /// the binding target - either in default values or in assignment target expressions.
    /// </summary>
    public static bool ExpressionContainsDestructuringWithYieldAnywhere(ExpressionNode expression)
    {
        while (true)
        {
            switch (expression)
            {
                case DestructuringAssignmentExpression destructuringExpr:
                    if (BindingTargetContainsYieldAnywhere(destructuringExpr.Target))
                    {
                        return true;
                    }

                    expression = destructuringExpr.Value;
                    continue;

                case AssignmentExpression assignmentExpr:
                    expression = assignmentExpr.Value;
                    continue;

                case PropertyAssignmentExpression propAssignExpr:
                    expression = propAssignExpr.Value;
                    continue;

                case IndexAssignmentExpression indexAssignExpr:
                    expression = indexAssignExpr.Value;
                    continue;

                case ConditionalExpression conditionalExpr:
                    return ExpressionContainsDestructuringWithYieldAnywhere(conditionalExpr.Consequent) ||
                           ExpressionContainsDestructuringWithYieldAnywhere(conditionalExpr.Alternate);

                case SequenceExpression seqExpr:
                    return ExpressionContainsDestructuringWithYieldAnywhere(seqExpr.Left) ||
                           ExpressionContainsDestructuringWithYieldAnywhere(seqExpr.Right);

                default:
                    return false;
            }
        }
    }
}
