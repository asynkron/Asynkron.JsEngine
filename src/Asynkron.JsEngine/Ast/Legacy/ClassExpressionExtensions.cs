
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateClassExpression(this ClassExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var inferredName = expression.Name ?? context.CurrentFunctionNameHint;
        return expression.Definition.CreateClassValue(environment, context, inferredName);
    }
}
