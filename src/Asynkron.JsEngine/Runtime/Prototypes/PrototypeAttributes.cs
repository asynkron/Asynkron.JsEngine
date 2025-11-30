namespace Asynkron.JsEngine.Runtime.Prototypes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class JsPrototypeAttribute(string intrinsicName) : Attribute
{
    /// <summary>
    ///     Friendly name for diagnostics (e.g. "Intl.Locale").
    /// </summary>
    public string IntrinsicName { get; } = intrinsicName;

    /// <summary>
    ///     Optional value for %prototype%[@@toStringTag]; when specified the source generator
    ///     emits the descriptor automatically.
    /// </summary>
    public string? ToStringTag { get; set; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostGetterAttribute(string propertyName) : Attribute
{
    public string PropertyName { get; } = propertyName;

    /// <summary>
    ///     Optional display name (e.g. "get calendar") assigned to the generated getter function.
    /// </summary>
    public string? DisplayName { get; set; }

    public bool Enumerable { get; set; }

    public bool Configurable { get; set; } = true;
}

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

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class JsConstructorAttribute(string intrinsicName) : Attribute
{
    /// <summary>
    ///     Friendly identifier for diagnostics/logging (e.g. "Intl.Locale").
    /// </summary>
    public string IntrinsicName { get; } = intrinsicName;

    /// <summary>
    ///     Optional Function.length metadata. Defaults to 0.
    /// </summary>
    public double Length { get; set; }

    /// <summary>
    ///     Optional Function.name metadata. Defaults to the property name specified in Standard Library.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    ///     Prototype class that this constructor should expose via its "prototype" property.
    /// </summary>
    public Type PrototypeType { get; set; } = null!;
}
