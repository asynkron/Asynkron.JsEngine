using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
/// Marks a method as a setter for a property on the prototype.
/// If a getter with the same property name exists, they will be combined into a single accessor property.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostSetterAttribute(string propertyName) : JsAccessorAttribute
{
    [UsedImplicitly]
    public string PropertyName { get; } = propertyName;
}
