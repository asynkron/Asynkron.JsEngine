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
    private bool _canPoolLoopEnvironmentComputed;
    private bool _canPoolLoopEnvironment;

    /// <summary>
    /// Returns true if the loop environment can be pooled (no closures in target or iterable).
    /// Cached to avoid repeated ContainsInnerFunctionExpression checks.
    /// </summary>
    internal bool CanPoolLoopEnvironment
    {
        get
        {
            if (!_canPoolLoopEnvironmentComputed)
            {
                _canPoolLoopEnvironment = !TypedAstEvaluator.ContainsInnerFunctionExpression(Target) &&
                                          !TypedAstEvaluator.ContainsInnerFunctionExpression(Iterable);
                _canPoolLoopEnvironmentComputed = true;
            }
            return _canPoolLoopEnvironment;
        }
    }

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
