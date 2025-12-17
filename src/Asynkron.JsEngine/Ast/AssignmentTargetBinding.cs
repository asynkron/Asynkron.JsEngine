using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an assignment-style binding target (e.g. a member expression)
///     used in destructuring assignment patterns.
/// </summary>
public sealed record AssignmentTargetBinding(SourceReference? Source, ExpressionNode Expression)
    : BindingTarget(Source);
