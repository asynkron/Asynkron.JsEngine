#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.JsTypes;

internal sealed class JsArrayIterator : IJsObjectLike, IAsJsValue, IPrototypeAccessorProvider
{
    private readonly IJsPropertyAccessor _accessor;
    private readonly ArrayIteratorKind _kind;
    private readonly RealmState? _realm;
    private readonly Func<uint, JsValue>? _projector;
    private readonly JsObject _properties = new();
    private readonly TypedArrayBase? _typedAccessor;
    private JsValue _cachedJsValue;
    private uint _index;
    private bool _exhausted;

    internal JsArrayIterator(IJsPropertyAccessor accessor, ArrayIteratorKind kind, RealmState? realm, JsObject? prototype)
    {
        _accessor = accessor;
        _kind = kind;
        _realm = realm;
        _typedAccessor = accessor as TypedArrayBase;
        _properties.RealmState = realm;
        if (prototype is not null)
        {
            _properties.SetPrototype(prototype);
        }

        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
    }

    internal JsArrayIterator(IJsPropertyAccessor accessor, Func<uint, JsValue> projector, RealmState? realm,
        JsObject? prototype)
        : this(accessor, ArrayIteratorKind.Values, realm, prototype)
    {
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
    }

    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    internal JsValue Next()
    {
        _realm?.Logger?.LogInformation("ArrayIterator.next index={Index}", _index);
        if (_exhausted)
        {
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        if (_typedAccessor?.IsDetachedOrOutOfBounds() == true)
        {
            throw _typedAccessor.CreateOutOfBoundsTypeError();
        }

        if (!_accessor.TryGetProperty("length", out var lenVal))
        {
            lenVal = JsValue.Zero;
        }

        var length = ReadLength(lenVal);
        if (_index < length)
        {
            var valueJs = _projector is null ? ProjectIteratorValue(_kind, _index) : _projector(_index);
            _index++;
            return IteratorResultObject.Create(valueJs, false);
        }

        _exhausted = true;
        return IteratorResultObject.DoneUndefined.AsJsValue;
    }

    private uint ReadLength(JsValue lengthValue)
    {
        if (_realm is null)
        {
            var length = StandardLibrary.ToLengthOrZero(lengthValue);
            return (uint)Math.Min(Math.Max(length, 0), uint.MaxValue);
        }

        var evalContext = _realm.CreateContext();
        var lengthNumeric = StandardLibrary.ToLengthOrZero(lengthValue, evalContext);
        if (evalContext.IsThrow)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        return (uint)Math.Min(Math.Max(lengthNumeric, 0), uint.MaxValue);
    }

    private JsValue ProjectIteratorValue(ArrayIteratorKind kind, uint index)
    {
        return kind switch
        {
            ArrayIteratorKind.Keys => new JsValue((double)index),
            ArrayIteratorKind.Values => StandardLibrary.GetElementOrUndefinedJsValue(_accessor, index),
            ArrayIteratorKind.Entries => CreateIteratorEntryPair(index),
            _ => JsValue.Undefined
        };
    }

    private JsValue CreateIteratorEntryPair(uint index)
    {
        var pair = new JsArray(_realm);
        pair.Push((double)index);
        pair.Push(StandardLibrary.GetElementOrUndefinedJsValue(_accessor, index));
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
