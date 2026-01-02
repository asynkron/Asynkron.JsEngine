#region

using System.Collections.Immutable;
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

        // Collect per-iteration bindings from Target if DeclarationKind is let/const/using/await using
        // (independent of ScopeAnalyzer which may not have run)
        var perIterationBindings = statement.PerIterationBindings;
        if (perIterationBindings.IsDefault && statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing)
        {
            var bindingNames = new List<Symbol>();
            CollectBindingNames(statement.Target, bindingNames);
            perIterationBindings = [..bindingNames];
        }

        var perIterationSlotCount = statement.PerIterationSlotCount >= 0
            ? statement.PerIterationSlotCount
            : (!perIterationBindings.IsDefaultOrEmpty ? perIterationBindings.Length : -1);

        var perIterationSlotIndices = !statement.PerIterationSlotIndices.IsDefaultOrEmpty
            ? statement.PerIterationSlotIndices
            : (!perIterationBindings.IsDefaultOrEmpty
                ? Enumerable.Repeat(-1, perIterationBindings.Length).ToImmutableArray()
                : ImmutableArray<int>.Empty);

        return new IteratorDriverPlan(
            kind,
            statement.Iterable,
            statement.Target,
            statement.DeclarationKind,
            rewrittenBody,
            statement.PerIterationScopeId,
            statement.PerIterationParentScopeId,
            perIterationSlotCount,
            perIterationSlotIndices,
            perIterationBindings,
            canReuseIterationEnvironment);
    }

    private static void CollectBindingNames(BindingTarget target, List<Symbol> names)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    names.Add(id.Name);
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectBindingNames(element.Target, names);
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        CollectBindingNames(property.Target, names);
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }

                    break;
            }

            break;
        }
    }
}
