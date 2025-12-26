
using System.Collections.Immutable;



namespace Asynkron.JsParser;

/// <summary>
///     Represents a "new" expression.
/// </summary>
public sealed record NewExpression(
    SourceReference? Source,
    ExpressionNode Constructor,
    ImmutableArray<CallArgument> Arguments) : ExpressionNode(Source);
