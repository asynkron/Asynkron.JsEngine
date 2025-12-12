using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(DoWhileStatement statement)
    {
        private object? EvaluateDoWhile(JsEnvironment environment,
            EvaluationContext context,
            Symbol? loopLabel)
        {
            var plan = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
            return EvaluateLoopPlan(plan, environment, context, loopLabel);
        }
    }
}
