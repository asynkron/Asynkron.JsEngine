using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Represents a JavaScript property descriptor.
/// </summary>
public sealed class PropertyDescriptor
{
    public object? Value { get; set; }
    public bool Writable { get; set; } = true;
    public bool Enumerable { get; set; } = true;
    public bool Configurable { get; set; } = true;
    public IJsCallable? Get { get; set; }
    public IJsCallable? Set { get; set; }

    public bool IsAccessorDescriptor => Get != null || Set != null;
    public bool IsDataDescriptor => !IsAccessorDescriptor;
}

/// <summary>
/// Simple JavaScript-like object that supports prototype chaining for property lookups.
/// </summary>
public sealed class JsObject() : Dictionary<string, object?>(StringComparer.Ordinal), IJsPropertyAccessor
{
    private const string PrototypeKey = "__proto__";
    private const string GetterPrefix = "__getter__";
    private const string SetterPrefix = "__setter__";
    private const string DescriptorPrefix = "__descriptor__";

    private JsObject? _prototype;
    private bool _isFrozen;
    private bool _isSealed;
    private readonly Dictionary<string, PropertyDescriptor> _descriptors = new(StringComparer.Ordinal);

    public JsObject? Prototype => _prototype;

    public bool IsFrozen => _isFrozen;
    public bool IsSealed => _isSealed;

    public void SetPrototype(object? candidate)
    {
        if (candidate is JsObject prototype)
        {
            _prototype = prototype;
        }
        else
        {
            _prototype = null;
        }

        if (candidate is not null)
        {
            this[PrototypeKey] = candidate;
        }
        else
        {
            Remove(PrototypeKey);
        }
    }

    public void SetGetter(string name, IJsCallable getter)
    {
        this[GetterPrefix + name] = getter;
    }

    public void SetSetter(string name, IJsCallable setter)
    {
        this[SetterPrefix + name] = setter;
    }

    public bool HasGetter(string name)
    {
        return TryGetValue(GetterPrefix + name, out _);
    }

    public bool HasSetter(string name)
    {
        return TryGetValue(SetterPrefix + name, out _);
    }

    public IJsCallable? GetGetter(string name)
    {
        if (TryGetValue(GetterPrefix + name, out var getter))
        {
            return getter as IJsCallable;
        }

        // Check prototype chain
        if (_prototype != null)
        {
            return _prototype.GetGetter(name);
        }

        return null;
    }

    public IJsCallable? GetSetter(string name)
    {
        if (TryGetValue(SetterPrefix + name, out var setter))
        {
            return setter as IJsCallable;
        }

        // Check prototype chain
        if (_prototype != null)
        {
            return _prototype.GetSetter(name);
        }

        return null;
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        // Check if property exists and is not configurable
        if (_descriptors.TryGetValue(name, out var existingDesc) &&
            !existingDesc.Configurable)
        {
            return; // Silently ignore in non-strict mode
        }

        // Sealed/frozen objects cannot have new properties added
        if ((_isSealed || _isFrozen) && !ContainsKey(name))
        {
            return;
        }

        // Frozen objects cannot have properties modified
        if (_isFrozen && ContainsKey(name))
        {
            return;
        }

        _descriptors[name] = descriptor;

        if (descriptor.IsAccessorDescriptor)
        {
            // Store getter/setter
            if (descriptor.Get != null)
            {
                this[GetterPrefix + name] = descriptor.Get;
            }

            if (descriptor.Set != null)
            {
                this[SetterPrefix + name] = descriptor.Set;
            }
        }
        else
        {
            // Store data value
            this[name] = descriptor.Value;
        }
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (_descriptors.TryGetValue(name, out var descriptor))
        {
            return descriptor;
        }

        // If no explicit descriptor but property exists, return default descriptor
        if (ContainsKey(name))
        {
            return new PropertyDescriptor
            {
                Value = this[name],
                Writable = !_isFrozen,
                Enumerable = true,
                Configurable = !_isSealed && !_isFrozen
            };
        }

        return null;
    }

