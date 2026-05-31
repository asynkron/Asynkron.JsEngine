using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a single call argument, optionally marked as a spread argument.
/// </summary>
public sealed record CallArgument(SourceReference? Source, ExpressionNode Expression, bool IsSpread);
