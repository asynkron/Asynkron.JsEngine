#region

using Asynkron.JsEngine.Execution;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// JsValue-returning version for use in hot paths.
    /// </summary>
    private static JsValue EvaluateWhileJsValue(this WhileStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        var plan = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
        return plan.EvaluateLoopPlanJsValue(environment, context, loopLabel);
    }
}
