namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
/// Marks a method as a symbol-keyed method on the prototype (e.g., [Symbol.iterator], [Symbol.toPrimitive]).
/// The symbol name should be the well-known symbol name without the "Symbol." prefix (e.g., "iterator", "toPrimitive").
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsSymbolMethodAttribute(string symbolName) : Attribute
{
    /// <summary>
    /// The well-known symbol name (e.g., "iterator" for Symbol.iterator).
    /// </summary>
    public string SymbolName { get; } = symbolName;

    /// <summary>
    /// Function.length metadata for the method.
    /// </summary>
    public double Length { get; set; }

    public bool Enumerable { get; set; }

    public bool Writable { get; set; } = true;

    public bool Configurable { get; set; } = true;

    /// <summary>
    /// Optional display name used for Function.prototype.name.
    /// Defaults to "[Symbol.{symbolName}]" when omitted.
    /// </summary>
    public string? DisplayName { get; set; }
}
