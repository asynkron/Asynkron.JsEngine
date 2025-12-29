using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Emitters;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Partial class exposing internal methods for emitters.
/// </summary>
internal sealed partial class ExecutionPlanBuilder
{
    // Cached EmitContext instance
    private EmitContext? _emitContext;

    /// <summary>
    /// Get or create the EmitContext for this builder.
    /// </summary>
    private EmitContext GetEmitContext()
    {
        return _emitContext ??= new EmitContext(this, _instructions, _loopScopes);
    }

    /// <summary>
    /// Internal method for EmitContext to build a statement list.
    /// </summary>
    internal bool TryBuildStatementListInternal(ImmutableArray<StatementNode> statements, int nextIndex, out int entryIndex)
    {
        return TryBuildStatementList(statements, nextIndex, out entryIndex);
    }

    /// <summary>
    /// Internal method for EmitContext to build a single statement.
    /// </summary>
    internal bool TryBuildStatementInternal(StatementNode statement, int nextIndex, out int entryIndex, Symbol? activeLabel = null)
    {
        return TryBuildStatement(statement, nextIndex, out entryIndex, activeLabel);
    }

    /// <summary>
    /// Internal method for EmitContext to set failure reason.
    /// </summary>
    internal void SetFailureReasonInternal(string reason)
    {
        _failureReason ??= reason;
    }

    /// <summary>
    /// Internal method for EmitContext to create catch slot symbol.
    /// </summary>
    internal Symbol CreateCatchSlotSymbolInternal()
    {
        return CreateCatchSlotSymbol();
    }

    /// <summary>
    /// Internal static method for EmitContext to build catch block.
    /// </summary>
    internal static BlockStatement BuildCatchBlockInternal(CatchClause catchClause, Symbol catchSlotSymbol)
    {
        return BuildCatchBlock(catchClause, catchSlotSymbol);
    }

    /// <summary>
    /// Internal method for EmitContext to allocate a slot index.
    /// </summary>
    internal int AllocateSlotInternal(Symbol symbol)
    {
        return AllocateSlot(symbol);
    }

    /// <summary>
    /// Internal property for EmitContext to access the instruction list.
    /// </summary>
    internal List<ExecutionInstruction> InstructionsInternal => _instructions;

    /// <summary>
    /// Internal static method to create iterator binding statement.
    /// </summary>
    internal static StatementNode CreateIteratorBindingStatementInternal(
        IteratorDriverPlan plan,
        Symbol valueSymbol,
        int valueSlotIndex)
    {
        return CreateIteratorBindingStatement(plan, valueSymbol, valueSlotIndex);
    }

    /// <summary>
    /// Internal static method to check if a statement is a strict block.
    /// </summary>
    internal static bool IsStrictBlockInternal(StatementNode statement)
    {
        return IsStrictBlock(statement);
    }

    /// <summary>
    /// Internal static method to check for unlabeled break/continue in finally blocks.
    /// </summary>
    internal static bool ContainsUnlabeledAbruptInFinallyInternal(StatementNode statement)
    {
        return ContainsUnlabeledAbruptInFinally(statement);
    }

    /// <summary>
    /// Internal method for EmitContext to create with scope slot symbol.
    /// </summary>
    internal Symbol CreateWithScopeSlotSymbolInternal()
    {
        return CreateWithScopeSlotSymbol();
    }

    /// <summary>
    /// Internal method for EmitContext to create resume slot symbol.
    /// </summary>
    internal Symbol CreateResumeSlotSymbolInternal()
    {
        return CreateResumeSlotSymbol();
    }

    /// <summary>
    /// Internal method for EmitContext to append a yield sequence.
    /// </summary>
    internal int AppendYieldSequenceInternal(ExpressionNode? expression, int continuationIndex, Symbol? resumeSlot)
    {
        return AppendYieldSequence(expression, continuationIndex, resumeSlot);
    }

    /// <summary>
    /// Internal method for EmitContext to append a yield* sequence.
    /// </summary>
    internal int AppendYieldStarSequenceInternal(YieldExpression expression, int continuationIndex, Symbol? resultSlot)
    {
        return AppendYieldStarSequence(expression, continuationIndex, resultSlot);
    }

    /// <summary>
    /// Loop scope structure for break/continue resolution.
    /// Made internal so EmitContext can access it.
    /// </summary>
    internal readonly record struct LoopScope(Symbol? Label, int ContinueTarget, int BreakTarget, int TargetScopeId);
}
