using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostGetterAttribute(string propertyName) : Attribute
{
    public string PropertyName { get; } = propertyName;

    /// <summary>
    ///     Optional display name (e.g. "get calendar") assigned to the generated getter function.
    /// </summary>
    [UsedImplicitly]
    public string? DisplayName { get; set; }
    [UsedImplicitly]

    public bool Enumerable { get; set; }
    [UsedImplicitly]

    public bool Configurable { get; set; } = true;
}
