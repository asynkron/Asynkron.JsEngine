



namespace Asynkron.JsParser;

/// <summary>
///     Represents a yield expression inside a generator.
/// </summary>
public sealed record YieldExpression(SourceReference? Source, ExpressionNode? Expression, bool IsDelegated)
    : ExpressionNode(Source);
