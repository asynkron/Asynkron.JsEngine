
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateClassExpression(this ClassExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var inferredName = expression.Name ?? context.CurrentFunctionNameHint;
        return expression.Definition.CreateClassValue(environment, context, inferredName);
    }
}
