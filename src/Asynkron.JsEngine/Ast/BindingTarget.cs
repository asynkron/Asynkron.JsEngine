#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Base type for the left-hand side of a variable declaration.
/// </summary>
public abstract record BindingTarget(SourceReference? Source) : AstNode(Source);
