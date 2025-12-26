namespace Asynkron.JsParser;

/// <summary>
///     Represents a regex literal. Kept separate because regex objects require RealmState at runtime.
/// </summary>
public sealed record RegexLiteralExpression(SourceReference? Source, string Pattern, string Flags)
    : ExpressionNode(Source);
