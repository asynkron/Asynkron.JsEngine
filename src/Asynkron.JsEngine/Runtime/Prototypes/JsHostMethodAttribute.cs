using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostMethodAttribute(string propertyName) : Attribute
{
    [UsedImplicitly]
    public string PropertyName { get; } = propertyName;

    [UsedImplicitly]
    public double Length { get; set; }

    [UsedImplicitly]
    public bool Enumerable { get; set; }

    [UsedImplicitly]
    public bool Writable { get; set; } = true;

    [UsedImplicitly]
    public bool Configurable { get; set; } = true;

    /// <summary>
    ///     Optional display name used for Function.prototype.name.
    ///     Defaults to the property name when omitted.
    /// </summary>
    [UsedImplicitly]
    public string? DisplayName { get; set; }
}
