using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

public abstract class JsDescriptorAttribute : Attribute
{
    [UsedImplicitly]
    public bool Enumerable { get; set; }

    [UsedImplicitly]
    public bool Configurable { get; set; } = true;
}

public abstract class JsWritableDescriptorAttribute : JsDescriptorAttribute
{
    [UsedImplicitly]
    public bool Writable { get; set; } = true;
}

public abstract class JsFunctionAttribute : JsWritableDescriptorAttribute
{
    /// <summary>
    ///     Optional Function.length metadata. Defaults to 0.
    /// </summary>
    [UsedImplicitly]
    public double Length { get; set; }

    /// <summary>
    ///     Optional display name used for Function.prototype.name.
    /// </summary>
    [UsedImplicitly]
    public string? DisplayName { get; set; }
}

public abstract class JsAccessorAttribute : JsDescriptorAttribute
{
    /// <summary>
    ///     Optional display name assigned to the generated accessor function.
    /// </summary>
    [UsedImplicitly]
    public string? DisplayName { get; set; }
}

public abstract class JsAliasAttribute : JsWritableDescriptorAttribute
{
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostMethodAttribute(string propertyName) : JsFunctionAttribute
{
    [UsedImplicitly]
    public string PropertyName { get; } = propertyName;
}
