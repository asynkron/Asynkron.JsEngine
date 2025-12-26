



namespace Asynkron.JsParser;

/// <summary>
///     Represents the meta-property new.target.
/// </summary>
public sealed record NewTargetExpression(SourceReference? Source) : ExpressionNode(Source);
