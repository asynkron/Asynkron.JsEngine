#region

using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.JsTypes;

internal sealed class JsArgumentsObject : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl,
    IPrototypeAccessorProvider
{
    private readonly JsObject _backing = new();
    private readonly PropertyDescriptor? _calleeDescriptor;
    private readonly JsEnvironment _environment;
    private readonly string[] _indexNames;
    private readonly bool _isStrict;
    private readonly bool _mappedEnabled;
    private readonly Symbol?[] _mappedParameters;
    private readonly Dictionary<string, PropertyDescriptor> _ownDescriptors = new(StringComparer.Ordinal);
    private readonly RealmState _realm;
    private readonly JsValue[] _values;
    private bool _suppressObserver;

    public JsArgumentsObject(
        IReadOnlyList<JsValue> values,
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
        _values = new JsValue[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            _values[i] = values[i];
        }

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
                JsValue = _values[i],
                Writable = true,
                Enumerable = true,
                Configurable = true
            };
            _backing.DefinePropertyDirect(name, descriptor);
            TrackDescriptorDirect(name, descriptor);
        }

        _backing.DefinePropertyDirect("length",
            new PropertyDescriptor
            {
                Value = (double)_values.Length,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

        _backing.DefinePropertyDirect("__arguments__",
            new PropertyDescriptor { Value = true, Writable = false, Enumerable = false, Configurable = false });

        var tagKey = SymbolKeys.ToStringTag;
        _backing.DefinePropertyDirect(tagKey,
            new PropertyDescriptor { Value = "Arguments", Writable = false, Enumerable = false, Configurable = true });

        if (callee is not null)
        {
            if (mappedEnabled)
            {
                _calleeDescriptor = new PropertyDescriptor
                {
                    Value = callee,
                    Writable = true,
                    Enumerable = false,
                    Configurable = true
                };
            }
            else
            {
                // Use the shared %ThrowTypeError% intrinsic per realm (ES spec 10.2.4).
                // All strict mode arguments objects share the same thrower function.
                var thrower = realm.ThrowTypeErrorIntrinsic ?? new HostFunction((_, _) =>
                        throw new ThrowSignal(StandardLibrary.CreateTypeError(
                            "Access to callee is not allowed in strict mode.", realm.CreateContext(), realm)),
                    isConstructor: false);

                _calleeDescriptor = new PropertyDescriptor
                {
                    Get = thrower,
                    Set = thrower,
                    Enumerable = false,
                    Configurable = false
                };
            }

            _backing.DefinePropertyDirect("callee", _calleeDescriptor);
        }

        var iteratorKey = SymbolKeys.Iterator;
        if (TryGetArrayIterator(realm, iteratorKey, out var iteratorValue))
        {
            _backing.DefinePropertyDirect(iteratorKey,
                new PropertyDescriptor
                {
                    JsValue = iteratorValue,
                    Writable = true,
                    Enumerable = false,
                    Configurable = true
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

    public bool IsExtensible => _backing.IsExtensible;

    public void PreventExtensions()
    {
        _backing.PreventExtensions();
    }

    public JsObject? Prototype => _backing.Prototype;

    public bool IsSealed => _backing.IsSealed;
    public bool IsFrozen => _backing.IsFrozen;

    public IEnumerable<string> Keys => _backing.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        DefinePropertyInternal(name, descriptor, true);
    }

    public void SetPrototype(IJsPropertyAccessor? candidate)
    {
        _backing.SetPrototype(candidate);
    }

    public void Seal()
    {
        _backing.Seal();
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        if (TryResolveIndex(name, out var index) &&
            _mappedEnabled &&
            index < _mappedParameters.Length &&
            _mappedParameters[index] is { } mappedSymbol)
        {
            value = _environment.GetJsValue(mappedSymbol);
            return true;
        }

        return _backing.TryGetProperty(name, receiver.IsUndefined ? JsValue.FromObjectUnsafe(this) : receiver,
            out value);
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, JsValue.FromObjectUnsafe(this), out value);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, JsValue.FromObjectUnsafe(this));
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
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
            WithSuppressedObserver(() => _environment.AssignJsValue(mappedSymbol, value));
        }

        _backing.SetProperty(name, value, receiver.IsUndefined ? JsValue.FromObjectUnsafe(this) : receiver);
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
                _backing.TryGetProperty("callee", JsValue.FromObjectUnsafe(this), out var calleeValue);
                return new PropertyDescriptor
                {
                    JsValue = calleeValue.IsNullOrUndefined ? _calleeDescriptor.JsValue : calleeValue,
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
            cloned.JsValue = _environment.GetJsValue(mappedSymbol);
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
                _values[index] = JsValue.Undefined;
            }
        }

        if (deleted)
        {
            _ownDescriptors.Remove(name);
        }

        return deleted;
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return DefinePropertyInternal(name, descriptor, false);
    }

    public IJsPropertyAccessor? PrototypeAccessor =>
        _backing is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

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
                              descriptor is { HasWritable: true, Writable: false };

            var success = _backing.TryDefineProperty(name, normalized);
            if (!success)
            {
                return FailDefine(throwOnError);
            }

            TrackDescriptor(name, normalized);

            if (descriptor.HasValue)
            {
                _values[index] = descriptor.JsValue;
                WithSuppressedObserver(() => _environment.AssignJsValue(mappedSymbol, descriptor.JsValue));
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

    private void UpdateFromBinding(int index, JsValue jsValue)
    {
        if (_suppressObserver || index >= _values.Length || _mappedParameters[index] is null)
        {
            return;
        }

        _values[index] = jsValue;
        WithSuppressedObserver(() =>
        {
            var existing = _backing.GetOwnPropertyDescriptor(_indexNames[index]);
            var descriptor = new PropertyDescriptor
            {
                JsValue = jsValue,
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
            var currentWritable = !current.HasWritable || current.Writable;

            if (currentWritable)
            {
                return true;
            }

            if (candidate is { HasWritable: true, Writable: true })
            {
                return false;
            }

            if (candidate.HasValue && !JsOps.StrictEquals(candidate.JsValue, current.JsValue))
            {
                return false;
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

    /// <summary>
    /// Tracks a descriptor by taking direct ownership without cloning.
    /// Used in constructor where we create fresh descriptors.
    /// </summary>
    private void TrackDescriptorDirect(string name, PropertyDescriptor descriptor)
    {
        _ownDescriptors[name] = descriptor;
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
            normalized.JsValue = descriptor.JsValue;
        }
        else if (existing is not null)
        {
            if (_backing.TryGetProperty(name, out var existingValue))
            {
                normalized.JsValue = existingValue;
            }
            else if (existing.HasValue)
            {
                normalized.JsValue = existing.JsValue;
            }
            else
            {
                normalized.JsValue = JsValue.Undefined;
            }
        }
        else
        {
            normalized.JsValue = JsValue.Undefined;
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
        var clone = new PropertyDescriptor();

        if (source.HasValue)
        {
            clone.JsValue = source.JsValue;
        }

        if (source.HasWritable)
        {
            clone.Writable = source.Writable;
        }

        if (source.HasEnumerable)
        {
            clone.Enumerable = source.Enumerable;
        }

        if (source.HasConfigurable)
        {
            clone.Configurable = source.Configurable;
        }

        if (source.HasGet)
        {
            clone.Get = source.Get;
        }

        if (source.HasSet)
        {
            clone.Set = source.Set;
        }

        return clone;
    }

    private static bool TryGetArrayIterator(RealmState realmState, string iteratorKey, out JsValue iteratorValue)
    {
        iteratorValue = JsValue.Undefined;

        if (realmState.ArrayPrototype is IJsPropertyAccessor arrayPrototype &&
            arrayPrototype.TryGetProperty(iteratorKey, out var protoIterator))
        {
            iteratorValue = protoIterator;
            return true;
        }

        var temp = new JsArray(realmState);
        if (!temp.TryGetProperty(iteratorKey, out var tmpIterator))
        {
            return false;
        }

        iteratorValue = tmpIterator;
        return true;

    }
}
