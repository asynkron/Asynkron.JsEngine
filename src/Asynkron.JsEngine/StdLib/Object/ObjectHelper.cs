#region

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
        // Use IsObject instead of TryGetObject to accept any object type (JsObject, JsArgumentsObject, etc.)
        if (!candidate.IsObject)
        {
            throw ThrowTypeError("Property description must be an object", realm: realm);
        }

        var descriptor = new PropertyDescriptor();

        // Use JsOps.TryGetPropertyValue to access properties on any object type
        if (JsOps.TryGetPropertyValue(candidate, "enumerable", out var enumerableValue))
        {
            descriptor.Enumerable = JsOps.ToBoolean(enumerableValue);
        }

        if (JsOps.TryGetPropertyValue(candidate, "configurable", out var configurableValue))
        {
            descriptor.Configurable = JsOps.ToBoolean(configurableValue);
        }

        if (JsOps.TryGetPropertyValue(candidate, "value", out var valueValue))
        {
            descriptor.JsValue = valueValue;
        }

        if (JsOps.TryGetPropertyValue(candidate, "writable", out var writableValue))
        {
            descriptor.Writable = JsOps.ToBoolean(writableValue);
        }

        if (JsOps.TryGetPropertyValue(candidate, "get", out var getterValue))
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

        if (JsOps.TryGetPropertyValue(candidate, "set", out var setterValue))
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

    /// <summary>
    /// Performs the abstract operation Set(O, P, V, Throw) per ES spec.
    /// When throwOnError is true, throws a TypeError if the property cannot be set.
    /// This is used by Object.assign which requires Set(to, nextKey, propValue, true).
    /// </summary>
    internal static void SetPropertyOrThrow(
        IJsObjectLike target,
        string propertyKey,
        JsValue value,
        JsValue receiver,
        RealmState realm)
    {
        // For proxy objects, SetProperty already throws when the set trap returns false
        if (target is JsProxy)
        {
            target.SetProperty(propertyKey, value, receiver);
            return;
        }

        // Check own property descriptor first
        var descriptor = target.GetOwnPropertyDescriptor(propertyKey);
        if (descriptor is not null)
        {
            if (descriptor.IsAccessorDescriptor)
            {
                if (descriptor.Set is null)
                {
                    throw ThrowTypeError(
                        $"Cannot set property '{propertyKey}' of object",
                        realm: realm);
                }

                descriptor.Set.Invoke(new SingleValueArgs(value), receiver);
                return;
            }

            if (!descriptor.Writable)
            {
                throw ThrowTypeError(
                    $"Cannot assign to read only property '{propertyKey}' of object",
                    realm: realm);
            }
        }
        else if (target is IExtensibilityControl { IsExtensible: false })
        {
            // Non-extensible and property doesn't exist -> can't create new properties
            throw ThrowTypeError(
                $"Cannot add property {propertyKey}, object is not extensible",
                realm: realm);
        }

        target.SetProperty(propertyKey, value, receiver);
    }

    /// <summary>
    /// Performs SetIntegrityLevel(O, "frozen") per ES spec.
    /// Works with Proxy objects by using the proper abstract operations.
    /// Returns false if the operation fails (e.g., preventExtensions returns false).
    /// </summary>
    internal static bool SetIntegrityLevel(IJsObjectLike target, bool freeze, RealmState realm)
    {
        // For Proxy objects we must use the spec-compliant trap-based approach
        // so that preventExtensions/defineProperty traps are invoked correctly.
        if (target is JsProxy)
        {
            return SetIntegrityLevelViaTraps(target, freeze, realm);
        }

        // For non-Proxy objects, use native Freeze()/Seal() methods directly.
        // Types like JsArray have dense storage optimizations that conflict with
        // the generic PreventExtensions + DefineProperty approach (because after
        // PreventExtensions marks _properties as non-extensible, promoting dense
        // elements into _properties would fail).
        if (freeze)
        {
            FreezeTarget(target);
        }
        else
        {
            target.Seal();
        }

        return true;
    }

    private static void FreezeTarget(IJsObjectLike target)
    {
        switch (target)
        {
            case JsArray jsArray:
                jsArray.Freeze();
                break;
            case JsObject jsObject:
                jsObject.Freeze();
                break;
            default:
                // Fallback: seal first, then make data properties non-writable via DefineProperty
                target.Seal();
                foreach (var key in target.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
                {
                    var desc = target.GetOwnPropertyDescriptor(key);
                    if (desc is { IsDataDescriptor: true, Writable: true })
                    {
                        target.DefineProperty(key, new PropertyDescriptor { Writable = false });
                    }
                }

                break;
        }
    }

    private static bool SetIntegrityLevelViaTraps(IJsObjectLike target, bool freeze, RealmState realm)
    {
        // Step 3: Let status be ? O.[[PreventExtensions]]().
        // Step 4: If status is false, return false.
        if (!TryPreventExtensions(target, realm))
        {
            return false;
        }

        // Step 5: Let keys be ? O.[[OwnPropertyKeys]]().
        var keys = target.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true);

        if (freeze)
        {
            // Step 7: For each element k of keys, do
            foreach (var key in keys)
            {
                var currentDesc = target.GetOwnPropertyDescriptor(key);
                if (currentDesc is null)
                {
                    continue;
                }

                PropertyDescriptor newDesc;
                if (currentDesc.IsAccessorDescriptor)
                {
                    // Step 7.b.i.1: If currentDesc.[[Configurable]] is true, set desc to PropertyDescriptor { [[Configurable]]: false }
                    newDesc = new PropertyDescriptor { Configurable = false };
                }
                else
                {
                    // Step 7.b.ii: Set desc to PropertyDescriptor { [[Configurable]]: false, [[Writable]]: false }
                    newDesc = new PropertyDescriptor { Configurable = false, Writable = false };
                }

                TryDefinePropertyOnTarget(target, key, newDesc, realm, true);
            }
        }
        else
        {
            // seal: Step 6.a: For each element k of keys, do
            foreach (var key in keys)
            {
                TryDefinePropertyOnTarget(target, key,
                    new PropertyDescriptor { Configurable = false }, realm, true);
            }
        }

        return true;
    }

    /// <summary>
    /// Tests the integrity level of an object per ES spec TestIntegrityLevel(O, level).
    /// Works correctly with Proxy objects.
    /// </summary>
    internal static bool TestIntegrityLevel(IJsObjectLike target, bool frozen)
    {
        // Step 3: Let extensible be ? IsExtensible(O).
        // Step 4: If extensible is true, return false.
        if (IsTargetExtensible(target))
        {
            return false;
        }

        // Step 5: Let keys be ? O.[[OwnPropertyKeys]]().
        var keys = target.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true);

        // Step 6: For each element k of keys, do
        foreach (var key in keys)
        {
            var currentDesc = target.GetOwnPropertyDescriptor(key);
            if (currentDesc is null)
            {
                continue;
            }

            // Step 6.a: Let currentDesc be ? O.[[GetOwnProperty]](k).
            // Step 6.b: If currentDesc is not undefined, then
            // Step 6.b.i: If currentDesc.[[Configurable]] is true, return false.
            if (currentDesc.Configurable)
            {
                return false;
            }

            if (frozen)
            {
                // Step 6.b.ii: If level is frozen and IsDataDescriptor(currentDesc) is true,
                //              and currentDesc.[[Writable]] is true, return false.
                if (currentDesc.IsDataDescriptor && currentDesc.Writable)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Tries to call [[PreventExtensions]] on the target.
    /// For Proxy objects, invokes the preventExtensions trap.
    /// Returns true if successful, false if the operation failed.
    /// </summary>
    internal static bool TryPreventExtensions(IJsObjectLike target, RealmState realm)
    {
        if (target is JsProxy proxy)
        {
            return TryPreventExtensionsProxy(proxy, realm);
        }

        PreventExtensionsOnTarget(target);
        return true;
    }

    private static bool TryPreventExtensionsProxy(JsProxy proxy, RealmState realm)
    {
        var handler = proxy.Handler ?? throw ThrowTypeError("Cannot perform operation on a revoked Proxy", realm: realm);

        if (handler.TryGetProperty("preventExtensions", out var trapValue) &&
            !trapValue.IsUndefined && !trapValue.IsNull)
        {
            if (!trapValue.TryGetObject<IJsCallable>(out var trap))
            {
                throw ThrowTypeError("Proxy handler's 'preventExtensions' trap is not callable", realm: realm);
            }

            var targetJsValue = JsValue.FromObjectUnsafe(proxy.Target);
            var result = trap.Invoke(new SingleValueArgs(targetJsValue), JsValue.FromObjectUnsafe(handler));

            if (!JsOps.ToBoolean(result))
            {
                return false;
            }

            // Invariant: If the trap returns true, the target must actually be non-extensible
            if (proxy.Target is IExtensibilityControl { IsExtensible: true })
            {
                throw ThrowTypeError(
                    "proxy preventExtensions handler returned true but the target is extensible",
                    realm: realm);
            }

            return true;
        }

        // No trap - forward to the proxy target's own [[PreventExtensions]].
        return TryPreventExtensions(proxy.Target, realm);
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
