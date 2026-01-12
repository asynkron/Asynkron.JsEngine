#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Base class for JavaScript iterator objects (Map, Set, Array iterators).
/// </summary>
internal abstract class JsIteratorBase : IJsObjectLike, IAsJsValue, IPrototypeAccessorProvider
{
    protected readonly JsObject _properties = new();
    protected readonly JsValue _cachedJsValue;
    protected readonly RealmState? _realm;
    protected bool _done;

    protected JsIteratorBase(RealmState? realm, JsObject? prototype)
    {
        _realm = realm;
        _properties.RealmState = realm;
        if (prototype is not null)
        {
            _properties.SetPrototype(prototype);
        }

        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
    }

    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    public JsObject? Prototype => _properties.Prototype;
    public bool IsSealed => _properties.IsSealed;
    public bool IsFrozen => _properties.IsFrozen;
    IEnumerable<string> IJsObjectLike.Keys => _properties.Keys;

    public IJsPropertyAccessor? PrototypeAccessor =>
        _properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    public bool TryGetProperty(string name, out JsValue value) =>
        _properties.TryGetProperty(name, _cachedJsValue, out value);

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value) =>
        _properties.TryGetProperty(name, receiver, out value);

    public void SetProperty(string name, JsValue value) =>
        _properties.SetProperty(name, value, _cachedJsValue);

    public void SetProperty(string name, JsValue value, JsValue receiver) =>
        _properties.SetProperty(name, value, receiver);

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name) =>
        _properties.GetOwnPropertyDescriptor(name);

    public IEnumerable<string> GetOwnPropertyNames() =>
        _properties.GetOwnPropertyNames();

    public IEnumerable<string> GetEnumerablePropertyNames() =>
        _properties.GetEnumerablePropertyNames();

    public IEnumerable<string> GetOwnPropertyKeysInOrder(bool includeSymbols = true, bool includeNonEnumerable = true) =>
        _properties.GetOwnPropertyKeysInOrder(includeSymbols, includeNonEnumerable);

    public void DefineProperty(string name, PropertyDescriptor descriptor) =>
        _properties.DefineProperty(name, descriptor);

    public void SetPrototype(IJsPropertyAccessor? candidate) =>
        _properties.SetPrototype(candidate);

    public void Seal() => _properties.Seal();

    public bool Delete(string name) => _properties.DeleteOwnProperty(name);
}
