#region

using System;
using System.Collections.Generic;
using Asynkron.JsEngine.StdLib;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.JsTypes;

internal sealed class JsArrayIterator : IJsObjectLike, IAsJsValue, IPrototypeAccessorProvider
{
    private readonly IJsPropertyAccessor _accessor;
    private readonly ArrayIteratorKind _kind;
    private readonly RealmState? _realm;
    private readonly JsObject _properties = new();
    private readonly JsValue _cachedJsValue;
    private readonly TypedArrayBase? _typedAccessor;
    private uint _index;
    private bool _done;

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

    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    internal JsValue Next()
    {
        _realm?.Logger?.LogInformation("ArrayIterator.next index={Index}", _index);
        if (_done)
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

        var evalContext = _realm?.CreateContext();
        var length = (uint)Math.Min(Math.Max(StandardLibrary.ToLengthOrZero(lenVal, evalContext), 0), uint.MaxValue);
        if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);
        if (_index < length)
        {
            var value = ProjectIteratorValue(_index);
            _index++;
            return new IteratorResultObject(value, false).AsJsValue;
        }

        _done = true;
        return IteratorResultObject.DoneUndefined.AsJsValue;
    }

    private JsValue ProjectIteratorValue(uint index)
    {
        return _kind switch
        {
            ArrayIteratorKind.Keys => new JsValue((double)index),
            ArrayIteratorKind.Values => GetValue(index),
            ArrayIteratorKind.Entries => CreateIteratorEntryPair(index),
            _ => JsValue.Undefined
        };
    }

    private JsValue GetValue(uint index)
    {
        if (_typedAccessor is not null)
        {
            return _typedAccessor.GetValueForIndex((int)index);
        }

        return StandardLibrary.GetElementOrUndefinedJsValue(_accessor, index);
    }

    private JsValue CreateIteratorEntryPair(uint index)
    {
        var pair = new JsArray(_realm);
        pair.Push((double)index);
        pair.Push(GetValue(index));
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
