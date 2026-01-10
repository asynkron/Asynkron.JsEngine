#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// JsValue-returning version for use in hot paths.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
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
