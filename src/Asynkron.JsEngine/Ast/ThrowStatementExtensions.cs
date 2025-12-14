namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ThrowStatement statement)
    {
        private object? EvaluateThrow(JsEnvironment environment, EvaluationContext context)
        {
            var jsValue = EvaluateExpression(statement.Expression, environment, context);
            // If evaluating the throw expression itself caused an abrupt completion
            // (e.g., ReferenceError from accessing undefined variable), propagate that
            // instead of overwriting with the expression result.
            if (context.ShouldStopEvaluation)
            {
                return context.FlowValue;
            }
            context.SetThrow(jsValue);
            return jsValue.ToObject();
        }
    }
}
