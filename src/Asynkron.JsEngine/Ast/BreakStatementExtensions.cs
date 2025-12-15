using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BreakStatement statement)
    {
        private JsValue EvaluateBreakJsValue(EvaluationContext context)
        {
            context.SetBreak(statement.Label);
            return JsValue.Undefined;
        }
    }
}
