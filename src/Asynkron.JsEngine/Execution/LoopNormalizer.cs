#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Produces normalized <see cref="LoopPlan" /> instances for while/do/for loops.
///     Yield lowering and resume-slot plumbing will be layered on top of these plans
///     in subsequent steps so both the lowerer and IR builder can share the same
///     loop shape.
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
        var perIterationBindings = ImmutableArray<Symbol>.Empty;

        if (statement.Initializer is not null)
        {
            leadingStatements = [statement.Initializer];

            // Check if the initializer contains let/const declarations
            // These require per-iteration environment creation
            if (statement.Initializer is VariableDeclaration
                {
                    Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                } decl)
            {
                var bindingNames = new List<Symbol>();
                foreach (var declarator in decl.Declarators)
                {
                    declarator.Target.CollectSymbolsFromBinding(bindingNames);
                }

                perIterationBindings = [..bindingNames];
            }
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
            false,
            perIterationBindings,
            statement.PerIterationScopeId,
            statement.PerIterationParentScopeId,
            statement.PerIterationSlotCount,
            statement.PerIterationSlotIndices);
        failureReason = null;
        return true;
    }

    public static bool TryNormalize(LoopStatementNode statement, bool isStrict,
        out LoopPlan plan, out string? failureReason)
    {
        return statement switch
        {
            WhileStatement whileStatement => TryNormalize(whileStatement, isStrict, out plan, out failureReason),
            DoWhileStatement doWhileStatement => TryNormalize(doWhileStatement, isStrict, out plan, out failureReason),
            ForStatement forStatement => TryNormalize(forStatement, isStrict, out plan, out failureReason),
            _ => throw new NotSupportedException($"Unknown loop statement type: {statement.GetType().Name}")
        };
    }

    private static LoopPlan CreateSimplePlan(
        LoopKind kind,
        ImmutableArray<StatementNode> leading,
        ImmutableArray<StatementNode> conditionPrologue,
        ExpressionNode condition,
        BlockStatement body,
        ImmutableArray<StatementNode> postIteration,
        bool conditionAfterBody,
        ImmutableArray<Symbol> perIterationBindings = default,
        int iterationScopeId = -1,
        int iterationParentScopeId = -1,
        int iterationSlotCount = -1,
        ImmutableArray<int> perIterationSlotIndices = default)
    {
        // Disable pooling for loops with inner function expressions (closures capture environment reference).
        // Async loops with await are now handled correctly - the generator tracks resume state and
        // skips pool return when resuming mid-iteration (see _justResumedFromSuspend flag).
        var allowIterationEnvironmentPooling =
            !TypedAstEvaluator.ContainsInnerFunctionExpression(body) &&
            !TypedAstEvaluator.ContainsInnerFunctionExpression(condition) &&
            !StatementsContainInnerFunctionExpression(leading) &&
            !StatementsContainInnerFunctionExpression(conditionPrologue) &&
            !StatementsContainInnerFunctionExpression(postIteration);

        if (iterationScopeId < 0 && !perIterationBindings.IsDefaultOrEmpty)
        {
            iterationScopeId = SyntheticScopeIdAllocator.Next();
        }

        return new LoopPlan(
            kind,
            leading,
            conditionPrologue,
            condition,
            body,
            postIteration,
            conditionAfterBody,
            perIterationBindings,
            allowIterationEnvironmentPooling,
            iterationScopeId,
            iterationParentScopeId,
            iterationSlotCount,
            perIterationSlotIndices);
    }

    private static bool StatementsContainInnerFunctionExpression(ImmutableArray<StatementNode> statements)
    {
        if (statements.IsDefaultOrEmpty)
        {
            return false;
        }

        var synthetic = new BlockStatement(null, statements, false);
        return TypedAstEvaluator.ContainsInnerFunctionExpression(synthetic);
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
