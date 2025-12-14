using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ConditionalExpression expression)
    {
        private JsValue EvaluateConditional(JsEnvironment environment,
            EvaluationContext context)
        {
            var test = EvaluateExpression(expression.Test, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return test.IsTruthy
                ? EvaluateExpression(expression.Consequent, environment, context)
                : EvaluateExpression(expression.Alternate, environment, context);
        }
    }
}
