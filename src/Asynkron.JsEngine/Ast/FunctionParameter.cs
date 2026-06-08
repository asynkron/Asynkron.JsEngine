using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a single function parameter. Parameters may use destructuring or rest syntax,
///     so we capture the typed binding target while exposing default values.
/// </summary>
public sealed partial record FunctionParameter(
    SourceReference? Source,
    Symbol? Name,
    bool IsRest,
    BindingTarget? Pattern,
    ExpressionNode? DefaultValue)
    : IAstCacheable<LoweredExpressionProgramCache>;

public sealed partial record FunctionParameter
{
    private LoweredExpressionProgramCache? _cachedDefaultProgram;

    LoweredExpressionProgramCache IAstCacheable<LoweredExpressionProgramCache>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(
            ref _cachedDefaultProgram,
            this,
            static parameter => LoweredExpressionProgramCache.Build(parameter.DefaultValue));
    }
}
