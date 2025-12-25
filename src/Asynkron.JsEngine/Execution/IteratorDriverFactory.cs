#region

using Asynkron.JsEngine.Ast;
using static Asynkron.JsEngine.Ast.TypedAstEvaluator;

#endregion

namespace Asynkron.JsEngine.Execution;

internal static class IteratorDriverFactory
{
    public static IteratorDriverPlan CreatePlan(ForEachStatement statement, BlockStatement rewrittenBody)
    {
        var kind = statement.Kind == ForEachKind.AwaitOf
            ? IteratorDriverKind.Await
            : IteratorDriverKind.Sync;

        // Check if there are closures in body or target (destructuring defaults).
        var bodyHasClosures = ContainsInnerFunctionExpression(rewrittenBody);
        var targetHasClosures = ContainsInnerFunctionExpression(statement.Target);
        var iterableHasClosures = ContainsInnerFunctionExpression(statement.Iterable);

        // Check if iteration environment can be reused (no closures in loop body or destructuring pattern).
        // This enables JsVariable caching for let/const bindings.
        // We must check BOTH body AND target because closures in destructuring defaults
        // also capture the iteration environment.
        var canReuseIterationEnvironment = !bodyHasClosures && !targetHasClosures;

        // Check if loop environment can be pooled (no closures anywhere that could capture it).
        // When closures exist, they may capture the loop environment or environments that have
        // the loop environment in their scope chain. When pooled environments are returned,
        // Reset() sets Enclosing to null, breaking the scope chain.
        var canPoolLoopEnvironment = !bodyHasClosures && !targetHasClosures && !iterableHasClosures;

        return new IteratorDriverPlan(
            kind,
            statement.Iterable,
            statement.Target,
            statement.DeclarationKind,
            rewrittenBody,
            statement.PerIterationScopeId,
            statement.PerIterationSlotCount,
            statement.PerIterationSlotIndices,
            statement.PerIterationBindings,
            canReuseIterationEnvironment,
            canPoolLoopEnvironment);
    }
}
