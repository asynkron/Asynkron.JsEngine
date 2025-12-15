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
            // Always create a loop environment to ensure for-loops appear in the call stack
            // for debugging purposes, even when no block-scoped bindings exist
            var loopEnvironment = new JsEnvironment(environment, creatingSource: statement.Source, description: "for-loop");
            return EvaluateLoopPlan(plan, loopEnvironment, context, loopLabel);
        }
    }
}
