namespace Asynkron.JsEngine;

public sealed class JsFunction : IEnvironmentAwareCallable
{
    private readonly Symbol? _name;
    private readonly IReadOnlyList<object> _parameters; // Can be Symbol or Cons (for destructuring patterns)
    private readonly Symbol? _restParameter;
    private readonly Cons _body;
    private readonly Environment _closure;
    private readonly JsObject _properties = new();
    private JsFunction? _superConstructor;
    private JsObject? _superPrototype;

    /// <summary>
    /// The environment that is calling this function. Used for building call stacks.
    /// </summary>
    public Environment? CallingEnvironment { get; set; }

    public JsFunction(Symbol? name, IReadOnlyList<object> parameters, Symbol? restParameter, Cons body,
        Environment closure)
    {
        _name = name;
        _parameters = parameters;
        _restParameter = restParameter;
        _body = body;
        _closure = closure;

        // Every function in JavaScript exposes a prototype object so instances created via `new` can inherit from it.
        _properties.SetProperty("prototype", new JsObject());
    }

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        // With rest parameters, we accept variable arguments
        if (_restParameter is null)
        {
            // JavaScript allows passing more arguments than parameters
            // Only check for too few arguments
            if (arguments.Count < _parameters.Count)
                throw new InvalidOperationException(
                    $"Function expected {_parameters.Count} arguments but received {arguments.Count}.");
        }
        else
        {
            if (arguments.Count < _parameters.Count)
                throw new InvalidOperationException(
                    $"Function expected at least {_parameters.Count} arguments but received {arguments.Count}.");
        }

        var context = new EvaluationContext();
        var functionDescription = _name != null ? $"function {_name.Name}" : "anonymous function";
        var environment = new Environment(_closure, true, creatingExpression: _body, description: functionDescription);

        // Bind regular parameters (could be symbols or destructuring patterns)
        for (var i = 0; i < _parameters.Count; i++)
        {
            var parameter = _parameters[i];
            var argument = i < arguments.Count ? arguments[i] : null;

            if (parameter is Symbol symbol)
                // Simple parameter
                environment.Define(symbol, argument);
            else if (parameter is Cons pattern)
                // Destructuring parameter
                Evaluator.DestructureParameter(pattern, argument, environment, context);
            else
                throw new InvalidOperationException($"Invalid parameter type: {parameter?.GetType().Name ?? "null"}");
        }

        // Bind rest parameter if present
        if (_restParameter is not null)
        {
            var restArgs = new List<object?>();
            for (var i = _parameters.Count; i < arguments.Count; i++) restArgs.Add(arguments[i]);
            var restArray = new JsArray();
            foreach (var arg in restArgs) restArray.Push(arg);
            environment.Define(_restParameter, restArray);
        }

        // In non-strict mode JavaScript, when this is null or undefined, it should reference the global object.
        // For simplicity, we create a new empty object when this is null.
        // This handles cases where constructor functions are called without 'new'.
        var effectiveThis = thisValue ?? new JsObject();
        environment.Define(JsSymbols.This, effectiveThis);

        if (_name is not null) environment.Define(_name, this);

        if (_superConstructor is not null || _superPrototype is not null)
        {
            var binding = new SuperBinding(_superConstructor, _superPrototype, thisValue);
            environment.Define(JsSymbols.Super, binding);
        }

        var result = Evaluator.EvaluateBlock(_body, environment, context);

        if (context.IsReturn) return context.FlowValue;

        if (context.IsThrow) throw new ThrowSignal(context.FlowValue);

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

    public void SetSuperBinding(JsFunction? superConstructor, JsObject? superPrototype)
    {
        _superConstructor = superConstructor;
        _superPrototype = superPrototype;
    }
}