using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

public static class ObjectHelper
{
    public static HostFunction CreateObjectConstructor(RealmState realm)
    {
        return ObjectConstructor.CreateConstructor(realm);
    }

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
            descriptor.Value = valueValue;
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
                : getterValue.TryGetObject<IJsCallable>(out var getter) ? getter : null;
        }

        if (descriptorObject.TryGetProperty("set", out var setterValue))
        {
            if (!setterValue.IsUndefined && !setterValue.TryGetObject<IJsCallable>(out _))
            {
                throw ThrowTypeError("Setter must be a function", realm: realm);
            }

            descriptor.Set = setterValue.IsUndefined
                ? null
                : setterValue.TryGetObject<IJsCallable>(out var setter) ? setter : null;
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
                descriptor is { HasGet: true, Get: not null } ? JsValue.FromObjectUnsafe(descriptor.Get) : JsValue.Undefined);
            result.SetProperty("set",
                descriptor is { HasSet: true, Set: not null } ? JsValue.FromObjectUnsafe(descriptor.Set) : JsValue.Undefined);
        }
        else
        {
            JsValue valJs;
            if (descriptor.HasValue)
            {
                // Handle case where Value is already a boxed JsValue
                valJs = descriptor.Value is JsValue v ? v : JsValue.FromObjectUnsafe(descriptor.Value);
            }
            else
            {
                valJs = JsValue.Undefined;
            }
            result.SetProperty("value", valJs);
            result.SetProperty("writable", new JsValue(descriptor.HasWritable ? descriptor.Writable : false));
        }

        result.SetProperty("enumerable", new JsValue(descriptor.HasEnumerable ? descriptor.Enumerable : false));
        result.SetProperty("configurable", new JsValue(descriptor.HasConfigurable ? descriptor.Configurable : false));
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
                jsObject.GetOwnPropertyDescriptor(propertyKey) is { } current &&
                !current.Configurable &&
                descriptor.IsDataDescriptor &&
                descriptor.HasValue &&
                (!descriptor.HasConfigurable || descriptor.Configurable == current.Configurable) &&
                (!descriptor.HasEnumerable || descriptor.Enumerable == current.Enumerable) &&
                (!descriptor.HasWritable || descriptor.Writable == current.Writable))
            {
                jsObject.SetProperty(propertyKey, JsValue.FromObjectUnsafe(descriptor.Value));
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

    internal static object? ObjectDefineProperties(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 2)
        {
            throw ThrowTypeError("Object.defineProperties requires both target and descriptors", realm: realmState);
        }

        if (!TryGetObject(args[0].ToObject(), realmState, out var target))
        {
            throw ThrowTypeError("Object.defineProperties called on non-object", realm: realmState);
        }

        if (!args[1].TryGetObject(out var props))
        {
            throw ThrowTypeError("Property description must be an object", realm: realmState);
        }

        foreach (var key in props.GetOwnPropertyNames())
        {
            if (!props.TryGetProperty(key, out var descriptorValue))
            {
                continue;
            }

            var descriptor = ToPropertyDescriptor(descriptorValue, realmState);
            TryDefinePropertyOnTarget(target, key, descriptor, realmState, true);
        }

        return target;
    }

    internal static object? ObjectSetPrototypeOf(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 2)
        {
            return args.GetArgument(0).ToObject();
        }

        var targetValue = args[0];
        var protoValue = args[1].ToObject();

        var target = targetValue.ToObject();
        switch (target)
        {
            case ModuleNamespace when protoValue is null:
                return target;
            case ModuleNamespace:
                throw ThrowTypeError("Cannot set prototype on module namespace", realm: realmState);
            case JsArray array:
                array.SetPrototype(protoValue);
                break;
            case JsObject obj:
                obj.SetPrototype(protoValue);
                break;
            case IJsObjectLike objectLike:
                objectLike.SetPrototype(protoValue);
                break;
        }

        return target;
    }

    internal static object? ObjectPreventExtensions(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !TryGetObject(args[0].ToObject(), realmState, out var target))
        {
            throw ThrowTypeError("Object.preventExtensions requires an object", realm: realmState);
        }

        PreventExtensionsOnTarget(target);
        return target;
    }

    internal static object? ObjectIsExtensible(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !TryGetObject(args[0].ToObject(), realmState, out var target))
        {
            return false;
        }

        return IsTargetExtensible(target);
    }

    internal static object? ObjectGetOwnPropertySymbols(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return new JsArray(realmState);
        }

        if (!TryGetObject(args[0].ToObject(), realmState, out var obj))
        {
            return new JsArray(realmState);
        }

        var symbols = new JsArray(realmState);
        if (obj is ModuleNamespace moduleNamespace)
        {
            foreach (var key in moduleNamespace.OwnKeys())
            {
                if (key is TypedAstSymbol symbol)
                {
                    symbols.Push(symbol);
                }
            }

            return symbols;
        }

        foreach (var key in obj.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
        {
            if (TypedAstSymbol.TryGetByInternalKey(key, out var symbol))
            {
                symbols.Push(symbol);
            }
        }

        return symbols;
    }

    internal static object? ObjectKeys(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return new JsArray(realmState);
        }

        var obj = args[0].ToObject() as IJsPropertyAccessor;
        if (obj is null && TryGetObject(args[0].ToObject(), realmState, out var coerced))
        {
            obj = coerced;
        }

        if (obj is null)
        {
            return new JsArray(realmState);
        }

        var keys = new JsArray(realmState);
        foreach (var key in obj.GetEnumerablePropertyNames())
        {
            var desc = obj.GetOwnPropertyDescriptor(key);
            if (desc is { Enumerable: true })
            {
                keys.Push(key);
            }
        }

        return keys;
    }

    internal static object? ObjectValues(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return new JsArray(realmState);
        }

        var obj = args[0].ToObject() as IJsPropertyAccessor;
        if (obj is null && TryGetObject(args[0].ToObject(), realmState, out var coerced))
        {
            obj = coerced;
        }

        if (obj is null)
        {
            return new JsArray(realmState);
        }

        var values = new JsArray(realmState);
        foreach (var key in obj.GetEnumerablePropertyNames())
        {
            if (obj.TryGetProperty(key, out var value))
            {
                values.Push(value);
            }
        }

        return values;
    }

    internal static object? ObjectEntries(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return new JsArray(realmState);
        }

        var obj = args[0].ToObject() as IJsPropertyAccessor;
        if (obj is null && TryGetObject(args[0].ToObject(), realmState, out var coerced))
        {
            obj = coerced;
        }

        if (obj is null)
        {
            return new JsArray(realmState);
        }

        var entries = new JsArray(realmState);
        foreach (var key in obj.GetEnumerablePropertyNames())
        {
            if (!obj.TryGetProperty(key, out var value))
            {
                continue;
            }

            var entry = new JsArray([key, value], realmState);
            entries.Push(entry);
        }

        return entries;
    }

    internal static object? ObjectAssign(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !args[0].TryGetObject<IJsPropertyAccessor>(out var targetAccessor))
        {
            return args.GetArgument(0).ToObject();
        }

        for (var i = 1; i < args.Count; i++)
        {
            if (!args[i].TryGetObject(out var source))
            {
                continue;
            }

            foreach (var key in source.GetOwnPropertyNames())
            {
                if (source.TryGetProperty(key, out var value))
                {
                    targetAccessor.SetProperty(key, value);
                }
            }
        }

        return args[0].ToObject();
    }

    internal static object? ObjectFromEntries(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !args[0].TryGetObject(out JsArray? entries))
        {
            return new JsObject(realmState.ObjectPrototype) { RealmState = realmState };
        }

        var result = new JsObject(realmState.ObjectPrototype) { RealmState = realmState };
        foreach (var entry in entries.Items)
        {
            if (!entry.TryGetObject<JsArray>(out var entryArray) || entryArray.Items.Count < 2)
            {
                continue;
            }

            var keyValue = entryArray.GetElement(0);
            var key = keyValue.ToObject()?.ToString() ?? "";
            var value = entryArray.GetElement(1);
            result[key] = value;
        }

        return result;
    }

    internal static object? ObjectHasOwn(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 2)
        {
            return false;
        }

        var propName = JsOps.ToPropertyName(args[1].ToObject());
        if (propName is null)
        {
            return false;
        }

        var target = args[0].ToObject();
        return target switch
        {
            JsObject obj => obj.GetOwnPropertyDescriptor(propName) is not null,
            JsArray array => array.GetOwnPropertyDescriptor(propName) is not null,
            IJsObjectLike accessor => accessor.GetOwnPropertyDescriptor(propName) is not null,
            _ => false
        };
    }

    internal static object? ObjectFreeze(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return null;
        }

        var target = args[0].ToObject();
        if (target is ModuleNamespace)
        {
            throw ThrowTypeError("Cannot freeze module namespace", realm: realmState);
        }

        if (target is TypedArrayBase typedArray && typedArray.Buffer.Resizable)
        {
            throw ThrowTypeError("Cannot freeze a typed array backed by a resizable ArrayBuffer", realm: realmState);
        }

        if (target is not JsObject obj)
        {
            return target;
        }

        obj.Freeze();
        return obj;
    }

    internal static object? ObjectSeal(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return null;
        }

        var target = args[0].ToObject();
        if (target is not JsObject obj)
        {
            return target;
        }

        obj.Seal();
        return obj;
    }

    internal static object? ObjectIsFrozen(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return true;
        }

        var target = args[0].ToObject();
        if (target is ModuleNamespace)
        {
            return false;
        }

        if (target is not JsObject obj)
        {
            return true;
        }

        return obj.IsFrozen;
    }

    internal static object? ObjectIsSealed(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return true;
        }

        var target = args[0].ToObject();
        if (target is not JsObject obj)
        {
            return true;
        }

        return obj.IsSealed;
    }

    internal static object? ObjectIs(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        _ = realm;
        var left = args.GetArgument(0).ToObject();
        var right = args.GetArgument(1).ToObject();

        if (left is double ld && right is double rd)
        {
            if (double.IsNaN(ld) && double.IsNaN(rd))
            {
                return true;
            }

            if (ld == 0.0 && rd == 0.0)
            {
                return BitConverter.DoubleToInt64Bits(ld) == BitConverter.DoubleToInt64Bits(rd);
            }

            return ld.Equals(rd);
        }

        if (left is float lf && right is float rf)
        {
            if (float.IsNaN(lf) && float.IsNaN(rf))
            {
                return true;
            }

            if (lf == 0f && rf == 0f)
            {
                return BitConverter.SingleToInt32Bits(lf) == BitConverter.SingleToInt32Bits(rf);
            }

            return lf.Equals(rf);
        }

        if (left is JsBigInt lbi && right is JsBigInt rbi)
        {
            return lbi == rbi;
        }

        return JsOps.StrictEquals(left, right);
    }

    internal static object? ObjectCreate(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var obj = new JsObject { RealmState = realmState };
        if (args.Count > 0 && !args[0].IsNull)
        {
            obj.SetPrototype(args[0].ToObject());
        }

        if (args.Count <= 1 || !args[1].TryGetObject(out var propsObj))
        {
            return obj;
        }

        foreach (var propName in propsObj.GetOwnPropertyNames())
        {
            if (!propsObj.TryGetProperty(propName, out var descriptorValue))
            {
                continue;
            }

            // descriptorValue is already a JsValue from TryGetProperty
            var descriptor = ToPropertyDescriptor(descriptorValue, realmState);
            TryDefinePropertyOnTarget(obj, propName, descriptor, realmState, true);
        }

        return obj;
    }

    internal static object? ObjectGetOwnPropertyNames(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return new JsArray(realmState);
        }

        var obj = args[0].ToObject() as IJsPropertyAccessor;
        if (obj is null && TryGetObject(args[0].ToObject(), realmState, out var coerced))
        {
            obj = coerced;
        }

        if (obj is null)
        {
            return new JsArray(realmState);
        }

        var names = new JsArray(obj.GetOwnPropertyNames(), realmState);
        return names;
    }

    internal static object? ObjectGetOwnPropertyDescriptors(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !TryGetObject(args[0].ToObject(), realmState, out var obj))
        {
            throw ThrowTypeError("Object.getOwnPropertyDescriptors requires an object", realm: realmState);
        }

        var descriptors = new JsObject(realmState.ObjectPrototype) { RealmState = realmState };

        foreach (var key in obj.GetOwnPropertyNames())
        {
            var descriptor = obj.GetOwnPropertyDescriptor(key);
            if (descriptor is null)
            {
                continue;
            }

            descriptors.SetProperty(key, (JsValue)(FromPropertyDescriptor(descriptor, realmState) ?? new JsObject()));
        }

        return descriptors;
    }

    internal static object? ObjectGetOwnPropertyDescriptor(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 2 || !TryGetObject(args[0].ToObject(), realmState, out var obj))
        {
            return JsValue.Undefined.ToObject();
        }

        var propName = JsOps.GetRequiredPropertyName(args[1].ToObject());

        var desc = obj.GetOwnPropertyDescriptor(propName);
        if (desc is null)
        {
            return JsValue.Undefined.ToObject();
        }

        var descriptorForResult = desc;
        var target = args[0].ToObject();
        if (string.Equals(propName, "name", StringComparison.Ordinal) && target is IJsCallable)
        {
            descriptorForResult = desc.Clone();
            descriptorForResult.Configurable = true;
        }

        var result = FromPropertyDescriptor(descriptorForResult, realmState);
        return result ?? JsValue.Undefined.ToObject();
    }

    internal static object? ObjectGetPrototypeOf(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !TryGetObject(args[0].ToObject(), realmState, out var obj))
        {
            throw ThrowTypeError("Object.getPrototypeOf called on null or undefined", realm: realmState);
        }

        if (obj is ModuleNamespace)
        {
            return null;
        }

        if (obj is JsProxy proxy)
        {
            return proxy.GetPrototypeWithTrap();
        }

        object? proto = obj.Prototype;
        if (proto is null && obj is IPrototypeAccessorProvider provider)
        {
            proto = provider.PrototypeAccessor;
        }

        if (proto is not IJsPropertyAccessor &&
            obj is HostFunction { Realm: { } fnRealm } &&
            fnRealm.TryGetProperty("Function", out var fnVal) &&
            fnVal.TryGetObject<IJsPropertyAccessor>(out var fnAccessor) &&
            fnAccessor.TryGetProperty("prototype", out var fnProtoObj) &&
            fnProtoObj.TryGetObject<JsObject>(out var fnProto))
        {
            proto = fnProto;
        }

        return proto;
    }

    internal static object? ObjectDefineProperty(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 3)
        {
            throw ThrowTypeError("Object.defineProperty requires a property descriptor", realm: realmState);
        }

        if (!TryGetObject(args[0].ToObject(), realmState, out var obj))
        {
            throw ThrowTypeError("Object.defineProperty called on non-object", realm: realmState);
        }

        var propName = JsOps.ToPropertyName(args[1].ToObject()) ?? string.Empty;
        var descriptor = ToPropertyDescriptor(args[2], realmState);

        TryDefinePropertyOnTarget(obj, propName, descriptor, realmState, true);
        return obj;
    }
}
