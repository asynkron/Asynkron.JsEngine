#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Base type for <c>export default</c> payloads.
/// </summary>
public abstract record ExportDefaultValue(SourceReference? Source) : AstNode(Source);
