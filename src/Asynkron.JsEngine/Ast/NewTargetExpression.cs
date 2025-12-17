using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents the meta-property new.target.
/// </summary>
public sealed record NewTargetExpression(SourceReference? Source) : ExpressionNode(Source);
