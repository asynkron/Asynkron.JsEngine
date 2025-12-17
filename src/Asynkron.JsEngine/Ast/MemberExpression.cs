using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a property access (dot or computed) expression.
/// </summary>
public sealed record MemberExpression(
    SourceReference? Source,
    ExpressionNode Target,
    ExpressionNode Property,
    bool IsComputed,
    bool IsOptional) : ExpressionNode(Source);
