



namespace Asynkron.JsParser;

/// <summary>
///     Represents a single element within an array literal.
/// </summary>
public sealed record ArrayElement(SourceReference? Source, ExpressionNode? Expression, bool IsSpread);
