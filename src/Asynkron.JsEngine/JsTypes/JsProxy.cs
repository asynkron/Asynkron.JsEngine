using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Proxy wrapper that forwards object operations through the handler traps when available.
///     Only the traps required by the current test surface (has/get/set/defineProperty/getOwnPropertyDescriptor/delete)
///     are implemented for now; other operations fall back to the underlying target.
/// </summary>
public sealed class JsProxy : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl
{
    private readonly IJsObjectLike _target;
    private readonly JsObject _meta = new();

    public JsProxy(IJsObjectLike target, IJsObjectLike handler)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        if (_target is JsObject { Prototype: not null } jsObject)
        {
            _meta.SetPrototype(jsObject.Prototype);
        }
    }

    public IJsObjectLike Target => _target;
    public IJsObjectLike? Handler { get; set; }

    public JsObject? Prototype => _meta.Prototype;

    public bool IsSealed => _target.IsSealed;
    public bool IsExtensible => _target is IExtensibilityControl extensibility ? extensibility.IsExtensible : true;

    public IEnumerable<string> Keys => _target.Keys;

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        if (TryGetTrap("get", out var trap))
        {
            var args = new[] { (object?)_target, DecodePropertyKey(name), receiver ?? this };
            value = trap.Invoke(args, Handler);
            return true;
        }

        return _target.TryGetProperty(name, receiver ?? this, out value);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return TryGetProperty(name, this, out value);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        if (TryGetTrap("set", out var trap))
        {
            var args = new[] { (object?)_target, DecodePropertyKey(name), value, receiver ?? this };
            var result = trap.Invoke(args, Handler);
            if (!JsOps.ToBoolean(result))
            {
                throw StandardLibrary.ThrowTypeError("Proxy 'set' trap returned a falsy value");
            }

            return;
        }

        _target.SetProperty(name, value, receiver ?? this);
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
            var args = new[] { (object?)_target, DecodePropertyKey(name), descriptorObject };
            var result = trap.Invoke(args, Handler);
            if (!JsOps.ToBoolean(result))
            {
                throw StandardLibrary.ThrowTypeError("Proxy 'defineProperty' trap returned a falsy value");
            }

            return;
        }

        _target.DefineProperty(name, descriptor);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (TryGetTrap("defineProperty", out var trap))
        {
            var descriptorObject = CreateDescriptorObject(descriptor);
            var args = new[] { (object?)_target, DecodePropertyKey(name), descriptorObject };
            var result = trap.Invoke(args, Handler);
            return JsOps.ToBoolean(result);
        }

        try
        {
            _target.DefineProperty(name, descriptor);
            return true;
        }
        catch (ThrowSignal)
        {
            return false;
        }
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (TryGetTrap("getOwnPropertyDescriptor", out var trap))
        {
            var args = new[] { (object?)_target, DecodePropertyKey(name) };
            var result = trap.Invoke(args, Handler);
            return ConvertPropertyDescriptor(result);
        }

        return _target.GetOwnPropertyDescriptor(name);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        return _target.GetOwnPropertyNames();
    }

    public void SetPrototype(object? candidate)
    {
        _target.SetPrototype(candidate);
        _meta.SetPrototype(candidate);
    }

    public void PreventExtensions()
    {
        if (_target is IExtensibilityControl extensibilityControl)
        {
            extensibilityControl.PreventExtensions();
        }
        else
        {
            _target.Seal();
        }
    }

    public void Seal()
    {
        _target.Seal();
    }

    public bool Delete(string name)
    {
        if (TryGetTrap("deleteProperty", out var trap))
        {
            var args = new[] { (object?)_target, DecodePropertyKey(name) };
            var result = trap.Invoke(args, Handler);
            return JsOps.ToBoolean(result);
        }

        return _target.Delete(name);
    }

    internal bool HasProperty(string name)
    {
        if (TryGetTrap("has", out var trap))
        {
            var args = new[] { (object?)_target, DecodePropertyKey(name) };
            var result = trap.Invoke(args, Handler);
            return JsOps.ToBoolean(result);
        }

        if (_target is JsObject jsObject && jsObject.HasProperty(name))
        {
            return true;
        }

        if (_target.GetOwnPropertyDescriptor(name) is not null)
        {
            return true;
        }

        var prototype = _target.Prototype;
        while (prototype is not null)
        {
            if (prototype.HasProperty(name))
            {
                return true;
            }

            prototype = prototype.Prototype;
        }

        return _target.TryGetProperty(name, out _);
    }

    private bool TryGetTrap(string trapName, out IJsCallable callable)
    {
        callable = null!;
        var handler = Handler ?? throw StandardLibrary.ThrowTypeError("Cannot perform operation on a revoked Proxy");

        if (!handler.TryGetProperty(trapName, out var trapValue) ||
            ReferenceEquals(trapValue, Symbol.Undefined) ||
            trapValue is null)
        {
            return false;
        }

        if (trapValue is not IJsCallable callableTrap)
        {
            throw StandardLibrary.ThrowTypeError($"Proxy handler's '{trapName}' trap is not callable");
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

    private static PropertyDescriptor? ConvertPropertyDescriptor(object? candidate)
    {
        if (candidate is null || ReferenceEquals(candidate, Symbol.Undefined))
        {
            return null;
        }

        if (candidate is not JsObject descriptorObject)
        {
            throw StandardLibrary.ThrowTypeError("Proxy getOwnPropertyDescriptor trap must return an object or undefined");
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
                throw StandardLibrary.ThrowTypeError("Getter must be a function");
            }

            descriptor.Get = ReferenceEquals(getterValue, Symbol.Undefined) ? null : (IJsCallable?)getterValue;
        }

        if (descriptorObject.TryGetProperty("set", out var setterValue))
        {
            if (!ReferenceEquals(setterValue, Symbol.Undefined) && setterValue is not IJsCallable)
            {
                throw StandardLibrary.ThrowTypeError("Setter must be a function");
            }

            descriptor.Set = ReferenceEquals(setterValue, Symbol.Undefined) ? null : (IJsCallable?)setterValue;
        }

        if (descriptor is { IsAccessorDescriptor: true, IsDataDescriptor: true })
        {
            throw StandardLibrary.ThrowTypeError(
                "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute");
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
