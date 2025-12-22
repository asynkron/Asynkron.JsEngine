#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a single property inside an object destructuring binding.
/// </summary>
public sealed record ObjectBindingProperty(
    SourceReference? Source,
    string Name,
    BindingTarget Target,
    ExpressionNode? DefaultValue,
    ExpressionNode? NameExpression = null) : AstNode(Source);
