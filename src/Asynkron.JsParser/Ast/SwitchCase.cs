



namespace Asynkron.JsParser;

/// <summary>
///     Represents a single case clause inside a switch statement.
/// </summary>
public sealed record SwitchCase(SourceReference? Source, ExpressionNode? Test, BlockStatement Body) : AstNode(Source);
