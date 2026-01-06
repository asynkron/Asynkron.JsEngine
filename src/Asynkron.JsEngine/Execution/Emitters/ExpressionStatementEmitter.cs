#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

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
        if (EmitContext.ExpressionContainsDestructuringWithYieldAnywhere(expressionStatement.Expression))
        {
            // AST fallback: expression statement with yield in destructuring
            // Reason: Conditional yield semantics in destructuring defaults/targets
            // Tracking: #398, #416 (IR-only execution epic)
            var suppressCompletionFallback =
                ctx.SuppressCompletionValue || expressionStatement.SuppressCompletionValue;
            entryIndex = ctx.Append(new EvaluateAndDiscardInstruction(
                nextIndex,
                expressionStatement.Expression,
                suppressCompletionFallback));
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
            ctx.SetFailureReason(
                "Expression statement contains unlowered yield - this should have been handled by GeneratorYieldLowerer.");
            return false;
        }

        // NOTE: Await expressions are handled by EvaluateAndDiscardInstruction via
        // normal expression evaluation - no fallback to StatementInstruction needed.
        // The IR runner's TryHandlePendingAwait handles async suspension/resumption.

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

        // Fast path: compound assignment on simple identifiers (e.g., s += i, s -= 1)
        // Only handle non-short-circuit operators (+=, -=, *=, /=, %=, **=, bitwise ops)
        // Logical operators (&&=, ||=, ??=) need special short-circuit handling
        // NOTE: Skip this optimization for script-level code because:
        //   1. Scripts may contain 'with' statements that require dynamic identifier resolution
        //   2. Eval'd code runs at script level and may be inside a with-scope from caller
        //   3. Slot-based lookup would bypass the with-scope, breaking 'with' semantics
        if (!ctx.IsScriptLevel &&
            expressionStatement.Expression is AssignmentExpression
            {
                IsCompoundAssignment: true,
                Value: BinaryExpression compoundBinary
            } compoundAssign &&
            compoundBinary.Operator is
                BinaryOperator.Add or BinaryOperator.Subtract or
                BinaryOperator.Multiply or BinaryOperator.Divide or
                BinaryOperator.Modulo or BinaryOperator.Power or
                BinaryOperator.BitwiseAnd or BinaryOperator.BitwiseOr or
                BinaryOperator.BitwiseXor or BinaryOperator.LeftShift or
                BinaryOperator.RightShift or BinaryOperator.UnsignedRightShift)
        {
            entryIndex = ctx.Append(new CompoundAssignmentSlotInstruction(
                nextIndex,
                compoundAssign.Target,
                compoundBinary.Operator,
                compoundBinary.Right,
                suppressCompletion));
            return true;
        }

        // Use native EvaluateAndDiscardInstruction - evaluates expression and discards result
        entryIndex =
            ctx.Append(new EvaluateAndDiscardInstruction(nextIndex, expressionStatement.Expression,
                suppressCompletion));
        return true;
    }
}
