namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
/// Marks a static method to be registered on the constructor function (e.g., Object.keys, Array.isArray).
/// The method must be static and have one of the following signatures:
/// - (object?, IReadOnlyList&lt;JsValue&gt;, RealmState?) - receives thisArg, args, and realm
/// - (IReadOnlyList&lt;JsValue&gt;, RealmState?) - receives args and realm
/// - (IReadOnlyList&lt;JsValue&gt;) - receives args only
/// - () - no parameters
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class JsConstructorMethodAttribute(string propertyName) : Attribute
{
    /// <summary>
    /// The JavaScript property name (e.g., "keys" for Object.keys).
    /// </summary>
    public string PropertyName { get; } = propertyName;

    /// <summary>
    /// Function.length metadata for the method.
    /// </summary>
    public double Length { get; set; }

    /// <summary>
    /// Whether the property is enumerable. Defaults to false.
    /// </summary>
    public bool Enumerable { get; set; }

    /// <summary>
    /// Whether the property is writable. Defaults to true.
    /// </summary>
    public bool Writable { get; set; } = true;

    /// <summary>
    /// Whether the property is configurable. Defaults to true.
    /// </summary>
    public bool Configurable { get; set; } = true;

    /// <summary>
    /// Optional display name used for Function.prototype.name.
    /// Defaults to the property name when omitted.
    /// </summary>
    public string? DisplayName { get; set; }
}
