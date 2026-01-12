using JetBrains.Annotations;

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
    [UsedImplicitly]
    public string SymbolName { get; } = symbolName;

    /// <summary>
    /// Function.length metadata for the method.
    /// </summary>
    [UsedImplicitly]
    public double Length { get; set; }

    [UsedImplicitly]
    public bool Enumerable { get; set; }

    [UsedImplicitly]
    public bool Writable { get; set; } = true;

    [UsedImplicitly]
    public bool Configurable { get; set; } = true;

    /// <summary>
    /// Optional display name used for Function.prototype.name.
    /// Defaults to "[Symbol.{symbolName}]" when omitted.
    /// </summary>
    [UsedImplicitly]
    public string? DisplayName { get; set; }
}
