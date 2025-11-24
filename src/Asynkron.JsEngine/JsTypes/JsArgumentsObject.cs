using System;
using System.Globalization;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.StdLib;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

internal sealed class JsArgumentsObject : IJsObjectLike
{
    private readonly JsObject _backing = new();
    private readonly JsEnvironment _environment;
    private readonly Symbol?[] _mappedParameters;
    private readonly object?[] _values;
    private readonly bool _mappedEnabled;
    private readonly bool _isStrict;
    private readonly PropertyDescriptor? _calleeDescriptor;
    private readonly string[] _indexNames;
    private bool _suppressObserver;

    public JsArgumentsObject(
        IReadOnlyList<object?> values,
        Symbol?[] mappedParameters,
        JsEnvironment environment,
        bool mappedEnabled,
        RealmState realm,
        IJsCallable? callee,
        bool isStrict)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _mappedParameters = mappedParameters;
        _mappedEnabled = mappedEnabled;
        _isStrict = isStrict;
        _values = values.ToArray();
        _indexNames = new string[_values.Length];

        for (var i = 0; i < _values.Length; i++)
        {
            var name = i.ToString(CultureInfo.InvariantCulture);
            _indexNames[i] = name;
            _backing.SetProperty(name, _values[i]);
        }

        _backing.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = (double)_values.Length, Writable = true, Enumerable = false, Configurable = true
            });

        _backing.DefineProperty("__arguments__",
            new PropertyDescriptor { Value = true, Writable = false, Enumerable = false, Configurable = false });

        if (callee is not null)
        {
            if (isStrict)
            {
                var thrower = new HostFunction((_, _) =>
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        "Access to callee is not allowed in strict mode.", new EvaluationContext(realm), realm)))
                {
                    IsConstructor = false
                };

                _calleeDescriptor = new PropertyDescriptor
                {
                    Get = thrower,
                    Set = thrower,
                    Enumerable = false,
                    Configurable = false
                };
            }
            else
            {
                _calleeDescriptor = new PropertyDescriptor
                {
                    Value = callee, Writable = true, Enumerable = false, Configurable = true
                };
            }

            _backing.DefineProperty("callee", _calleeDescriptor);
        }

        var iteratorKey = $"@@symbol:{TypedAstSymbol.For("Symbol.iterator").GetHashCode()}";
        if (TryGetArrayIterator(realm, iteratorKey, out var iteratorValue))
        {
            _backing.DefineProperty(iteratorKey,
                new PropertyDescriptor
                {
                    Value = iteratorValue, Writable = true, Enumerable = false, Configurable = true
                });
        }

        if (_mappedEnabled)
        {
            for (var i = 0; i < _mappedParameters.Length; i++)
            {
                var symbol = _mappedParameters[i];
                if (symbol is null)
                {
                    continue;
                }

                var index = i;
                _environment.AddBindingObserver(symbol, value => UpdateFromBinding(index, value));
            }
        }
    }

    public JsObject? Prototype
    {
        get => _backing.Prototype;
    }

    public bool IsSealed => _backing.IsSealed;

    public IEnumerable<string> Keys => _backing.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        var normalized = NormalizeDescriptor(name, descriptor);

        if (TryResolveIndex(name, out var index) &&
            _mappedEnabled &&
            index < _mappedParameters.Length &&
            _mappedParameters[index] is { } mappedSymbol)
        {
            var shouldUnmap = descriptor.IsAccessorDescriptor ||
                              (descriptor.HasWritable && !descriptor.Writable);

            if (descriptor.HasValue)
            {
                _values[index] = descriptor.Value;
                WithSuppressedObserver(() => _environment.Assign(mappedSymbol, descriptor.Value));
            }

            _backing.DefineProperty(name, normalized);

            if (shouldUnmap)
            {
                _mappedParameters[index] = null;
            }

            return;
        }

        _backing.DefineProperty(name, normalized);
    }

    public void SetPrototype(object? candidate)
    {
        _backing.SetPrototype(candidate);
    }

    public void Seal()
    {
        _backing.Seal();
    }

    public bool TryGetProperty(string name, out object? value)
    {
        if (TryResolveIndex(name, out var index) &&
            _mappedEnabled &&
            index < _mappedParameters.Length &&
            _mappedParameters[index] is { } mappedSymbol)
        {
            value = _environment.Get(mappedSymbol);
            return true;
        }

        return _backing.TryGetProperty(name, out value);
    }

    public void SetProperty(string name, object? value)
    {
        var descriptor = _backing.GetOwnPropertyDescriptor(name);
        var isWritable = descriptor?.IsAccessorDescriptor != true &&
                         (!descriptor?.HasWritable ?? true || descriptor.Writable);

        if (TryResolveIndex(name, out var index) &&
            _mappedEnabled &&
            isWritable &&
            index < _mappedParameters.Length &&
            _mappedParameters[index] is { } mappedSymbol)
        {
            _values[index] = value;
            WithSuppressedObserver(() => _environment.Assign(mappedSymbol, value));
        }

        _backing.SetProperty(name, value);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        var descriptor = _backing.GetOwnPropertyDescriptor(name);
        if (descriptor is null)
        {
            if (_calleeDescriptor is not null &&
                string.Equals(name, "callee", StringComparison.Ordinal))
            {
                return CloneDescriptor(_calleeDescriptor);
            }

            return null;
        }

        if (TryResolveIndex(name, out var index) &&
            _mappedEnabled &&
            index < _mappedParameters.Length &&
            _mappedParameters[index] is { } mappedSymbol &&
            !descriptor.IsAccessorDescriptor)
        {
            var cloned = CloneDescriptor(descriptor);
            cloned.Value = _environment.Get(mappedSymbol);
            return cloned;
        }

        return descriptor;
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        return _backing.GetOwnPropertyNames();
    }

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        return _backing.GetEnumerablePropertyNames();
    }

    public bool Delete(string name)
    {
        var deleted = _backing.DeleteOwnProperty(name);
        if (deleted && TryResolveIndex(name, out var index) && index < _mappedParameters.Length)
        {
            _mappedParameters[index] = null;
            if (index < _values.Length)
            {
                _values[index] = Symbols.Undefined;
            }
        }

        return deleted;
    }

    private void UpdateFromBinding(int index, object? value)
    {
        if (_suppressObserver || index >= _values.Length || _mappedParameters[index] is null)
        {
            return;
        }

        _values[index] = value;
        WithSuppressedObserver(() => _backing.SetProperty(_indexNames[index], value));
    }

    private void WithSuppressedObserver(Action action)
    {
        try
        {
            _suppressObserver = true;
            action();
        }
        finally
        {
            _suppressObserver = false;
        }
    }

    private static bool TryResolveIndex(string candidate, out int index)
    {
        return int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && index >= 0;
    }

    private PropertyDescriptor NormalizeDescriptor(string name, PropertyDescriptor descriptor)
    {
        var existing = _backing.GetOwnPropertyDescriptor(name);
        var normalized = new PropertyDescriptor();

        if (descriptor.IsAccessorDescriptor)
        {
            normalized.Get = descriptor.Get;
            normalized.Set = descriptor.Set;
            normalized.Enumerable = descriptor.HasEnumerable
                ? descriptor.Enumerable
                : existing?.Enumerable ?? false;
            normalized.Configurable = descriptor.HasConfigurable
                ? descriptor.Configurable
                : existing?.Configurable ?? false;
            return normalized;
        }

        if (descriptor.HasValue)
        {
            normalized.Value = descriptor.Value;
        }
        else if (existing is not null && existing.HasValue)
        {
            normalized.Value = existing.Value;
        }
        else
        {
            normalized.Value = Symbols.Undefined;
        }

        normalized.Writable = descriptor.HasWritable
            ? descriptor.Writable
            : existing?.Writable ?? false;
        normalized.Enumerable = descriptor.HasEnumerable
            ? descriptor.Enumerable
            : existing?.Enumerable ?? false;
        normalized.Configurable = descriptor.HasConfigurable
            ? descriptor.Configurable
            : existing?.Configurable ?? false;

        return normalized;
    }

    private static PropertyDescriptor CloneDescriptor(PropertyDescriptor source)
    {
        var clone = new PropertyDescriptor
        {
            Enumerable = source.Enumerable,
            Writable = source.Writable,
            Configurable = source.Configurable,
            Get = source.Get,
            Set = source.Set
        };

        if (source.HasValue)
        {
            clone.Value = source.Value;
        }

        return clone;
    }

    private static bool TryGetArrayIterator(RealmState realmState, string iteratorKey, out object? iteratorValue)
    {
        iteratorValue = null;

        if (realmState.ArrayPrototype is JsObject arrayPrototype &&
            arrayPrototype.TryGetProperty(iteratorKey, out var protoIterator))
        {
            iteratorValue = protoIterator;
            return true;
        }

        var temp = new JsArray(realmState);
        StandardLibrary.AddArrayMethods(temp, realmState);
        if (temp.TryGetProperty(iteratorKey, out var tmpIterator))
        {
            iteratorValue = tmpIterator;
            return true;
        }

        return false;
    }
}
