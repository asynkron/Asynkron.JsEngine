#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a private identifier reference used in the 'in' operator for brand checking.
///     For example: #field in obj
/// </summary>
public sealed record PrivateIdentifierExpression(SourceReference? Source, string Name) : ExpressionNode(Source);
