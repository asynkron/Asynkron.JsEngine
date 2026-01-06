#region

using System.Reflection;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.ObjectHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class ReflectHelper
{
    internal static JsValue ReflectApply(JsValue _, IReadOnlyList<JsValue> args)
    {
        if (args.Count < 2 || !args[0].TryGetObject<IJsCallable>(out var callable))
        {
            throw new Exception("Reflect.apply: target must be callable.");
        }

        var thisArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        var argList = args.Count > 2 && args[2].TryGetObject<JsArray>(out var arr)
            ? arr.Items.ToArray()
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
            ? arr.Items.ToArray()
            : [];
        IJsCallable newTarget;
        if (args.Count > 2)
        {
            if (!args[2].TryGetObject<IJsCallable>(out var ctor))
            {
                const string message = "newTarget is not a constructor";
                var errorResult = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                    ? typeErrorCtor.Invoke(new SingleValueArgs(new JsValue(message)), JsValue.Undefined)
                    : JsValue.FromObjectUnsafe(new InvalidOperationException(message));
                throw new ThrowSignal(errorResult);
            }

            newTarget = ctor;
        }
        else
        {
            newTarget = target;
        }

        return Construct(target, argList, newTarget, realm);
    }

    internal static JsValue Construct(IJsCallable target, IReadOnlyList<JsValue> argList, IJsCallable newTarget,
        RealmState realm)
    {
        if (target is HostFunction hostTarget &&
            (!hostTarget.IsConstructor || hostTarget.DisallowConstruct))
        {
            var message = hostTarget.ConstructErrorMessage ?? "Target is not a constructor";
            var errorResult = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                ? typeErrorCtor.Invoke(new SingleValueArgs(new JsValue(message)), JsValue.Undefined)
                : JsValue.FromObjectUnsafe(new InvalidOperationException(message));
            throw new ThrowSignal(errorResult);
        }

        if (newTarget is HostFunction { IsConstructor: false } hostNewTarget)
        {
            var message = hostNewTarget.ConstructErrorMessage ?? "newTarget is not a constructor";
            var errorResult = realm.TypeErrorConstructor is IJsCallable typeErrorCtor2
                ? typeErrorCtor2.Invoke(new SingleValueArgs(new JsValue(message)), JsValue.Undefined)
                : JsValue.FromObjectUnsafe(new InvalidOperationException(message));
            throw new ThrowSignal(errorResult);
        }

        if (target is HostFunction hostCtor &&
            (ReferenceEquals(hostCtor, realm.ArrayBufferConstructor) ||
             ReferenceEquals(hostCtor, realm.SharedArrayBufferConstructor)))
        {
            var constructContext = realm.CreateContext(pushScope: false);
            return hostCtor.InvokeWithContext(argList, JsValue.Undefined, constructContext,
                JsValue.FromObjectUnsafe(newTarget));
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
                    TypedAstEvaluator.SyncFunctionInvoker { RealmState: { } tfRealm } => tfRealm,
                    _ => realm
                };
            var arrayInstance = new JsArray(instanceRealm);
            if (proto is not null)
            {
                arrayInstance.SetPrototype(proto);
            }

            JsValue result;
            if (target is HostFunction hostFunction &&
                hostFunction.InvokeWithContextForSnapshot is not null)
            {
                var constructContext = realm.CreateContext(pushScope: false);
                result = hostFunction.InvokeWithContext(
                    argList,
                    JsValue.FromJsArray(arrayInstance),
                    constructContext,
                    JsValue.FromObjectUnsafe(newTarget));

                if (constructContext.IsThrow)
                {
                    throw new ThrowSignal(constructContext.FlowValue);
                }
            }
            else
            {
                result = target.Invoke(argList, JsValue.FromJsArray(arrayInstance));
            }

            return result.TryGetObject<IJsObjectLike>(out var jsObj)
                ? JsValue.FromObjectUnsafe(jsObj)
                : JsValue.FromJsArray(arrayInstance);
        }

        var instance = new JsObject();
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }

        JsValue constructed;
        instance.BeginConstruction();
        try
        {
            var invokeWithContext = target.GetType().GetMethod(
                "InvokeWithContext",
                [typeof(IReadOnlyList<JsValue>), typeof(JsValue), typeof(EvaluationContext), typeof(JsValue)]);
            if (invokeWithContext is not null)
            {
                var constructContext = realm.CreateContext(pushScope: false);
                try
                {
                    var invokeResult = invokeWithContext.Invoke(target,
                        [argList, new JsValue(instance), constructContext, JsValue.FromObjectUnsafe(newTarget)]);
                    constructed = invokeResult is JsValue jsv ? jsv : JsValue.FromObjectUnsafe(invokeResult);
                }
                catch (TargetInvocationException tie) when (tie.InnerException is ThrowSignal)
                {
                    throw tie.InnerException;
                }

                // Check if the constructor set a throw on the context (InvokeWithContext doesn't throw
                // when it has a calling context, it sets the throw state instead)
                if (constructContext.IsThrow)
                {
                    throw new ThrowSignal(constructContext.FlowValue);
                }
            }
            else
            {
                constructed = target.Invoke(argList, new JsValue(instance));
            }
        }
        finally
        {
            instance.EndConstruction();
        }

        return constructed.TryGetObject<IJsPropertyAccessor>(out _) ? constructed : new JsValue(instance);
    }

    internal static JsValue ReflectDefineProperty(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.defineProperty.");
        }

        if (args.Count < 3 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.defineProperty: target must be an object.");
        }

        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
        var descriptor = ToPropertyDescriptor(args[2], realm);

        return TryDefinePropertyOnTarget(target, propertyKey, descriptor, realm, false);
    }

    internal static JsValue ReflectDeleteProperty(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.deleteProperty.");
        }

        if (args.Count < 2 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.deleteProperty: target must be an object.");
        }

        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
        var result = target switch
        {
            ModuleNamespace moduleNamespace => moduleNamespace.Delete(propertyKey),
            JsArray jsArray when JsOps.TryResolveArrayIndex(propertyKey, out var index) => jsArray.DeleteElement(index),
            JsArray jsArray => jsArray.DeleteProperty(propertyKey),
            _ => target is JsObject jsObj && jsObj.Remove(propertyKey)
        };
        return result;
    }

    internal static JsValue ReflectGet(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.get.");
        }

        if (args.Count < 2 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.get: target must be an object.");
        }

        var receiver = args.Count > 2 ? args[2] : JsValue.FromObjectUnsafe(target);
        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
        return target.TryGetProperty(propertyKey, receiver, out var value) ? value : JsValue.Undefined;
    }

    internal static JsValue ReflectGetOwnPropertyDescriptor(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.getOwnPropertyDescriptor.");
        }

        if (args.Count < 2 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.getOwnPropertyDescriptor: target must be an object.");
        }

        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
        var descriptor = target.GetOwnPropertyDescriptor(propertyKey);
        var result = FromPropertyDescriptor(descriptor, realm);
        return result is not null ? (JsValue)result : JsValue.Undefined;
    }

    internal static JsValue ReflectGetPrototypeOf(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.getPrototypeOf.");
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.getPrototypeOf: target must be an object.");
        }

        if (target is ModuleNamespace)
        {
            return JsValue.Null;
        }

        return target.Prototype is null ? JsValue.Null : (JsValue)target.Prototype;
    }

    internal static JsValue ReflectHas(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.has.");
        }

        if (args.Count < 2 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.has: target must be an object.");
        }

        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
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

        if (args.Count == 0 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.isExtensible: target must be an object.");
        }

        return IsTargetExtensible(target);
    }

    internal static JsValue ReflectOwnKeys(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.ownKeys.");
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.ownKeys: target must be an object.");
        }

        if (target is ModuleNamespace moduleNamespace)
        {
            return JsValue.FromJsArray(new JsArray(moduleNamespace.OwnKeys(), realm));
        }

        if (target is IJsPropertyAccessor accessor)
        {
            var ordered = new JsArray(realm);
            foreach (var key in accessor.GetOwnPropertyKeysInOrder(true, true))
            {
                if (key.StartsWith("__getter__", StringComparison.Ordinal) ||
                    key.StartsWith("__setter__", StringComparison.Ordinal) ||
                    string.Equals(key, "__proto__", StringComparison.Ordinal))
                {
                    continue;
                }

                ordered.Push(key);
            }

            return JsValue.FromJsArray(ordered);
        }

        var keys = target.Keys
            .Where(static k => !k.StartsWith("__getter__", StringComparison.Ordinal) &&
                        !k.StartsWith("__setter__", StringComparison.Ordinal) &&
                        !string.Equals(k, "__proto__", StringComparison.Ordinal))
            .ToArray();
        return JsValue.FromJsArray(new JsArray(keys, realm));
    }

    internal static JsValue ReflectPreventExtensions(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.preventExtensions.");
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.preventExtensions: target must be an object.");
        }

        PreventExtensionsOnTarget(target);
        return new JsValue(true);
    }

    internal static JsValue ReflectSet(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.set.");
        }

        if (args.Count < 2 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.set: target must be an object.");
        }

        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
        var value = args.Count > 2 ? args[2] : JsValue.Undefined;
        var receiver = args.Count > 3 ? args[3] : JsValue.FromObjectUnsafe(target);
        switch (target)
        {
            case ModuleNamespace moduleNamespace:
                try
                {
                    moduleNamespace.SetProperty(propertyKey, value, receiver);
                }
                catch (ThrowSignal)
                {
                    return new JsValue(false);
                }

                return new JsValue(false);
            case JsArray jsArray when string.Equals(propertyKey, "length", StringComparison.Ordinal):
                // Pass JsValue directly - ToNumericAsJsValue handles boxed JsValue efficiently
                return jsArray.SetLength(value, null, false);
            default:
                target.SetProperty(propertyKey, value, receiver);
                return new JsValue(true);
        }
    }

    internal static JsValue ReflectSetPrototypeOf(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.setPrototypeOf.");
        }

        if (args.Count < 2 || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw new Exception("Reflect.setPrototypeOf: target must be an object.");
        }

        // Extract prototype: null is valid, objects are valid, others should be handled by SetPrototype
        var protoArg = args.Count > 1 ? args[1] : JsValue.Null;
        var proto = protoArg.IsNull ? null : protoArg.ObjectValue as IJsPropertyAccessor;
        try
        {
            target.SetPrototype(proto);
            return new JsValue(true);
        }
        catch (ThrowSignal)
        {
            return new JsValue(false);
        }
    }

    internal static IJsObjectLike? ResolveConstructPrototype(IJsCallable newTarget, IJsCallable target,
        RealmState realmState)
    {
        // Step 1: use newTarget.prototype if it is an object
        if (newTarget is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("prototype", out var protoVal) &&
            protoVal.TryGetObject<IJsObjectLike>(out var protoObj))
        {
            return protoObj;
        }

        TryGetRealmInfo(newTarget, out var newTargetRealmState, out var newTargetRealmObject);

        // Step 2: try realm default for Array (handles cross-realm Array subclassing)
        if ((realmState.ArrayConstructor is not null && ReferenceEquals(target, realmState.ArrayConstructor)) ||
            (realmState.ArrayConstructor is not null && ReferenceEquals(newTarget, realmState.ArrayConstructor)))
        {
            if (newTargetRealmState?.ArrayPrototype is { } realmArrayProtoFromState)
            {
                return realmArrayProtoFromState;
            }

            if (newTargetRealmObject is not null &&
                newTargetRealmObject.TryGetProperty("Array", out var realmArrayCtor) &&
                TryGetPrototype(realmArrayCtor, out var realmArrayProto))
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

    private static bool TryResolveRealmDefaultPrototype(object newTarget, IJsCallable target,
        out IJsObjectLike? prototype)
    {
        prototype = null;
        if (!TryGetRealmInfo(newTarget, out var realmState, out var realmObject))
        {
            return false;
        }

        if (target is not IJsPropertyAccessor accessor ||
            !accessor.TryGetProperty("name", out var nameValue) ||
            !nameValue.IsString)
        {
            return false;
        }

        var ctorName = nameValue.AsString();

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
            !realmCtor.IsUndefined &&
            TryGetPrototype(realmCtor, out var realmProto))
        {
            prototype = realmProto;
            return true;
        }

        if (realmObject is not null &&
            realmObject.TryGetProperty("Object", out var objectCtor) &&
            !objectCtor.IsUndefined &&
            TryGetPrototype(objectCtor, out var objectProto))
        {
            prototype = objectProto;
            return true;
        }

        return false;
    }

    private static bool TryGetPrototypeFromRealmState(string ctorName, RealmState realmState,
        out IJsObjectLike? prototype)
    {
        prototype = ctorName switch
        {
            "Array" => realmState.ArrayPrototype,
            "ArrayBuffer" => realmState.ArrayBufferPrototype,
            "AsyncFunction" => realmState.AsyncFunctionPrototype,
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
            "DisposableStack" => realmState.DisposableStackPrototype,
            "AsyncDisposableStack" => realmState.AsyncDisposableStackPrototype,
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

        return TryGetPrototype(ctorValue, out prototype);
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
            intlValue.TryGetObject<JsObject>(out var intlObj))
        {
            return intlObj;
        }

        var engine = realmState?.Engine;
        if (engine?.GlobalObject.TryGetProperty("Intl", out var globalIntl) == true &&
            globalIntl.TryGetObject<JsObject>(out var globalIntlObj))
        {
            return globalIntlObj;
        }

        return null;
    }

    public static bool TryGetRealmInfo(object candidate, out RealmState? realmState, out JsObject? realmObject)
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

    /// <summary>
    /// JsValue overload that avoids boxing when the value is already a JsValue.
    /// </summary>
    private static bool TryGetPrototype(JsValue value, out IJsObjectLike? prototype)
    {
        prototype = null;

        if (value.Kind != JsValueKind.Object)
        {
            return false;
        }

        return TryGetPrototype(value.ObjectValue!, out prototype);
    }

    private static bool TryGetPrototype(object candidate, out IJsObjectLike? prototype)
    {
        prototype = null;

        // Prefer an explicit "prototype" property when present (e.g. constructors
        // where [[Prototype]] is Function.prototype but the instance prototype
        // lives on the .prototype data property).
        if (candidate is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("prototype", out var protoProperty) &&
            protoProperty.TryGetObject<IJsObjectLike>(out var protoObj))
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
