using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
/// Marks a method as a symbol-keyed getter on the prototype (e.g., [Symbol.iterator], [Symbol.toStringTag]).
/// The symbol name should be the well-known symbol name without the "Symbol." prefix (e.g., "iterator", "species").
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
[UsedImplicitly]
public sealed class JsSymbolGetterAttribute(string symbolName) : JsAccessorAttribute
{
    /// <summary>
    /// The well-known symbol name (e.g., "iterator" for Symbol.iterator, "species" for Symbol.species).
    /// </summary>
    [UsedImplicitly]
    public string SymbolName { get; } = symbolName;
}
