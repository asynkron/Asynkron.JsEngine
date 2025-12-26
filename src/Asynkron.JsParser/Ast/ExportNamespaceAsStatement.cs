



namespace Asynkron.JsParser;

/// <summary>
///     Represents <c>export * as ns from "module"</c> declarations.
/// </summary>
public sealed record ExportNamespaceAsStatement(SourceReference? Source, Symbol Exported, string ModulePath)
    : ModuleStatement(Source);
