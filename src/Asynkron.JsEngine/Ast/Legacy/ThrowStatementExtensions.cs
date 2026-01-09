
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateThrowJsValue(this ThrowStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var jsValue = statement.Expression.EvaluateExpression(environment, context);
        // If evaluating the throw expression itself caused an abrupt completion
        // (e.g., ReferenceError from accessing undefined variable), propagate that
        // instead of overwriting with the expression result.
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        context.SetThrow(jsValue);
        return jsValue;
    }
}
