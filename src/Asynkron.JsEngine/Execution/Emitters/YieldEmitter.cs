using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for yield expressions and yield-related returns.
/// </summary>
internal static class YieldEmitter
{
    /// <summary>
    /// Try to build IR for a return statement containing a yield expression.
    /// Handles both simple yield and delegated yield*.
    /// </summary>
    public static bool TryEmitReturnWithYield(
        EmitContext ctx,
        YieldExpression yieldExpression,
        out int entryIndex)
    {
        // Only handle simple `yield` / `yield*` used directly as the return
        // expression, and reject nested `yield` inside the yielded expression
        // for now.
        if (AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        var resumeSymbol = ctx.CreateResumeSlotSymbol();
        var returnExpression = new IdentifierExpression(yieldExpression.Source, resumeSymbol);

        // Build a return that uses the resume slot value, then prefix it with
        // either:
        //   - a simple yield sequence that captures the resume payload into
        //     that slot (`return yield <expr>;`), or
        //   - a delegated yield* sequence that stores the delegate's final
        //     completion value into the slot (`return yield* <expr>;`).
        var returnIndex = ctx.Append(new ReturnInstruction(-1, returnExpression));
        entryIndex = yieldExpression.IsDelegated
            ? ctx.AppendYieldStarSequence(yieldExpression, returnIndex, resumeSymbol)
            : ctx.AppendYieldSequence(yieldExpression.Expression, returnIndex, resumeSymbol);
        return true;
    }

    /// <summary>
    /// Try to emit IR for a yield expression statement.
    /// </summary>
    public static bool TryEmitYieldExpressionStatement(
        EmitContext ctx,
        YieldExpression yieldExpression,
        int nextIndex,
        out int entryIndex)
    {
        if (yieldExpression.IsDelegated)
        {
            if (AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
            {
                entryIndex = -1;
                return false;
            }

            entryIndex = ctx.AppendYieldStarSequence(yieldExpression, nextIndex, null);
            return true;
        }

        if (AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        entryIndex = ctx.AppendYieldSequence(yieldExpression.Expression, nextIndex, null);
        return true;
    }

    /// <summary>
    /// Try to emit IR for a yield assignment expression (lowerer temp assignment).
    /// </summary>
    public static bool TryEmitYieldAssignment(
        EmitContext ctx,
        Symbol targetSymbol,
        YieldExpression yieldAssignment,
        int nextIndex,
        out int entryIndex)
    {
        if (AstShapeAnalyzer.ContainsYield(yieldAssignment.Expression))
        {
            entryIndex = -1;
            return false;
        }

        entryIndex = yieldAssignment.IsDelegated
            ? ctx.AppendYieldStarSequence(yieldAssignment, nextIndex, targetSymbol)
            : ctx.AppendYieldSequence(yieldAssignment.Expression, nextIndex, targetSymbol);
        return true;
    }

    /// <summary>
    /// Try to emit IR for a variable declaration with yield initializer (lowerer temp).
    /// </summary>
    public static bool TryEmitVariableWithYieldInitializer(
        EmitContext ctx,
        Symbol targetSymbol,
        YieldExpression yieldInitializer,
        int nextIndex,
        out int entryIndex)
    {
        if (AstShapeAnalyzer.ContainsYield(yieldInitializer.Expression))
        {
            entryIndex = -1;
            return false;
        }

        entryIndex = yieldInitializer.IsDelegated
            ? ctx.AppendYieldStarSequence(yieldInitializer, nextIndex, targetSymbol)
            : ctx.AppendYieldSequence(yieldInitializer.Expression, nextIndex, targetSymbol);
        return true;
    }
}
