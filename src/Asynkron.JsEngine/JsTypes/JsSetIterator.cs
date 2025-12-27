#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.JsTypes;

internal sealed class JsSetIterator : IJsObjectLike, IAsJsValue, IPrototypeAccessorProvider
{
    private readonly JsSet _set;
    private readonly SetIterationKind _kind;
    private readonly RealmState _realm;
    private readonly JsObject _properties = new();
    private JsValue _cachedJsValue;
    private int _index;
    private bool _done;

    internal JsSetIterator(JsSet set, SetIterationKind kind, RealmState realm, JsObject? prototype)
    {
        _set = set;
        _kind = kind;
        _realm = realm;
        _properties.RealmState = realm;
        if (prototype is not null)
        {
            _properties.SetPrototype(prototype);
        }

        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
    }

    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    internal JsValue Next()
    {
        if (_done)
        {
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        if (_index >= _set.ValueCount)
        {
            _done = true;
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        var value = _set.GetValue(_index++);
        var result = _kind == SetIterationKind.Entries ? CreateEntryPair(value) : value;
        return IteratorResultObject.Create(result, false);
    }

    private JsValue CreateEntryPair(JsValue value)
    {
        var pair = new JsArray(_realm);
        pair.SetElement(0, value);
        pair.SetElement(1, value);
        return JsValue.FromJsArray(pair);
    }

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
