#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a do/while loop.
/// </summary>
public sealed record DoWhileStatement(SourceReference? Source, StatementNode Body, ExpressionNode Condition)
    : LoopStatementNode(Source)
{
    public override StatementNode Body { get; init; } = Body;
    protected override string LoopTypeName => "do/while";
}
