#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents the meta-property import.meta.
/// </summary>
public sealed record ImportMetaExpression(SourceReference? Source) : ExpressionNode(Source);
