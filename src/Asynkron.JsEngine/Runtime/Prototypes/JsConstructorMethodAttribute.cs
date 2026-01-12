using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
/// Marks a static method to be registered on the constructor function (e.g., Object.keys, Array.isArray).
/// The method must be static and have one of the following signatures:
/// - (object?, IReadOnlyList<JsValue>, RealmState?) - receives thisArg, args, and realm
/// - (IReadOnlyList<JsValue>, RealmState?) - receives args and realm
/// - (IReadOnlyList<JsValue>) - receives args only
/// - () - no parameters
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[UsedImplicitly]
public sealed class JsConstructorMethodAttribute(string propertyName) : JsFunctionAttribute
{
    /// <summary>
    /// The JavaScript property name (e.g., "keys" for Object.keys).
    /// </summary>
    [UsedImplicitly]
    public string PropertyName { get; } = propertyName;
}
