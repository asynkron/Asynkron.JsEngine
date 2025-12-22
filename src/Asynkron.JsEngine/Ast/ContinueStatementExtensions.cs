#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ContinueStatement statement)
    {
        private JsValue EvaluateContinueJsValue(EvaluationContext context)
        {
            context.SetContinue(statement.Label);
            // Return Unit (empty completion) per ES spec - UpdateEmpty will preserve the previous value
            return JsValue.Unit;
        }
    }
}
