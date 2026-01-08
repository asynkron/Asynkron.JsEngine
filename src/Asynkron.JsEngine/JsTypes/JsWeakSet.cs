#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript WeakSet collection.
///     WeakSets store unique objects where values are held weakly.
///     Unlike Set, WeakSet does not prevent garbage collection of values and does not support iteration.
/// </summary>
public sealed class JsWeakSet : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl,
    IPrototypeAccessorProvider, IAsJsValue
{
    private readonly JsObject _properties = new();
    private readonly JsValue _cachedJsValue;

    // Use ConditionalWeakTable to track object membership
    // We use a dummy value since we only care about key presence
    private readonly ConditionalWeakTable<object, object?> _values = new();
    public bool IsExtensible => _properties.IsExtensible;

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
    }

    public JsWeakSet()
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        return _properties.TryGetProperty(name, receiver.IsUndefined ? _cachedJsValue : receiver, out value);
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, _cachedJsValue, out value);
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        _properties.SetProperty(name, value, receiver.IsUndefined ? _cachedJsValue : receiver);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, _cachedJsValue);
    }

    public JsObject? Prototype => _properties.Prototype;

    public bool IsSealed => _properties.IsSealed;
    public bool IsFrozen => _properties.IsFrozen;

    public IEnumerable<string> Keys => _properties.Keys;

    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _properties.DefineProperty(name, descriptor);
    }

    public void SetPrototype(IJsPropertyAccessor? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public void Seal()
    {
        _properties.Seal();
    }

    public bool Delete(string name)
    {
        return _properties.DeleteOwnProperty(name);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return _properties.TryDefineProperty(name, descriptor);
    }

    public IJsPropertyAccessor? PrototypeAccessor =>
        _properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    /// <summary>
    ///     Adds a value to the WeakSet. Returns the WeakSet object to allow chaining.
    ///     The value must be an object (not a primitive value).
    /// </summary>
    public JsWeakSet Add(JsValue value)
    {
        var obj = JsWeakCollectionHelpers.ExtractWeakKeyObject(value);

        // WeakSet only accepts objects as values
        if (obj == null)
        {
            throw new Exception("Invalid value used in weak set");
        }

        // If already present, do nothing; otherwise add it
        if (!_values.TryGetValue(obj, out _))
        {
            _values.Add(obj, null);
        }

        return this;
    }

    /// <summary>
    ///     Returns true if the value exists in the WeakSet, false otherwise.
    /// </summary>
    public bool Has(JsValue value)
    {
        var obj = JsWeakCollectionHelpers.ExtractWeakKeyObject(value);
        return obj != null && _values.TryGetValue(obj, out _);
    }

    /// <summary>
    ///     Removes the specified value from the WeakSet.
    ///     Returns true if the value was in the WeakSet and has been removed, false otherwise.
    /// </summary>
    public bool Delete(JsValue value)
    {
        var obj = JsWeakCollectionHelpers.ExtractWeakKeyObject(value);
        return obj != null && _values.Remove(obj);
    }

}
