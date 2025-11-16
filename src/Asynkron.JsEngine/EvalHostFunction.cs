using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
/// A special host function for eval() that has access to the calling environment
/// and can evaluate code synchronously in that context.
/// </summary>
public sealed class EvalHostFunction : IJsEnvironmentAwareCallable, IJsPropertyAccessor
{
    private readonly JsEngine _engine;
    private readonly JsObject _properties = new();

    public EvalHostFunction(JsEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _properties.SetProperty("prototype", new JsObject());
    }

    /// <summary>
    /// The environment that is calling this function.
    /// This allows eval to execute code in the caller's scope.
    /// </summary>
    public JsEnvironment? CallingJsEnvironment { get; set; }

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        if (arguments.Count == 0 || arguments[0] is not string code)
        {
            return arguments.Count > 0 ? arguments[0] : JsSymbols.Undefined;
        }

        // Use the calling environment if available, otherwise use global
        var environment = CallingJsEnvironment ?? throw new InvalidOperationException(
            "eval() called without a calling environment");

        // Parse the code
        var program = _engine.Parse(code);

        // Evaluate directly in the calling environment without going through the event queue
        // This is safe because eval() is synchronous in JavaScript
        var result = _engine.ExecuteProgram(program, environment);

        return result;
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return _properties.TryGetProperty(name, out value);
    }

    public void SetProperty(string name, object? value)
    {
        _properties.SetProperty(name, value);
    }
}