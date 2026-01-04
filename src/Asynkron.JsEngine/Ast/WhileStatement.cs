#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a while loop.
/// </summary>
public sealed record WhileStatement(SourceReference? Source, ExpressionNode Condition, StatementNode Body)
    : LoopStatementNode(Source)
{
    public override StatementNode Body { get; init; } = Body;
    protected override string LoopTypeName => "while";
}
