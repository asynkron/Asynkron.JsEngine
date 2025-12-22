#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a single call argument, optionally marked as a spread argument.
/// </summary>
public sealed record CallArgument(SourceReference? Source, ExpressionNode Expression, bool IsSpread);
