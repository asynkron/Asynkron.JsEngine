
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateFunctionDeclarationJsValue()
    {
        // Function declarations are hoisted and instantiated during FunctionDeclarationInstantiation.
        // The actual declaration statement is a no-op at runtime.
        // Per ES spec, FunctionDeclaration returns NormalCompletion(empty).
        return JsValue.Unit;
    }
}
