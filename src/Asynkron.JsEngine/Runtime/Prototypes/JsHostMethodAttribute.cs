namespace Asynkron.JsEngine.Runtime.Prototypes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostMethodAttribute(string propertyName) : Attribute
{
    public string PropertyName { get; } = propertyName;

    public double Length { get; set; }

    public bool Enumerable { get; set; }

    public bool Writable { get; set; } = true;

    public bool Configurable { get; set; } = true;

    /// <summary>
    ///     Optional display name used for Function.prototype.name.
    ///     Defaults to the property name when omitted.
    /// </summary>
    public string? DisplayName { get; set; }
}
