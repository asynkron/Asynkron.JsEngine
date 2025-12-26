



namespace Asynkron.JsParser;

/// <summary>
///     Represents a break statement, optionally labeled.
/// </summary>
public sealed record BreakStatement(SourceReference? Source, Symbol? Label) : StatementNode(Source);
