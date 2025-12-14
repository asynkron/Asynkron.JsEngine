using System.Collections;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Proxy wrapper that forwards object operations through the handler traps when available.
///     Only the traps required by the current test surface (has/get/set/defineProperty/getOwnPropertyDescriptor/delete)
///     are implemented for now; other operations fall back to the underlying target.
/// </summary>
public sealed class JsProxy : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl, IJsCallable,
    IPrototypeAccessorProvider, IPrivateBrandHolder
{
    private readonly JsObject _meta = new();
    private readonly JsObject _privateStorage = new();
    private readonly HashSet<object> _privateBrands = new(ReferenceEqualityComparer<object>.Instance);
    private readonly RealmState? _realm;

    public JsProxy(IJsObjectLike target, IJsObjectLike handler, RealmState? realm = null)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _realm = realm;
        _privateStorage.RealmState = realm;
        if (Target is JsObject { Prototype: not null } jsObject)
        {
            _meta.SetPrototype(jsObject.Prototype);
            _privateStorage.SetPrototype(_meta.Prototype);
        }
        else if (_meta.Prototype is null && Target is IPrototypeAccessorProvider provider &&
                 provider.PrototypeAccessor is { } protoAccessor)
        {
            _meta.SetPrototype(protoAccessor);
            _privateStorage.SetPrototype(_meta.Prototype);
        }
    }

    public IJsObjectLike Target { get; }

    public IJsObjectLike? Handler { get; set; }
    public bool IsExtensible => Target is IExtensibilityControl extensibility ? extensibility.IsExtensible : true;

    public void PreventExtensions()
    {
        if (Target is IExtensibilityControl extensibilityControl)
        {
            extensibilityControl.PreventExtensions();
        }
        else
        {
            Target.Seal();
        }
    }

    public JsObject? Prototype => _meta.Prototype;
    public IJsPropertyAccessor? PrototypeAccessor =>
        _meta is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    public bool IsSealed => Target.IsSealed;

    public bool IsFrozen => Target.IsFrozen;

    public IEnumerable<string> Keys => Target.Keys;

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        return GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: false);
    }

    public void AddPrivateBrand(object brand)
    {
        _privateBrands.Add(brand);
    }

    public bool HasPrivateBrand(object brand)
    {
        return _privateBrands.Contains(brand);
    }

    public IEnumerable<string> GetOwnPropertyKeysInOrder(bool includeSymbols = true, bool includeNonEnumerable = true)
    {
        IEnumerable<object?> keys;
        if (TryGetTrap("ownKeys", out var trap))
        {
            var trapResult = trap.Invoke([JsValue.FromObject(Target)], JsValue.FromObject(Handler));
            keys = ExtractKeys(trapResult);
        }
        else
        {
            keys = Target.GetOwnPropertyKeysInOrder(includeSymbols, includeNonEnumerable)
                .Cast<object?>();
        }

        foreach (var key in keys)
        {
            var propertyName = JsOps.ToPropertyName(key);
            if (propertyName is null)
            {
                continue;
            }

            if (!includeSymbols && TypedAstSymbol.TryGetByInternalKey(propertyName, out _))
            {
                continue;
            }

            if (!includeNonEnumerable)
            {
                var desc = GetOwnPropertyDescriptor(propertyName);
                if (desc is null || !desc.Enumerable)
                {
                    continue;
                }
            }

            yield return propertyName;
        }
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        if (name.IsPrivateSlotName())
        {
            return _privateStorage.TryGetProperty(name, receiver.IsUndefined ? this : receiver.AsObject(), out value);
        }

        if (TryGetTrap("get", out var trap))
        {
            var args = new[] { JsValue.FromObject(Target), JsValue.FromObject(DecodePropertyKey(name)), receiver.IsUndefined ? JsValue.FromObject(this) : receiver };
            value = trap.Invoke(args, JsValue.FromObject(Handler));
            return true;
        }

        return Target.TryGetProperty(name, receiver.IsUndefined ? this : receiver.AsObject(), out value);
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, JsValue.FromObject(this), out value);
    }

    // Legacy interface implementation
    bool IJsPropertyAccessor.TryGetProperty(string name, out object? value)
    {
        var result = TryGetProperty(name, out var jsValue);
        value = jsValue.ToObject();
        return result;
    }

    bool IJsPropertyAccessor.TryGetProperty(string name, object? receiver, out object? value)
    {
        var result = TryGetProperty(name, JsValue.FromObject(receiver ?? this), out var jsValue);
        value = jsValue.ToObject();
        return result;
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        if (name.IsPrivateSlotName())
        {
            _privateStorage.SetProperty(name, value, receiver.IsUndefined ? this : receiver.AsObject());
            return;
        }

        if (TryGetTrap("set", out var trap))
        {
            var args = new[] { JsValue.FromObject(Target), JsValue.FromObject(DecodePropertyKey(name)), value, receiver.IsUndefined ? JsValue.FromObject(this) : receiver };
            var result = trap.Invoke(args, JsValue.FromObject(Handler));
            if (!JsOps.ToBoolean(result))
            {
                throw StandardLibrary.ThrowTypeError("Proxy 'set' trap returned a falsy value", realm: _realm);
            }

            return;
        }

        Target.SetProperty(name, value, receiver.IsUndefined ? this : receiver.AsObject());
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, JsValue.FromObject(this));
    }

    // Legacy interface implementation
    void IJsPropertyAccessor.SetProperty(string name, object? value)
    {
        SetProperty(name, JsValue.FromObject(value));
    }

    void IJsPropertyAccessor.SetProperty(string name, object? value, object? receiver)
    {
        SetProperty(name, JsValue.FromObject(value), JsValue.FromObject(receiver ?? this));
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (name.IsPrivateSlotName())
        {
            _privateStorage.DefineProperty(name, descriptor);
            return;
        }

        if (TryGetTrap("defineProperty", out var trap))
        {
            var descriptorObject = CreateDescriptorObject(descriptor);
            var args = new[] { JsValue.FromObject(Target), JsValue.FromObject(DecodePropertyKey(name)), JsValue.FromObject(descriptorObject) };
            var result = trap.Invoke(args, JsValue.FromObject(Handler));
            if (!JsOps.ToBoolean(result))
            {
                throw StandardLibrary.ThrowTypeError("Proxy 'defineProperty' trap returned a falsy value",
                    realm: _realm);
            }

            return;
        }

        Target.DefineProperty(name, descriptor);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (name.IsPrivateSlotName())
        {
            return null;
        }

        if (TryGetTrap("getOwnPropertyDescriptor", out var trap))
        {
            var args = new[] { JsValue.FromObject(Target), JsValue.FromObject(DecodePropertyKey(name)) };
            var result = trap.Invoke(args, JsValue.FromObject(Handler));
            return ConvertPropertyDescriptor(result, _realm);
        }

        return Target.GetOwnPropertyDescriptor(name);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        return Target.GetOwnPropertyNames();
    }

    public void SetPrototype(object? candidate)
    {
        if (TryGetTrap("setPrototypeOf", out var trap))
        {
            var args = new[] { JsValue.FromObject(Target), JsValue.FromObject(candidate) };
            var result = trap.Invoke(args, JsValue.FromObject(Handler));
            if (!JsOps.ToBoolean(result))
            {
                throw StandardLibrary.ThrowTypeError("Proxy 'setPrototypeOf' trap returned a falsy value",
                    realm: _realm);
            }

            _meta.SetPrototype(candidate);
            _privateStorage.SetPrototype(_meta.Prototype);
            return;
        }

        Target.SetPrototype(candidate);
        _meta.SetPrototype(candidate);
        _privateStorage.SetPrototype(_meta.Prototype);
    }

    public void Seal()
    {
        Target.Seal();
    }

    public bool Delete(string name)
    {
        if (TryGetTrap("deleteProperty", out var trap))
        {
            var args = new[] { JsValue.FromObject(Target), JsValue.FromObject(DecodePropertyKey(name)) };
            var result = trap.Invoke(args, JsValue.FromObject(Handler));
            return JsOps.ToBoolean(result);
        }

        return Target.Delete(name);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (name.IsPrivateSlotName())
        {
            return _privateStorage.TryDefineProperty(name, descriptor);
        }

        if (TryGetTrap("defineProperty", out var trap))
        {
            var descriptorObject = CreateDescriptorObject(descriptor);
            var args = new[] { JsValue.FromObject(Target), JsValue.FromObject(DecodePropertyKey(name)), JsValue.FromObject(descriptorObject) };
            var result = trap.Invoke(args, JsValue.FromObject(Handler));
            return JsOps.ToBoolean(result);
        }

        try
        {
            Target.DefineProperty(name, descriptor);
            return true;
        }
        catch (ThrowSignal)
        {
            return false;
        }
    }

    internal bool HasProperty(string name)
    {
        if (TryGetTrap("has", out var trap))
        {
            var args = new[] { JsValue.FromObject(Target), JsValue.FromObject(DecodePropertyKey(name)) };
            var result = trap.Invoke(args, JsValue.FromObject(Handler));
            return JsOps.ToBoolean(result);
        }

        if (Target is JsObject jsObject && jsObject.HasProperty(name))
        {
            return true;
        }

        if (Target.GetOwnPropertyDescriptor(name) is not null)
        {
            return true;
        }

        var prototype = Target.Prototype;
        while (prototype is not null)
        {
            if (prototype.HasProperty(name))
            {
                return true;
            }

            prototype = prototype.Prototype;
        }

        return Target.TryGetProperty(name, out _);
    }

    public JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
    {
        _ = Handler ?? throw StandardLibrary.ThrowTypeError("Cannot perform operation on a revoked Proxy",
            realm: _realm);

        if (Target is not IJsCallable callableTarget)
        {
            throw StandardLibrary.ThrowTypeError("Proxy target is not callable", realm: _realm);
        }

        return callableTarget.Invoke(arguments, thisValue);
    }

    internal object? GetPrototypeWithTrap()
    {
        if (TryGetTrap("getPrototypeOf", out var trap))
        {
            var args = new[] { JsValue.FromObject(Target) };
            var result = trap.Invoke(args, JsValue.FromObject(Handler));
            if (result.IsNull)
            {
                _meta.SetPrototype(null);
                _privateStorage.SetPrototype(null);
                return null;
            }

            var resultObj = result.AsObject();
            if (resultObj is not IJsObjectLike && resultObj is not IPrototypeAccessorProvider)
            {
                throw StandardLibrary.ThrowTypeError(
                    "Proxy getPrototypeOf trap must return an object or null",
                    realm: _realm);
            }

            _meta.SetPrototype(resultObj);
            _privateStorage.SetPrototype(_meta.Prototype);
            return resultObj;
        }

        object? proto = Target.Prototype;
        if (proto is null && Target is IPrototypeAccessorProvider provider)
        {
            proto = provider.PrototypeAccessor;
        }

        _meta.SetPrototype(proto);
        _privateStorage.SetPrototype(_meta.Prototype);
        return proto;
    }

    private static IEnumerable<object?> ExtractKeys(JsValue trapResult)
    {
        if (trapResult.IsObject)
        {
            var obj = trapResult.AsObject();
            switch (obj)
            {
                case JsArray jsArray:
                    foreach (var item in jsArray.Items)
                    {
                        yield return item;
                    }

                    yield break;
                case IEnumerable enumerable:
                    foreach (var item in enumerable)
                    {
                        yield return item;
                    }

                    yield break;
            }
        }
    }

    private bool TryGetTrap(string trapName, out IJsCallable callable)
    {
        callable = null!;
        var handler = Handler ?? throw StandardLibrary.ThrowTypeError("Cannot perform operation on a revoked Proxy",
            realm: _realm);

        if (!handler.TryGetProperty(trapName, out var trapValue) || trapValue.IsUndefined || trapValue.IsNull)
        {
            return false;
        }

        if (!trapValue.IsObject || trapValue.AsObject() is not IJsCallable callableTrap)
        {
            throw StandardLibrary.ThrowTypeError($"Proxy handler's '{trapName}' trap is not callable", realm: _realm);
        }

        callable = callableTrap;
        return true;
    }

    private static object DecodePropertyKey(string propertyName)
    {
        return TypedAstSymbol.TryGetByInternalKey(propertyName, out var symbol)
            ? symbol
            : propertyName;
    }

    private static PropertyDescriptor? ConvertPropertyDescriptor(JsValue candidate, RealmState? realm)
    {
        if (candidate.IsNull || candidate.IsUndefined)
        {
            return null;
        }

        if (!candidate.IsObject || candidate.AsObject() is not JsObject descriptorObject)
        {
            throw StandardLibrary.ThrowTypeError(
                "Proxy getOwnPropertyDescriptor trap must return an object or undefined", realm: realm);
        }

        var descriptor = new PropertyDescriptor();

        if (descriptorObject.TryGetProperty("enumerable", out var enumerableValue))
        {
            descriptor.Enumerable = JsOps.ToBoolean(enumerableValue);
        }

        if (descriptorObject.TryGetProperty("configurable", out var configurableValue))
        {
            descriptor.Configurable = JsOps.ToBoolean(configurableValue);
        }

        if (descriptorObject.TryGetProperty("value", out var valueValue))
        {
            descriptor.Value = valueValue;
        }

        if (descriptorObject.TryGetProperty("writable", out var writableValue))
        {
            descriptor.Writable = JsOps.ToBoolean(writableValue);
        }

        if (descriptorObject.TryGetProperty("get", out var getterValue))
        {
            if (!getterValue.IsUndefined && (!getterValue.IsObject || getterValue.AsObject() is not IJsCallable))
            {
                throw StandardLibrary.ThrowTypeError("Getter must be a function", realm: realm);
            }

            descriptor.Get = getterValue.IsUndefined ? null : (IJsCallable?)getterValue.AsObject();
        }

        if (descriptorObject.TryGetProperty("set", out var setterValue))
        {
            if (!setterValue.IsUndefined && (!setterValue.IsObject || setterValue.AsObject() is not IJsCallable))
            {
                throw StandardLibrary.ThrowTypeError("Setter must be a function", realm: realm);
            }

            descriptor.Set = setterValue.IsUndefined ? null : (IJsCallable?)setterValue.AsObject();
        }

        if (descriptor is { IsAccessorDescriptor: true, IsDataDescriptor: true })
        {
            throw StandardLibrary.ThrowTypeError(
                "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute",
                realm: realm);
        }

        return descriptor;
    }

    private static JsObject CreateDescriptorObject(PropertyDescriptor descriptor)
    {
        var result = new JsObject();

        if (descriptor.IsAccessorDescriptor)
        {
            result.SetProperty("get",
                descriptor is { HasGet: true, Get: not null } ? JsValue.FromObject(descriptor.Get) : JsValue.Undefined);
            result.SetProperty("set",
                descriptor is { HasSet: true, Set: not null } ? JsValue.FromObject(descriptor.Set) : JsValue.Undefined);
        }
        else
        {
            result.SetProperty("value", descriptor.Value);
            result.SetProperty("writable", JsValue.FromObject(descriptor is { HasWritable: true, Writable: true }));
        }

        result.SetProperty("enumerable", JsValue.FromObject(descriptor is { HasEnumerable: true, Enumerable: true }));
        result.SetProperty("configurable", JsValue.FromObject(descriptor is { HasConfigurable: true, Configurable: true }));
        return result;
    }
}
