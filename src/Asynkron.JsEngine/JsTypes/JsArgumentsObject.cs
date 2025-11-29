using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.StdLib;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

internal sealed class JsArgumentsObject : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl
{
    private readonly JsObject _backing = new();
    private readonly JsEnvironment _environment;
    private readonly Symbol?[] _mappedParameters;
    private readonly object?[] _values;
    private readonly bool _mappedEnabled;
    private readonly bool _isStrict;
    private readonly PropertyDescriptor? _calleeDescriptor;
    private readonly string[] _indexNames;
    private readonly RealmState _realm;
    private readonly Dictionary<string, PropertyDescriptor> _ownDescriptors = new(StringComparer.Ordinal);
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
        _realm = realm ?? throw new ArgumentNullException(nameof(realm));
        _mappedParameters = mappedParameters;
        _mappedEnabled = mappedEnabled;
        _isStrict = isStrict;
        _values = values.ToArray();
        _indexNames = new string[_values.Length];

        if (realm.ObjectPrototype is not null)
        {
            _backing.SetPrototype(realm.ObjectPrototype);
        }

        for (var i = 0; i < _values.Length; i++)
        {
            var name = i.ToString(CultureInfo.InvariantCulture);
            _indexNames[i] = name;
            var descriptor = new PropertyDescriptor
            {
                Value = _values[i],
                Writable = true,
                Enumerable = true,
                Configurable = true
            };
            _backing.DefineProperty(name, descriptor);
            TrackDescriptor(name, descriptor);
        }

