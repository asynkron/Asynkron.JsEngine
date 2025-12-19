using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassExpression expression)
    {
        private JsValue EvaluateClassExpression(JsEnvironment environment,
            EvaluationContext context)
        {
            var inferredName = expression.Name ?? context.CurrentFunctionNameHint;
            return JsValue.FromObjectUnsafe(expression.Definition.CreateClassValue(environment, context, inferredName));
        }
    }
}
