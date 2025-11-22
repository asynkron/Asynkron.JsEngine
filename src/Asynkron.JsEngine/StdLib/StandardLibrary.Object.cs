using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateObjectConstructor(RealmState realm)
    {
        // Object constructor function
        var objectConstructor = new HostFunction(args =>
        {
            // Object() or Object(value) - creates a new object or wraps the value
            if (args.Count == 0 || args[0] == null || args[0] == Symbols.Undefined)
            {
                return CreateBlank();
            }

            // If value is already an object, return it as-is
            if (args[0] is JsObject jsObj)
            {
                return jsObj;
            }

            var value = args[0];
            return value switch
            {
                JsBigInt bigInt => CreateBigIntWrapper(bigInt),
                bool b => CreateBooleanWrapper(b),
                string s => CreateStringWrapper(s),
                double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte =>
                    CreateNumberWrapper(JsOps.ToNumber(value)),
                _ => CreateBlank()
            };

            JsObject CreateBlank()
            {
                var obj = new JsObject();
                var proto = realm.ObjectPrototype ?? ObjectPrototype;
                if (proto is not null)
                {
                    obj.SetPrototype(proto);
                }

                return obj;
            }
        });

        // Capture Object.prototype so Object.prototype methods can be attached
        // and used with call/apply patterns.
        if (objectConstructor.TryGetProperty("prototype", out var objectProto) &&
            objectProto is JsObject objectProtoObj)
        {
            realm.ObjectPrototype ??= objectProtoObj;
            ObjectPrototype ??= objectProtoObj;

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
            var objectToString = new HostFunction((thisValue, _) =>
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
            });

            objectProtoObj.SetProperty("toString", objectToString);

            // Object.prototype.hasOwnProperty
            var hasOwn = new HostFunction((thisValue, args) =>
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
                        // Accessor descriptors are stored outside the value map; use descriptors.
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
            });

            objectProtoObj.SetProperty("hasOwnProperty", hasOwn);

            // Object.prototype.propertyIsEnumerable
            var propertyIsEnumerable = new HostFunction((thisValue, args) =>
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
            });

            objectProtoObj.SetProperty("propertyIsEnumerable", propertyIsEnumerable);

            // Object.prototype.isPrototypeOf
            var isPrototypeOf = new HostFunction((thisValue, args) =>
            {
                if (thisValue is null || ReferenceEquals(thisValue, Symbols.Undefined))
                {
                    var error = TypeErrorConstructor is IJsCallable ctor
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
            });

            objectProtoObj.SetProperty("isPrototypeOf", isPrototypeOf);

            // Also expose Object.hasOwnProperty so patterns like
            // Object.hasOwnProperty.call(obj, key) behave as expected.
            objectConstructor.SetProperty("hasOwnProperty", hasOwn);
        }

        objectConstructor.SetProperty("defineProperty", new HostFunction((_, args) =>
        {
            if (args.Count < 3)
            {
                return args.Count > 0 ? args[0] : Symbols.Undefined;
            }

            var target = args[0];
            var propertyKey = args[1];
            var descriptorValue = args[2];

            if (target is not IJsPropertyAccessor accessor)
            {
                return args[0];
            }

            var name = JsOps.ToPropertyName(propertyKey) ?? string.Empty;

            if (descriptorValue is JsObject descObj)
            {
                // If an accessor is provided, eagerly evaluate the getter once
                // and store the resulting value. This approximates accessor
                // behaviour for the patterns used in chalk/debug without
                // requiring full descriptor support on all host objects.
                if (descObj.TryGetProperty("get", out var getterVal) && getterVal is IJsCallable getterFn)
                {
                    var builder = getterFn.Invoke([], target);
                    accessor.SetProperty(name, builder);
                    return args[0];
                }

                if (descObj.TryGetProperty("value", out var value))
                {
                    accessor.SetProperty(name, value);
                    return args[0];
                }
            }

            accessor.SetProperty(name, Symbols.Undefined);
            return args[0];
        }));

        objectConstructor.SetProperty("defineProperties", new HostFunction((_, args) =>
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
        }));

        objectConstructor.SetProperty("setPrototypeOf", new HostFunction((_, args) =>
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
                case JsObject obj:
                    obj.SetPrototype(proto);
                    break;
                case JsArray array:
                    array.SetPrototype(proto);
                    break;
            }

            return target;
        }));

        objectConstructor.SetProperty("preventExtensions", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not IJsObjectLike target)
            {
                var error = TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Object.preventExtensions requires an object"], null)
                    : new InvalidOperationException("Object.preventExtensions requires an object.");
                throw new ThrowSignal(error);
            }

            target.Seal();
            return target;
        }));

        objectConstructor.SetProperty("isExtensible", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not IJsObjectLike target)
            {
                return false;
            }

            return !target.IsSealed;
        }));

        objectConstructor.SetProperty("getOwnPropertySymbols", new HostFunction(_ =>
        {
            // The engine currently uses internal string keys for symbol
            // properties on JsObject instances (\"@@symbol:...\"), and Babel
            // only uses getOwnPropertySymbols in cleanup paths (e.g., to
            // null-out metadata). Returning an empty array here avoids
            // observable behavior differences while keeping the API
            // available for callers.
            return new JsArray();
        }));

        // Object.keys(obj)
        objectConstructor.SetProperty("keys", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return new JsArray();
            }

            var keys = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                keys.Push(key);
            }

            AddArrayMethods(keys);
            return keys;
        }));

        // Object.values(obj)
        objectConstructor.SetProperty("values", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return new JsArray();
            }

            var values = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                if (obj.TryGetValue(key, out var value))
                {
                    values.Push(value);
                }
            }

            AddArrayMethods(values);
            return values;
        }));

        // Object.entries(obj)
        objectConstructor.SetProperty("entries", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return new JsArray();
            }

            var entries = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                if (!obj.TryGetValue(key, out var value))
                {
                    continue;
                }

                var entry = new JsArray([key, value]);
                AddArrayMethods(entry);
                entries.Push(entry);
            }

            AddArrayMethods(entries);
            return entries;
        }));

        // Object.assign(target, ...sources)
        objectConstructor.SetProperty("assign", new HostFunction(args =>
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
        }));

        // Object.fromEntries(entries)
        objectConstructor.SetProperty("fromEntries", new HostFunction(args =>
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
        }));

        // Object.hasOwn(obj, prop)
        objectConstructor.SetProperty("hasOwn", new HostFunction(args =>
        {
            if (args.Count < 2 || args[0] is not JsObject obj)
            {
                return false;
            }

            var propName = args[1]?.ToString() ?? "";
            return obj.ContainsKey(propName);
        }));

        // Object.freeze(obj)
        objectConstructor.SetProperty("freeze", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return args.Count > 0 ? args[0] : null;
            }

            obj.Freeze();
            return obj;
        }));

        // Object.seal(obj)
        objectConstructor.SetProperty("seal", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return args.Count > 0 ? args[0] : null;
            }

            obj.Seal();
            return obj;
        }));

        // Object.isFrozen(obj)
        objectConstructor.SetProperty("isFrozen", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return true; // Non-objects are considered frozen
            }

            return obj.IsFrozen;
        }));

        // Object.isSealed(obj)
        objectConstructor.SetProperty("isSealed", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return true; // Non-objects are considered sealed
            }

            return obj.IsSealed;
        }));

        // Object.create(proto, propertiesObject)
        objectConstructor.SetProperty("create", new HostFunction(args =>
        {
            var obj = new JsObject();
            if (args.Count > 0 && args[0] != null)
            {
                obj.SetPrototype(args[0]);
            }

            // Handle second parameter: property descriptors
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

                // Check if this is an accessor descriptor
                var hasGet = descObj.TryGetValue("get", out var getVal);
                var hasSet = descObj.TryGetValue("set", out var setVal);

                if (hasGet || hasSet)
                {
                    // Accessor descriptor
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
                    // Data descriptor
                    if (descObj.TryGetValue("value", out var value))
                    {
                        descriptor.Value = value;
                    }

                    if (descObj.TryGetValue("writable", out var writableVal))
                    {
                        descriptor.Writable = writableVal is bool b ? b : ToBoolean(writableVal);
                    }
                }

                // Common properties
                if (descObj.TryGetValue("enumerable", out var enumerableVal))
                {
                    descriptor.Enumerable = enumerableVal is bool b ? b : ToBoolean(enumerableVal);
                }
                else
                {
                    descriptor.Enumerable = false; // Default is false for Object.create
                }

                if (descObj.TryGetValue("configurable", out var configurableVal))
                {
                    descriptor.Configurable = configurableVal is bool b ? b : ToBoolean(configurableVal);
                }
                else
                {
                    descriptor.Configurable = false; // Default is false for Object.create
                }

                obj.DefineProperty(propName, descriptor);
            }

            return obj;
        }));

        // Object.getOwnPropertyNames(obj)
        objectConstructor.SetProperty("getOwnPropertyNames", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, out var obj))
            {
                return new JsArray();
            }

            var names = new JsArray(obj.GetOwnPropertyNames());

            AddArrayMethods(names);
            return names;
        }));

        // Object.getOwnPropertyDescriptor(obj, prop)
        objectConstructor.SetProperty("getOwnPropertyDescriptor", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, out var obj))
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
        }));

        // Object.getPrototypeOf(obj)
        objectConstructor.SetProperty("getPrototypeOf", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, out var obj))
            {
                throw ThrowTypeError("Object.getPrototypeOf called on null or undefined");
            }

            var proto = obj.Prototype ?? (object?)Symbols.Undefined;
            if (proto is not JsObject &&
                obj is HostFunction { Realm: JsObject realm } &&
                realm.TryGetProperty("Function", out var fnVal) &&
                fnVal is IJsPropertyAccessor fnAccessor &&
                fnAccessor.TryGetProperty("prototype", out var fnProtoObj) &&
                fnProtoObj is JsObject fnProto)
            {
                proto = fnProto;
            }

            return proto;
        }));

        // Object.defineProperty(obj, prop, descriptor)
        objectConstructor.SetProperty("defineProperty", new HostFunction(args =>
        {
            if (args.Count < 3 || !TryGetObject(args[0]!, out var obj))
            {
                return args.Count > 0 ? args[0] : null;
            }

            var propName = JsOps.ToPropertyName(args[1]) ?? string.Empty;

            if (args[2] is not JsObject descriptorObj)
            {
                return args[0];
            }

            var descriptor = new PropertyDescriptor();

            // Check if this is an accessor descriptor
            var hasGet = descriptorObj.TryGetValue("get", out var getVal);
            var hasSet = descriptorObj.TryGetValue("set", out var setVal);

            if (hasGet || hasSet)
            {
                // Accessor descriptor
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
                // Data descriptor
                if (descriptorObj.TryGetValue("value", out var value))
                {
                    descriptor.Value = value;
                }

                if (descriptorObj.TryGetValue("writable", out var writableVal))
                {
                    descriptor.Writable = writableVal is bool b ? b : ToBoolean(writableVal);
                }
            }

            // Common properties
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
        }));

        return objectConstructor;
    }
}
