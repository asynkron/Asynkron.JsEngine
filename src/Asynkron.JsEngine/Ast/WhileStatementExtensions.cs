using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(WhileStatement statement)
    {
        private object? EvaluateWhile(JsEnvironment environment, EvaluationContext context,
            Symbol? loopLabel)
        {
            var plan = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
            return EvaluateLoopPlan(plan, environment, context, loopLabel);
        }
    }
}
