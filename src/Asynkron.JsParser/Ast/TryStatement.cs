



namespace Asynkron.JsParser;

/// <summary>
///     Represents a try/catch/finally statement.
/// </summary>
public sealed record TryStatement(
    SourceReference? Source,
    BlockStatement TryBlock,
    CatchClause? Catch,
    BlockStatement? Finally) : StatementNode(Source);
