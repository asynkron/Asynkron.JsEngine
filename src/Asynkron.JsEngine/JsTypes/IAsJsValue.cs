namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Interface for types that can provide a cached JsValue reference.
/// This avoids repeated JsValue struct creation when the same object
/// is converted to JsValue multiple times (e.g., in array iteration callbacks).
/// </summary>
public interface IAsJsValue
{
    /// <summary>
    /// Returns a readonly reference to a cached JsValue wrapping this object.
    /// The JsValue is created once and reused for all subsequent calls.
    /// </summary>
    ref readonly JsValue AsJsValue { get; }
}
