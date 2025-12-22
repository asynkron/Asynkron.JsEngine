#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     A single variable declarator within a declaration statement.
/// </summary>
public sealed record VariableDeclarator(SourceReference? Source, BindingTarget Target, ExpressionNode? Initializer);
