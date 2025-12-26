



namespace Asynkron.JsParser;

/// <summary>
///     Base type for <c>export default</c> payloads.
/// </summary>
public abstract record ExportDefaultValue(SourceReference? Source) : AstNode(Source);
