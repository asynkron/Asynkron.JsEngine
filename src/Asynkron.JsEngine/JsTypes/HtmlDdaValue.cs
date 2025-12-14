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
    public bool IsExtensible => _backing.IsExtensible;

    public void PreventExtensions()
    {
        _backing.PreventExtensions();
    }

    public JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
    {
        return JsValue.Null;
    }

    bool IJsPropertyAccessor.TryGetProperty(string name, out object? value)
    {
        return _backing.TryGetProperty(name, out value);
    }

    bool IJsPropertyAccessor.TryGetProperty(string name, object? receiver, out object? value)
    {
        return _backing.TryGetProperty(name, receiver, out value);
    }

    void IJsPropertyAccessor.SetProperty(string name, object? value)
    {
        _backing.SetProperty(name, value);
    }

    void IJsPropertyAccessor.SetProperty(string name, object? value, object? receiver)
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
    public bool IsFrozen => _backing.IsFrozen;
    public IEnumerable<string> Keys => _backing.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _backing.DefineProperty(name, descriptor);
    }

    void IJsObjectLike.SetPrototype(object? candidate)
    {
        _backing.SetPrototype(candidate);
    }

    public void Seal()
    {
        _backing.Seal();
    }

    public bool Delete(string name)
    {
        return _backing.Delete(name);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return _backing.TryDefineProperty(name, descriptor);
    }
}

internal interface IIsHtmlDda
{
}
