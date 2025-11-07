namespace Asynkron.JsEngine;

internal sealed class Environment
{
    private sealed class Binding
    {
        public Binding(object? value, bool isConst)
        {
            Value = value;
            IsConst = isConst;
        }

        public object? Value { get; set; }

        public bool IsConst { get; }
    }

    private readonly Dictionary<Symbol, Binding> _values = new();
    private readonly Environment? _enclosing;
    private readonly bool _isFunctionScope;

    public Environment(Environment? enclosing = null, bool isFunctionScope = false)
    {
        _enclosing = enclosing;
        _isFunctionScope = isFunctionScope;
    }

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

        throw new InvalidOperationException($"Undefined symbol '{name.Name}'.");
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
}
