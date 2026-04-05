namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateDestructuringAssignment(this DestructuringAssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        return EvaluateCachedExpressionProgram(
            expression,
            environment,
            context,
            "Dynamic destructuring assignment");
    }
}
