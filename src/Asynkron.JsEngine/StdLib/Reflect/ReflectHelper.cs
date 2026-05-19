#region

using System.Reflection;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.ObjectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class ReflectHelper
{
    internal static JsValue ReflectApply(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        // 1. If IsCallable(target) is false, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsCallable>(out var callable))
        {
            throw ThrowTypeError("Reflect.apply: target must be a function", realm: realm);
        }

        var thisArg = args.Count > 1 ? args[1] : JsValue.Undefined;

        // 3. Let args be ? CreateListFromArrayLike(argumentsList).
        var argumentsList = args.Count > 2 ? args[2] : JsValue.Undefined;
        var argList = CreateListFromArrayLike(argumentsList, realm);

        return callable.Invoke(argList, thisArg);
    }

    /// <summary>
    /// Implements the abstract operation CreateListFromArrayLike (ES2023 7.3.22).
    /// </summary>
    internal static IReadOnlyList<JsValue> CreateFunctionApplyArgumentList(JsValue obj, RealmState? realm)
    {
        if (obj.IsNullOrUndefined)
        {
            return ArgumentSlice.Empty;
        }

        return CreateListFromArrayLike(obj, realm);
    }

    internal static JsValue[] CreateListFromArrayLike(JsValue obj, RealmState? realm)
    {
        if (obj.IsNullOrUndefined)
        {
            throw ThrowTypeError("CreateListFromArrayLike called on null or undefined", realm: realm);
        }

        if (!obj.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            throw ThrowTypeError("CreateListFromArrayLike called on non-object", realm: realm);
        }

        var length = LengthOfArrayLike(accessor, realm);
        var list = new JsValue[(int)Math.Min(length, int.MaxValue)];
        for (var i = 0L; i < length && i < list.Length; i++)
        {
            var indexStr = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            list[i] = accessor.TryGetProperty(indexStr, out var value) ? value : JsValue.Undefined;
        }

        return list;
    }

    internal static JsValue ReflectConstruct(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.construct.");
        }

        // 1. If IsConstructor(target) is false, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!JsOps.IsConstructor(targetArg) || !targetArg.TryGetObject<IJsCallable>(out var target))
        {
            throw ThrowTypeError("Reflect.construct: target is not a constructor", realm: realm);
        }

        // 2. If newTarget is not present, set newTarget to target.
        // 4. If IsConstructor(newTarget) is false, throw a TypeError exception.
        IJsCallable newTarget;
        if (args.Count > 2)
        {
            if (!JsOps.IsConstructor(args[2]) || !args[2].TryGetObject<IJsCallable>(out var ctor))
            {
                throw ThrowTypeError("newTarget is not a constructor", realm: realm);
            }

            newTarget = ctor;
        }
        else
        {
            newTarget = target;
        }

        // 3. Let args be ? CreateListFromArrayLike(argumentsList).
        var argumentsList = args.Count > 1 ? args[1] : JsValue.Undefined;
        var argList = CreateListFromArrayLike(argumentsList, realm);

        return Construct(target, argList, newTarget, realm);
    }

    internal static JsValue Construct(IJsCallable target, IReadOnlyList<JsValue> argList, IJsCallable newTarget,
        RealmState realm)
    {
        // Route through JsProxy construct trap when target is a proxy
        if (target is JsProxy proxyTarget)
        {
            return proxyTarget.Construct(argList, newTarget);
        }

        if (target is HostFunction hostTarget &&
            (!hostTarget.IsConstructor || hostTarget.DisallowConstruct))
        {
            var message = hostTarget.ConstructErrorMessage ?? "Target is not a constructor";
            var errorResult = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                ? typeErrorCtor.Invoke(new SingleValueArgs(new JsValue(message)), JsValue.Undefined)
                : JsValue.FromObjectUnsafe(new InvalidOperationException(message));
            throw new ThrowSignal(errorResult);
        }

        if (newTarget is HostFunction hostNewTarget &&
            (!hostNewTarget.IsConstructor || hostNewTarget.DisallowConstruct))
        {
            var message = hostNewTarget.ConstructErrorMessage ?? "newTarget is not a constructor";
            var errorResult = realm.TypeErrorConstructor is IJsCallable typeErrorCtor2
                ? typeErrorCtor2.Invoke(new SingleValueArgs(new JsValue(message)), JsValue.Undefined)
                : JsValue.FromObjectUnsafe(new InvalidOperationException(message));
            throw new ThrowSignal(errorResult);
        }

        if (target is ICallableMetadata { DisallowConstruct: true })
        {
            throw ThrowTypeError("Target is not a constructor", realm: realm);
        }

        if (newTarget is ICallableMetadata { DisallowConstruct: true })
        {
            throw ThrowTypeError("newTarget is not a constructor", realm: realm);
        }

        // For Proxy-wrapped non-constructors used as newTarget
        if (newTarget is JsProxy && !JsOps.IsConstructor(JsValue.FromObjectUnsafe(newTarget)))
        {
            throw ThrowTypeError("newTarget is not a constructor", realm: realm);
        }

        if (target is HostFunction hostCtor &&
            (ReferenceEquals(hostCtor, realm.ArrayBufferConstructor) ||
             ReferenceEquals(hostCtor, realm.SharedArrayBufferConstructor) ||
             ReferenceEquals(hostCtor, realm.DataViewConstructor) ||
             ReferenceEquals(hostCtor, realm.PromiseConstructor) ||
             IsConcreteTypedArrayConstructor(hostCtor, realm)))
        {
            var constructContext = realm.CreateContext(pushScope: false);
            return hostCtor.InvokeWithContext(argList, JsValue.Undefined, constructContext,
                JsValue.FromObjectUnsafe(newTarget));
        }

        var proto = ResolveConstructPrototype(newTarget, target, realm);

        if (IsArrayConstructor(target, realm))
        {
            TryGetRealmInfo(newTarget, out var newTargetRealmState, out _);
            var instanceRealm = proto is JsObject { RealmState: { } protoRealm }
                ? protoRealm
                : newTargetRealmState is { } proxiedRealm
                    ? proxiedRealm
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
            if (target is TypedAstEvaluator.SyncFunctionInvoker typedTarget)
            {
                var constructContext = realm.CreateContext(pushScope: false);
                constructed = typedTarget.InvokeWithContext(
                    argList,
                    new JsValue(instance),
                    constructContext,
                    JsValue.FromObjectUnsafe(newTarget));

                if (constructContext.IsThrow)
                {
                    throw new ThrowSignal(constructContext.FlowValue);
                }
            }
            else if (target.GetType().GetMethod(
                         "InvokeWithContext",
                         [typeof(IReadOnlyList<JsValue>), typeof(JsValue), typeof(EvaluationContext), typeof(JsValue)])
                     is { } invokeWithContext)
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

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.defineProperty: target is not an Object", realm: realm);
        }

        // 2. Let key be ? ToPropertyKey(propertyKey).
        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
        // 3. Let desc be ? ToPropertyDescriptor(Attributes).
        var attrArg = args.Count > 2 ? args[2] : JsValue.Undefined;
        var descriptor = ToPropertyDescriptor(attrArg, realm);

        return TryDefinePropertyOnTarget(target, propertyKey, descriptor, realm, false);
    }

    internal static JsValue ReflectDeleteProperty(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.deleteProperty.");
        }

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.deleteProperty: target is not an Object", realm: realm);
        }

        // 2. Let key be ? ToPropertyKey(propertyKey).
        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
        // 3. Return ? target.[[Delete]](key).
        return new JsValue(target.Delete(propertyKey));
    }

    internal static JsValue ReflectGet(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.get.");
        }

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.get: target is not an Object", realm: realm);
        }

        // 2. Let key be ? ToPropertyKey(propertyKey).
        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
        // 3. If receiver is not present, set receiver to target.
        var receiver = args.Count > 2 ? args[2] : JsValue.FromObjectUnsafe(target);
        return target.TryGetProperty(propertyKey, receiver, out var value) ? value : JsValue.Undefined;
    }

    internal static JsValue ReflectGetOwnPropertyDescriptor(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.getOwnPropertyDescriptor.");
        }

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.getOwnPropertyDescriptor: target is not an Object", realm: realm);
        }

        // 2. Let key be ? ToPropertyKey(propertyKey).
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

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.getPrototypeOf: target is not an Object", realm: realm);
        }

        if (target is ModuleNamespace)
        {
            return JsValue.Null;
        }

        // 2. Return ? target.[[GetPrototypeOf]]().
        // For proxies, GetPrototypeOf may throw (return-abrupt-from-result test)
        if (target is JsProxy proxy)
        {
            var proto = proxy.GetPrototypeWithTrap();
            return proto is null ? JsValue.Null : JsValue.FromObjectUnsafe(proto);
        }

        return target.Prototype is null ? JsValue.Null : (JsValue)target.Prototype;
    }

    internal static JsValue ReflectHas(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.has.");
        }

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.has: target is not an Object", realm: realm);
        }

        // 2. Let key be ? ToPropertyKey(propertyKey).
        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
        if (target is ModuleNamespace moduleNamespace)
        {
            // Use HasProperty which triggers evaluation for deferred namespaces per ES spec
            return new JsValue(moduleNamespace.HasProperty(propertyKey));
        }

        // 3. Return ? target.[[HasProperty]](key).
        return new JsValue(HasProperty(target, propertyKey));
    }

    internal static JsValue ReflectIsExtensible(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.isExtensible.");
        }

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.isExtensible: target is not an Object", realm: realm);
        }

        // 2. Return ? target.[[IsExtensible]]().
        // For proxies, invoke the isExtensible trap if present
        if (target is JsProxy isExtProxy)
        {
            var trapResult = InvokeProxyTrap(isExtProxy, "isExtensible", realm);
            if (trapResult is not null)
            {
                return new JsValue(JsOps.ToBoolean(trapResult.Value));
            }
        }

        return IsTargetExtensible(target);
    }

    internal static JsValue ReflectOwnKeys(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.ownKeys.");
        }

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.ownKeys: target is not an Object", realm: realm);
        }

        if (target is ModuleNamespace moduleNamespace)
        {
            return JsValue.FromJsArray(new JsArray(moduleNamespace.OwnKeys(), realm));
        }

        if (target is JsProxy proxy)
        {
            var result = new JsArray(realm);
            foreach (var key in proxy.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
            {
                if (JsSymbol.TryGetByInternalKey(key, out var symbol) && symbol is not null)
                {
                    result.Push((JsValue)symbol);
                    continue;
                }

                result.Push(key);
            }

            return JsValue.FromJsArray(result);
        }

        // Collect all raw keys, then sort per [[OwnPropertyKeys]] spec ordering:
        // 1. Integer indices in ascending numeric order
        // 2. Remaining string keys in property creation order
        // 3. Symbol keys in property creation order
        return JsValue.FromJsArray(CollectOwnKeysOrdered(target, realm));
    }

    internal static JsValue ReflectPreventExtensions(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.preventExtensions.");
        }

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.preventExtensions: target is not an Object", realm: realm);
        }

        // 2. Return ? target.[[PreventExtensions]]().
        // For proxies, invoke the preventExtensions trap if present
        if (target is JsProxy prevExtProxy)
        {
            var trapResult = InvokeProxyTrap(prevExtProxy, "preventExtensions", realm);
            if (trapResult is not null)
            {
                return new JsValue(JsOps.ToBoolean(trapResult.Value));
            }
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

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.set: target is not an Object", realm: realm);
        }

        // 2. Let key be ? ToPropertyKey(propertyKey).
        var propertyKey = args.Count > 1 ? JsOps.ToPropertyName(args[1]) ?? string.Empty : string.Empty;
        var value = args.Count > 2 ? args[2] : JsValue.Undefined;
        // 4. If receiver is not present, set receiver to target.
        var receiver = args.Count > 3 ? args[3] : JsValue.FromObjectUnsafe(target);
        if (target is ModuleNamespace)
        {
            return new JsValue(false);
        }

        // 5. Return ? target.[[Set]](key, V, receiver).
        return new JsValue(SetPropertyWithReceiver(target, propertyKey, value, receiver));
    }

    internal static bool SetPropertyWithReceiver(IJsPropertyAccessor target, string propertyKey, JsValue value,
        JsValue receiver)
    {
        if (target is JsProxy proxy)
        {
            return proxy.TrySetProperty(propertyKey, value, receiver);
        }

        // TypedArray exotic [[Set]] (P, V, Receiver):
        // For canonical numeric index strings, the typed array intercepts the operation.
        if (target is TypedArrayBase typedArray &&
            TypedArrayBase.TryCanonicalNumericIndex(propertyKey, out var numericIndex))
        {
            // If SameValue(O, Receiver) is true, perform IntegerIndexedElementSet.
            var isSameReceiver = receiver.TryGetObject<TypedArrayBase>(out var receiverTa) &&
                                 ReferenceEquals(receiverTa, typedArray);
            if (isSameReceiver)
            {
                typedArray.SetProperty(propertyKey, value, receiver);
                return true;
            }

            // If IsValidIntegerIndex(O, numericIndex) is false, return true.
            if (!typedArray.IsValidIntegerIndex(numericIndex))
            {
                return true;
            }

            // Valid index but different receiver -- fall through to OrdinarySet
            // which will try to set on the receiver object.
            return OrdinarySetWithReceiver(typedArray, propertyKey, value, receiver);
        }

        if (target is IJsObjectLike objectLike)
        {
            return OrdinarySetWithReceiver(objectLike, propertyKey, value, receiver);
        }

        target.SetProperty(propertyKey, value, receiver);
        return true;
    }

    private static bool OrdinarySetWithReceiver(IJsObjectLike target, string propertyKey, JsValue value,
        JsValue receiver)
    {
        var ownDesc = target.GetOwnPropertyDescriptor(propertyKey);
        if (ownDesc is null)
        {
            var parent = GetPrototypeAccessor(target);
            if (parent is not null)
            {
                return SetPropertyWithReceiver(parent, propertyKey, value, receiver);
            }

            ownDesc = new PropertyDescriptor
            {
                JsValue = JsValue.Undefined,
                Writable = true,
                Enumerable = true,
                Configurable = true
            };
        }

        if (ownDesc.IsAccessorDescriptor)
        {
            if (ownDesc.Set is null)
            {
                return false;
            }

            ownDesc.Set.Invoke(new SingleValueArgs(value), receiver);
            return true;
        }

        if (!ownDesc.Writable)
        {
            return false;
        }

        if (!receiver.TryGetObject<IJsObjectLike>(out var receiverObject))
        {
            return false;
        }

        var existingDesc = receiverObject.GetOwnPropertyDescriptor(propertyKey);
        if (existingDesc is not null)
        {
            if (existingDesc.IsAccessorDescriptor || !existingDesc.Writable)
            {
                return false;
            }

            var valueDesc = new PropertyDescriptor { JsValue = value };
            return TryDefineProperty(receiverObject, propertyKey, valueDesc);
        }

        var newDesc = new PropertyDescriptor
        {
            JsValue = value,
            Writable = true,
            Enumerable = true,
            Configurable = true
        };

        return TryDefineProperty(receiverObject, propertyKey, newDesc);
    }

    private static IJsPropertyAccessor? GetPrototypeAccessor(IJsObjectLike target)
    {
        if (target.Prototype is not null)
        {
            return target.Prototype;
        }

        return target is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;
    }

    private static bool TryDefineProperty(IJsObjectLike target, string propertyKey, PropertyDescriptor descriptor)
    {
        if (target is IPropertyDefinitionHost host)
        {
            return host.TryDefineProperty(propertyKey, descriptor);
        }

        target.DefineProperty(propertyKey, descriptor);
        return true;
    }

    internal static JsValue ReflectSetPrototypeOf(JsValue _, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Reflect.setPrototypeOf.");
        }

        // 1. If Type(target) is not Object, throw a TypeError exception.
        var targetArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!targetArg.TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Reflect.setPrototypeOf: target is not an Object", realm: realm);
        }

        // 2. If Type(proto) is not Object and proto is not null, throw a TypeError exception.
        var protoArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        if (!protoArg.IsNull && (protoArg.Kind != JsValueKind.Object || protoArg.ObjectValue is not IJsPropertyAccessor))
        {
            throw ThrowTypeError("Reflect.setPrototypeOf: proto is not an Object and not null", realm: realm);
        }

        var proto = protoArg.IsNull ? null : protoArg.ObjectValue as IJsPropertyAccessor;

        // 3. Return ? target.[[SetPrototypeOf]](proto).
        try
        {
            target.SetPrototype(proto);
            return new JsValue(true);
        }
        catch (ThrowSignal signal)
        {
            // JsObject.SetPrototype throws ThrowSignal(Undefined) for non-extensible/immutable
            // Proxy setPrototypeOf trap returning false throws "Proxy 'setPrototypeOf' trap returned a falsy value"
            // These cases should return false. Real abrupt completions (like trap throwing an error) should propagate.
            if (signal.ThrownValue.IsUndefined)
            {
                return new JsValue(false);
            }

            if (signal.ThrownValue.TryGetObject<JsObject>(out var errorObj) &&
                errorObj.TryGetProperty("message", out var msgVal) &&
                msgVal.TryGetString(out var msg) &&
                string.Equals(msg, "Proxy 'setPrototypeOf' trap returned a falsy value", StringComparison.Ordinal))
            {
                return new JsValue(false);
            }

            throw;
        }
    }

    /// <summary>
    /// Returns true if the key is an engine-internal slot that should never be exposed via
    /// Reflect.ownKeys or similar APIs.
    /// </summary>
    private static bool IsInternalSlotKey(string key)
    {
        return key.StartsWith("__getter__", StringComparison.Ordinal) ||
               key.StartsWith("__setter__", StringComparison.Ordinal) ||
               string.Equals(key, "__value__", StringComparison.Ordinal) ||
               string.Equals(key, "__regex__", StringComparison.Ordinal) ||
               string.Equals(key, "__arguments__", StringComparison.Ordinal) ||
               string.Equals(key, "__promise__", StringComparison.Ordinal);
    }

    /// <summary>
    /// Collects own property keys from a target and returns them in [[OwnPropertyKeys]] order:
    /// integer indices in ascending numeric order, then string keys in insertion order, then symbols.
    /// </summary>
    private static JsArray CollectOwnKeysOrdered(IJsObjectLike target, RealmState? realm)
    {
        // We use two sources of keys:
        // 1. target.Keys (raw storage keys) - uncorrupted strings, used for numeric index classification.
        // 2. GetOwnPropertyKeysInOrder (insertion-ordered) - used for string/symbol key ordering.
        //    Note: GetOwnPropertyKeysInOrder has a bug where large uint indices (>= 2^31) are
        //    corrupted due to a uint-to-int cast. We work around this by classifying numeric
        //    keys from the raw storage and getting string/symbol ordering from the ordered API.

        // Step 1: Collect numeric indices from raw keys (uncorrupted).
        var numericKeys = new List<(uint index, string key)>();
        var numericKeySet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in target.Keys)
        {
            if (IsInternalSlotKey(key))
            {
                continue;
            }

            if (JsSymbol.TryGetByInternalKey(key, out _))
            {
                continue;
            }

            if (uint.TryParse(key, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var index) &&
                index < uint.MaxValue &&
                string.Equals(index.ToString(System.Globalization.CultureInfo.InvariantCulture), key,
                    StringComparison.Ordinal))
            {
                numericKeys.Add((index, key));
                numericKeySet.Add(key);
            }
        }

        numericKeys.Sort(static (a, b) => a.index.CompareTo(b.index));

        // Step 2: Collect string and symbol keys in insertion order from the ordered API.
        // Skip keys we already classified as numeric, and skip internal/corrupted keys.
        var stringKeys = new List<string>();
        var symbolKeys = new List<string>();

        IEnumerable<string> orderedKeys = target is IJsPropertyAccessor accessor
            ? accessor.GetOwnPropertyKeysInOrder(true, true)
            : target.Keys;

        foreach (var key in orderedKeys)
        {
            if (IsInternalSlotKey(key))
            {
                continue;
            }

            if (JsSymbol.TryGetByInternalKey(key, out _))
            {
                symbolKeys.Add(key);
                continue;
            }

            // Skip keys that are numeric indices (already collected from raw keys).
            if (numericKeySet.Contains(key))
            {
                continue;
            }

            // Skip corrupted negative-integer keys produced by the uint-to-int overflow
            // in EnumerateOwnKeysInOrder. These are phantom keys - the real key was already
            // collected from the raw storage above.
            if (key.Length > 0 && key[0] == '-' &&
                int.TryParse(key, System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            stringKeys.Add(key);
        }

        // Build result: integer indices ascending, then strings in insertion order, then symbols.
        var result = new JsArray(realm);

        foreach (var (_, key) in numericKeys)
        {
            result.Push(key);
        }

        foreach (var key in stringKeys)
        {
            result.Push(key);
        }

        foreach (var key in symbolKeys)
        {
            if (JsSymbol.TryGetByInternalKey(key, out var symbol) && symbol is not null)
            {
                result.Push((JsValue)symbol);
            }
            else
            {
                result.Push(key);
            }
        }

        return result;
    }

    /// <summary>
    /// Attempts to invoke a Proxy handler trap by name.
    /// Returns null if the trap is not defined; otherwise returns the trap result.
    /// Abrupt completions from the trap propagate normally.
    /// </summary>
    private static JsValue? InvokeProxyTrap(JsProxy proxy, string trapName, RealmState? realm)
    {
        var handler = proxy.Handler;
        if (handler is null)
        {
            throw ThrowTypeError("Cannot perform operation on a revoked Proxy", realm: realm);
        }

        var handlerJsValue = JsValue.FromObjectUnsafe(handler);
        if (!handler.TryGetProperty(trapName, handlerJsValue, out var trapValue))
        {
            return null;
        }

        if (trapValue.IsUndefined || trapValue.IsNull)
        {
            return null;
        }

        if (!trapValue.TryGetObject<IJsCallable>(out var trap))
        {
            throw ThrowTypeError($"Proxy handler's '{trapName}' trap is not callable", realm: realm);
        }

        var targetJsValue = JsValue.FromObjectUnsafe(proxy.Target);
        var args = new[] { targetJsValue };
        return trap.Invoke(args, handlerJsValue);
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
        if (IsArrayConstructor(target, realmState) || IsArrayConstructor(newTarget, realmState))
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

        var realmGlobal = realmObject ?? realmState?.Engine?.GlobalObject;
        if (realmGlobal is not null &&
            realmGlobal.TryGetProperty(ctorName, out var ctorValue) &&
            TryGetPrototype(ctorValue, out prototype))
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
            "AsyncGeneratorFunction" => realmState.AsyncGeneratorFunctionPrototype,
            "SharedArrayBuffer" => realmState.SharedArrayBufferPrototype,
            "DataView" => realmState.DataViewPrototype,
            "Boolean" => realmState.BooleanPrototype,
            "Date" => realmState.DatePrototype,
            "Function" => realmState.FunctionPrototype,
            "GeneratorFunction" => realmState.GeneratorFunctionPrototype,
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
            "RelativeTimeFormat" or "DisplayNames" or "ListFormat" or "PluralRules" or "Segmenter";
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
            case JsProxy proxy:
                if (proxy.Handler is null)
                {
                    throw ThrowTypeError(
                        "Cannot perform operation on a revoked Proxy",
                        realm: RealmState.Current);
                }

                if (proxy.Target is IJsCallable callableTarget)
                {
                    return TryGetRealmInfo(callableTarget, out realmState, out realmObject);
                }

                realmState = null;
                realmObject = null;
                return false;
            case HostFunction hostFunction:
                if (hostFunction.IsBoundFunction && hostFunction.BoundTargetFunction is { } boundTarget)
                {
                    return TryGetRealmInfo(boundTarget, out realmState, out realmObject);
                }

                realmState = hostFunction.RealmState;
                realmObject = hostFunction.Realm;
                return realmState is not null || realmObject is not null;
            case ICallableMetadata metadata:
                realmState = metadata.RealmState;
                realmObject = null;
                return true;
            default:
                realmState = null;
                realmObject = null;
                return false;
        }
    }

    /// <summary>
    /// Returns true if the given HostFunction is a concrete typed array constructor
    /// (e.g. Int8Array, Float64Array) that manages its own prototype resolution.
    /// These constructors have their Properties prototype set to the %TypedArray% constructor's Properties.
    /// </summary>
    private static bool IsConcreteTypedArrayConstructor(HostFunction hostCtor, RealmState realm)
    {
        if (realm.TypedArrayConstructor is null)
        {
            return false;
        }

        return ReferenceEquals(hostCtor.PropertiesObject.Prototype, realm.TypedArrayConstructor.PropertiesObject);
    }

    private static bool IsArrayConstructor(IJsCallable candidate, RealmState realm)
    {
        if (realm.ArrayConstructor is not null && ReferenceEquals(candidate, realm.ArrayConstructor))
        {
            return true;
        }

        if (candidate is JsProxy { Target: IJsCallable proxyTarget })
        {
            return IsArrayConstructor(proxyTarget, realm);
        }

        if (!TryGetRealmInfo(candidate, out var candidateRealmState, out var candidateRealmObject))
        {
            return false;
        }

        if (candidateRealmState?.ArrayConstructor is not null &&
            ReferenceEquals(candidate, candidateRealmState.ArrayConstructor))
        {
            return true;
        }

        if (candidateRealmObject is not null &&
            candidateRealmObject.TryGetProperty("Array", out var realmArrayCtor) &&
            realmArrayCtor.TryGetObject<IJsCallable>(out var callableArrayCtor) &&
            ReferenceEquals(candidate, callableArrayCtor))
        {
            return true;
        }

        return false;
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
