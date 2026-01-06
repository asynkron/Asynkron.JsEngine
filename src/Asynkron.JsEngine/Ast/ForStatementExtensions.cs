#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// JsValue-returning version for use in hot paths.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue EvaluateForJsValue(this ForStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        var plan = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
        // Always create a loop environment to ensure for-loops appear in the call stack
        // for debugging purposes, even when no block-scoped bindings exist
        var loopEnvironment =
            new JsEnvironment(environment, creatingSource: statement.Source, description: "for-loop");
        return plan.EvaluateLoopPlanJsValue(loopEnvironment, context, loopLabel);
    }
}
