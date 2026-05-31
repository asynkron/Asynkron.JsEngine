using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a single function parameter. Parameters may use destructuring or rest syntax,
///     so we capture the typed binding target while exposing default values.
/// </summary>
public sealed record FunctionParameter(
    SourceReference? Source,
    Symbol? Name,
    bool IsRest,
    BindingTarget? Pattern,
    ExpressionNode? DefaultValue);
