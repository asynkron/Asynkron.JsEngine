#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a static initialization block within a class.
/// </summary>
public sealed partial record ClassStaticBlock(SourceReference? Source, BlockStatement Body)
    : AstNode(Source), IAstCacheable<StaticBlockPlanCache>;

public sealed partial record ClassStaticBlock
{
    private StaticBlockPlanCache? _cachedPlan;

    StaticBlockPlanCache IAstCacheable<StaticBlockPlanCache>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedPlan, this,
            static block => StaticBlockPlanCache.Build(block));
    }
}
