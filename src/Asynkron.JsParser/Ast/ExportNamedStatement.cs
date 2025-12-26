
using System.Collections.Immutable;



namespace Asynkron.JsParser;

/// <summary>
///     Represents <c>export { ... }</c> declarations.
/// </summary>
public sealed record ExportNamedStatement(
    SourceReference? Source,
    ImmutableArray<ExportSpecifier> Specifiers,
    string? FromModule) : ModuleStatement(Source);
