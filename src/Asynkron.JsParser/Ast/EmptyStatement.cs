



namespace Asynkron.JsParser;

/// <summary>
///     Represents an empty statement (";").
/// </summary>
public sealed record EmptyStatement(SourceReference? Source) : StatementNode(Source);
