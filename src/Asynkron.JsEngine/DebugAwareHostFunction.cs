namespace Asynkron.JsEngine;

/// <summary>
/// A host function that has access to the evaluation environment and context.
/// Used for debug and introspection functions.
/// </summary>
internal sealed class DebugAwareHostFunction : IEnvironmentAwareCallable
{
    private readonly Func<Environment, EvaluationContext, IReadOnlyList<object?>, object?> _handler;
    private readonly JsObject _properties = new();

    public DebugAwareHostFunction(Func<Environment, EvaluationContext, IReadOnlyList<object?>, object?> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _properties.SetProperty("prototype", new JsObject());
    }

    // Store the environment and context for the invoke
    internal Environment? CurrentEnvironment { get; set; }
    internal EvaluationContext? CurrentContext { get; set; }
    
    /// <summary>
    /// The environment that is calling this function. Not used for debug functions but required by interface.
    /// </summary>
    public Environment? CallingEnvironment { get; set; }

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        if (CurrentEnvironment is null || CurrentContext is null)
        {
            throw new InvalidOperationException("Debug-aware function called without environment/context set");
        }
        
        return _handler(CurrentEnvironment, CurrentContext, arguments);
    }

    public bool TryGetProperty(string name, out object? value) => _properties.TryGetProperty(name, out value);

    public void SetProperty(string name, object? value) => _properties.SetProperty(name, value);
}
