namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ReturnStatement statement)
    {
        private object? EvaluateReturn(
            JsEnvironment environment,
            EvaluationContext context)
        {
            var value = statement.Expression is null
                ? null
                : EvaluateExpression(statement.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return value;
            }

            context.SetReturn(value);
            return value;
        }
    }
}