        _backing.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = (double)_values.Length, Writable = true, Enumerable = false, Configurable = true
            });

        _backing.DefineProperty("__arguments__",
            new PropertyDescriptor { Value = true, Writable = false, Enumerable = false, Configurable = false });

        var tagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
        _backing.DefineProperty(tagKey,
            new PropertyDescriptor
            {
                Value = "Arguments", Writable = false, Enumerable = false, Configurable = true
            });

        if (callee is not null)
        {
            if (mappedEnabled)
            {
                _calleeDescriptor = new PropertyDescriptor
                {
                    Value = callee, Writable = true, Enumerable = false, Configurable = true
                };
            }
            else
            {
                var thrower = new HostFunction((_, _) =>
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        "Access to callee is not allowed in strict mode.", realm.CreateContext(), realm)))
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
    public bool IsExtensible => _backing.IsExtensible;

    public IEnumerable<string> Keys => _backing.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        DefinePropertyInternal(name, descriptor, throwOnError: true);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return DefinePropertyInternal(name, descriptor, throwOnError: false);
    }

    private bool DefinePropertyInternal(string name, PropertyDescriptor descriptor, bool throwOnError)
    {
        var existingDescriptor = GetTrackedDescriptor(name);
        var normalized = NormalizeDescriptor(name, descriptor, existingDescriptor);

        if (existingDescriptor is not null && !IsDescriptorCompatible(existingDescriptor, descriptor))
        {
            return FailDefine(throwOnError);
        }

        if (TryResolveIndex(name, out var index) &&
            _mappedEnabled &&
            index < _mappedParameters.Length &&
            _mappedParameters[index] is { } mappedSymbol)
        {
            var shouldUnmap = descriptor.IsAccessorDescriptor ||
                              (descriptor.HasWritable && !descriptor.Writable);

            var success = _backing.TryDefineProperty(name, normalized);
            if (!success)
            {
                return FailDefine(throwOnError);
            }

            TrackDescriptor(name, normalized);

            if (descriptor.HasValue)
            {
                _values[index] = descriptor.Value;
                WithSuppressedObserver(() => _environment.Assign(mappedSymbol, descriptor.Value));
            }

            if (shouldUnmap)
            {
                _mappedParameters[index] = null;
            }

            return true;
        }

        if (!_backing.TryDefineProperty(name, normalized))
        {
            return FailDefine(throwOnError);
        }

        TrackDescriptor(name, normalized);
        return true;
    }

    public void SetPrototype(object? candidate)
    {
        _backing.SetPrototype(candidate);
    }

    public void PreventExtensions()
    {
        _backing.PreventExtensions();
    }

    public void Seal()
    {
        _backing.Seal();
    }

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        if (TryResolveIndex(name, out var index) &&
            _mappedEnabled &&
            index < _mappedParameters.Length &&
            _mappedParameters[index] is { } mappedSymbol)
        {
            value = _environment.Get(mappedSymbol);
            return true;
        }

        return _backing.TryGetProperty(name, receiver ?? this, out value);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return TryGetProperty(name, this, out value);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        var descriptor = _backing.GetOwnPropertyDescriptor(name);
        var hasWritable = descriptor?.HasWritable ?? false;
        var isAccessor = descriptor?.IsAccessorDescriptor == true;
        var isWritable = !isAccessor && (!hasWritable || descriptor?.Writable != false);

        if (TryResolveIndex(name, out var index) &&
            _mappedEnabled &&
            isWritable &&
            index < _mappedParameters.Length &&
            _mappedParameters[index] is { } mappedSymbol)
        {
            _values[index] = value;
            WithSuppressedObserver(() => _environment.Assign(mappedSymbol, value));
        }

        _backing.SetProperty(name, value, receiver ?? this);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (string.Equals(name, "callee", StringComparison.Ordinal) && _calleeDescriptor is not null)
        {
            var backingDescriptor = _backing.GetOwnPropertyDescriptor(name);
            if (backingDescriptor is null)
            {
                return null;
            }

            if (_mappedEnabled)
            {
                _backing.TryGetProperty("callee", this, out var calleeValue);
                return new PropertyDescriptor
                {
                    Value = calleeValue ?? _calleeDescriptor.Value,
                    Writable = true,
                    Enumerable = false,
                    Configurable = true
                };
            }

            return CloneDescriptor(backingDescriptor);
        }

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
                _values[index] = Symbol.Undefined;
            }
        }

        if (deleted)
        {
            _ownDescriptors.Remove(name);
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
        WithSuppressedObserver(() =>
        {
            var existing = _backing.GetOwnPropertyDescriptor(_indexNames[index]);
            var descriptor = new PropertyDescriptor
            {
                Value = value,
                Writable = existing?.Writable ?? true,
                Enumerable = existing?.Enumerable ?? true,
                Configurable = existing?.Configurable ?? true
            };
            _backing.DefineProperty(_indexNames[index], descriptor);
            TrackDescriptor(_indexNames[index], descriptor);
        });
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

    private PropertyDescriptor? GetTrackedDescriptor(string name)
    {
        if (_ownDescriptors.TryGetValue(name, out var tracked))
        {
            return CloneDescriptor(tracked);
        }

        var existing = _backing.GetOwnPropertyDescriptor(name);
        return existing is null ? null : CloneDescriptor(existing);
    }

    private bool FailDefine(bool throwOnError)
    {
        if (throwOnError)
        {
            throw CreateDefineTypeError();
        }

        return false;
    }

    private ThrowSignal CreateDefineTypeError()
    {
        return new ThrowSignal(StandardLibrary.CreateTypeError("Cannot redefine property", null, _realm));
    }

    private static bool IsDescriptorCompatible(PropertyDescriptor current, PropertyDescriptor candidate)
    {
        if (current.Configurable)
        {
            return true;
        }

        if (candidate.HasConfigurable && candidate.Configurable != current.Configurable)
        {
            return false;
        }

        if (candidate.HasEnumerable && candidate.Enumerable != current.Enumerable)
        {
            return false;
        }

        var currentIsData = !current.IsAccessorDescriptor;
        var candidateIsData = !candidate.IsAccessorDescriptor;

        if (currentIsData != candidateIsData &&
            (candidate.HasValue || candidate.HasWritable || candidate.Get is not null || candidate.Set is not null))
        {
            return false;
        }

        if (currentIsData && candidateIsData)
        {
            var currentWritable = current.HasWritable ? current.Writable : true;

            if (!currentWritable)
            {
                if (candidate.HasWritable && candidate.Writable)
                {
                    return false;
                }

                if (candidate.HasValue && !JsOps.StrictEquals(candidate.Value, current.Value))
                {
                    return false;
                }
            }

            return true;
        }

        if (!currentIsData && !candidateIsData)
        {
            if (candidate.Get is not null && !ReferenceEquals(candidate.Get, current.Get))
            {
                return false;
            }

            if (candidate.Set is not null && !ReferenceEquals(candidate.Set, current.Set))
            {
                return false;
            }
        }

        return true;
    }

    private void TrackDescriptor(string name, PropertyDescriptor descriptor)
    {
        _ownDescriptors[name] = CloneDescriptor(descriptor);
    }

    private PropertyDescriptor NormalizeDescriptor(string name, PropertyDescriptor descriptor,
        PropertyDescriptor? existing)
    {
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
        else if (existing is not null)
        {
            if (_backing.TryGetProperty(name, out var existingValue))
            {
                normalized.Value = existingValue;
            }
            else if (existing.HasValue)
            {
                normalized.Value = existing.Value;
            }
            else
            {
                normalized.Value = Symbol.Undefined;
            }
        }
        else
        {
            normalized.Value = Symbol.Undefined;
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
            Configurable = source.Configurable,
            Get = source.Get,
            Set = source.Set
        };

        if (source.HasWritable)
        {
            clone.Writable = source.Writable;
        }

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
