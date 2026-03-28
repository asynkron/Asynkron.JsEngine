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
                Expression: expressionStatement.Expression,
                SuppressCompletionValue: suppressCompletionFallback));
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

        // NOTE: Plain await expressions now use a dedicated instruction so the
        // runner can suspend/resume without routing through EvaluateAndDiscard.

        // Combine context and statement-level suppression flags
        var suppressCompletion = ctx.SuppressCompletionValue || expressionStatement.SuppressCompletionValue;

        if (expressionStatement.Expression is SequenceExpression sequenceExpression)
        {
            return TryEmitSequenceExpressionStatement(
                ctx,
                sequenceExpression,
                suppressCompletion,
                nextIndex,
                out entryIndex);
        }

        if (expressionStatement.Expression is ConditionalExpression conditionalExpression)
        {
            return TryEmitConditionalExpressionStatement(
                ctx,
                conditionalExpression,
                suppressCompletion,
                nextIndex,
                out entryIndex);
        }

        if (expressionStatement.Expression is AwaitExpression awaitExpression)
        {
            if (!ExpressionProgramCompiler.TryCompile(
                    awaitExpression.Expression,
                    out var awaitedProgram,
                    out var awaitFailure))
            {
                ctx.SetExpressionProgramFailure(
                    "AwaitAndDiscardInstruction",
                    awaitExpression.Expression,
                    awaitFailure);
                entryIndex = -1;
                return false;
            }

            entryIndex = ctx.Append(new AwaitAndDiscardInstruction(
                nextIndex,
                ((IAstCacheable<Symbol>)awaitExpression).GetOrCreateCache(),
                awaitedProgram,
                SuppressCompletionValue: suppressCompletion));
            return true;
        }

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

        // Fast path: simple identifier assignment (e.g., x = value).
        // Keep script-level execution on the generic path because scripts may still
        // observe dynamic resolution through explicit dynamic-scope execution.
        if (!ctx.IsScriptLevel &&
            expressionStatement.Expression is AssignmentExpression
            {
                IsCompoundAssignment: false,
                IsImmutableTarget: false
            } assignment)
        {
            entryIndex = ctx.Append(new AssignmentSlotInstruction(
                nextIndex,
                assignment.Target,
                ValueExpression: assignment.Value,
                SuppressCompletionValue: suppressCompletion,
                AllowNameInference: ShouldAllowAssignmentNameInference(assignment)));
            return true;
        }

        // Fast path: compound assignment on simple identifiers.
        // NOTE: Skip this optimization for script-level code because:
        //   1. Scripts may contain 'with' statements that require dynamic identifier resolution
        //   2. Eval'd code runs at script level and may be inside a with-scope from caller
        //   3. Slot-based lookup would bypass the with-scope, breaking 'with' semantics
        if (!ctx.IsScriptLevel &&
            expressionStatement.Expression is AssignmentExpression
            {
                IsCompoundAssignment: true,
                Value: BinaryExpression logicalBinary
            } logicalCompoundAssign &&
            logicalBinary.Operator is
                BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.NullishCoalescing)
        {
            entryIndex = ctx.Append(new LogicalCompoundAssignmentSlotInstruction(
                nextIndex,
                logicalCompoundAssign.Target,
                logicalBinary.Operator,
                RhsExpression: logicalBinary.Right,
                SuppressCompletionValue: suppressCompletion,
                AllowNameInference: ShouldAllowAssignmentNameInference(logicalCompoundAssign)));
            return true;
        }

        if (!ctx.IsScriptLevel &&
            expressionStatement.Expression is AssignmentExpression
            {
                IsCompoundAssignment: true,
                Value: BinaryExpression arithmeticBinary
            } compoundAssign &&
            arithmeticBinary.Operator is
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
                arithmeticBinary.Operator,
                arithmeticBinary.Right,
                SuppressCompletionValue: suppressCompletion));
            return true;
        }

        // Use native EvaluateAndDiscardInstruction - evaluates expression and discards result
        entryIndex =
            ctx.Append(new EvaluateAndDiscardInstruction(
                nextIndex,
                Expression: expressionStatement.Expression,
                SuppressCompletionValue: suppressCompletion));
        return true;
    }

    private static bool IsParenthesizedIdentifierAssignment(AssignmentExpression expression)
    {
        if (expression.Source is null)
        {
            return false;
        }

        var source = expression.Source.Source;
        var index = expression.Source.StartPosition - 1;
        while (index >= 0 && char.IsWhiteSpace(source, index))
        {
            index--;
        }

        return index >= 0 && source[index] == '(';
    }

    private static bool ShouldAllowAssignmentNameInference(AssignmentExpression expression)
    {
        return IsAnonymousFunctionDefinitionForNameInference(expression.Value) &&
               !IsParenthesizedIdentifierAssignment(expression);
    }

    private static bool IsAnonymousFunctionDefinitionForNameInference(ExpressionNode? expression)
    {
        return expression switch
        {
            SequenceExpression => false,
            FunctionExpression { Name: null } => true,
            ClassExpression { Name: null } => true,
            _ => false
        };
    }

    private static bool TryEmitSequenceExpressionStatement(
        EmitContext ctx,
        SequenceExpression sequenceExpression,
        bool suppressCompletion,
        int nextIndex,
        out int entryIndex)
    {
        var rightStatement = new ExpressionStatement(
            sequenceExpression.Right.Source,
            sequenceExpression.Right,
            suppressCompletion);
        if (!TryEmitExpressionStatement(ctx, rightStatement, nextIndex, out var rightEntryIndex))
        {
            entryIndex = -1;
            return false;
        }

        var leftStatement = new ExpressionStatement(
            sequenceExpression.Left.Source,
            sequenceExpression.Left,
            SuppressCompletionValue: true);
        return TryEmitExpressionStatement(ctx, leftStatement, rightEntryIndex, out entryIndex);
    }

    private static bool TryEmitConditionalExpressionStatement(
        EmitContext ctx,
        ConditionalExpression conditionalExpression,
        bool suppressCompletion,
        int nextIndex,
        out int entryIndex)
    {
        var instructionStart = ctx.InstructionCount;

        var alternateStatement = new ExpressionStatement(
            conditionalExpression.Alternate.Source,
            conditionalExpression.Alternate,
            suppressCompletion);
        if (!TryEmitExpressionStatement(ctx, alternateStatement, nextIndex, out var alternateEntryIndex))
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        var consequentStatement = new ExpressionStatement(
            conditionalExpression.Consequent.Source,
            conditionalExpression.Consequent,
            suppressCompletion);
        if (!TryEmitExpressionStatement(ctx, consequentStatement, nextIndex, out var consequentEntryIndex))
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        if (!ExpressionProgramCompiler.TryCompile(conditionalExpression.Test, out var conditionProgram, out var conditionFailure))
        {
            ctx.Rollback(instructionStart);
            ctx.SetExpressionProgramFailure("BranchInstruction", conditionalExpression.Test, conditionFailure);
            entryIndex = -1;
            return false;
        }

        entryIndex = ctx.Append(new BranchInstruction(
            consequentEntryIndex,
            alternateEntryIndex,
            conditionProgram));
        return true;
    }
}
