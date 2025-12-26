



namespace Asynkron.JsParser;

/// <summary>
///     Represents a throw statement.
/// </summary>
public sealed record ThrowStatement(SourceReference? Source, ExpressionNode Expression) : StatementNode(Source);
