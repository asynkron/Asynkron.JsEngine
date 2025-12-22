#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents <c>export default</c> followed by an expression.
/// </summary>
public sealed record ExportDefaultExpression(SourceReference? Source, ExpressionNode Expression)
    : ExportDefaultValue(Source);
