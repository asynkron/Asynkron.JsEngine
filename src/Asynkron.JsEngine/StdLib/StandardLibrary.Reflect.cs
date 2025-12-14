using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static JsValue ReflectApply(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (args.Count < 2 || !args[0].TryGetObject<IJsCallable>(out var callable))
        {
            throw new Exception("Reflect.apply: target must be callable.");
        }

        var thisArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        var argList = args.Count > 2 && args[2].TryGetObject<JsArray>(out var arr)
            ? arr.Items.Select(JsValue.FromObject).ToArray()
            : [];

        return callable.Invoke(argList, thisArg);
    }

    internal static JsValue ReflectConstruct(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.construct.");
        }

        if (args.Count < 2 || !args[0].TryGetObject<IJsCallable>(out var target))
        {
            throw new Exception("Reflect.construct: target must be a constructor.");
        }

        var argList = args.Count > 1 && args[1].TryGetObject<JsArray>(out var arr)
            ? arr.Items.Select(JsValue.FromObject).ToArray()
            : [];
        IJsCallable newTarget;
        if (args.Count > 2)
        {
            if (!args[2].TryGetObject<IJsCallable>(out var ctor))
            {
                const string message = "newTarget is not a constructor";
                var errorResult = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                    ? typeErrorCtor.Invoke([new JsValue(message)], JsValue.Undefined)
                    : JsValue.FromObject(new InvalidOperationException(message));
                throw new ThrowSignal(errorResult.ToObject());
            }

            newTarget = ctor;
        }
        else
        {
            newTarget = target;
        }

        return JsValue.FromObject(Construct(target, argList, newTarget, realm));
    }

    internal static object? Construct(IJsCallable target, IReadOnlyList<object?> argList, IJsCallable newTarget,
        RealmState realm)
    {
        if (target is HostFunction hostTarget &&
            (!hostTarget.IsConstructor || hostTarget.DisallowConstruct))
        {
            var message = hostTarget.ConstructErrorMessage ?? "Target is not a constructor";
            var errorResult = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                ? typeErrorCtor.Invoke([new JsValue(message)], JsValue.Undefined)
                : JsValue.FromObject(new InvalidOperationException(message));
            throw new ThrowSignal(errorResult.ToObject());
        }

        if (newTarget is HostFunction { IsConstructor: false } hostNewTarget)
        {
            var message = hostNewTarget.ConstructErrorMessage ?? "newTarget is not a constructor";
            var errorResult = realm.TypeErrorConstructor is IJsCallable typeErrorCtor2
                ? typeErrorCtor2.Invoke([new JsValue(message)], JsValue.Undefined)
                : JsValue.FromObject(new InvalidOperationException(message));
            throw new ThrowSignal(errorResult.ToObject());
        }

        if (target is HostFunction hostCtor &&
            (ReferenceEquals(hostCtor, realm.ArrayBufferConstructor) ||
             ReferenceEquals(hostCtor, realm.SharedArrayBufferConstructor)))
        {
            var constructContext = realm.CreateContext(pushScope: false);
            var jsValueArgs = argList.Select(JsValue.FromObject).ToArray();
            return hostCtor.InvokeWithContext(jsValueArgs, JsValue.Undefined, constructContext, JsValue.FromObject(newTarget));
        }

        var proto = ResolveConstructPrototype(newTarget, target, realm);

        if ((realm.ArrayConstructor is not null && ReferenceEquals(target, realm.ArrayConstructor)) ||
            (realm.ArrayConstructor is not null && ReferenceEquals(newTarget, realm.ArrayConstructor)))
        {
            var instanceRealm = proto is JsObject { RealmState: { } protoRealm }
                ? protoRealm
                : newTarget switch
                {
                    HostFunction { RealmState: { } hostRealm } => hostRealm,
                    TypedAstEvaluator.TypedFunction { RealmState: { } tfRealm } => tfRealm,
                    _ => realm
                };
            var arrayInstance = new JsArray(instanceRealm);
            if (proto is not null)
            {
                arrayInstance.SetPrototype(proto);
            }

            var jsValueArgs = argList.Select(JsValue.FromObject).ToArray();
            var result = target.Invoke(jsValueArgs, new JsValue(arrayInstance));
            return result.TryGetObject<JsObject>(out var jsObj) ? jsObj : arrayInstance;
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
                var jsValueArgs = argList.Select(JsValue.FromObject).ToArray();
                var result = target.Invoke(jsValueArgs, new JsValue(instance));
                constructed = result.ToObject();
            }
        }
        finally
        {
            instance.EndConstruction();
        }

        return constructed is IJsPropertyAccessor ? constructed : instance;
    }

    internal static JsValue ReflectDefineProperty(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.defineProperty.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count < 3 || !TryGetObject(arg0, realm, out var target))
        {
            throw new Exception("Reflect.defineProperty: target must be an object.");
        }

        var arg1 = args.Count > 1 ? args[1].ToObject() : null;
        var propertyKey = JsOps.ToPropertyName(arg1) ?? string.Empty;
        var arg2 = args.Count > 2 ? args[2].ToObject() : null;
        var descriptor = ToPropertyDescriptor(arg2, realm);

        return JsValue.FromObject(TryDefinePropertyOnTarget(target, propertyKey, descriptor, realm, false));
    }

    internal static JsValue ReflectDeleteProperty(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.deleteProperty.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count < 2 || !TryGetObject(arg0, realm, out var target))
        {
            throw new Exception("Reflect.deleteProperty: target must be an object.");
        }

        var arg1 = args.Count > 1 ? args[1].ToObject() : null;
        var propertyKey = JsOps.ToPropertyName(arg1) ?? string.Empty;
        var result = target switch
        {
            ModuleNamespace moduleNamespace => moduleNamespace.Delete(propertyKey),
            JsArray jsArray when JsOps.TryResolveArrayIndex(propertyKey, out var index) => jsArray.DeleteElement(index),
            JsArray jsArray => jsArray.DeleteProperty(propertyKey),
            _ => target is JsObject jsObj && jsObj.Remove(propertyKey)
        };
        return JsValue.FromObject(result);
    }

    internal static JsValue ReflectGet(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.get.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count < 2 || !TryGetObject(arg0, realm, out var target))
        {
            throw new Exception("Reflect.get: target must be an object.");
        }

        var receiver = args.Count > 2 ? args[2].ToObject() : target;
        var arg1 = args.Count > 1 ? args[1].ToObject() : null;
        var propertyKey = JsOps.ToPropertyName(arg1) ?? string.Empty;
        return target.TryGetProperty(propertyKey, receiver, out var value) ? JsValue.FromObject(value) : JsValue.Undefined;
    }

    internal static JsValue ReflectGetOwnPropertyDescriptor(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.getOwnPropertyDescriptor.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count < 2 || !TryGetObject(arg0, realm, out var target))
        {
            throw new Exception("Reflect.getOwnPropertyDescriptor: target must be an object.");
        }

        var arg1 = args.Count > 1 ? args[1].ToObject() : null;
        var propertyKey = JsOps.ToPropertyName(arg1) ?? string.Empty;
        var descriptor = target.GetOwnPropertyDescriptor(propertyKey);
        var result = FromPropertyDescriptor(descriptor, realm);
        return result is not null ? JsValue.FromObject(result) : JsValue.Undefined;
    }

    internal static JsValue ReflectGetPrototypeOf(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.getPrototypeOf.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count == 0 || !TryGetObject(arg0, realm, out var target))
        {
            throw new Exception("Reflect.getPrototypeOf: target must be an object.");
        }

        if (target is ModuleNamespace)
        {
            return JsValue.Null;
        }

        return JsValue.FromObject(target.Prototype);
    }

    internal static JsValue ReflectHas(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.has.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count < 2 || !TryGetObject(arg0, realm, out var target))
        {
            throw new Exception("Reflect.has: target must be an object.");
        }

        var arg1 = args.Count > 1 ? args[1].ToObject() : null;
        var propertyKey = JsOps.ToPropertyName(arg1) ?? string.Empty;
        if (target is ModuleNamespace moduleNamespace)
        {
            // Use HasProperty which triggers evaluation for deferred namespaces per ES spec
            return new JsValue(moduleNamespace.HasProperty(propertyKey));
        }

        return new JsValue(target.TryGetProperty(propertyKey, out var _));
    }

    internal static JsValue ReflectIsExtensible(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.isExtensible.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count == 0 || !TryGetObject(arg0, realm, out var target))
        {
            throw new Exception("Reflect.isExtensible: target must be an object.");
        }

        return JsValue.FromObject(IsTargetExtensible(target));
    }

    internal static object? ReflectOwnKeys(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.ownKeys.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count == 0 || !TryGetObject(arg0, realm, out var target))
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

    internal static object? ReflectPreventExtensions(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.preventExtensions.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count == 0 || !TryGetObject(arg0, realm, out var target))
        {
            throw new Exception("Reflect.preventExtensions: target must be an object.");
        }

        PreventExtensionsOnTarget(target);
        return true;
    }

    internal static object? ReflectSet(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.set.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count < 2 || !TryGetObject(arg0, realm, out var target))
        {
            throw new Exception("Reflect.set: target must be an object.");
        }

        var arg1 = args.Count > 1 ? args[1].ToObject() : null;
        var propertyKey = JsOps.ToPropertyName(arg1) ?? string.Empty;
        var value = args.Count > 2 ? args[2].ToObject() : null;
        var receiver = args.Count > 3 ? args[3].ToObject() : target;
        switch (target)
        {
            case ModuleNamespace moduleNamespace:
                try
                {
                    moduleNamespace.SetProperty(propertyKey, value, receiver);
                }
                catch (ThrowSignal)
                {
                    return false;
                }

                return false;
            case JsArray jsArray when string.Equals(propertyKey, "length", StringComparison.Ordinal):
                return jsArray.SetLength(value, null, false);
            default:
                target.SetProperty(propertyKey, value, receiver);
                return true;
        }
    }

    internal static object? ReflectSetPrototypeOf(object? _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.setPrototypeOf.");
        }

        var arg0 = args.Count > 0 ? args[0].ToObject() : null;
        if (args.Count < 2 || !TryGetObject(arg0, realm, out var target))
        {
            throw new Exception("Reflect.setPrototypeOf: target must be an object.");
        }

        var proto = args.Count > 1 ? args[1].ToObject() : null;
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
