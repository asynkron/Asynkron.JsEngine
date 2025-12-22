#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an expression statement.
/// </summary>
public sealed record ExpressionStatement(SourceReference? Source, ExpressionNode Expression) : StatementNode(Source);
