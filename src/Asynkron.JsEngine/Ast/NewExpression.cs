using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a "new" expression.
/// </summary>
public sealed record NewExpression(
    SourceReference? Source,
    ExpressionNode Constructor,
    ImmutableArray<CallArgument> Arguments) : ExpressionNode(Source);
