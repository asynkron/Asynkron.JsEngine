using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a while loop.
/// </summary>
public sealed record WhileStatement(SourceReference? Source, ExpressionNode Condition, StatementNode Body)
    : StatementNode(Source), IAstCacheable<LoopPlan>
{
    private LoopPlan? _cachedPlan;

    LoopPlan IAstCacheable<LoopPlan>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedPlan, this, static self =>
        {
            var isStrict = self.Body is BlockStatement { IsStrict: true };
            if (!LoopNormalizer.TryNormalize(self, isStrict, out var plan, out var failureReason))
            {
                throw new NotSupportedException(failureReason ?? "Failed to normalize while loop.");
            }

            return plan;
        });
    }
}
