namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(DestructuringAssignmentExpression expression)
    {
        private object? EvaluateDestructuringAssignment(JsEnvironment environment, EvaluationContext context)
        {
            var assignedValueJs = EvaluateExpression(expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return assignedValueJs.ToObject();
            }

            var assignedValue = assignedValueJs.ToObject();
            // Reuse the same binding machinery as variable declarations so nested
            // destructuring assignments behave consistently.
            AssignBindingTarget(expression.Target, assignedValue, environment, context);
            return assignedValue;
        }
    }
}
