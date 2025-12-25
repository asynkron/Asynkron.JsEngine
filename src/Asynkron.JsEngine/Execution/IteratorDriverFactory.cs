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

        // Check if iteration environment can be reused (no closures in loop body).
        // This enables JsVariable caching for let/const bindings.
        var canReuseIterationEnvironment = !ContainsInnerFunctionExpression(rewrittenBody);

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
            canReuseIterationEnvironment);
    }
}
