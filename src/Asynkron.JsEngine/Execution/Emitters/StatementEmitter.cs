#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

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

                case FunctionDeclaration funcDecl:
                    // Block-scoped function declarations need to be instantiated at runtime.
                    // Function-scoped declarations are hoisted (no-op at runtime).
                    entryIndex = ctx.Append(new FunctionDeclarationInstruction(
                        nextIndex,
                        DeclarationEmitter.CreateFunctionDeclarationDescriptor(funcDecl)));
                    return true;

                case IfStatement ifStatement:
                    return ControlFlowEmitter.TryEmitIf(ctx, ifStatement, nextIndex, activeLabel, out entryIndex);

                case EmptyStatement:
                    entryIndex = nextIndex;
                    return true;

                case ExpressionStatement { Expression: YieldExpression yieldExpression }:
                    return YieldEmitter.TryEmitYieldExpressionStatement(ctx, yieldExpression, nextIndex,
                        out entryIndex);

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
                    return SwitchEmitter.TryEmitSwitch(ctx, switchStatement, nextIndex, activeLabel, out entryIndex);

                case TryStatement tryStatement:
                    return TryEmitter.TryEmitTry(ctx, tryStatement, nextIndex, activeLabel, out entryIndex);

                case ForEachStatement { Kind: ForEachKind.In } forInStatement:
                    return TryEmitForIn(ctx, forInStatement, nextIndex, activeLabel, out entryIndex);

                case ForEachStatement { Kind: ForEachKind.Of or ForEachKind.AwaitOf } forEachStatement
                    when true:
                    return TryEmitForEach(ctx, forEachStatement, nextIndex, activeLabel, out entryIndex);

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

                    // For non-loop statements (like blocks), wrap with BreakableEnter/BreakableExit
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

        var whileStrict = EmitContext.IsStrictBlock(whileStatement.Body);
        if (!LoopNormalizer.TryNormalize(whileStatement, whileStrict, out var whilePlan, out var whileFailure))
        {
            ctx.SetFailureReason(
                whileFailure ?? "Failed to normalize while loop.",
                ExecutionPlanFailureCode.NormalizationFailed);
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

        var doStrict = EmitContext.IsStrictBlock(doWhileStatement.Body);
        if (!LoopNormalizer.TryNormalize(doWhileStatement, doStrict, out var doWhilePlan, out var doFailure))
        {
            ctx.SetFailureReason(
                doFailure ?? "Failed to normalize do/while loop.",
                ExecutionPlanFailureCode.NormalizationFailed);
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

        var forStrict = EmitContext.IsStrictBlock(forStatement.Body);
        if (!LoopNormalizer.TryNormalize(forStatement, forStrict, out var forPlan, out var forFailure))
        {
            ctx.SetFailureReason(
                forFailure ?? "Failed to normalize for loop.",
                ExecutionPlanFailureCode.NormalizationFailed);
            entryIndex = -1;
            return false;
        }

        return LoopEmitter.TryEmitLoopPlan(ctx, forPlan, nextIndex, activeLabel, out entryIndex);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // For-Of/For-Await-Of Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool TryEmitForEach(
        EmitContext ctx,
        ForEachStatement forEachStatement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        return ForOfEmitter.TryEmit(ctx, forEachStatement, nextIndex, activeLabel, out entryIndex);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // For-In Helper
    // ─────────────────────────────────────────────────────────────────────────

    private static bool TryEmitForIn(
        EmitContext ctx,
        ForEachStatement forInStatement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        return ForInEmitter.TryEmit(ctx, forInStatement, nextIndex, activeLabel, out entryIndex);
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

        // Always use EnterWith/LeaveWith instructions for proper IR execution.
        // This removes the StatementInstruction fallback for with statements.
        return WithEmitter.TryEmitWith(ctx, withStatement, nextIndex, activeLabel, out entryIndex);
    }
}
