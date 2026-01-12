using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
/// Marks a string-keyed property as an alias to another property on the prototype.
/// For example, Date.prototype.toGMTString is an alias to toUTCString.
/// The alias will be set up after the target property is registered.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class JsMethodAliasAttribute(string aliasName, string targetPropertyName) : Attribute
{
    /// <summary>
    /// The name of the alias property (e.g., "toGMTString").
    /// </summary>
    [UsedImplicitly]
    public string AliasName { get; } = aliasName;

    /// <summary>
    /// The name of the property to alias (e.g., "toUTCString").
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
