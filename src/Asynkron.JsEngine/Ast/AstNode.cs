#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Base type for every node in the typed abstract syntax tree.
///     Using records keeps value semantics while allowing pattern matching.
/// </summary>
public abstract record AstNode(SourceReference? Source);
