namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(SequenceExpression expression)
    {
        private object? EvaluateSequence(JsEnvironment environment,
            EvaluationContext context)
        {
            _ = EvaluateExpression(expression.Left, environment, context);
            return context.ShouldStopEvaluation
                ? Symbol.Undefined
                : EvaluateExpression(expression.Right, environment, context);
        }
    }
}
