namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Minimal proxy wrapper used for Array.isArray proxy detection. General proxy
///     traps are not implemented; operations fall back to the underlying target's
///     shape via a backing JsObject.
/// </summary>
public sealed class JsProxy(object target, IJsObjectLike? handler) : IJsObjectLike
{
    private readonly JsObject _backing = new();

    public object Target { get; } = target;
    public IJsObjectLike? Handler { get; set; } = handler;

    public JsObject? Prototype => _backing.Prototype;

    public bool IsSealed => _backing.IsSealed;

    public IEnumerable<string> Keys => _backing.Keys;

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        return _backing.TryGetProperty(name, receiver ?? this, out value);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return TryGetProperty(name, this, out value);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        _backing.SetProperty(name, value, receiver ?? this);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _backing.DefineProperty(name, descriptor);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        return _backing.GetOwnPropertyDescriptor(name);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        return _backing.GetOwnPropertyNames();
    }

    public void SetPrototype(object? candidate)
    {
        _backing.SetPrototype(candidate);
    }

    public void Seal()
    {
        _backing.Seal();
    }

    public bool Delete(string name)
    {
        return _backing.DeleteOwnProperty(name);
    }
}
