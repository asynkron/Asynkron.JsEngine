



namespace Asynkron.JsParser;

/// <summary>
///     Represents an import attribute (e.g., <c>{ type: 'json' }</c>) in an import statement.
/// </summary>
public sealed record ImportAttribute(SourceReference? Source, string Key, string Value) : AstNode(Source);
