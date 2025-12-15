using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ContinueStatement statement)
    {
        private JsValue EvaluateContinueJsValue(EvaluationContext context)
        {
            context.SetContinue(statement.Label);
            return JsValue.Undefined;
        }
    }
}