    public void SetProperty(string name, object? value)
    {
        // First check if this object or its prototype chain has a setter
        var setter = GetSetter(name);
        if (setter != null)
        {
            setter.Invoke([value], this);
            return;
        }
        
        // Check if property has a descriptor that makes it non-writable
        if (_descriptors.TryGetValue(name, out var descriptor))
        {
            if (descriptor is { IsAccessorDescriptor: true, Set: not null })
            {
                // This should have been caught by GetSetter above
                descriptor.Set.Invoke([value], this);
                return;
            }

            if (!descriptor.Writable)
            {
                return; // Silently ignore in non-strict mode
            }
        }

        // Frozen objects cannot have properties modified
        if (_isFrozen)
        {
            return; // Silently ignore in non-strict mode
        }

        // Sealed objects cannot have new properties added
        if (_isSealed && !ContainsKey(name))
        {
            return; // Silently ignore in non-strict mode
        }

        if (string.Equals(name, PrototypeKey, StringComparison.Ordinal))
        {
            SetPrototype(value);
        }

        this[name] = value;
    }

    public void Freeze()
    {
        _isFrozen = true;
        _isSealed = true; // Frozen implies sealed

        // Update all existing descriptors to be non-writable and non-configurable
        foreach (var key in Keys.ToArray())
        {
            if (key == PrototypeKey || key.StartsWith(GetterPrefix) || key.StartsWith(SetterPrefix))
            {
                continue;
            }

            if (_descriptors.TryGetValue(key, out var desc))
            {
                desc.Writable = false;
                desc.Configurable = false;
            }
            else
            {
                _descriptors[key] = new PropertyDescriptor
                {
                    Value = this[key],
                    Writable = false,
                    Enumerable = true,
                    Configurable = false
                };
            }
        }
    }

    public void Seal()
    {
        _isSealed = true;

        // Update all existing descriptors to be non-configurable
        foreach (var key in Keys.ToArray())
        {
            if (key == PrototypeKey || key.StartsWith(GetterPrefix) || key.StartsWith(SetterPrefix))
            {
                continue;
            }

            if (_descriptors.TryGetValue(key, out var desc))
            {
                desc.Configurable = false;
            }
            else
            {
                _descriptors[key] = new PropertyDescriptor
                {
                    Value = this[key],
                    Writable = true,
                    Enumerable = true,
                    Configurable = false
                };
            }
        }
    }

    public bool TryGetProperty(string name, out object? value)
    {
        // Check for getter in this object or prototype chain
        var getter = GetGetter(name);
        if (getter != null)
        {
            // Important: call with 'this' as context, which is the original object that property access was done on
            value = getter.Invoke([], this);
            return true;
        }
        
        return TryGetProperty(name, new HashSet<JsObject>(ReferenceEqualityComparer<JsObject>.Instance), out value);
    }

    private bool TryGetProperty(string name, HashSet<JsObject> visited, out object? value)
    {
        // Check for regular properties (not getters)
        if (TryGetValue(name, out value))
        {
            return true;
        }

        if (_prototype is null)
        {
            value = null;
            return false;
        }

        if (!visited.Add(this))
        {
            value = null;
            return false;
        }

        return _prototype.TryGetProperty(name, visited, out value);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        foreach (var key in Keys)
        {
            // Skip internal keys (proto, getters, setters, and Symbol-keyed properties)
            if (key == PrototypeKey ||
                key.StartsWith(GetterPrefix) ||
                key.StartsWith(SetterPrefix) ||
                key.StartsWith("@@symbol:")) // Symbol-keyed properties are not enumerable
            {
                continue;
            }

            yield return key;
        }
    }

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        foreach (var key in Keys)
        {
            // Skip internal keys (proto, getters, setters, and Symbol-keyed properties)
            if (key == PrototypeKey ||
                key.StartsWith(GetterPrefix) ||
                key.StartsWith(SetterPrefix) ||
                key.StartsWith("@@symbol:"))
            {
                continue;
            }

            // Check if property is enumerable
            if (_descriptors.TryGetValue(key, out var descriptor))
            {
                if (!descriptor.Enumerable)
                {
                    continue;
                }
            }

            yield return key;
        }
    }
}

public sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    private ReferenceEqualityComparer()
    {
    }

    public bool Equals(T? x, T? y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(T obj)
    {
        return RuntimeHelpers.GetHashCode(obj);
    }
}
