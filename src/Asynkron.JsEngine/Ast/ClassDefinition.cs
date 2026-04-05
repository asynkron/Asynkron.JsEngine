#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Captures the structure of a class body.
/// </summary>
public sealed partial record ClassDefinition(
    SourceReference? Source,
    ExpressionNode? Extends,
    FunctionExpression Constructor,
    ImmutableArray<ClassMember> Members,
    ImmutableArray<ClassField> Fields,
    ImmutableArray<ClassStaticBlock> StaticBlocks,
    ImmutableArray<ClassStaticElement> StaticElements)
    : AstNode(Source),
        IAstCacheable<ClassDefinitionProgramCache>;

public sealed partial record ClassDefinition
{
    private ClassDefinitionProgramCache? _cachedPrograms;

    ClassDefinitionProgramCache IAstCacheable<ClassDefinitionProgramCache>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(
            ref _cachedPrograms,
            this,
            static definition => ClassDefinitionProgramCache.Build(definition));
    }
}
