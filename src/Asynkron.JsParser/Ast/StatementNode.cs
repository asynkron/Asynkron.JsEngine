namespace Asynkron.JsParser;

/// <summary>
///     Base type for statements.
/// </summary>
public abstract record StatementNode(SourceReference? Source) : AstNode(Source);
