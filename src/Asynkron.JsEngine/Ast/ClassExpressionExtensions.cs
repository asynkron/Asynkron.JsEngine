namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassExpression expression)
    {
        private object? EvaluateClassExpression(JsEnvironment environment,
            EvaluationContext context)
        {
            var inferredName = expression.Name ?? context.CurrentFunctionNameHint;
            return CreateClassValue(expression.Definition, environment, context, inferredName);
        }
    }
}
