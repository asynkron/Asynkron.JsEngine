using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BreakStatement statement)
    {
        private object EvaluateBreak(EvaluationContext context)
        {
            return EvaluateBreakJsValue(statement, context).ToObject()!;
        }

        private JsValue EvaluateBreakJsValue(EvaluationContext context)
        {
            context.SetBreak(statement.Label);
            return JsValue.Undefined;
        }
    }
}
