#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a return statement.
/// </summary>
/// <param name="Source">Source reference.</param>
/// <param name="Expression">The return expression, or null for bare return.</param>
public sealed record ReturnStatement(
    SourceReference? Source,
    ExpressionNode? Expression) : StatementNode(Source);
