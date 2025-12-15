using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(FunctionDeclaration declaration)
    {
        private object? EvaluateFunctionDeclaration(JsEnvironment environment,
            EvaluationContext context)
        {
            return EvaluateFunctionDeclarationJsValue(declaration, environment, context).ToObject();
        }

        private JsValue EvaluateFunctionDeclarationJsValue(JsEnvironment environment,
            EvaluationContext context)
        {
            // Function declarations are hoisted and instantiated during FunctionDeclarationInstantiation.
            // The actual declaration statement is a no-op at runtime.
            return JsValue.Undefined;
        }
    }
}
