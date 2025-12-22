#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BreakStatement statement)
    {
        private JsValue EvaluateBreakJsValue(EvaluationContext context)
        {
            context.SetBreak(statement.Label);
            // Return Unit (empty completion) per ES spec - UpdateEmpty will preserve the previous value
            return JsValue.Unit;
        }
    }
}
