using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    private static PropertyDescriptor ToPropertyDescriptor(object? candidate, RealmState realm)
    {
        if (candidate is not JsObject descriptorObject)
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
            if (!ReferenceEquals(getterValue, Symbol.Undefined) && getterValue is not IJsCallable)
            {
                throw ThrowTypeError("Getter must be a function", realm: realm);
            }

            descriptor.Get = ReferenceEquals(getterValue, Symbol.Undefined)
                ? null
                : getterValue as IJsCallable;
        }

        if (descriptorObject.TryGetProperty("set", out var setterValue))
        {
            if (!ReferenceEquals(setterValue, Symbol.Undefined) && setterValue is not IJsCallable)
            {
                throw ThrowTypeError("Setter must be a function", realm: realm);
            }

            descriptor.Set = ReferenceEquals(setterValue, Symbol.Undefined)
                ? null
                : setterValue as IJsCallable;
        }

        if (descriptor is { IsAccessorDescriptor: true, IsDataDescriptor: true })
        {
            throw ThrowTypeError(
                "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute",
                realm: realm);
        }

        return descriptor;
    }

    private static JsObject? FromPropertyDescriptor(PropertyDescriptor? descriptor, RealmState realm)
    {
        if (descriptor is null)
        {
            return null;
        }

        var result = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            result.SetPrototype(realm.ObjectPrototype);
        }

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
            result.SetProperty("writable", descriptor.HasWritable ? descriptor.Writable : false);
        }

        result.SetProperty("enumerable", descriptor.HasEnumerable ? descriptor.Enumerable : false);
        result.SetProperty("configurable", descriptor.HasConfigurable ? descriptor.Configurable : false);
        return result;
    }

    private static bool TryDefinePropertyOnTarget(
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
                jsObject.SetProperty(propertyKey, descriptor.Value);
                return true;
            }

            if (throwOnFailure)
            {
                throw;
            }

            return false;
        }
    }

    private static void PreventExtensionsOnTarget(IJsObjectLike target)
    {
        if (target is IExtensibilityControl extensibilityControl)
        {
            extensibilityControl.PreventExtensions();
            return;
        }

        target.Seal();
    }

    private static bool IsTargetExtensible(IJsObjectLike target)
    {
        if (target is IExtensibilityControl extensibilityControl)
        {
            return extensibilityControl.IsExtensible;
        }

        return !target.IsSealed;
    }

    public static HostFunction CreateObjectConstructor(RealmState realm)
    {
        var objectConstructor = new HostFunction(ObjectConstructor);

        // Capture Object.prototype so Object.prototype methods can be attached
        // and used with call/apply patterns.
        if (objectConstructor.TryGetProperty("prototype", out var objectProto) &&
            objectProto is JsObject objectProtoObj)
        {
            realm.ObjectPrototype ??= objectProtoObj;

            realm.FunctionPrototype?.SetPrototype(objectProtoObj);

            realm.BooleanPrototype?.SetPrototype(objectProtoObj);

            realm.NumberPrototype?.SetPrototype(objectProtoObj);

            realm.StringPrototype?.SetPrototype(objectProtoObj);

            objectProtoObj.DefineProperty("constructor",
                new PropertyDescriptor
                {
                    Value = objectConstructor, Writable = true, Enumerable = false, Configurable = true
                });

            if (realm.ErrorPrototype is not null && realm.ErrorPrototype.Prototype is null)
            {
                realm.ErrorPrototype.SetPrototype(objectProtoObj);
            }

            // Object.prototype.toString
            objectProtoObj.SetHostedProperty("toString", ObjectPrototypeToString);

            var hasOwn = new HostFunction(ObjectPrototypeHasOwnProperty);

            objectProtoObj.SetProperty("hasOwnProperty", hasOwn);

            // Object.prototype.propertyIsEnumerable
            objectProtoObj.SetHostedProperty("propertyIsEnumerable", ObjectPrototypePropertyIsEnumerable);

            // Object.prototype.isPrototypeOf
            objectProtoObj.SetHostedProperty("isPrototypeOf", ObjectPrototypeIsPrototypeOf);

            // Also expose Object.hasOwnProperty so patterns like
            // Object.hasOwnProperty.call(obj, key) behave as expected.
            objectConstructor.SetProperty("hasOwnProperty", hasOwn);
        }

        objectConstructor.SetHostedProperty("defineProperties", ObjectDefineProperties);

        objectConstructor.SetHostedProperty("setPrototypeOf", ObjectSetPrototypeOf);

        objectConstructor.SetHostedProperty("preventExtensions", ObjectPreventExtensions);

        objectConstructor.SetHostedProperty("isExtensible", ObjectIsExtensible);

        objectConstructor.SetHostedProperty("getOwnPropertySymbols", ObjectGetOwnPropertySymbols);

        objectConstructor.SetHostedProperty("keys", ObjectKeys);

        objectConstructor.SetHostedProperty("values", ObjectValues);

        objectConstructor.SetHostedProperty("entries", ObjectEntries);

        objectConstructor.SetHostedProperty("assign", ObjectAssign);

        objectConstructor.SetHostedProperty("fromEntries", ObjectFromEntries);

        objectConstructor.SetHostedProperty("hasOwn", ObjectHasOwn);

        objectConstructor.SetHostedProperty("freeze", ObjectFreeze);

        objectConstructor.SetHostedProperty("seal", ObjectSeal);

        objectConstructor.SetHostedProperty("isFrozen", ObjectIsFrozen);

        objectConstructor.SetHostedProperty("isSealed", ObjectIsSealed);

        objectConstructor.SetHostedProperty("is", ObjectIs);

        objectConstructor.SetHostedProperty("create", ObjectCreate);

        objectConstructor.SetHostedProperty("getOwnPropertyNames", ObjectGetOwnPropertyNames);

        objectConstructor.SetHostedProperty("getOwnPropertyDescriptor", ObjectGetOwnPropertyDescriptor);
        objectConstructor.SetHostedProperty("getOwnPropertyDescriptors", ObjectGetOwnPropertyDescriptors);

        objectConstructor.SetHostedProperty("getPrototypeOf", ObjectGetPrototypeOf);

        objectConstructor.SetHostedProperty("defineProperty", ObjectDefineProperty);

        return objectConstructor;

        object? ObjectConstructor(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] == null || args[0] == Symbol.Undefined)
            {
                return CreateBlank();
            }

            if (args[0] is JsObject jsObj)
            {
                return jsObj;
            }

            var value = args[0];
            return value switch
            {
                JsBigInt bigInt => CreateBigIntWrapper(bigInt, realm: realm),
                bool b => CreateBooleanWrapper(b, realm: realm),
                string s => CreateStringWrapper(s, realm: realm),
                TypedAstSymbol sym => CreateSymbolWrapper(sym, realm: realm),
                double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte =>
                    CreateNumberWrapper(JsOps.ToNumber(value), realm: realm),
                _ => CreateBlank()
            };

            JsObject CreateBlank()
            {
                var obj = new JsObject();
                var proto = realm.ObjectPrototype;
                if (proto is not null)
                {
                    obj.SetPrototype(proto);
                }

                return obj;
            }
        }

        object? ObjectPrototypeToString(object? thisValue, IReadOnlyList<object?> _)
        {
            if (thisValue is JsObject obj)
            {
                var tagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
                if (obj.TryGetProperty(tagKey, out var tagValue) && !ReferenceEquals(tagValue, Symbol.Undefined))
                {
                    var tagString = JsOps.ToJsString(tagValue);
                    return $"[object {tagString}]";
                }
            }
            else if (thisValue is IJsPropertyAccessor accessor)
            {
                var tagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
                if (accessor.TryGetProperty(tagKey, out var tagValue) && !ReferenceEquals(tagValue, Symbol.Undefined))
                {
                    var tagString = JsOps.ToJsString(tagValue);
                    return $"[object {tagString}]";
                }
            }

            var tag = thisValue switch
            {
                null => "Null",
                JsObject => "Object",
                JsArray => "Array",
                string => "String",
                double => "Number",
                bool => "Boolean",
                IJsCallable => "Function",
                _ when ReferenceEquals(thisValue, Symbol.Undefined) => "Undefined",
                _ => "Object"
            };

            return $"[object {tag}]";
        }

        object? ObjectPrototypeHasOwnProperty(object? thisValue, IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return false;
            }

            var propertyName = JsOps.ToPropertyName(args[0]);
            if (propertyName is null)
            {
                return false;
            }

            switch (thisValue)
            {
                case JsObject obj:
                    return obj.GetOwnPropertyDescriptor(propertyName) is not null;
                case JsArray array:
                    return array.GetOwnPropertyDescriptor(propertyName) is not null;
                case IJsObjectLike accessor:
                    return accessor.GetOwnPropertyDescriptor(propertyName) is not null;
                default:
                    return false;
            }
        }

        object? ObjectPrototypePropertyIsEnumerable(object? thisValue, IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return false;
            }

            var propertyName = JsOps.ToPropertyName(args[0]);
            if (propertyName is null)
            {
                return false;
            }

            if (thisValue is not IJsObjectLike accessor)
            {
                return false;
            }

            var desc = accessor.GetOwnPropertyDescriptor(propertyName);
            return desc?.Enumerable == true;
        }

        object? ObjectPrototypeIsPrototypeOf(object? thisValue, IReadOnlyList<object?> args)
        {
            if (thisValue is null || ReferenceEquals(thisValue, Symbol.Undefined))
            {
                var error = realm.TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Object.prototype.isPrototypeOf called on null or undefined"], null)
                    : new InvalidOperationException(
                        "Object.prototype.isPrototypeOf called on null or undefined");
                throw new ThrowSignal(error);
            }

            if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
            {
                return false;
            }

            if (args[0] is not IJsObjectLike objectLike)
            {
                return false;
            }

            var cursor = objectLike;
            while (cursor.Prototype is JsObject proto)
            {
                if (ReferenceEquals(proto, thisValue))
                {
                    return true;
                }

                if (proto is not IJsObjectLike next)
                {
                    break;
                }

                cursor = next;
            }

            return false;
        }

        object? ObjectDefineProperties(object? _, IReadOnlyList<object?> args)
        {
            if (args.Count < 2)
            {
                throw ThrowTypeError("Object.defineProperties requires both target and descriptors", realm: realm);
            }

            if (!TryGetObject(args[0]!, realm, out var target))
            {
                throw ThrowTypeError("Object.defineProperties called on non-object", realm: realm);
            }

            if (args[1] is not JsObject props)
            {
                throw ThrowTypeError("Property description must be an object", realm: realm);
            }

            foreach (var key in props.GetOwnPropertyNames())
            {
                if (!props.TryGetProperty(key, out var descriptorValue))
                {
                    continue;
                }

                var descriptor = ToPropertyDescriptor(descriptorValue, realm);
                TryDefinePropertyOnTarget(target, key, descriptor, realm, true);
            }

            return target;
        }

        object? ObjectSetPrototypeOf(object? _, IReadOnlyList<object?> args)
        {
            if (args.Count < 2)
            {
                return args.Count > 0 ? args[0] : Symbol.Undefined;
            }

            var target = args[0];
            var protoValue = args[1];
            var proto = protoValue as JsObject;

            switch (target)
            {
                case ModuleNamespace when proto is null:
                    return target;
                case ModuleNamespace:
                    throw ThrowTypeError("Cannot set prototype on module namespace", realm: realm);
                case JsObject obj:
                    obj.SetPrototype(proto);
                    break;
                case JsArray array:
                    array.SetPrototype(proto);
                    break;
            }

            return target;
        }

        object? ObjectPreventExtensions(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw ThrowTypeError("Object.preventExtensions requires an object", realm: realm);
            }

            PreventExtensionsOnTarget(target);
            return target;
        }

        object? ObjectIsExtensible(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, realm, out var target))
            {
                return false;
            }

            return IsTargetExtensible(target);
        }

        object? ObjectGetOwnPropertySymbols(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return new JsArray();
            }

            if (!TryGetObject(args[0]!, realm, out var obj))
            {
                return new JsArray();
            }

            var symbols = new JsArray(realm);
            if (obj is ModuleNamespace moduleNamespace)
            {
                foreach (var key in moduleNamespace.OwnKeys())
                {
                    if (key is TypedAstSymbol symbol)
                    {
                        symbols.Push(symbol);
                    }
                }
            }

            AddArrayMethods(symbols, realm);
            return symbols;
        }

        object? ObjectKeys(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return new JsArray();
            }

            var obj = args[0] as IJsPropertyAccessor;
            if (obj is null && TryGetObject(args[0]!, realm, out var coerced))
            {
                obj = coerced;
            }

            if (obj is null)
            {
                return new JsArray();
            }

            var keys = new JsArray(realm);
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                var desc = obj.GetOwnPropertyDescriptor(key);
                if (desc is { Enumerable: true })
                {
                    keys.Push(key);
                }
            }

            AddArrayMethods(keys, realm);
            return keys;
        }

        object? ObjectValues(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return new JsArray();
            }

            var obj = args[0] as IJsPropertyAccessor;
            if (obj is null && TryGetObject(args[0]!, realm, out var coerced))
            {
                obj = coerced;
            }

            if (obj is null)
            {
                return new JsArray();
            }

            var values = new JsArray(realm);
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                if (obj.TryGetProperty(key, out var value))
                {
                    values.Push(value);
                }
            }

            AddArrayMethods(values, realm);
            return values;
        }

        object? ObjectEntries(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return new JsArray();
            }

            var obj = args[0] as IJsPropertyAccessor;
            if (obj is null && TryGetObject(args[0]!, realm, out var coerced))
            {
                obj = coerced;
            }

            if (obj is null)
            {
                return new JsArray();
            }

            var entries = new JsArray(realm);
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                if (!obj.TryGetProperty(key, out var value))
                {
                    continue;
                }

                var entry = new JsArray([key, value], realm);
                AddArrayMethods(entry, realm);
                entries.Push(entry);
            }

            AddArrayMethods(entries, realm);
            return entries;
        }

        object? ObjectAssign(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] is not IJsPropertyAccessor targetAccessor)
            {
                return args.Count > 0 ? args[0] : Symbol.Undefined;
            }

            for (var i = 1; i < args.Count; i++)
            {
                if (args[i] is not JsObject source)
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

            return args[0];
        }

        object? ObjectFromEntries(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] is not JsArray entries)
            {
                return new JsObject();
            }

            var result = new JsObject();
            foreach (var entry in entries.Items)
            {
                if (entry is not JsArray { Items.Count: >= 2 } entryArray)
                {
                    continue;
                }

                var key = entryArray.GetElement(0)?.ToString() ?? "";
                var value = entryArray.GetElement(1);
                result[key] = value;
            }

            return result;
        }

        object? ObjectHasOwn(IReadOnlyList<object?> args)
        {
            if (args.Count < 2 || args[0] is not JsObject obj)
            {
                return false;
            }

            var propName = args[1]?.ToString() ?? "";
            return obj.ContainsKey(propName);
        }

        object? ObjectFreeze(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return args.Count > 0 ? args[0] : null;
            }

            if (args[0] is ModuleNamespace)
            {
                throw ThrowTypeError("Cannot freeze module namespace", realm: realm);
            }

            if (args[0] is not JsObject obj)
            {
                return args[0];
            }

            obj.Freeze();
            return obj;
        }

        object? ObjectSeal(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return args.Count > 0 ? args[0] : null;
            }

            obj.Seal();
            return obj;
        }

        object? ObjectIsFrozen(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return true;
            }

            if (args[0] is ModuleNamespace)
            {
                return false;
            }

            if (args[0] is not JsObject obj)
            {
                return true;
            }

            return obj.IsFrozen;
        }

        object? ObjectIsSealed(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return true;
            }

            return obj.IsSealed;
        }

        // ECMA-262 §7.2.9 (SameValue) exposed as Object.is.
        object? ObjectIs(IReadOnlyList<object?> args)
        {
            var left = args.Count > 0 ? args[0] : Symbol.Undefined;
            var right = args.Count > 1 ? args[1] : Symbol.Undefined;

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

        object? ObjectCreate(IReadOnlyList<object?> args)
        {
            var obj = new JsObject();
            if (args.Count > 0 && args[0] != null)
            {
                obj.SetPrototype(args[0]);
            }

            if (args.Count <= 1 || args[1] is not JsObject propsObj)
            {
                return obj;
            }

            foreach (var propName in propsObj.GetOwnPropertyNames())
            {
                if (!propsObj.TryGetProperty(propName, out var descriptorValue))
                {
                    continue;
                }

                var descriptor = ToPropertyDescriptor(descriptorValue, realm);
                TryDefinePropertyOnTarget(obj, propName, descriptor, realm, true);
            }

            return obj;
        }

        object? ObjectGetOwnPropertyNames(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return new JsArray();
            }

            var obj = args[0] as IJsPropertyAccessor;
            if (obj is null && TryGetObject(args[0]!, realm, out var coerced))
            {
                obj = coerced;
            }

            if (obj is null)
            {
                return new JsArray();
            }

            var names = new JsArray(obj.GetOwnPropertyNames(), realm);

            AddArrayMethods(names, realm);
            return names;
        }

        object? ObjectGetOwnPropertyDescriptors(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, realm, out var obj))
            {
                throw ThrowTypeError("Object.getOwnPropertyDescriptors requires an object", realm: realm);
            }

            var descriptors = new JsObject();
            if (realm.ObjectPrototype is not null)
            {
                descriptors.SetPrototype(realm.ObjectPrototype);
            }

            foreach (var key in obj.GetOwnPropertyNames())
            {
                var descriptor = obj.GetOwnPropertyDescriptor(key);
                if (descriptor is null)
                {
                    continue;
                }

                descriptors.SetProperty(key, FromPropertyDescriptor(descriptor, realm) ?? new JsObject());
            }

            return descriptors;
        }

        object? ObjectGetOwnPropertyDescriptor(IReadOnlyList<object?> args)
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, realm, out var obj))
            {
                return Symbol.Undefined;
            }

            var propName = JsOps.GetRequiredPropertyName(args[1]);

            var desc = obj.GetOwnPropertyDescriptor(propName);
            if (desc is null)
            {
                return Symbol.Undefined;
            }

            var descriptorForResult = desc;
            if (string.Equals(propName, "name", StringComparison.Ordinal) && args[0] is IJsCallable)
            {
                descriptorForResult = desc.Clone();
                descriptorForResult.Configurable = true;
            }

            var result = FromPropertyDescriptor(descriptorForResult, realm);
            return result ?? (object)Symbol.Undefined;
        }

        object? ObjectGetPrototypeOf(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, realm, out var obj))
            {
                throw ThrowTypeError("Object.getPrototypeOf called on null or undefined", realm: realm);
            }

            if (obj is ModuleNamespace)
            {
                return null;
            }

            var proto = obj.Prototype ?? (object?)Symbol.Undefined;
            if (proto is not JsObject &&
                obj is HostFunction { Realm: JsObject fnRealm } &&
                fnRealm.TryGetProperty("Function", out var fnVal) &&
                fnVal is IJsPropertyAccessor fnAccessor &&
                fnAccessor.TryGetProperty("prototype", out var fnProtoObj) &&
                fnProtoObj is JsObject fnProto)
            {
                proto = fnProto;
            }

            return proto;
        }

        object? ObjectDefineProperty(IReadOnlyList<object?> args)
        {
            if (args.Count < 3)
            {
                throw ThrowTypeError("Object.defineProperty requires a property descriptor", realm: realm);
            }

            if (!TryGetObject(args[0]!, realm, out var obj))
            {
                throw ThrowTypeError("Object.defineProperty called on non-object", realm: realm);
            }

            var propName = JsOps.ToPropertyName(args[1]) ?? string.Empty;
            var descriptor = ToPropertyDescriptor(args[2], realm);

            TryDefinePropertyOnTarget(obj, propName, descriptor, realm, true);
            return obj;
        }
    }
}
