using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
/// Marks a symbol-keyed property as an alias to another property on the prototype.
/// For example, [Symbol.iterator] is commonly aliased to "values" on Array.prototype.
/// The alias will be set up after the target property is registered.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class JsSymbolAliasAttribute(string symbolName, string targetPropertyName) : Attribute
{
    /// <summary>
    /// The well-known symbol name (e.g., "iterator" for Symbol.iterator).
    /// </summary>
    [UsedImplicitly]
    public string SymbolName { get; } = symbolName;

    /// <summary>
    /// The name of the property to alias (e.g., "values").
    /// </summary>
    [UsedImplicitly]
    public string TargetPropertyName { get; } = targetPropertyName;
    [UsedImplicitly]
    public bool Enumerable { get; set; }

    [UsedImplicitly]
    public bool Writable { get; set; } = true;

    [UsedImplicitly]
    public bool Configurable { get; set; } = true;
}
