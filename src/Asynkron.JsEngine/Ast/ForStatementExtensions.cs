using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ForStatement statement)
    {
        private object? EvaluateFor(JsEnvironment environment, EvaluationContext context,
            Symbol? loopLabel)
        {
            var plan = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
            var bodyHoist = ((IAstCacheable<HoistPlan>)plan.Body).GetOrCreateCache();
            var needsLoopEnvironment = bodyHoist.NeedsEnvironment || !plan.PerIterationBindings.IsDefaultOrEmpty;

            var loopEnvironment = needsLoopEnvironment
                ? new JsEnvironment(environment, creatingSource: statement.Source, description: "for-loop")
                : environment;
            return EvaluateLoopPlan(plan, loopEnvironment, context, loopLabel);
        }
    }
}
