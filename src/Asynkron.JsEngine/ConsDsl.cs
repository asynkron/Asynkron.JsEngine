namespace Asynkron.JsEngine;

/// <summary>
/// Provides a convenient DSL for creating S-expressions using the Cons structure.
/// Import this class statically to use the S() method for concise S-expression construction.
/// Example: S(Symbol.Foo, 123, "hello")
/// </summary>
public static class ConsDsl
{
    /// <summary>
    /// Creates a Cons list from the supplied arguments.
    /// This is a shorthand for Cons.From() for use in DSL-style code.
    /// </summary>
    /// <param name="args">The items to include in the S-expression list</param>
    /// <returns>A Cons representing the S-expression list</returns>
    public static Cons S(params object?[] args)
    {
        return Cons.From(args);
    }
}
