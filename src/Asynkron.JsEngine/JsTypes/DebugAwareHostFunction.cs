using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     A host function that has access to the evaluation environment and context.
///     Used for debug and introspection functions.
/// </summary>
public sealed class DebugAwareHostFunction : IJsEnvironmentAwareCallable, IJsPropertyAccessor
{
    private readonly Func<JsEnvironment, EvaluationContext, IReadOnlyList<JsValue>, JsValue> _handler;
    private readonly JsObject _properties = new();

    public DebugAwareHostFunction(Func<JsEnvironment, EvaluationContext, IReadOnlyList<JsValue>, JsValue> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _properties.SetProperty("prototype", JsValue.FromObject(new JsObject()));
    }

    // Store the environment and context for the invoke
    internal JsEnvironment? CurrentJsEnvironment { get; set; }
    internal EvaluationContext? CurrentContext { get; set; }

    /// <summary>
    ///     The environment that is calling this function. Not used for debug functions but required by interface.
    /// </summary>
    public JsEnvironment? CallingJsEnvironment { get; set; }

    public JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
    {
        if (CurrentJsEnvironment is null || CurrentContext is null)
        {
            throw new InvalidOperationException("Debug-aware function called without environment/context set");
        }

        return _handler(CurrentJsEnvironment, CurrentContext, arguments);
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        return _properties.TryGetProperty(name, receiver.IsUndefined ? JsValue.FromObject(this) : receiver, out value);
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, JsValue.FromObject(this), out value);
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        _properties.SetProperty(name, value, receiver.IsUndefined ? JsValue.FromObject(this) : receiver);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, JsValue.FromObject(this));
    }
}
