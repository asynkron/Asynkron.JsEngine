using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateFunctionDeclarationJsValue()
    {
        // Function declarations are hoisted and instantiated during FunctionDeclarationInstantiation.
        // The actual declaration statement is a no-op at runtime.
        // Per ES spec, FunctionDeclaration returns NormalCompletion(empty).
        return JsValue.Unit;
    }
}
