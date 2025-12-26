



namespace Asynkron.JsParser;

/// <summary>
///     Represents an expression statement.
/// </summary>
public sealed record ExpressionStatement(SourceReference? Source, ExpressionNode Expression) : StatementNode(Source);
