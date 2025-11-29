namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(ClassExpression expression)
    {
        private object? EvaluateClassExpression(JsEnvironment environment,
            EvaluationContext context)
        {
            return CreateClassValue(expression.Definition, environment, context);
        }
    }

}
