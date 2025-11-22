using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Produces normalized <see cref="LoopPlan"/> instances for while/do/for loops.
/// Yield lowering and resume-slot plumbing will be layered on top of these plans
/// in subsequent steps so both the lowerer and IR builder can share the same
/// loop shape.
/// </summary>
internal static class LoopNormalizer
{
    public static bool TryNormalize(WhileStatement statement, bool isStrict,
        out LoopPlan plan, out string? failureReason)
    {
        plan = CreateSimplePlan(
            LoopKind.While,
            ImmutableArray<StatementNode>.Empty,
            ImmutableArray<StatementNode>.Empty,
            statement.Condition,
            EnsureBlock(statement.Body, isStrict),
            ImmutableArray<StatementNode>.Empty,
            false);
        failureReason = null;
        return true;
    }

    public static bool TryNormalize(DoWhileStatement statement, bool isStrict,
        out LoopPlan plan, out string? failureReason)
    {
        plan = CreateSimplePlan(
            LoopKind.DoWhile,
            ImmutableArray<StatementNode>.Empty,
            ImmutableArray<StatementNode>.Empty,
            statement.Condition,
            EnsureBlock(statement.Body, isStrict),
            ImmutableArray<StatementNode>.Empty,
            true);
        failureReason = null;
        return true;
    }

    public static bool TryNormalize(ForStatement statement, bool isStrict,
        out LoopPlan plan, out string? failureReason)
    {
        var leadingStatements = ImmutableArray<StatementNode>.Empty;
        if (statement.Initializer is not null)
        {
            leadingStatements = [statement.Initializer];
        }

        var postIteration = ImmutableArray<StatementNode>.Empty;
        if (statement.Increment is not null)
        {
            postIteration =
            [
                new ExpressionStatement(statement.Increment.Source, statement.Increment)
            ];
        }

        var condition = statement.Condition ?? new LiteralExpression(statement.Source, true);

        plan = CreateSimplePlan(
            LoopKind.For,
            leadingStatements,
            ImmutableArray<StatementNode>.Empty,
            condition,
            EnsureBlock(statement.Body, isStrict),
            postIteration,
            false);
        failureReason = null;
        return true;
    }

    private static LoopPlan CreateSimplePlan(
        LoopKind kind,
        ImmutableArray<StatementNode> leading,
        ImmutableArray<StatementNode> conditionPrologue,
        ExpressionNode condition,
        BlockStatement body,
        ImmutableArray<StatementNode> postIteration,
        bool conditionAfterBody)
    {
        return new LoopPlan(
            kind,
            leading,
            conditionPrologue,
            condition,
            body,
            postIteration,
            conditionAfterBody);
    }

    private static BlockStatement EnsureBlock(StatementNode statement, bool isStrict)
    {
        if (statement is BlockStatement block)
        {
            return block;
        }

        return new BlockStatement(statement.Source, [statement], isStrict);
    }
}
