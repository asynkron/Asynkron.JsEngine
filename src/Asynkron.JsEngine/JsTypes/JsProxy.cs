using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    IPrototypeAccessorProvider
{
    private readonly JsObject _meta = new();
    private readonly RealmState? _realm;

    public JsProxy(IJsObjectLike target, IJsObjectLike handler, RealmState? realm = null)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _realm = realm;
        if (Target is JsObject { Prototype: not null } jsObject)
        {
            _meta.SetPrototype(jsObject.Prototype);
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

    public IEnumerable<string> Keys => Target.Keys;

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        return GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: false);
    }

    public IEnumerable<string> GetOwnPropertyKeysInOrder(bool includeSymbols = true, bool includeNonEnumerable = true)
    {
        IEnumerable<object?> keys;
        if (TryGetTrap("ownKeys", out var trap))
        {
            var trapResult = trap.Invoke([Target], Handler);
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

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        if (TryGetTrap("get", out var trap))
        {
            var args = new[] { Target, DecodePropertyKey(name), receiver ?? this };
            value = trap.Invoke(args, Handler);
            return true;
        }

        return Target.TryGetProperty(name, receiver ?? this, out value);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return TryGetProperty(name, this, out value);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        if (TryGetTrap("set", out var trap))
        {
            var args = new[] { Target, DecodePropertyKey(name), value, receiver ?? this };
            var result = trap.Invoke(args, Handler);
            if (!JsOps.ToBoolean(result))
            {
                throw StandardLibrary.ThrowTypeError("Proxy 'set' trap returned a falsy value", realm: _realm);
            }

            return;
        }

        Target.SetProperty(name, value, receiver ?? this);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (TryGetTrap("defineProperty", out var trap))
        {
            var descriptorObject = CreateDescriptorObject(descriptor);
            var args = new[] { Target, DecodePropertyKey(name), descriptorObject };
            var result = trap.Invoke(args, Handler);
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
        if (TryGetTrap("getOwnPropertyDescriptor", out var trap))
        {
            var args = new[] { Target, DecodePropertyKey(name) };
            var result = trap.Invoke(args, Handler);
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
            var args = new[] { Target, candidate };
            var result = trap.Invoke(args, Handler);
            if (!JsOps.ToBoolean(result))
            {
                throw StandardLibrary.ThrowTypeError("Proxy 'setPrototypeOf' trap returned a falsy value",
                    realm: _realm);
            }

            _meta.SetPrototype(candidate);
            return;
        }

        Target.SetPrototype(candidate);
        _meta.SetPrototype(candidate);
    }

    public void Seal()
    {
        Target.Seal();
    }

    public bool Delete(string name)
    {
        if (TryGetTrap("deleteProperty", out var trap))
        {
            var args = new[] { Target, DecodePropertyKey(name) };
            var result = trap.Invoke(args, Handler);
            return JsOps.ToBoolean(result);
        }

        return Target.Delete(name);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (TryGetTrap("defineProperty", out var trap))
        {
            var descriptorObject = CreateDescriptorObject(descriptor);
            var args = new[] { Target, DecodePropertyKey(name), descriptorObject };
            var result = trap.Invoke(args, Handler);
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
            var args = new[] { Target, DecodePropertyKey(name) };
            var result = trap.Invoke(args, Handler);
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

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
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
            var args = new object?[] { Target };
            var result = trap.Invoke(args, Handler);
            if (result is null)
            {
                _meta.SetPrototype(null);
                return null;
            }

            if (result is not IJsObjectLike && result is not IPrototypeAccessorProvider)
            {
                throw StandardLibrary.ThrowTypeError(
                    "Proxy getPrototypeOf trap must return an object or null",
                    realm: _realm);
            }

            _meta.SetPrototype(result);
            return result;
        }

        object? proto = Target.Prototype;
        if (proto is null && Target is IPrototypeAccessorProvider provider)
        {
            proto = provider.PrototypeAccessor;
        }

        _meta.SetPrototype(proto);
        return proto;
    }

    private static IEnumerable<object?> ExtractKeys(object? trapResult)
    {
        switch (trapResult)
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
            default:
                yield break;
        }
    }

    private bool TryGetTrap(string trapName, out IJsCallable callable)
    {
        callable = null!;
        var handler = Handler ?? throw StandardLibrary.ThrowTypeError("Cannot perform operation on a revoked Proxy",
            realm: _realm);

        if (!handler.TryGetProperty(trapName, out var trapValue) ||
            ReferenceEquals(trapValue, Symbol.Undefined) ||
            trapValue is null)
        {
            return false;
        }

        if (trapValue is not IJsCallable callableTrap)
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

    private static PropertyDescriptor? ConvertPropertyDescriptor(object? candidate, RealmState? realm)
    {
        if (candidate is null || ReferenceEquals(candidate, Symbol.Undefined))
        {
            return null;
        }

        if (candidate is not JsObject descriptorObject)
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
            if (!ReferenceEquals(getterValue, Symbol.Undefined) && getterValue is not IJsCallable)
            {
                throw StandardLibrary.ThrowTypeError("Getter must be a function", realm: realm);
            }

            descriptor.Get = ReferenceEquals(getterValue, Symbol.Undefined) ? null : (IJsCallable?)getterValue;
        }

        if (descriptorObject.TryGetProperty("set", out var setterValue))
        {
            if (!ReferenceEquals(setterValue, Symbol.Undefined) && setterValue is not IJsCallable)
            {
                throw StandardLibrary.ThrowTypeError("Setter must be a function", realm: realm);
            }

            descriptor.Set = ReferenceEquals(setterValue, Symbol.Undefined) ? null : (IJsCallable?)setterValue;
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
                descriptor is { HasGet: true, Get: not null } ? descriptor.Get : Symbol.Undefined);
            result.SetProperty("set",
                descriptor is { HasSet: true, Set: not null } ? descriptor.Set : Symbol.Undefined);
        }
        else
        {
            result.SetProperty("value", descriptor.HasValue ? descriptor.Value : Symbol.Undefined);
            result.SetProperty("writable", descriptor is { HasWritable: true, Writable: true });
        }

        result.SetProperty("enumerable", descriptor is { HasEnumerable: true, Enumerable: true });
        result.SetProperty("configurable", descriptor is { HasConfigurable: true, Configurable: true });
        return result;
    }
}
