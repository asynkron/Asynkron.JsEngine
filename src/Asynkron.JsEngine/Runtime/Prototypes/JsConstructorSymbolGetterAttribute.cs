using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
/// Marks a static method as a symbol-keyed getter on the constructor (e.g., [Symbol.species] on Array).
/// The symbol name should be the well-known symbol name without the "Symbol." prefix (e.g., "species").
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class JsConstructorSymbolGetterAttribute(string symbolName) : Attribute
{
    /// <summary>
    /// The well-known symbol name (e.g., "species" for Symbol.species).
    /// </summary>
    [UsedImplicitly]
    public string SymbolName { get; } = symbolName;

    /// <summary>
    /// Optional display name (e.g., "get [Symbol.species]") assigned to the generated getter function.
    /// Defaults to "get [Symbol.{symbolName}]" when omitted.
    /// </summary>
    [UsedImplicitly]
    public string? DisplayName { get; set; }
    [UsedImplicitly]

    public bool Enumerable { get; set; }
    [UsedImplicitly]

    public bool Configurable { get; set; } = true;
}
