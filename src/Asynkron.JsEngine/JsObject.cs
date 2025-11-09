using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

/// <summary>
/// Simple JavaScript-like object that supports prototype chaining for property lookups.
/// </summary>
internal sealed class JsObject() : Dictionary<string, object?>(StringComparer.Ordinal)
{
    private const string PrototypeKey = "__proto__";
    private const string GetterPrefix = "__getter__";
    private const string SetterPrefix = "__setter__";

    private JsObject? _prototype;
    private bool _isFrozen;
    private bool _isSealed;

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

    public void SetProperty(string name, object? value)
    {
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
    }
    
    public void Seal()
    {
        _isSealed = true;
    }

    public bool TryGetProperty(string name, out object? value)
        => TryGetProperty(name, new HashSet<JsObject>(ReferenceEqualityComparer<JsObject>.Instance), out value);

    private bool TryGetProperty(string name, HashSet<JsObject> visited, out object? value)
    {
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
            // Skip internal keys (proto, getters, setters)
            if (key == PrototypeKey || key.StartsWith(GetterPrefix) || key.StartsWith(SetterPrefix))
            {
                continue;
            }
            yield return key;
        }
    }
}

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    private ReferenceEqualityComparer()
    {
    }

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
