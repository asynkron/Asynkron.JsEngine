using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents one part of a template literal (either raw text or an interpolated expression).
/// </summary>
public sealed record TemplatePart(SourceReference? Source, string? Text, ExpressionNode? Expression);
