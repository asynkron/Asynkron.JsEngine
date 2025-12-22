#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents <c>export * from "module"</c> declarations.
/// </summary>
public sealed record ExportAllStatement(SourceReference? Source, string ModulePath) : ModuleStatement(Source);
