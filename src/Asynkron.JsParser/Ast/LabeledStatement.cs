



namespace Asynkron.JsParser;

/// <summary>
///     Represents a labeled statement.
/// </summary>
public sealed record LabeledStatement(SourceReference? Source, Symbol Label, StatementNode Statement)
    : StatementNode(Source);
