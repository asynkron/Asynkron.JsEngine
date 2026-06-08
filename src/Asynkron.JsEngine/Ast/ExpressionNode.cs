using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Base type for expressions.
/// </summary>
public abstract record ExpressionNode(SourceReference? Source)
    : AstNode(Source), IAstCacheable<LoweredExpressionProgramCache>
{
    private LoweredExpressionProgramCache? _cachedLoweredProgram;

    LoweredExpressionProgramCache IAstCacheable<LoweredExpressionProgramCache>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(
            ref _cachedLoweredProgram,
            this,
            static expression => LoweredExpressionProgramCache.Build(expression));
    }
}
