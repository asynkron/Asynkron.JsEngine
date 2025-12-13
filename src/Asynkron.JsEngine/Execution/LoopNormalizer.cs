using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

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
                    CollectBindingNames(declarator.Target, bindingNames);
                }
                perIterationBindings = bindingNames.ToImmutableArray();
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
            perIterationBindings);
        failureReason = null;
        return true;
    }

    private static void CollectBindingNames(BindingTarget target, List<Symbol> names)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    names.Add(id.Name);
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectBindingNames(element.Target, names);
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        CollectBindingNames(property.Target, names);
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private static LoopPlan CreateSimplePlan(
        LoopKind kind,
        ImmutableArray<StatementNode> leading,
        ImmutableArray<StatementNode> conditionPrologue,
        ExpressionNode condition,
        BlockStatement body,
        ImmutableArray<StatementNode> postIteration,
        bool conditionAfterBody,
        ImmutableArray<Symbol> perIterationBindings = default)
    {
        var allowIterationEnvironmentPooling =
            !TypedAstEvaluator.ContainsInnerFunctionExpression(body) &&
            !TypedAstEvaluator.ContainsInnerFunctionExpression(condition) &&
            !StatementsContainInnerFunctionExpression(leading) &&
            !StatementsContainInnerFunctionExpression(conditionPrologue) &&
            !StatementsContainInnerFunctionExpression(postIteration);

        return new LoopPlan(
            kind,
            leading,
            conditionPrologue,
            condition,
            body,
            postIteration,
            conditionAfterBody,
            perIterationBindings,
            allowIterationEnvironmentPooling);
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
