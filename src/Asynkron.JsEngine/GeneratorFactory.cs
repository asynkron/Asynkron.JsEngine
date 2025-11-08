namespace Asynkron.JsEngine;

/// <summary>
/// Factory class for creating generator instances.
/// When a generator function is declared (function*), this factory is created.
/// Calling the factory creates a new generator instance.
/// </summary>
internal sealed class GeneratorFactory : IJsCallable
{
    private readonly Symbol? _name;
    private readonly Cons _parameters;
    private readonly Cons _body;
    private readonly Environment _closure;

    public GeneratorFactory(Symbol? name, Cons parameters, Cons body, Environment closure)
    {
        _name = name;
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _closure = closure ?? throw new ArgumentNullException(nameof(closure));
    }

    /// <summary>
    /// When the generator factory is called, it creates and returns a new generator instance.
    /// The generator instance is an object with next(), return(), and throw() methods.
    /// </summary>
    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        // Create a new generator instance
        var generator = new JsGenerator(_parameters, _body, _closure, arguments);
        
        // Create a JavaScript object that represents the generator with iterator protocol methods
        var generatorObject = new JsObject();
        
        // Add the next() method
        generatorObject.SetProperty("next", new HostFunction((thisVal, args) =>
        {
            var value = args.Count > 0 ? args[0] : null;
            return generator.Next(value);
        }));
        
        // Add the return() method
        generatorObject.SetProperty("return", new HostFunction((thisVal, args) =>
        {
            var value = args.Count > 0 ? args[0] : null;
            return generator.Return(value);
        }));
        
        // Add the throw() method
        generatorObject.SetProperty("throw", new HostFunction((thisVal, args) =>
        {
            var error = args.Count > 0 ? args[0] : null;
            return generator.Throw(error);
        }));
        
        return generatorObject;
    }

    public override string ToString()
    {
        return _name != null ? $"[GeneratorFunction: {_name}]" : "[GeneratorFunction]";
    }
}
