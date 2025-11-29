using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BreakStatement statement)
    {
        private object EvaluateBreak(EvaluationContext context)
        {
            context.SetBreak(statement.Label);
            return EmptyCompletion;
        }
    }
}
