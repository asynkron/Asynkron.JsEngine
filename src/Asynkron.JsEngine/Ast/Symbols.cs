namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Centralised symbol definitions so parser and evaluator agree on structure.
/// </summary>
public static class Symbols
{
    public static readonly Symbol Undefined = Symbol.Intern("undefined");
    public static readonly Symbol This = Symbol.Intern("this");
    public static readonly Symbol Super = Symbol.Intern("super");
}
