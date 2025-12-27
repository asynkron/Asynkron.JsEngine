#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a classic C-style for loop.
/// </summary>
public sealed record ForStatement(
    SourceReference? Source,
    StatementNode? Initializer,
    ExpressionNode? Condition,
    ExpressionNode? Increment,
    StatementNode Body,
    int PerIterationScopeId = -1,
    int PerIterationParentScopeId = -1,
    int PerIterationSlotCount = -1,
    ImmutableArray<int> PerIterationSlotIndices = default) : StatementNode(Source), IAstCacheable<LoopPlan>
{
    private LoopPlan? _cachedPlan;

    LoopPlan IAstCacheable<LoopPlan>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedPlan, this, static self =>
        {
            var isStrict = self.Body is BlockStatement { IsStrict: true };
            if (!LoopNormalizer.TryNormalize(self, isStrict, out var plan, out var failureReason))
            {
                throw new NotSupportedException(failureReason ?? "Failed to normalize for loop.");
            }

            return plan;
        });
    }
}
