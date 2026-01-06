#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using static Asynkron.JsEngine.Ast.TypedAstEvaluator;

#endregion

namespace Asynkron.JsEngine.Execution;

internal static class IteratorDriverFactory
{
    /// <remarks>
    /// Always reach this through the cached path (IAstCacheable&lt;IteratorDriverPlan&gt;.GetOrCreateCache)
    /// so a ForEachStatement has exactly one plan instance.
    /// </remarks>
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

        // Collect per-iteration bindings from Target if DeclarationKind is let/const/using/await using
        // (independent of ScopeAnalyzer which may not have run)
        var perIterationBindings = statement.PerIterationBindings;
        if (perIterationBindings.IsDefaultOrEmpty && statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                or VariableKind.Using or VariableKind.AwaitUsing)
        {
            var bindingNames = new List<Symbol>();
            statement.Target.CollectSymbolsFromBinding(bindingNames);
            perIterationBindings = [.. bindingNames];
        }

        var iterationScopeId = statement.PerIterationScopeId;
        if (iterationScopeId < 0 && !perIterationBindings.IsDefaultOrEmpty)
        {
            iterationScopeId = SyntheticScopeIdAllocator.Next();
        }

        var perIterationSlotCount = statement.PerIterationSlotCount >= 0
            ? statement.PerIterationSlotCount
            : !perIterationBindings.IsDefaultOrEmpty
                ? perIterationBindings.Length
                : -1;

        // Use sequential slot indices (0, 1, 2...) when not pre-stamped by execution plan.
        // This enables the fast path in ExecuteIteratorDriverJsValue even without IR compilation.
        var perIterationSlotIndices = !statement.PerIterationSlotIndices.IsDefaultOrEmpty
            ? statement.PerIterationSlotIndices
            : !perIterationBindings.IsDefaultOrEmpty
                ? [.. Enumerable.Range(0, perIterationBindings.Length)]
                : ImmutableArray<int>.Empty;

        return new IteratorDriverPlan(
            kind,
            statement.Iterable,
            statement.Target,
            statement.DeclarationKind,
            rewrittenBody,
            iterationScopeId,
            statement.PerIterationParentScopeId,
            perIterationSlotCount,
            perIterationSlotIndices,
            perIterationBindings,
            canReuseIterationEnvironment);
    }
}
