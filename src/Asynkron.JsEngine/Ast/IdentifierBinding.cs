using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Simple identifier binding.
/// </summary>
public sealed record IdentifierBinding(SourceReference? Source, Symbol Name) : BindingTarget(Source);
