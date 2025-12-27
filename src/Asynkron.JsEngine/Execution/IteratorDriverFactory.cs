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

        // Check if iteration environment can be reused (no closures in loop body or target).
        // This enables JsVariable caching for let/const bindings.
        // Must check both body AND target - closures in destructuring defaults also capture
        // the iteration environment.
        var canReuseIterationEnvironment = !ContainsInnerFunctionExpression(rewrittenBody) &&
                                           !ContainsInnerFunctionExpression(statement.Target);

        return new IteratorDriverPlan(
            kind,
            statement.Iterable,
            statement.Target,
            statement.DeclarationKind,
            rewrittenBody,
            statement.PerIterationScopeId,
            statement.PerIterationParentScopeId,
            statement.PerIterationSlotCount,
            statement.PerIterationSlotIndices,
            statement.PerIterationBindings,
            canReuseIterationEnvironment);
    }
}
