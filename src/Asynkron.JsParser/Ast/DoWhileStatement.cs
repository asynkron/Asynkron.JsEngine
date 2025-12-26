namespace Asynkron.JsParser;

/// <summary>
///     Represents a do/while loop.
/// </summary>
public sealed record DoWhileStatement(SourceReference? Source, StatementNode Body, ExpressionNode Condition)
    : StatementNode(Source);
