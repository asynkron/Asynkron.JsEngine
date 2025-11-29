namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Minimal stub for HTMLDDA-like values (e.g. Test262's $262.IsHTMLDDA).
///     Behaves like a callable object with ordinary property storage so
///     test262 harness helpers can hang Symbol.* hooks off of it.
/// </summary>
public sealed class HtmlDdaValue : IIsHtmlDda, IJsCallable, IJsObjectLike, IPropertyDefinitionHost,
    IExtensibilityControl
{
    private readonly JsObject _backing = new();

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        return null;
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return _backing.TryGetProperty(name, out value);
    }

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        return _backing.TryGetProperty(name, receiver, out value);
    }

    public void SetProperty(string name, object? value)
    {
        _backing.SetProperty(name, value);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        _backing.SetProperty(name, value, receiver);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        return _backing.GetOwnPropertyDescriptor(name);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        return _backing.GetOwnPropertyNames();
    }

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        return _backing.GetEnumerablePropertyNames();
    }

    public JsObject? Prototype => _backing.Prototype;
    public bool IsSealed => _backing.IsSealed;
    public bool IsExtensible => _backing.IsExtensible;
    public IEnumerable<string> Keys => _backing.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _backing.DefineProperty(name, descriptor);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return _backing.TryDefineProperty(name, descriptor);
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

    public bool Delete(string name)
    {
        return _backing.Delete(name);
    }
}

internal interface IIsHtmlDda
{
}
