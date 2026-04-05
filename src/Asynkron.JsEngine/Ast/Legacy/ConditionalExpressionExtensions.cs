
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateConditional(this ConditionalExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var test = EvaluateCachedExpressionProgram(
            expression.Test,
            environment,
            context,
            "Dynamic conditional test");
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return test.IsTruthy
            ? EvaluateCachedExpressionProgram(
                expression.Consequent,
                environment,
                context,
                "Dynamic conditional consequent")
            : EvaluateCachedExpressionProgram(
                expression.Alternate,
                environment,
                context,
                "Dynamic conditional alternate");
    }
}
