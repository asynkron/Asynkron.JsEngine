



namespace Asynkron.JsParser;

/// <summary>
///     Represents <c>export default</c> followed by a declaration (function/class).
/// </summary>
public sealed record ExportDefaultDeclaration(SourceReference? Source, StatementNode Declaration)
    : ExportDefaultValue(Source);
