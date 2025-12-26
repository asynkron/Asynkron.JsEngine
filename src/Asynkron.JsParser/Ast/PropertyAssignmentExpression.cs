



namespace Asynkron.JsParser;

/// <summary>
///     Represents an assignment to a property access.
/// </summary>
public sealed record PropertyAssignmentExpression(
    SourceReference? Source,
    ExpressionNode Target,
    ExpressionNode Property,
    ExpressionNode Value,
    bool IsComputed,
    bool IsCompoundAssignment = false) : ExpressionNode(Source);
