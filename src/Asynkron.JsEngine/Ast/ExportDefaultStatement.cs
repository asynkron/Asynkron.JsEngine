using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an <c>export default</c> declaration.
/// </summary>
public sealed record ExportDefaultStatement(SourceReference? Source, ExportDefaultValue Value)
    : ModuleStatement(Source);
