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
    public string AliasName { get; } = aliasName;

    /// <summary>
    /// The name of the property to alias (e.g., "toUTCString").
    /// </summary>
    public string TargetPropertyName { get; } = targetPropertyName;

    public bool Enumerable { get; set; }

    public bool Writable { get; set; } = true;

    public bool Configurable { get; set; } = true;
}
