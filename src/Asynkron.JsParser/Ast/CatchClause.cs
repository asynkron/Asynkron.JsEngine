



namespace Asynkron.JsParser;

/// <summary>
///     Represents a catch clause in a try statement.
/// </summary>
public sealed record CatchClause(SourceReference? Source, BindingTarget? Binding, BlockStatement Body)
    : AstNode(Source);
