#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an if/else statement.
/// </summary>
public sealed record IfStatement(
    SourceReference? Source,
    ExpressionNode Condition,
    StatementNode Then,
    StatementNode? Else) : StatementNode(Source);
