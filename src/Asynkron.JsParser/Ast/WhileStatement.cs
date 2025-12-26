namespace Asynkron.JsParser;

/// <summary>
///     Represents a while loop.
/// </summary>
public sealed record WhileStatement(SourceReference? Source, ExpressionNode Condition, StatementNode Body)
    : StatementNode(Source);
