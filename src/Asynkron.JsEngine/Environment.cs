namespace Asynkron.JsEngine;

internal sealed class Environment(Environment? enclosing = null, bool isFunctionScope = false, bool isStrict = false)
{
    private sealed class Binding(object? value, bool isConst)
    {
        public object? Value { get; set; } = value;

        public bool IsConst { get; } = isConst;
    }

    private readonly Dictionary<Symbol, Binding> _values = new();
    private readonly Environment? _enclosing = enclosing;
    private readonly bool _isFunctionScope = isFunctionScope;
    private readonly bool _isStrict = isStrict;

    /// <summary>
    /// Returns true if this environment or any enclosing environment is in strict mode.
    /// </summary>
    public bool IsStrict => _isStrict || (_enclosing?.IsStrict ?? false);

    public void Define(Symbol name, object? value, bool isConst = false)
    {
        _values[name] = new Binding(value, isConst);
    }

    public void DefineFunctionScoped(Symbol name, object? value, bool hasInitializer)
    {
        // `var` declarations are hoisted to the nearest function/global scope, so we skip block environments here.
        var scope = GetFunctionScope();
        if (scope._values.TryGetValue(name, out var existing))
        {
            if (hasInitializer)
            {
                existing.Value = value;
            }

            return;
        }

        scope._values[name] = new Binding(value, isConst: false);
    }

    public object? Get(Symbol name)
    {
        if (_values.TryGetValue(name, out var binding))
        {
            return binding.Value;
        }

        if (_enclosing is not null)
        {
            return _enclosing.Get(name);
        }

        throw new InvalidOperationException($"Undefined symbol '{name.Name}'.");
    }

    public void Assign(Symbol name, object? value)
    {
        if (_values.TryGetValue(name, out var binding))
        {
            if (binding.IsConst)
            {
                throw new InvalidOperationException($"Cannot reassign constant '{name.Name}'.");
            }

            binding.Value = value;
            return;
        }

        if (_enclosing is not null)
        {
            _enclosing.Assign(name, value);
            return;
        }

        // In strict mode, assignment to undefined variable is an error
        // Use ReferenceError message format
        throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
    }

    private Environment GetFunctionScope()
    {
        var current = this;
        while (!current._isFunctionScope)
        {
            current = current._enclosing
                ?? throw new InvalidOperationException("Unable to locate function scope for var declaration.");
        }

        return current;
    }

    /// <summary>
    /// Gets all variables from this environment and all enclosing environments.
    /// Used for debugging purposes.
    /// </summary>
    public Dictionary<string, object?> GetAllVariables()
    {
        var result = new Dictionary<string, object?>();
        
        // Traverse up the scope chain
        var current = this;
        while (current is not null)
        {
            // Add variables from current scope (only if not already present from inner scope)
            foreach (var kvp in current._values)
            {
                if (!result.ContainsKey(kvp.Key.Name))
                {
                    result[kvp.Key.Name] = kvp.Value.Value;
                }
            }
            
            current = current._enclosing;
        }
        
        return result;
    }
}
