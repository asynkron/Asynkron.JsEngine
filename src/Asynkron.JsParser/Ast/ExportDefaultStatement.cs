



namespace Asynkron.JsParser;

/// <summary>
///     Represents an <c>export default</c> declaration.
/// </summary>
public sealed record ExportDefaultStatement(SourceReference? Source, ExportDefaultValue Value)
    : ModuleStatement(Source);
