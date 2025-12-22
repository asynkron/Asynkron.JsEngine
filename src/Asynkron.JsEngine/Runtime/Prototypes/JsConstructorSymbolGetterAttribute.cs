namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
/// Marks a static method as a symbol-keyed getter on the constructor (e.g., [Symbol.species] on Array).
/// The symbol name should be the well-known symbol name without the "Symbol." prefix (e.g., "species").
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class JsConstructorSymbolGetterAttribute(string symbolName) : Attribute
{
    /// <summary>
    /// The well-known symbol name (e.g., "species" for Symbol.species).
    /// </summary>
    public string SymbolName { get; } = symbolName;

    /// <summary>
    /// Optional display name (e.g., "get [Symbol.species]") assigned to the generated getter function.
    /// Defaults to "get [Symbol.{symbolName}]" when omitted.
    /// </summary>
    public string? DisplayName { get; set; }

    public bool Enumerable { get; set; }

    public bool Configurable { get; set; } = true;
}
