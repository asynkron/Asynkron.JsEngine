using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     A single variable declarator within a declaration statement.
/// </summary>
public sealed partial record VariableDeclarator(SourceReference? Source, BindingTarget Target, ExpressionNode? Initializer)
    : IAstCacheable<LoweredExpressionProgramCache>;

public sealed partial record VariableDeclarator
{
    private LoweredExpressionProgramCache? _cachedInitializerProgram;

    LoweredExpressionProgramCache IAstCacheable<LoweredExpressionProgramCache>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(
            ref _cachedInitializerProgram,
            this,
            static declarator => LoweredExpressionProgramCache.Build(declarator.Initializer));
    }
}
