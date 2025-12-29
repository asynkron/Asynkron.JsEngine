using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
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
            IsLowererTemp(targetSymbol))
        {
            if (YieldEmitter.TryEmitYieldAssignment(
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
        if (ExpressionContainsDestructuringWithYieldAnywhere(expressionStatement.Expression))
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
                unaryExpr.IsPrefix));
            return true;
        }

        // Use native EvaluateAndDiscardInstruction - evaluates expression and discards result
        entryIndex = ctx.Append(new EvaluateAndDiscardInstruction(nextIndex, expressionStatement.Expression));
        return true;
    }

    private static bool IsLowererTemp(Symbol symbol)
    {
        return symbol.Name?.StartsWith("__yield_lower_", StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// Checks if an expression contains a destructuring assignment with yields anywhere in
    /// the binding target - either in default values or in assignment target expressions.
    /// </summary>
    private static bool ExpressionContainsDestructuringWithYieldAnywhere(ExpressionNode expression)
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

    private static bool BindingTargetContainsYieldAnywhere(BindingTarget target)
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
}
