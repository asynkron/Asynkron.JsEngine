
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateConditional(this ConditionalExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var test = expression.Test.EvaluateExpression(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return test.IsTruthy
            ? expression.Consequent.EvaluateExpression(environment, context)
            : expression.Alternate.EvaluateExpression(environment, context);
    }
}
