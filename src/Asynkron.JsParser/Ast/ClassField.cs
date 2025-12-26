



namespace Asynkron.JsParser;

/// <summary>
///     Represents a field declared on a class.
/// </summary>
public sealed record ClassField(
    SourceReference? Source,
    string Name,
    ExpressionNode? Initializer,
    bool IsStatic,
    bool IsPrivate,
    bool IsComputed = false,
    ExpressionNode? ComputedName = null) : AstNode(Source);
