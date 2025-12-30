#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a regex literal. Kept separate because regex objects require RealmState at runtime.
/// </summary>
public sealed record RegexLiteralExpression(SourceReference? Source, string Pattern, string Flags)
    : ExpressionNode(Source);
