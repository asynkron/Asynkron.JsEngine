using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class JsHostGetterAttribute(string propertyName) : JsAccessorAttribute
{
    public string PropertyName { get; } = propertyName;
}
