using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ConditionalExpression expression)
    {
        private JsValue EvaluateConditional(JsEnvironment environment,
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
}
