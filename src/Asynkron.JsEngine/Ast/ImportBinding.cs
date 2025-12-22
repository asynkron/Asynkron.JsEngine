#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a single named binding within an <c>import</c> declaration.
/// </summary>
public sealed record ImportBinding(SourceReference? Source, Symbol Imported, Symbol Local) : AstNode(Source);
