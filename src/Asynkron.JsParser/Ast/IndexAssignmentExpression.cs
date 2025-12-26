



namespace Asynkron.JsParser;

/// <summary>
///     Represents an assignment to an indexed access.
/// </summary>
public sealed record IndexAssignmentExpression(
    SourceReference? Source,
    ExpressionNode Target,
    ExpressionNode Index,
    ExpressionNode Value,
    bool IsCompoundAssignment = false) : ExpressionNode(Source);
