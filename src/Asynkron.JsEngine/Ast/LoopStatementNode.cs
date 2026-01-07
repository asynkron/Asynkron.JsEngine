#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Base type for loop statements (while, do/while, for).
///     Provides common LoopPlan caching infrastructure.
/// </summary>
public abstract record LoopStatementNode(SourceReference? Source) : StatementNode(Source), IAstCacheable<LoopPlan>
{
    private LoopPlan? _cachedPlan;

    /// <summary>
    ///     The loop body statement.
    /// </summary>
    public abstract StatementNode Body { get; init; }

    /// <summary>
    ///     The name of this loop type for error messages (e.g., "while", "do/while", "for").
    /// </summary>
    protected abstract string LoopTypeName { get; }

    LoopPlan IAstCacheable<LoopPlan>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedPlan, this, static self =>
        {
            var isStrict = self.Body is BlockStatement { IsStrict: true };
            return !LoopNormalizer.TryNormalize(self, isStrict, out var plan, out var failureReason)
                ? throw new NotSupportedException(failureReason ?? $"Failed to normalize {self.LoopTypeName} loop.")
                : plan;
        });
    }
}
