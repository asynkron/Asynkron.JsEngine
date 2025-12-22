#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a single <c>export { local as exported }</c> specifier.
/// </summary>
public sealed record ExportSpecifier(SourceReference? Source, Symbol Local, Symbol Exported) : AstNode(Source);
