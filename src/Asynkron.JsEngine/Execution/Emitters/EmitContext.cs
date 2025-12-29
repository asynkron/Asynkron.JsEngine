using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

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
        return builder.TryBuildStatementListInternal(statements, nextIndex, out entryIndex);
    }

    /// <summary>
    /// Build a single statement by dispatching to the StatementEmitter.
    /// </summary>
    public bool TryBuildStatement(StatementNode statement, int nextIndex, out int entryIndex, Symbol? activeLabel = null)
    {
        return StatementEmitter.TryEmitStatement(this, statement, nextIndex, out entryIndex, activeLabel);
    }

    /// <summary>
    /// Set the failure reason on the builder.
    /// </summary>
    public void SetFailureReason(string reason)
    {
        builder.SetFailureReasonInternal(reason);
    }

    /// <summary>
    /// Create a catch slot symbol.
    /// </summary>
    public Symbol CreateCatchSlotSymbol()
    {
        return builder.CreateCatchSlotSymbolInternal();
    }

    /// <summary>
    /// Build a catch block with the given symbol.
    /// </summary>
    public BlockStatement BuildCatchBlock(CatchClause catchClause, Symbol catchSlotSymbol)
    {
        return ExecutionPlanBuilder.BuildCatchBlockInternal(catchClause, catchSlotSymbol);
    }

    /// <summary>
    /// Allocate a slot index for a generator-internal symbol.
    /// </summary>
    public int AllocateSlot(Symbol symbol)
    {
        return builder.AllocateSlotInternal(symbol);
    }

    /// <summary>
    /// Get the instruction list (for IteratorInstructionTemplate).
    /// </summary>
    public List<ExecutionInstruction> Instructions => builder.InstructionsInternal;

    /// <summary>
    /// Create iterator binding statement.
    /// </summary>
    public static StatementNode CreateIteratorBindingStatement(IteratorDriverPlan plan, Symbol valueSymbol, int valueSlotIndex)
    {
        return ExecutionPlanBuilder.CreateIteratorBindingStatementInternal(plan, valueSymbol, valueSlotIndex);
    }

    /// <summary>
    /// Check if a statement is a strict block.
    /// </summary>
    public static bool IsStrictBlock(StatementNode statement)
    {
        return ExecutionPlanBuilder.IsStrictBlockInternal(statement);
    }

    /// <summary>
    /// Check for unlabeled break/continue in finally blocks.
    /// </summary>
    public static bool ContainsUnlabeledAbruptInFinally(StatementNode statement)
    {
        return ExecutionPlanBuilder.ContainsUnlabeledAbruptInFinallyInternal(statement);
    }

    /// <summary>
    /// Create a with scope slot symbol.
    /// </summary>
    public Symbol CreateWithScopeSlotSymbol()
    {
        return builder.CreateWithScopeSlotSymbolInternal();
    }

    /// <summary>
    /// Create a resume slot symbol for yield expressions.
    /// </summary>
    public Symbol CreateResumeSlotSymbol()
    {
        return builder.CreateResumeSlotSymbolInternal();
    }

    /// <summary>
    /// Append a yield sequence (yield instruction followed by store resume value).
    /// </summary>
    public int AppendYieldSequence(ExpressionNode? expression, int continuationIndex, Symbol? resumeSlot)
    {
        return builder.AppendYieldSequenceInternal(expression, continuationIndex, resumeSlot);
    }

    /// <summary>
    /// Append a yield* sequence (delegated yield instruction).
    /// </summary>
    public int AppendYieldStarSequence(YieldExpression expression, int continuationIndex, Symbol? resultSlot)
    {
        return builder.AppendYieldStarSequenceInternal(expression, continuationIndex, resultSlot);
    }
}
