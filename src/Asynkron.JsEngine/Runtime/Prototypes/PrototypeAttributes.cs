namespace Asynkron.JsEngine.Runtime.Prototypes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class JsPrototypeAttribute : Attribute
{
    public JsPrototypeAttribute(string intrinsicName)
    {
        IntrinsicName = intrinsicName;
    }

    /// <summary>
    ///     Friendly name for diagnostics (e.g. "Intl.Locale").
    /// </summary>
    public string IntrinsicName { get; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostGetterAttribute : Attribute
{
    public JsHostGetterAttribute(string propertyName)
    {
        PropertyName = propertyName;
    }

    public string PropertyName { get; }

    /// <summary>
    ///     Optional display name (e.g. "get calendar") assigned to the generated getter function.
    /// </summary>
    public string? DisplayName { get; set; }

    public bool Enumerable { get; set; }

    public bool Configurable { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostMethodAttribute : Attribute
{
    public JsHostMethodAttribute(string propertyName)
    {
        PropertyName = propertyName;
    }

    public string PropertyName { get; }

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
public sealed class JsConstructorAttribute : Attribute
{
    public JsConstructorAttribute(string intrinsicName)
    {
        IntrinsicName = intrinsicName;
    }

    /// <summary>
    ///     Friendly identifier for diagnostics/logging (e.g. "Intl.Locale").
    /// </summary>
    public string IntrinsicName { get; }

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
