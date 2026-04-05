#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateIndexAssignment(this IndexAssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        return EvaluateCachedExpressionProgram(
            expression,
            environment,
            context,
            "Dynamic index assignment");
    }
}
