



namespace Asynkron.JsParser;

/// <summary>
///     Represents <c>export * from "module"</c> declarations.
/// </summary>
public sealed record ExportAllStatement(SourceReference? Source, string ModulePath) : ModuleStatement(Source);
