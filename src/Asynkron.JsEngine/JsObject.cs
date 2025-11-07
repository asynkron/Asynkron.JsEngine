using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

/// <summary>
/// Simple JavaScript-like object that supports prototype chaining for property lookups.
/// </summary>
internal sealed class JsObject : Dictionary<string, object?>
{
    private const string PrototypeKey = "__proto__";

    private JsObject? _prototype;

    public JsObject()
        : base(StringComparer.Ordinal)
    {
    }

    public JsObject? Prototype => _prototype;

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

    public void SetProperty(string name, object? value)
    {
        if (string.Equals(name, PrototypeKey, StringComparison.Ordinal))
        {
            SetPrototype(value);
        }

        this[name] = value;
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
