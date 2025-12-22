namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
/// Marks a method as a setter for a property on the prototype.
/// If a getter with the same property name exists, they will be combined into a single accessor property.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostSetterAttribute(string propertyName) : Attribute
{
    public string PropertyName { get; } = propertyName;

    /// <summary>
    ///     Optional display name (e.g. "set __proto__") assigned to the generated setter function.
    /// </summary>
    public string? DisplayName { get; set; }

    public bool Enumerable { get; set; }

    public bool Configurable { get; set; } = true;
}
