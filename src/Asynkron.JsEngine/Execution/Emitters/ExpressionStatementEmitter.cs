using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for expression statements.
/// </summary>
internal static class ExpressionStatementEmitter
{
    /// <summary>
    /// Try to emit IR for an expression statement.
    /// Returns false if the expression cannot be handled and sets failure reason.
    /// </summary>
    public static bool TryEmitExpressionStatement(
        EmitContext ctx,
        ExpressionStatement expressionStatement,
        int nextIndex,
        out int entryIndex)
    {
        // Handle yield assignment to lowerer temp
        if (expressionStatement.Expression is AssignmentExpression
            {
                Target: { } targetSymbol, Value: YieldExpression yieldAssignment
            } &&
            EmitContext.IsLowererTemp(targetSymbol))
        {
            if (YieldEmitter.TryEmitYieldToSymbol(
                ctx, targetSymbol, yieldAssignment, nextIndex, out entryIndex))
            {
                return true;
            }
        }

        // Check for destructuring assignment expressions with yields that cannot be safely
        // extracted. This includes:
        // - Yields in default values (only evaluated when element is undefined)
        // - Yields in assignment target expressions (e.g., [ {}[ yield ] ] = x)
        // Wrap them as StatementInstruction to use AST evaluation's state-saving.
        if (EmitContext.ExpressionContainsDestructuringWithYieldAnywhere(expressionStatement.Expression))
        {
            entryIndex = ctx.Append(new StatementInstruction(nextIndex, expressionStatement));
            return true;
        }

        var expressionShape = AstShapeAnalyzer.AnalyzeExpression(expressionStatement.Expression);
        if (expressionShape.DelegatedYieldCount > 0 ||
            expressionShape.YieldOperandContainsYield)
        {
            entryIndex = -1;
            ctx.SetFailureReason("Expression statement contains unsupported yield shape.");
            return false;
        }

        // After lowering, no yields should remain in expression statements.
        // If we still have yields here, the lowerer missed a pattern.
        if (expressionShape.YieldCount > 0)
        {
            entryIndex = -1;
            ctx.SetFailureReason("Expression statement contains unlowered yield - this should have been handled by GeneratorYieldLowerer.");
            return false;
        }

        // For async generators, await expressions are lowered to yield points.
        // Don't use native instruction if there are awaits - fall back to StatementInstruction.
        if (AstShapeAnalyzer.ContainsAwait(expressionStatement.Expression))
        {
            entryIndex = ctx.Append(new StatementInstruction(nextIndex, expressionStatement));
            return true;
        }

        // Combine context and statement-level suppression flags
        var suppressCompletion = ctx.SuppressCompletionValue || expressionStatement.SuppressCompletionValue;

        // Fast path: simple increment/decrement on identifiers (e.g., i++, --j)
        if (expressionStatement.Expression is UnaryExpression
            {
                Operator: UnaryOperator.Increment or UnaryOperator.Decrement,
                Operand: IdentifierExpression identTarget
            } unaryExpr)
        {
            var isIncrement = unaryExpr.Operator == UnaryOperator.Increment;
            entryIndex = ctx.Append(new IncrementSlotInstruction(
                nextIndex,
                identTarget.Name,
                isIncrement,
                unaryExpr.IsPrefix,
                suppressCompletion));
            return true;
        }

        // Use native EvaluateAndDiscardInstruction - evaluates expression and discards result
        entryIndex = ctx.Append(new EvaluateAndDiscardInstruction(nextIndex, expressionStatement.Expression, suppressCompletion));
        return true;
    }
}
