namespace Asynkron.JsParser;

/// <summary>
///     Base type for expressions.
/// </summary>
public abstract record ExpressionNode(SourceReference? Source) : AstNode(Source);
