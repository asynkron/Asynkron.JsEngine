
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateContinueJsValue(this ContinueStatement statement, EvaluationContext context)
    {
        context.SetContinue(statement.Label);
        // Return Unit (empty completion) per ES spec - UpdateEmpty will preserve the previous value
        return JsValue.Unit;
    }
}
