using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(DestructuringAssignmentExpression expression)
    {
        private JsValue EvaluateDestructuringAssignment(JsEnvironment environment, EvaluationContext context)
        {
            var assignedValueJs = expression.Value.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return assignedValueJs;
            }

            var assignedValue = assignedValueJs.ToObject();
            // Reuse the same binding machinery as variable declarations so nested
            // destructuring assignments behave consistently.
            expression.Target.AssignBindingTarget(assignedValue, environment, context);
            return JsValue.FromObjectUnsafe(assignedValue);
        }
    }
}
