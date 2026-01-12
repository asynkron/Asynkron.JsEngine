using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

[UsedImplicitly]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JsConstructorAttribute(string intrinsicName) : Attribute
{
    /// <summary>
    ///     Friendly identifier for diagnostics/logging (e.g. "Intl.Locale").
    /// </summary>
    [UsedImplicitly]
    public string IntrinsicName { get; } = intrinsicName;

    /// <summary>
    ///     Optional Function.length metadata. Defaults to 0.
    /// </summary>
    [UsedImplicitly]
    public double Length { get; set; }

    /// <summary>
    ///     Optional Function.name metadata. Defaults to the property name specified in Standard Library.
    /// </summary>
    [UsedImplicitly]
    public string? DisplayName { get; set; }

    /// <summary>
    ///     Prototype class that this constructor should expose via its "prototype" property.
    /// </summary>
    [UsedImplicitly]
    public Type PrototypeType { get; set; } = null!;
}
