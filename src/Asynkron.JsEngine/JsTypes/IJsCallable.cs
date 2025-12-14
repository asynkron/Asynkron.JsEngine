namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a callable object in JavaScript.
/// </summary>
public interface IJsCallable
{
    JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue);
}
