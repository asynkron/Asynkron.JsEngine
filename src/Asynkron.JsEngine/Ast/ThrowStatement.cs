#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a throw statement.
/// </summary>
public sealed record ThrowStatement(SourceReference? Source, ExpressionNode Expression) : StatementNode(Source);
