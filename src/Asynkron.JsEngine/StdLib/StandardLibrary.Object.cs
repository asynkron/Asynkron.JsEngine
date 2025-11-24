using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
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

        objectConstructor.SetHostedProperty("create", ObjectCreate);

        objectConstructor.SetHostedProperty("getOwnPropertyNames", ObjectGetOwnPropertyNames);

        objectConstructor.SetHostedProperty("getOwnPropertyDescriptor", ObjectGetOwnPropertyDescriptor);

        objectConstructor.SetHostedProperty("getPrototypeOf", ObjectGetPrototypeOf);

        objectConstructor.SetHostedProperty("defineProperty", ObjectDefineProperty);

        return objectConstructor;

        object? ObjectConstructor(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] == null || args[0] == Symbols.Undefined)
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
            var tag = thisValue switch
            {
                null => "Null",
                JsObject => "Object",
                JsArray => "Array",
                string => "String",
                double => "Number",
                bool => "Boolean",
                IJsCallable => "Function",
                _ when ReferenceEquals(thisValue, Symbols.Undefined) => "Undefined",
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
                    if (string.Equals(propertyName, "length", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (JsOps.TryResolveArrayIndex(propertyName, out var index))
                    {
                        return array.HasOwnIndex(index);
                    }

                    return false;
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
            if (thisValue is null || ReferenceEquals(thisValue, Symbols.Undefined))
            {
                var error = realm.TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Object.prototype.isPrototypeOf called on null or undefined"], null)
                    : new InvalidOperationException(
                        "Object.prototype.isPrototypeOf called on null or undefined");
                throw new ThrowSignal(error);
            }

            if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbols.Undefined))
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
                return args.Count > 0 ? args[0] : Symbols.Undefined;
            }

            var target = args[0];
            var propsValue = args[1];

            if (target is not IJsPropertyAccessor accessor || propsValue is not JsObject props)
            {
                return args[0];
            }

            foreach (var key in props.GetOwnPropertyNames())
            {
                if (!props.TryGetProperty(key, out var descriptorValue) || descriptorValue is not JsObject descObj)
                {
                    continue;
                }

                if (descObj.TryGetProperty("get", out var getterVal) && getterVal is IJsCallable getterFn)
                {
                    var builder = getterFn.Invoke([], target);
                    accessor.SetProperty(key, builder);
                    continue;
                }

                if (descObj.TryGetProperty("value", out var value))
                {
                    accessor.SetProperty(key, value);
                    continue;
                }

                accessor.SetProperty(key, Symbols.Undefined);
            }

            return args[0];
        }

        object? ObjectSetPrototypeOf(object? _, IReadOnlyList<object?> args)
        {
            if (args.Count < 2)
            {
                return args.Count > 0 ? args[0] : Symbols.Undefined;
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
            if (args.Count == 0 || args[0] is not IJsObjectLike target)
            {
                var error = realm.TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Object.preventExtensions requires an object"], null)
                    : new InvalidOperationException("Object.preventExtensions requires an object.");
                throw new ThrowSignal(error);
            }

            target.Seal();
            return target;
        }

        object? ObjectIsExtensible(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] is not IJsObjectLike target)
            {
                return false;
            }

            return !target.IsSealed;
        }

        object? ObjectGetOwnPropertySymbols(IReadOnlyList<object?> _)
        {
            return new JsArray();
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

            var keys = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                keys.Push(key);
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

            var values = new JsArray();
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

            var entries = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                if (!obj.TryGetProperty(key, out var value))
                {
                    continue;
                }

                var entry = new JsArray([key, value]);
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
                return args.Count > 0 ? args[0] : Symbols.Undefined;
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
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return args.Count > 0 ? args[0] : null;
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
            if (args.Count == 0 || args[0] is not JsObject obj)
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
                if (!propsObj.TryGetValue(propName, out var descriptorObj) || descriptorObj is not JsObject descObj)
                {
                    continue;
                }

                var descriptor = new PropertyDescriptor();

                var hasGet = descObj.TryGetValue("get", out var getVal);
                var hasSet = descObj.TryGetValue("set", out var setVal);

                if (hasGet || hasSet)
                {
                    if (hasGet && getVal is IJsCallable getter)
                    {
                        descriptor.Get = getter;
                    }

                    if (hasSet && setVal is IJsCallable setter)
                    {
                        descriptor.Set = setter;
                    }
                }
                else
                {
                    if (descObj.TryGetValue("value", out var value))
                    {
                        descriptor.Value = value;
                    }

                    if (descObj.TryGetValue("writable", out var writableVal))
                    {
                        descriptor.Writable = writableVal is bool b ? b : ToBoolean(writableVal);
                    }
                }

                if (descObj.TryGetValue("enumerable", out var enumerableVal))
                {
                    descriptor.Enumerable = enumerableVal is bool b ? b : ToBoolean(enumerableVal);
                }
                else
                {
                    descriptor.Enumerable = false;
                }

                if (descObj.TryGetValue("configurable", out var configurableVal))
                {
                    descriptor.Configurable = configurableVal is bool b ? b : ToBoolean(configurableVal);
                }
                else
                {
                    descriptor.Configurable = false;
                }

                obj.DefineProperty(propName, descriptor);
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

            var names = new JsArray(obj.GetOwnPropertyNames());

            AddArrayMethods(names, realm);
            return names;
        }

        object? ObjectGetOwnPropertyDescriptor(IReadOnlyList<object?> args)
        {
            if (args.Count < 2)
            {
                return Symbols.Undefined;
            }

            var obj = args[0] as IJsPropertyAccessor;
            if (obj is null && TryGetObject(args[0]!, realm, out var coerced))
            {
                obj = coerced;
            }

            if (obj is null)
            {
                return Symbols.Undefined;
            }

            var propName = JsOps.GetRequiredPropertyName(args[1]);

            var desc = obj.GetOwnPropertyDescriptor(propName);
            if (desc == null)
            {
                return Symbols.Undefined;
            }

            var resultDesc = new JsObject();
            if (desc.IsAccessorDescriptor)
            {
                if (desc.Get != null)
                {
                    resultDesc["get"] = desc.Get;
                }

                if (desc.Set != null)
                {
                    resultDesc["set"] = desc.Set;
                }
            }
            else
            {
                resultDesc["value"] = desc.Value;
                resultDesc["writable"] = desc.Writable;
            }

            resultDesc["enumerable"] = desc.Enumerable;
            resultDesc["configurable"] = desc.Configurable;

            return resultDesc;
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

            var proto = obj.Prototype ?? (object?)Symbols.Undefined;
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
            if (args.Count < 3 || !TryGetObject(args[0]!, realm, out var obj))
            {
                return args.Count > 0 ? args[0] : null;
            }

            var propName = JsOps.ToPropertyName(args[1]) ?? string.Empty;

            if (args[2] is not JsObject descriptorObj)
            {
                return args[0];
            }

            var descriptor = new PropertyDescriptor();

            var hasGet = descriptorObj.TryGetValue("get", out var getVal);
            var hasSet = descriptorObj.TryGetValue("set", out var setVal);

            if (hasGet || hasSet)
            {
                if (hasGet && getVal is IJsCallable getter)
                {
                    descriptor.Get = getter;
                }

                if (hasSet && setVal is IJsCallable setter)
                {
                    descriptor.Set = setter;
                }
            }
            else
            {
                if (descriptorObj.TryGetValue("value", out var value))
                {
                    descriptor.Value = value;
                }

                if (descriptorObj.TryGetValue("writable", out var writableVal))
                {
                    descriptor.Writable = writableVal is bool b ? b : ToBoolean(writableVal);
                }
            }

            if (descriptorObj.TryGetValue("enumerable", out var enumerableVal))
            {
                descriptor.Enumerable = enumerableVal is bool b ? b : ToBoolean(enumerableVal);
            }

            if (descriptorObj.TryGetValue("configurable", out var configurableVal))
            {
                descriptor.Configurable = configurableVal is bool b ? b : ToBoolean(configurableVal);
            }

            if (obj is JsArray jsArray && string.Equals(propName, "length", StringComparison.Ordinal))
            {
                jsArray.DefineLength(descriptor, null, true);
            }
            else
            {
                obj.DefineProperty(propName, descriptor);
            }

            return args[0];
        }
    }
}
