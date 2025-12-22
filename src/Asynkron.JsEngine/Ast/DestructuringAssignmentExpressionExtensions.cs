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

            // Reuse the same binding machinery as variable declarations so nested
            // destructuring assignments behave consistently.
            expression.Target.AssignBindingTarget(assignedValueJs, environment, context);
            return assignedValueJs;
        }
    }
}
