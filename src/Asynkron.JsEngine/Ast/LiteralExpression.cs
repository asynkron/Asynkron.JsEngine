#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a literal (number, string, boolean, null, undefined, BigInt).
/// </summary>
public sealed record LiteralExpression(SourceReference? Source, JsValue Value) : ExpressionNode(Source);
