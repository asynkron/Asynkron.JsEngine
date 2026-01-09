#region

using Asynkron.JsEngine.Execution;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// JsValue-returning version for use in hot paths.
    /// </summary>
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateWhileJsValue(this WhileStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        var plan = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
        return plan.EvaluateLoopPlanJsValue(environment, context, loopLabel);
    }
}
