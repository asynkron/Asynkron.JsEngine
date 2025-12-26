



namespace Asynkron.JsParser;

/// <summary>
///     Represents an assignment-style binding target (e.g. a member expression)
///     used in destructuring assignment patterns.
/// </summary>
public sealed record AssignmentTargetBinding(SourceReference? Source, ExpressionNode Expression)
    : BindingTarget(Source);
