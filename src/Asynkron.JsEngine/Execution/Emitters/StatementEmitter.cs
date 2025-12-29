using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Main statement dispatcher - routes statements to specialized emitters.
/// </summary>
internal static class StatementEmitter
{
    /// <summary>
    /// Try to emit IR for a statement. This is the main dispatch method that routes
    /// to specialized emitters based on statement type.
    /// </summary>
    public static bool TryEmitStatement(
        EmitContext ctx,
        StatementNode statement,
        int nextIndex,
        out int entryIndex,
        Symbol? activeLabel = null)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    return BlockEmitter.TryEmitBlock(ctx, block, nextIndex, out entryIndex);

                case FunctionDeclaration:
                    // Function declarations are hoisted - this is a no-op at runtime
                    entryIndex = ctx.Append(new FunctionDeclarationInstruction(nextIndex));
                    return true;

                case IfStatement ifStatement:
                    return ControlFlowEmitter.TryEmitIf(ctx, ifStatement, nextIndex, activeLabel, out entryIndex);

                case EmptyStatement:
                    entryIndex = nextIndex;
                    return true;

                case ExpressionStatement { Expression: YieldExpression yieldExpression }:
                    return YieldEmitter.TryEmitYieldExpressionStatement(ctx, yieldExpression, nextIndex, out entryIndex);

                case ExpressionStatement expressionStatement:
                    return ExpressionStatementEmitter.TryEmitExpressionStatement(
                        ctx, expressionStatement, nextIndex, out entryIndex);

                case VariableDeclaration declaration:
                    return DeclarationEmitter.TryEmitVariableDeclaration(ctx, declaration, nextIndex, out entryIndex);

                case WhileStatement whileStatement:
                    return TryEmitWhile(ctx, whileStatement, nextIndex, activeLabel, out entryIndex);

                case DoWhileStatement doWhileStatement:
                    return TryEmitDoWhile(ctx, doWhileStatement, nextIndex, activeLabel, out entryIndex);

                case ForStatement forStatement:
                    return TryEmitFor(ctx, forStatement, nextIndex, activeLabel, out entryIndex);

                case SwitchStatement switchStatement:
                    if (SwitchEmitter.TryEmitSwitch(ctx, switchStatement, nextIndex, activeLabel, out entryIndex))
                    {
                        return true;
                    }
                    ctx.SetFailureReason("Unsupported statement 'SwitchStatement'.");
                    entryIndex = -1;
                    return false;

                case TryStatement tryStatement:
                    return TryEmitter.TryEmitTry(ctx, tryStatement, nextIndex, activeLabel, out entryIndex);

                case ForEachStatement { Kind: ForEachKind.Of } forEachStatement
                    when IsSimpleForOfBinding(forEachStatement):
                    return TryEmitForOf(ctx, forEachStatement, nextIndex, activeLabel, out entryIndex);

                case ForEachStatement { Kind: ForEachKind.AwaitOf } forEachStatement
                    when IsSimpleForOfBinding(forEachStatement):
                    return TryEmitForAwaitOf(ctx, forEachStatement, nextIndex, activeLabel, out entryIndex);

                case ReturnStatement returnStatement:
                    // First check for yield return
                    if (returnStatement.Expression is YieldExpression yieldReturn &&
                        YieldEmitter.TryEmitReturnWithYield(ctx, yieldReturn, out entryIndex))
                    {
                        return true;
                    }
                    return DeclarationEmitter.TryEmitReturn(ctx, returnStatement, nextIndex, out entryIndex);

                case BreakStatement breakStatement:
                    return ControlFlowEmitter.TryEmitBreak(ctx, breakStatement, out entryIndex);

                case ContinueStatement continueStatement:
                    return ControlFlowEmitter.TryEmitContinue(ctx, continueStatement, out entryIndex);

                case WithStatement withStatement:
                    return TryEmitWith(ctx, withStatement, nextIndex, activeLabel, out entryIndex);

                case ClassDeclaration classDeclaration:
                    return DeclarationEmitter.TryEmitClassDeclaration(ctx, classDeclaration, nextIndex, out entryIndex);

                case ThrowStatement throwStatement:
                    return DeclarationEmitter.TryEmitThrow(ctx, throwStatement, out entryIndex);

                case LabeledStatement labeled:
                    // For loop-like statements, pass the label through - they handle it internally
                    if (labeled.Statement is WhileStatement or DoWhileStatement or ForStatement
                        or ForEachStatement or SwitchStatement)
                    {
                        statement = labeled.Statement;
                        activeLabel = labeled.Label;
                        continue;
                    }

                    // For non-loop statements (like blocks), wrap with LoopEnter/LoopExit
                    // to provide break targets for labeled break statements
                    return ControlFlowEmitter.TryEmitLabeledNonLoop(ctx, labeled, nextIndex, out entryIndex);

                default:
                    ctx.SetFailureReason($"Unsupported statement '{statement.GetType().Name}'.");
                    entryIndex = -1;
                    return false;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Loop Statement Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool TryEmitWhile(
        EmitContext ctx,
        WhileStatement whileStatement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        if (AstShapeAnalyzer.ContainsYield(whileStatement.Condition))
        {
            ctx.SetFailureReason("While condition contains unsupported yield shape.");
            entryIndex = -1;
            return false;
        }

        var whileStrict = IsStrictBlock(whileStatement.Body);
        if (!LoopNormalizer.TryNormalize(whileStatement, whileStrict, out var whilePlan, out var whileFailure))
        {
            ctx.SetFailureReason(whileFailure ?? "Failed to normalize while loop.");
            entryIndex = -1;
            return false;
        }

        return LoopEmitter.TryEmitLoopPlan(ctx, whilePlan, nextIndex, activeLabel, out entryIndex);
    }

