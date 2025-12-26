



namespace Asynkron.JsParser;

/// <summary>
///     Represents a single element within an array destructuring binding.
/// </summary>
public sealed record ArrayBindingElement(SourceReference? Source, BindingTarget? Target, ExpressionNode? DefaultValue)
    : AstNode(Source);
