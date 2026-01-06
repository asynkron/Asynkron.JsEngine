#region

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateBreakJsValue(this BreakStatement statement, EvaluationContext context)
    {
        context.SetBreak(statement.Label);
        // Return Unit (empty completion) per ES spec - UpdateEmpty will preserve the previous value
        return JsValue.Unit;
    }
}
