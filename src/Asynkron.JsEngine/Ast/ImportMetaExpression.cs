using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents the meta-property import.meta.
/// </summary>
public sealed record ImportMetaExpression(SourceReference? Source) : ExpressionNode(Source);
