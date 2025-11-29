namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ContinueStatement statement)
    {
        private object EvaluateContinue(EvaluationContext context)
        {
            context.SetContinue(statement.Label);
            return EmptyCompletion;
        }
    }
}
