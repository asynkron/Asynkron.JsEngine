using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;

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

        /// <summary>
        /// JsValue-returning version for use in hot paths.
        /// </summary>
        private JsValue EvaluateWhileJsValue(JsEnvironment environment, EvaluationContext context,
            Symbol? loopLabel)
        {
            var plan = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
            return EvaluateLoopPlanJsValue(plan, environment, context, loopLabel);
        }
    }
}