    private static bool TryEmitDoWhile(
        EmitContext ctx,
        DoWhileStatement doWhileStatement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        if (AstShapeAnalyzer.ContainsYield(doWhileStatement.Condition))
        {
            ctx.SetFailureReason("Do/while condition contains unsupported yield shape.");
            entryIndex = -1;
            return false;
        }

        var doStrict = IsStrictBlock(doWhileStatement.Body);
        if (!LoopNormalizer.TryNormalize(doWhileStatement, doStrict, out var doWhilePlan, out var doFailure))
        {
            ctx.SetFailureReason(doFailure ?? "Failed to normalize do/while loop.");
            entryIndex = -1;
            return false;
        }

        return LoopEmitter.TryEmitLoopPlan(ctx, doWhilePlan, nextIndex, activeLabel, out entryIndex);
    }

    private static bool TryEmitFor(
        EmitContext ctx,
        ForStatement forStatement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        if (forStatement.Condition is not null && AstShapeAnalyzer.ContainsYield(forStatement.Condition))
        {
            ctx.SetFailureReason("For condition contains unsupported yield shape.");
            entryIndex = -1;
            return false;
        }

        if (forStatement.Increment is not null && AstShapeAnalyzer.ContainsYield(forStatement.Increment))
        {
            ctx.SetFailureReason("For increment contains unsupported yield shape.");
            entryIndex = -1;
            return false;
        }

        var forStrict = IsStrictBlock(forStatement.Body);
        if (!LoopNormalizer.TryNormalize(forStatement, forStrict, out var forPlan, out var forFailure))
        {
            ctx.SetFailureReason(forFailure ?? "Failed to normalize for loop.");
            entryIndex = -1;
            return false;
        }

        return LoopEmitter.TryEmitLoopPlan(ctx, forPlan, nextIndex, activeLabel, out entryIndex);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // For-Of/For-Await-Of Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool TryEmitForOf(
        EmitContext ctx,
        ForEachStatement forEachStatement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        // If binding target has yields anywhere (defaults or assignment target expressions),
        // wrap as StatementInstruction. The AST evaluator handles yield state-saving correctly.
        // This handles patterns like: for ([ {}[ yield ] ] of iterable) { }
        if (BindingTargetContainsYieldAnywhere(forEachStatement.Target) &&
            !AstShapeAnalyzer.StatementContainsYield(forEachStatement.Body) &&
            !AstShapeAnalyzer.ContainsYield(forEachStatement.Iterable))
        {
            entryIndex = ctx.Append(new StatementInstruction(nextIndex, forEachStatement));
            return true;
        }

        return ForOfEmitter.TryEmitForOf(ctx, forEachStatement, nextIndex, activeLabel, out entryIndex);
    }

    private static bool TryEmitForAwaitOf(
        EmitContext ctx,
        ForEachStatement forEachStatement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        // If binding target has yields anywhere (defaults or assignment target expressions),
        // wrap as StatementInstruction. Same reasoning as for regular for-of loops above.
        if (BindingTargetContainsYieldAnywhere(forEachStatement.Target) &&
            !AstShapeAnalyzer.StatementContainsYield(forEachStatement.Body) &&
            !AstShapeAnalyzer.ContainsYield(forEachStatement.Iterable))
        {
            entryIndex = ctx.Append(new StatementInstruction(nextIndex, forEachStatement));
            return true;
        }

        return ForOfEmitter.TryEmitForAwaitOf(ctx, forEachStatement, nextIndex, activeLabel, out entryIndex);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // With Statement Helper
    // ─────────────────────────────────────────────────────────────────────────

    private static bool TryEmitWith(
        EmitContext ctx,
        WithStatement withStatement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        // Yield is not allowed in the object expression
        if (AstShapeAnalyzer.ContainsYield(withStatement.Object))
        {
            ctx.SetFailureReason("With statement object expression contains unsupported yield shape.");
            entryIndex = -1;
            return false;
        }

        // If the body contains yield, we need to use EnterWith/LeaveWith instructions
        if (AstShapeAnalyzer.StatementContainsYield(withStatement.Body))
        {
            return WithEmitter.TryEmitWith(ctx, withStatement, nextIndex, activeLabel, out entryIndex);
        }

        // If no yield in body, emit as a simple statement instruction
        entryIndex = ctx.Append(new StatementInstruction(nextIndex, withStatement));
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper Methods
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsStrictBlock(StatementNode statement)
    {
        return statement is BlockStatement { IsStrict: true };
    }

    private static bool IsSimpleForOfBinding(ForEachStatement statement)
    {
        // We now allow identifier or destructuring targets for all declaration kinds.
        return statement.Target is not null;
    }

    /// <summary>
    /// Checks if a binding target contains yields anywhere - either in default values
    /// or in assignment target expressions (like [ {}[ yield ] ]).
    /// This is used to determine when to wrap declarations in StatementInstruction.
    /// </summary>
    private static bool BindingTargetContainsYieldAnywhere(BindingTarget target)
    {
        switch (target)
        {
            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    // Check for yields in default values
                    if (element.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(element.DefaultValue))
                    {
                        return true;
                    }

                    // Recursively check nested bindings
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
                    // Check for yields in default values
                    if (prop.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(prop.DefaultValue))
                    {
                        return true;
                    }

                    // Check for yields in computed property names
                    if (prop.NameExpression is not null && AstShapeAnalyzer.ContainsYield(prop.NameExpression))
                    {
                        return true;
                    }

                    // Recursively check nested bindings
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
                // Check if the assignment target expression contains a yield
                return AstShapeAnalyzer.ContainsYield(assignmentTarget.Expression);

            default:
                // IdentifierBinding doesn't have expressions or default values
                return false;
        }
    }
}
