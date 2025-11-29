namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IndexAssignmentExpression expression)
    {
        private object? EvaluateIndexAssignment(JsEnvironment environment,
            EvaluationContext context)
        {
            if (expression.Target is SuperExpression)
            {
                throw new InvalidOperationException(
                    $"Assigning through super is not supported.{GetSourceInfo(context, expression.Source)}");
            }

            var target = EvaluateExpression(expression.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var index = EvaluateExpression(expression.Index, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var value = EvaluateExpression(expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            AssignPropertyValue(target, index, value, context);
            return value;
        }
    }
}
