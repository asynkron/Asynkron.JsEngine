using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateReflectObject(RealmState realm)
    {
        var reflect = new JsObject();

        reflect.SetProperty("apply", new HostFunction(args =>
        {
            if (args.Count < 2 || args[0] is not IJsCallable callable)
            {
                throw new Exception("Reflect.apply: target must be callable.");
            }

            var thisArg = args[1];
            var argList = args.Count > 2 && args[2] is JsArray arr
                ? arr.Items.ToArray()
                : [];

            return callable.Invoke(argList, thisArg);
        }));

        reflect.SetProperty("construct", new HostFunction(args =>
        {
            if (args.Count < 2 || args[0] is not IJsCallable target)
            {
                throw new Exception("Reflect.construct: target must be a constructor.");
            }

            var argList = args[1] is JsArray arr ? arr.Items.ToArray() : [];
            var newTarget = args.Count > 2 && args[2] is IJsCallable ctor ? ctor : target;

            if (target is HostFunction hostTarget &&
                (!hostTarget.IsConstructor || hostTarget.DisallowConstruct))
            {
                var message = hostTarget.ConstructErrorMessage ?? "Target is not a constructor";
                var error = TypeErrorConstructor is IJsCallable typeErrorCtor
                    ? typeErrorCtor.Invoke([message], null)
                    : new InvalidOperationException(message);
                throw new ThrowSignal(error);
            }

            if (newTarget is HostFunction { IsConstructor: false } hostNewTarget)
            {
                var message = hostNewTarget.ConstructErrorMessage ?? "newTarget is not a constructor";
                var error = TypeErrorConstructor is IJsCallable typeErrorCtor2
                    ? typeErrorCtor2.Invoke([message], null)
                    : new InvalidOperationException(message);
                throw new ThrowSignal(error);
            }

            var proto = ResolveConstructPrototype(newTarget, target);

            // If we are constructing Array (or a subclass), create a real JsArray
            // so length/index semantics behave correctly, then invoke the
            // constructor with that receiver.
            if (ReferenceEquals(target, ArrayConstructor) || ReferenceEquals(newTarget, ArrayConstructor))
            {
                var arrayInstance = new JsArray();
                if (proto is not null)
                {
                    arrayInstance.SetPrototype(proto);
                }

                var result = target.Invoke(argList, arrayInstance);
                return result is JsObject jsObj ? jsObj : arrayInstance;
            }

            var instance = new JsObject();
            if (proto is not null)
            {
                instance.SetPrototype(proto);
            }

            var constructed = target.Invoke(argList, instance);
            return constructed is JsObject obj ? obj : instance;
        }));

        reflect.SetProperty("defineProperty", new HostFunction(args =>
        {
            if (args.Count < 3 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.defineProperty: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            if (args[2] is not JsObject descriptorObj)
            {
                throw new Exception("Reflect.defineProperty: descriptor must be an object.");
            }

            var descriptor = new PropertyDescriptor();
            if (descriptorObj.TryGetProperty("value", out var value))
            {
                descriptor.Value = value;
            }

            if (descriptorObj.TryGetProperty("writable", out var writable))
            {
                descriptor.Writable = writable is bool b ? b : ToBoolean(writable);
            }

            if (descriptorObj.TryGetProperty("enumerable", out var enumerable))
            {
                descriptor.Enumerable = enumerable is bool b ? b : ToBoolean(enumerable);
            }

            if (descriptorObj.TryGetProperty("configurable", out var configurable))
            {
                descriptor.Configurable = configurable is bool b ? b : ToBoolean(configurable);
            }

            if (descriptorObj.TryGetProperty("get", out var getter) && getter is IJsCallable getterFn)
            {
                descriptor.Get = getterFn;
            }

            if (descriptorObj.TryGetProperty("set", out var setter) && setter is IJsCallable setterFn)
            {
                descriptor.Set = setterFn;
            }

            if (target is JsArray jsArray && string.Equals(propertyKey, "length", StringComparison.Ordinal))
            {
                return jsArray.DefineLength(descriptor, null, false);
            }

            try
            {
                target.DefineProperty(propertyKey, descriptor);
                return true;
            }
            catch (ThrowSignal)
            {
                return false;
            }
        }));

        reflect.SetProperty("deleteProperty", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.deleteProperty: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            return target is JsObject jsObj && jsObj.Remove(propertyKey);
        }));

        reflect.SetProperty("get", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.get: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            return target.TryGetProperty(propertyKey, out var value) ? value : null;
        }));

        reflect.SetProperty("getOwnPropertyDescriptor", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.getOwnPropertyDescriptor: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            var descriptor = target.GetOwnPropertyDescriptor(propertyKey);
            if (descriptor is null)
            {
                return null;
            }

            var descObj = new JsObject
            {
                ["value"] = descriptor.Value,
                ["writable"] = descriptor.Writable,
                ["enumerable"] = descriptor.Enumerable,
                ["configurable"] = descriptor.Configurable
            };

            if (descriptor.Get is not null)
            {
                descObj["get"] = descriptor.Get;
            }

            if (descriptor.Set is not null)
            {
                descObj["set"] = descriptor.Set;
            }

            return descObj;
        }));

        reflect.SetProperty("getPrototypeOf", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.getPrototypeOf: target must be an object.");
            }

            return target.Prototype;
        }));

        reflect.SetProperty("has", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.has: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            return target.TryGetProperty(propertyKey, out _);
        }));

        reflect.SetProperty("isExtensible", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.isExtensible: target must be an object.");
            }

            return !target.IsSealed;
        }));

        reflect.SetProperty("ownKeys", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.ownKeys: target must be an object.");
            }

            var keys = target.Keys
                .Where(k => !k.StartsWith("__getter__", StringComparison.Ordinal) &&
                            !k.StartsWith("__setter__", StringComparison.Ordinal))
                .ToArray();
            return new JsArray(keys);
        }));

        reflect.SetProperty("preventExtensions", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.preventExtensions: target must be an object.");
            }

            target.Seal();
            return true;
        }));

        reflect.SetProperty("set", new HostFunction(args =>
        {
            if (args.Count < 3 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.set: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            var value = args[2];
            if (target is JsArray jsArray && string.Equals(propertyKey, "length", StringComparison.Ordinal))
            {
                return jsArray.SetLength(value, null, false);
            }

            try
            {
                target.SetProperty(propertyKey, value);
                return true;
            }
            catch (ThrowSignal)
            {
                return false;
            }
        }));

        reflect.SetProperty("setPrototypeOf", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, realm, out var target))
            {
                throw new Exception("Reflect.setPrototypeOf: target must be an object.");
            }

            var proto = args[1];
            target.SetPrototype(proto);
            return true;
        }));

        return reflect;
    }

    private static JsObject? ResolveConstructPrototype(IJsCallable newTarget, IJsCallable target)
    {
        // Step 1: use newTarget.prototype if it is an object
        if (newTarget is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("prototype", out var protoVal) &&
            protoVal is JsObject protoObj)
        {
            return protoObj;
        }

        // Step 2: try realm default for Array (handles cross-realm Array subclassing)
        if (ReferenceEquals(target, ArrayConstructor) || ReferenceEquals(newTarget, ArrayConstructor))
        {
            if (newTarget is HostFunction { RealmState.ArrayPrototype: JsObject realmArrayProtoFromState })
            {
                return realmArrayProtoFromState;
            }

            if (newTarget is HostFunction { Realm: JsObject realmObj } &&
                realmObj.TryGetProperty("Array", out var realmArrayCtor) &&
                TryGetPrototype(realmArrayCtor!, out var realmArrayProto))
            {
                return realmArrayProto;
            }

            if (ArrayPrototype is not null)
            {
                return ArrayPrototype;
            }
            // Fall through to other realm lookups if needed.
        }

        // Step 3: for other constructors, look for the intrinsic in the
        // newTarget's realm using the target's name.
        if (TryResolveRealmDefaultPrototype(newTarget, target, out var realmProto))
        {
            return realmProto;
        }

        // Step 4: fall back to target.prototype if available
        if (TryGetPrototype(target, out var targetProto))
        {
            return targetProto;
        }

        return null;
    }

    private static bool TryResolveRealmDefaultPrototype(object newTarget, IJsCallable target, out JsObject? prototype)
    {
        prototype = null;
        if (newTarget is not HostFunction hostFunction)
        {
            return false;
        }

        if (target is not IJsPropertyAccessor accessor ||
            !accessor.TryGetProperty("name", out var nameValue) ||
            nameValue is not string ctorName)
        {
            return false;
        }

        if (hostFunction.RealmState is RealmState realmState &&
            TryGetPrototypeFromRealmState(ctorName, realmState, out prototype))
        {
            return true;
        }

        if (hostFunction.RealmState is RealmState { ObjectPrototype: not null } realmDefaults)
        {
            prototype = realmDefaults.ObjectPrototype;
            return true;
        }

        if (hostFunction.Realm is JsObject realmObj &&
            realmObj.TryGetProperty(ctorName, out var realmCtor) &&
            realmCtor is not null &&
            TryGetPrototype(realmCtor, out var realmProto))
        {
            prototype = realmProto;
            return true;
        }

        if (hostFunction.Realm is JsObject fallbackRealm &&
            fallbackRealm.TryGetProperty("Object", out var objectCtor) &&
            objectCtor is not null &&
            TryGetPrototype(objectCtor, out var objectProto))
        {
            prototype = objectProto;
            return true;
        }

        return false;
    }

    private static bool TryGetPrototypeFromRealmState(string ctorName, RealmState realmState, out JsObject? prototype)
    {
        prototype = ctorName switch
        {
            "Array" => realmState.ArrayPrototype,
            "Date" => realmState.DatePrototype,
            _ => null
        };

        return prototype is not null;
    }

    private static bool TryGetPrototype(object candidate, out JsObject? prototype)
    {
        prototype = null;

        // Prefer an explicit "prototype" property when present (e.g. constructors
        // where [[Prototype]] is Function.prototype but the instance prototype
        // lives on the .prototype data property).
        if (candidate is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("prototype", out var protoProperty) &&
            protoProperty is JsObject protoObj)
        {
            prototype = protoObj;
            return true;
        }

        if (candidate is IJsObjectLike { Prototype: not null } objectLike)
        {
            prototype = objectLike.Prototype;
            return true;
        }

        if (candidate is JsObject { Prototype: not null } jsObject)
        {
            prototype = jsObject.Prototype;
            return true;
        }

        return false;
    }
}
