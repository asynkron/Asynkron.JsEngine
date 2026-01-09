
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateContinueJsValue(this ContinueStatement statement, EvaluationContext context)
    {
        context.SetContinue(statement.Label);
        // Return Unit (empty completion) per ES spec - UpdateEmpty will preserve the previous value
        return JsValue.Unit;
    }
}
