#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

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
