#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Specifies the kind of array iterator to create.
/// Using an enum avoids closure allocations compared to passing Func delegates.
/// </summary>
internal enum ArrayIteratorKind
{
    /// <summary>Iterator yields [index, value] pairs.</summary>
    Entries,
    /// <summary>Iterator yields index values only.</summary>
    Keys,
    /// <summary>Iterator yields element values only.</summary>
    Values
}

internal sealed class JsArrayIterator : IJsObjectLike, IAsJsValue, IPrototypeAccessorProvider
{
    private readonly IJsPropertyAccessor _accessor;
    private readonly ArrayIteratorKind _kind;
    private readonly RealmState? _realm;
    private readonly JsObject _properties = new();
    private readonly JsValue _cachedJsValue;
    private uint _index;
    private bool _done;

    internal JsArrayIterator(IJsPropertyAccessor accessor, ArrayIteratorKind kind, RealmState? realm, JsObject? prototype)
    {
        _accessor = accessor;
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

        // Check for detached typed arrays
        if (_accessor is TypedArrayBase typedAccessor && typedAccessor.IsDetachedOrOutOfBounds())
        {
            throw typedAccessor.CreateOutOfBoundsTypeError();
        }

        // Get length as JsValue, avoiding boxing from ternary expression
        if (!_accessor.TryGetProperty("length", out var lenVal))
        {
            lenVal = JsValue.Zero;
        }

        var evalContext = _realm?.CreateContext();
        var length = (uint)Math.Min(Math.Max(StandardLibrary.ToLengthOrZero(lenVal, evalContext), 0), uint.MaxValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        if (_index < length)
        {
            var valueJs = ProjectIteratorValue();
            _index++;
            return IteratorResultObjectPool.Rent(valueJs, false).AsJsValue;
        }

        _done = true;
        return IteratorResultObject.DoneUndefined.AsJsValue;
    }

    private JsValue ProjectIteratorValue()
    {
        return _kind switch
        {
            ArrayIteratorKind.Keys => new JsValue((double)_index),
            ArrayIteratorKind.Values => GetElementOrUndefinedJsValue(_accessor, _index),
            ArrayIteratorKind.Entries => CreateIteratorEntryPair(_index, _accessor, _realm),
            _ => JsValue.Undefined
        };
    }

    private static JsValue GetElementOrUndefinedJsValue(IJsPropertyAccessor accessor, uint index)
    {
        if (accessor.TryGetProperty(index.ToString(System.Globalization.CultureInfo.InvariantCulture), out var val))
        {
            return val;
        }

        return JsValue.Undefined;
    }

    private static JsValue CreateIteratorEntryPair(uint index, IJsPropertyAccessor accessor, RealmState? realm)
    {
        var pair = new JsArray(realm);
        pair.Push((double)index);
        pair.Push(GetElementOrUndefinedJsValue(accessor, index));
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
