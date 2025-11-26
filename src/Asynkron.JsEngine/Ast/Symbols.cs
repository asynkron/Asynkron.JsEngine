namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Centralised symbol definitions so parser and evaluator agree on structure.
/// </summary>
public static class Symbols
{
    public static readonly Symbol Undefined = Symbol.Intern("undefined");
    public static readonly Symbol This = Symbol.Intern("this");
    public static readonly Symbol Super = Symbol.Intern("super");
    public static readonly Symbol NewTarget = Symbol.Intern("new.target");
    public static readonly Symbol ThisInitialized = Symbol.Intern("[[thisInitialized]]");
    public static readonly Symbol Arguments = Symbol.Intern("arguments");
}
