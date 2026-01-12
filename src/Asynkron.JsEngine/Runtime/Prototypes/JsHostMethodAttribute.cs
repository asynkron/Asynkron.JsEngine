using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

public enum JsHostFunctionTarget
{
    Global,
    Constructor,
    Prototype,
    Custom
}

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

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostFunctionAttribute(string name) : Attribute
{
    [UsedImplicitly]
    public string Name { get; } = name;

    [UsedImplicitly]
    public JsHostFunctionTarget Target { get; set; } = JsHostFunctionTarget.Global;

    [UsedImplicitly]
    public string? TargetName { get; set; }

    [UsedImplicitly]
    public bool ThrowOnMissingTarget { get; set; }

    [UsedImplicitly]
    public double Length { get; set; }

    [UsedImplicitly]
    public bool Enumerable { get; set; }

    [UsedImplicitly]
    public bool Writable { get; set; } = true;

    [UsedImplicitly]
    public bool Configurable { get; set; } = true;

    [UsedImplicitly]
    public bool DeletePrototype { get; set; }

    /// <summary>
    ///     Optional display name used for Function.prototype.name.
    ///     Defaults to the function name when omitted.
    /// </summary>
    [UsedImplicitly]
    public string? DisplayName { get; set; }
}
