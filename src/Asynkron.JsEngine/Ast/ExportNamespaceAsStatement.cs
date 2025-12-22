#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents <c>export * as ns from "module"</c> declarations.
/// </summary>
public sealed record ExportNamespaceAsStatement(SourceReference? Source, Symbol Exported, string ModulePath)
    : ModuleStatement(Source);
