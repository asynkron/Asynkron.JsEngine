namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ConditionalExpression expression)
    {
        private object? EvaluateConditional(JsEnvironment environment,
            EvaluationContext context)
        {
            var test = EvaluateExpression(expression.Test, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            return test.IsTruthy
                ? EvaluateExpression(expression.Consequent, environment, context).ToObject()
                : EvaluateExpression(expression.Alternate, environment, context).ToObject();
        }
    }
}
