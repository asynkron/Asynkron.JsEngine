namespace Asynkron.JsEngine;

internal sealed class JsFunction : IJsCallable
{
    private readonly Symbol? _name;
    private readonly IReadOnlyList<Symbol> _parameters;
    private readonly Cons _body;
    private readonly Environment _closure;
    private readonly JsObject _properties = new();
    private JsFunction? _superConstructor;
    private JsObject? _superPrototype;

    public JsFunction(Symbol? name, IReadOnlyList<Symbol> parameters, Cons body, Environment closure)
    {
        _name = name;
        _parameters = parameters;
        _body = body;
        _closure = closure;

        // Every function in JavaScript exposes a prototype object so instances created via `new` can inherit from it.
        _properties.SetProperty("prototype", new JsObject());
    }

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        if (arguments.Count != _parameters.Count)
        {
            throw new InvalidOperationException($"Function expected {_parameters.Count} arguments but received {arguments.Count}.");
        }

        var environment = new Environment(_closure, isFunctionScope: true);
        for (var i = 0; i < _parameters.Count; i++)
        {
            environment.Define(_parameters[i], arguments[i]);
        }

        environment.Define(JsSymbols.This, thisValue);

        if (_name is not null)
        {
            environment.Define(_name, this);
        }

        if (_superConstructor is not null || _superPrototype is not null)
        {
            var binding = new SuperBinding(_superConstructor, _superPrototype, thisValue);
            environment.Define(JsSymbols.Super, binding);
        }

        try
        {
            return Evaluator.EvaluateBlock(_body, environment);
        }
        catch (ReturnSignal signal)
        {
            return signal.Value;
        }
    }

    public bool TryGetProperty(string name, out object? value) => _properties.TryGetProperty(name, out value);

    public void SetProperty(string name, object? value) => _properties.SetProperty(name, value);

    public void SetSuperBinding(JsFunction? superConstructor, JsObject? superPrototype)
    {
        _superConstructor = superConstructor;
        _superPrototype = superPrototype;
    }
}
