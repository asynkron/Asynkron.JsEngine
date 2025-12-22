#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// Utility methods for Object operations. Used by ObjectConstructor and ReflectHelper.
/// </summary>
public static class ObjectHelper
{
    internal static RealmState RequireRealm(RealmState? realm)
    {
        return realm ?? throw new InvalidOperationException("Realm is required for Object built-ins.");
    }

    internal static PropertyDescriptor ToPropertyDescriptor(JsValue candidate, RealmState realm)
    {
        if (!candidate.TryGetObject(out var descriptorObject))
        {
            throw ThrowTypeError("Property description must be an object", realm: realm);
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
            descriptor.JsValue = valueValue;
        }

        if (descriptorObject.TryGetProperty("writable", out var writableValue))
        {
            descriptor.Writable = JsOps.ToBoolean(writableValue);
        }

        if (descriptorObject.TryGetProperty("get", out var getterValue))
        {
            if (!getterValue.IsUndefined && !getterValue.TryGetObject<IJsCallable>(out _))
            {
                throw ThrowTypeError("Getter must be a function", realm: realm);
            }

            descriptor.Get = getterValue.IsUndefined
                ? null
                : getterValue.TryGetObject<IJsCallable>(out var getter)
                    ? getter
                    : null;
        }

        if (descriptorObject.TryGetProperty("set", out var setterValue))
        {
            if (!setterValue.IsUndefined && !setterValue.TryGetObject<IJsCallable>(out _))
            {
                throw ThrowTypeError("Setter must be a function", realm: realm);
            }

            descriptor.Set = setterValue.IsUndefined
                ? null
                : setterValue.TryGetObject<IJsCallable>(out var setter)
                    ? setter
                    : null;
        }

        if (descriptor is { IsAccessorDescriptor: true, IsDataDescriptor: true })
        {
            throw ThrowTypeError(
                "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute",
                realm: realm);
        }

        return descriptor;
    }

    internal static JsObject? FromPropertyDescriptor(PropertyDescriptor? descriptor, RealmState realm)
    {
        if (descriptor is null)
        {
            return null;
        }

        var result = new JsObject(realm.ObjectPrototype) { RealmState = realm };

        if (descriptor.IsAccessorDescriptor)
        {
            result.SetProperty("get",
                descriptor is { HasGet: true, Get: not null }
                    ? JsValue.FromObjectUnsafe(descriptor.Get)
                    : JsValue.Undefined);
            result.SetProperty("set",
                descriptor is { HasSet: true, Set: not null }
                    ? JsValue.FromObjectUnsafe(descriptor.Set)
                    : JsValue.Undefined);
        }
        else
        {
            var valJs = descriptor.HasValue ? descriptor.JsValue : JsValue.Undefined;
            result.SetProperty("value", valJs);
            result.SetProperty("writable", new JsValue(descriptor is { HasWritable: true, Writable: true }));
        }

        result.SetProperty("enumerable", new JsValue(descriptor is { HasEnumerable: true, Enumerable: true }));
        result.SetProperty("configurable", new JsValue(descriptor is { HasConfigurable: true, Configurable: true }));
        return result;
    }

    internal static bool TryDefinePropertyOnTarget(
        IJsObjectLike target,
        string propertyKey,
        PropertyDescriptor descriptor,
        RealmState realm,
        bool throwOnFailure)
    {
        if (target is JsArray jsArray && string.Equals(propertyKey, "length", StringComparison.Ordinal))
        {
            var success = jsArray.DefineLength(descriptor, null, throwOnFailure);
            if (!success && throwOnFailure)
            {
                throw ThrowTypeError("Cannot redefine property", realm: realm);
            }

            return success;
        }

        if (target is IPropertyDefinitionHost definitionHost)
        {
            var success = definitionHost.TryDefineProperty(propertyKey, descriptor);
            if (!success && throwOnFailure)
            {
                throw ThrowTypeError("Cannot redefine property", realm: realm);
            }

            return success;
        }

        try
        {
            target.DefineProperty(propertyKey, descriptor);
            return true;
        }
        catch (ThrowSignal)
        {
            if (throwOnFailure &&
                target is JsObject jsObject &&
                jsObject.GetOwnPropertyDescriptor(propertyKey) is { Configurable: false } current &&
                descriptor is { IsDataDescriptor: true, HasValue: true } &&
                (!descriptor.HasConfigurable || descriptor.Configurable == current.Configurable) &&
                (!descriptor.HasEnumerable || descriptor.Enumerable == current.Enumerable) &&
                (!descriptor.HasWritable || descriptor.Writable == current.Writable))
            {
                jsObject.SetProperty(propertyKey, descriptor.JsValue);
                return true;
            }

            if (throwOnFailure)
            {
                throw;
            }

            return false;
        }
    }

    internal static void PreventExtensionsOnTarget(IJsObjectLike target)
    {
        if (target is IExtensibilityControl extensibilityControl)
        {
            extensibilityControl.PreventExtensions();
            return;
        }

        target.Seal();
    }

    internal static bool IsTargetExtensible(IJsObjectLike target)
    {
        if (target is IExtensibilityControl extensibilityControl)
        {
            return extensibilityControl.IsExtensible;
        }

        return !target.IsSealed;
    }
}
