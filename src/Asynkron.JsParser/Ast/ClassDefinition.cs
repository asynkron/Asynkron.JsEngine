
using System.Collections.Immutable;



namespace Asynkron.JsParser;

/// <summary>
///     Captures the structure of a class body.
/// </summary>
public sealed record ClassDefinition(
    SourceReference? Source,
    ExpressionNode? Extends,
    FunctionExpression Constructor,
    ImmutableArray<ClassMember> Members,
    ImmutableArray<ClassField> Fields,
    ImmutableArray<ClassStaticBlock> StaticBlocks,
    ImmutableArray<ClassStaticElement> StaticElements) : AstNode(Source);
