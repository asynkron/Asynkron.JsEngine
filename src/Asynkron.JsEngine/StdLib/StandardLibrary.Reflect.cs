using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static object? ReflectApply(object? _, IReadOnlyList<object?> args, RealmState? realm)
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
    }

    internal static object? ReflectConstruct(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.construct.");
        }

        if (args.Count < 2 || args[0] is not IJsCallable target)
        {
            throw new Exception("Reflect.construct: target must be a constructor.");
        }

        var argList = args[1] is JsArray arr ? arr.Items.ToArray() : [];
        IJsCallable newTarget;
        if (args.Count > 2)
        {
            if (args[2] is not IJsCallable ctor)
            {
                var message = "newTarget is not a constructor";
                var error = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                    ? typeErrorCtor.Invoke([message], null)
                    : new InvalidOperationException(message);
                throw new ThrowSignal(error);
            }

            newTarget = ctor;
        }
        else
        {
            newTarget = target;
        }

        return Construct(target, argList, newTarget, realm);
    }

    internal static object? Construct(IJsCallable target, IReadOnlyList<object?> argList, IJsCallable newTarget,
        RealmState realm)
    {
        if (target is HostFunction hostTarget &&
            (!hostTarget.IsConstructor || hostTarget.DisallowConstruct))
        {
            var message = hostTarget.ConstructErrorMessage ?? "Target is not a constructor";
            var error = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                ? typeErrorCtor.Invoke([message], null)
                : new InvalidOperationException(message);
            throw new ThrowSignal(error);
        }

        if (newTarget is HostFunction { IsConstructor: false } hostNewTarget)
        {
            var message = hostNewTarget.ConstructErrorMessage ?? "newTarget is not a constructor";
            var error = realm.TypeErrorConstructor is IJsCallable typeErrorCtor2
                ? typeErrorCtor2.Invoke([message], null)
                : new InvalidOperationException(message);
            throw new ThrowSignal(error);
        }

        var proto = ResolveConstructPrototype(newTarget, target, realm);

        if ((realm.ArrayConstructor is not null && ReferenceEquals(target, realm.ArrayConstructor)) ||
            (realm.ArrayConstructor is not null && ReferenceEquals(newTarget, realm.ArrayConstructor)))
        {
            var arrayInstance = new JsArray(realm);
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

        object? constructed;
        instance.BeginConstruction();
        try
        {
            var invokeWithContext = target.GetType().GetMethod(
                "InvokeWithContext",
                [typeof(IReadOnlyList<object?>), typeof(object), typeof(EvaluationContext), typeof(object)]);
            if (invokeWithContext is not null)
            {
                var constructContext = realm.CreateContext(pushScope: false);
                constructed = invokeWithContext.Invoke(target, [argList, instance, constructContext, newTarget]);
            }
            else
            {
                constructed = target.Invoke(argList, instance);
            }
        }
        finally
        {
            instance.EndConstruction();
        }

        return constructed is JsObject obj ? obj : instance;
    }

    internal static object? ReflectDefineProperty(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.defineProperty.");
        }

        if (args.Count < 3 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.defineProperty: target must be an object.");
        }

        var propertyKey = JsOps.ToPropertyName(args[1]) ?? string.Empty;
        var descriptor = ToPropertyDescriptor(args[2], realm);

        return TryDefinePropertyOnTarget(target, propertyKey, descriptor, realm, false);
    }

    internal static object? ReflectDeleteProperty(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.deleteProperty.");
        }

        if (args.Count < 2 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.deleteProperty: target must be an object.");
        }

        var propertyKey = JsOps.ToPropertyName(args[1]) ?? string.Empty;
        if (target is ModuleNamespace moduleNamespace)
        {
            return moduleNamespace.Delete(propertyKey);
        }

        if (target is JsArray jsArray)
        {
            if (JsOps.TryResolveArrayIndex(propertyKey, out var index))
            {
                return jsArray.DeleteElement(index);
            }

            return jsArray.DeleteProperty(propertyKey);
        }

        return target is JsObject jsObj && jsObj.Remove(propertyKey);
    }

    internal static object? ReflectGet(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.get.");
        }

        if (args.Count < 2 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.get: target must be an object.");
        }

        var receiver = args.Count > 2 ? args[2] : target;
        var propertyKey = JsOps.ToPropertyName(args[1]) ?? string.Empty;
        return target.TryGetProperty(propertyKey, receiver, out var value) ? value : null;
    }

    internal static object? ReflectGetOwnPropertyDescriptor(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.getOwnPropertyDescriptor.");
        }

        if (args.Count < 2 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.getOwnPropertyDescriptor: target must be an object.");
        }

        var propertyKey = JsOps.ToPropertyName(args[1]) ?? string.Empty;
        var descriptor = target.GetOwnPropertyDescriptor(propertyKey);
        var result = FromPropertyDescriptor(descriptor, realm);
        return result ?? (object)Symbol.Undefined;
    }

    internal static object? ReflectGetPrototypeOf(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.getPrototypeOf.");
        }

        if (args.Count == 0 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.getPrototypeOf: target must be an object.");
        }

        if (target is ModuleNamespace)
        {
            return null;
        }

        return target.Prototype;
    }

    internal static object? ReflectHas(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.has.");
        }

        if (args.Count < 2 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.has: target must be an object.");
        }

        var propertyKey = JsOps.ToPropertyName(args[1]) ?? string.Empty;
        if (target is ModuleNamespace moduleNamespace)
        {
            // Use HasProperty which triggers evaluation for deferred namespaces per ES spec
            return moduleNamespace.HasProperty(propertyKey);
        }

        return target.TryGetProperty(propertyKey, out var _);
    }

    internal static object? ReflectIsExtensible(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.isExtensible.");
        }

        if (args.Count == 0 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.isExtensible: target must be an object.");
        }

        return IsTargetExtensible(target);
    }

    internal static object? ReflectOwnKeys(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.ownKeys.");
        }

        if (args.Count == 0 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.ownKeys: target must be an object.");
        }

        if (target is ModuleNamespace moduleNamespace)
        {
            return new JsArray(moduleNamespace.OwnKeys(), realm);
        }

        if (target is IJsPropertyAccessor accessor)
        {
            var ordered = new JsArray(realm);
            foreach (var key in accessor.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
            {
                if (key.StartsWith("__getter__", StringComparison.Ordinal) ||
                    key.StartsWith("__setter__", StringComparison.Ordinal) ||
                    string.Equals(key, "__proto__", StringComparison.Ordinal))
                {
                    continue;
                }

                ordered.Push(key);
            }

            return ordered;
        }

        var keys = target.Keys
            .Where(k => !k.StartsWith("__getter__", StringComparison.Ordinal) &&
                        !k.StartsWith("__setter__", StringComparison.Ordinal) &&
                        !string.Equals(k, "__proto__", StringComparison.Ordinal))
            .ToArray();
        return new JsArray(keys, realm);
    }

    internal static object? ReflectPreventExtensions(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.preventExtensions.");
        }

        if (args.Count == 0 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.preventExtensions: target must be an object.");
        }

        PreventExtensionsOnTarget(target);
        return true;
    }

    internal static object? ReflectSet(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.set.");
        }

        if (args.Count < 2 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.set: target must be an object.");
        }

        var propertyKey = JsOps.ToPropertyName(args[1]) ?? string.Empty;
        var value = args.GetArgument(2);
        var receiver = args.Count > 3 ? args[3] : target;
        if (target is ModuleNamespace moduleNamespace)
        {
            try
            {
                moduleNamespace.SetProperty(propertyKey, value, receiver);
            }
            catch (ThrowSignal)
            {
                return false;
            }

            return false;
        }

        if (target is JsArray jsArray && string.Equals(propertyKey, "length", StringComparison.Ordinal))
        {
            return jsArray.SetLength(value, null, false);
        }

        target.SetProperty(propertyKey, value, receiver);
        return true;
    }

    internal static object? ReflectSetPrototypeOf(object? _, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.setPrototypeOf.");
        }

        if (args.Count < 2 || !TryGetObject(args[0], realm, out var target))
        {
            throw new Exception("Reflect.setPrototypeOf: target must be an object.");
        }

        var proto = args[1];
        try
        {
            target.SetPrototype(proto);
            return true;
        }
        catch (ThrowSignal)
        {
            return false;
        }
    }

    internal static IJsObjectLike? ResolveConstructPrototype(IJsCallable newTarget, IJsCallable target,
        RealmState realmState)
    {
        // Step 1: use newTarget.prototype if it is an object
        if (newTarget is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("prototype", out var protoVal) &&
            protoVal is IJsObjectLike protoObj)
        {
            return protoObj;
        }

        TryGetRealmInfo(newTarget, out var newTargetRealmState, out var newTargetRealmObject);

        // Step 2: try realm default for Array (handles cross-realm Array subclassing)
        if ((realmState.ArrayConstructor is not null && ReferenceEquals(target, realmState.ArrayConstructor)) ||
            (realmState.ArrayConstructor is not null && ReferenceEquals(newTarget, realmState.ArrayConstructor)))
        {
            if (newTargetRealmState?.ArrayPrototype is IJsObjectLike realmArrayProtoFromState)
            {
                return realmArrayProtoFromState;
            }

            if (newTargetRealmObject is JsObject &&
                newTargetRealmObject.TryGetProperty("Array", out var realmArrayCtor) &&
                TryGetPrototype(realmArrayCtor!, out var realmArrayProto))
            {
                return realmArrayProto;
            }

            if (realmState.ArrayPrototype is not null)
            {
                return realmState.ArrayPrototype;
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

    private static bool TryResolveRealmDefaultPrototype(object newTarget, IJsCallable target, out IJsObjectLike? prototype)
    {
        prototype = null;
        if (!TryGetRealmInfo(newTarget, out var realmState, out var realmObject))
        {
            return false;
        }

        if (target is not IJsPropertyAccessor accessor ||
            !accessor.TryGetProperty("name", out var nameValue) ||
            nameValue is not string ctorName)
        {
            return false;
        }

        if (realmState is not null &&
            TryGetPrototypeFromRealmState(ctorName, realmState, out prototype))
        {
            return true;
        }

        if (TryGetIntlPrototype(ctorName, realmState, realmObject, out prototype))
        {
            return true;
        }

        if (realmState is { ObjectPrototype: not null })
        {
            prototype = realmState.ObjectPrototype;
            return true;
        }

        if (realmObject is not null &&
            realmObject.TryGetProperty(ctorName, out var realmCtor) &&
            realmCtor is not null &&
            TryGetPrototype(realmCtor, out var realmProto))
        {
            prototype = realmProto;
            return true;
        }

        if (realmObject is not null &&
            realmObject.TryGetProperty("Object", out var objectCtor) &&
            objectCtor is not null &&
            TryGetPrototype(objectCtor, out var objectProto))
        {
            prototype = objectProto;
            return true;
        }

        return false;
    }

    private static bool TryGetPrototypeFromRealmState(string ctorName, RealmState realmState, out IJsObjectLike? prototype)
    {
        prototype = ctorName switch
        {
            "Array" => realmState.ArrayPrototype,
            "ArrayBuffer" => realmState.ArrayBufferPrototype,
            "SharedArrayBuffer" => realmState.SharedArrayBufferPrototype,
            "Boolean" => realmState.BooleanPrototype,
            "Date" => realmState.DatePrototype,
            "Function" => realmState.FunctionPrototype,
            "Map" => realmState.MapPrototype,
            "Number" => realmState.NumberPrototype,
            "Set" => realmState.SetPrototype,
            "Object" => realmState.ObjectPrototype,
            "WeakMap" => realmState.WeakMapPrototype,
            "WeakSet" => realmState.WeakSetPrototype,
            "String" => realmState.StringPrototype,
            _ => null
        };

        return prototype is not null;
    }

    private static bool TryGetIntlPrototype(string ctorName, RealmState? realmState, JsObject? realmObject,
        out IJsObjectLike? prototype)
    {
        prototype = null;
        if (!IsIntlConstructor(ctorName))
        {
            return false;
        }

        var intl = ResolveRealmIntlObject(realmState, realmObject);
        if (intl is null)
        {
            return false;
        }

        if (!intl.TryGetProperty(ctorName, out var ctorValue))
        {
            return false;
        }

        return TryGetPrototype(ctorValue!, out prototype);
    }

    private static bool IsIntlConstructor(string ctorName)
    {
        return ctorName is "Locale" or "DurationFormat" or "Collator" or "DateTimeFormat" or "NumberFormat" or
            "RelativeTimeFormat" or "DisplayNames";
    }

    private static JsObject? ResolveRealmIntlObject(RealmState? realmState, JsObject? realmObject)
    {
        if (realmObject is not null &&
            realmObject.TryGetProperty("Intl", out var intlValue) &&
            intlValue is JsObject intlObj)
        {
            return intlObj;
        }

        var engine = realmState?.Engine;
        if (engine?.GlobalObject.TryGetProperty("Intl", out var globalIntl) == true &&
            globalIntl is JsObject globalIntlObj)
        {
            return globalIntlObj;
        }

        return null;
    }

    private static bool TryGetRealmInfo(object candidate, out RealmState? realmState, out JsObject? realmObject)
    {
        switch (candidate)
        {
            case HostFunction hostFunction:
                realmState = hostFunction.RealmState;
                realmObject = hostFunction.Realm;
                return realmState is not null || realmObject is not null;
            case ICallableMetadata metadata:
                realmState = metadata.RealmState;
                realmObject = null;
                return realmState is not null;
            default:
                realmState = null;
                realmObject = null;
                return false;
        }
    }

    private static bool TryGetPrototype(object candidate, out IJsObjectLike? prototype)
    {
        prototype = null;

        // Prefer an explicit "prototype" property when present (e.g. constructors
        // where [[Prototype]] is Function.prototype but the instance prototype
        // lives on the .prototype data property).
        if (candidate is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("prototype", out var protoProperty) &&
            protoProperty is IJsObjectLike protoObj)
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
