



namespace Asynkron.JsParser;

/// <summary>
///     Represents a destructuring assignment (<c>[a, b] = value</c> or <c>({ x } = value)</c>).
///     The pattern is expressed via the same typed binding nodes used by declarations so the
///     evaluator can reuse its destructuring logic.
/// </summary>
public sealed record DestructuringAssignmentExpression(
    SourceReference? Source,
    BindingTarget Target,
    ExpressionNode Value) : ExpressionNode(Source);
