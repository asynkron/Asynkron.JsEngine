#region

using System.Collections.Immutable;
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
    ImmutableArray<int> PerIterationSlotIndices = default) : LoopStatementNode(Source)
{
    public override StatementNode Body { get; init; } = Body;
    protected override string LoopTypeName => "for";
}
