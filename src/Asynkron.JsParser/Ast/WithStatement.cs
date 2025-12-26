



namespace Asynkron.JsParser;

/// <summary>
///     Represents a with statement.
/// </summary>
public sealed record WithStatement(SourceReference? Source, ExpressionNode Object, StatementNode Body)
    : StatementNode(Source);
