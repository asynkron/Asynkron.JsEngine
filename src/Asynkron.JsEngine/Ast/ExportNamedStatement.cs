using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents <c>export { ... }</c> declarations.
/// </summary>
public sealed record ExportNamedStatement(
    SourceReference? Source,
    ImmutableArray<ExportSpecifier> Specifiers,
    string? FromModule) : ModuleStatement(Source);
