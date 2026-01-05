#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents for...in / for...of / for await...of loops.
/// </summary>
public sealed record ForEachStatement(
    SourceReference? Source,
    BindingTarget Target,
    ExpressionNode Iterable,
    StatementNode Body,
    ForEachKind Kind,
    VariableKind? DeclarationKind,
    int PerIterationScopeId = -1,
    int PerIterationParentScopeId = -1,
    int PerIterationSlotCount = -1,
    ImmutableArray<int> PerIterationSlotIndices = default,
    ImmutableArray<Symbol> PerIterationBindings = default) : StatementNode(Source), IAstCacheable<IteratorDriverPlan>
{
    private IteratorDriverPlan? _cachedPlan;

    IteratorDriverPlan IAstCacheable<IteratorDriverPlan>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedPlan, this, static self =>
        {
            var isStrict = self.Body is BlockStatement { IsStrict: true };
            var planBody = self.Body is BlockStatement blockBody
                ? blockBody
                : new BlockStatement(self.Source, [self.Body], isStrict);

            return IteratorDriverFactory.CreatePlan(self, planBody);
        });
    }
}
