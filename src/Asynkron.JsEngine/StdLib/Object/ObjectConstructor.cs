#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.BigIntHelper;
using static Asynkron.JsEngine.StdLib.ObjectHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;
using static Asynkron.JsEngine.StdLib.SymbolHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Object", PrototypeType = typeof(ObjectPrototype), Length = 1d, DisplayName = "Object")]
public sealed partial class ObjectConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Object constructor not initialized");

    /// <summary>
    /// Common argument extraction for keys/values/entries methods.
    /// Throws TypeError for null/undefined, returns null for non-object primitives.
    /// </summary>
    private static IJsPropertyAccessor? GetObjectForEnumeration(
        IReadOnlyList<JsValue> args,
        RealmState? realm,
        out RealmState realmState)
    {
        realmState = RequireRealm(realm);
        var arg = args.GetArgument(0);

        if (arg.IsNullOrUndefined)
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
        }

        if (arg.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor;
        }

        return TryGetObject(arg, realmState, out var coerced) ? coerced : null;
    }

    private static IEnumerable<string> EnumerateEnumerableOwnStringKeys(IJsPropertyAccessor obj)
    {
        foreach (var key in obj.GetOwnPropertyKeysInOrder(includeSymbols: false, includeNonEnumerable: true))
        {
            var desc = obj.GetOwnPropertyDescriptor(key);
            if (desc is { Enumerable: true })
            {
                yield return key;
            }
        }
    }

    // Static methods registered via code generation

    [JsConstructorMethod("keys", Length = 1d)]
    private static JsValue Keys(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var obj = GetObjectForEnumeration(args, realm, out var realmState);
        if (obj is null)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        var keys = new JsArray(realmState);
        foreach (var key in EnumerateEnumerableOwnStringKeys(obj))
        {
            keys.Push(key);
        }

        return JsValue.FromJsArray(keys);
    }

    [JsConstructorMethod("values", Length = 1d)]
    private static JsValue Values(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var obj = GetObjectForEnumeration(args, realm, out var realmState);
        if (obj is null)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        var values = new JsArray(realmState);
        var receiver = JsValue.FromObjectUnsafe(obj);
        foreach (var key in EnumerateEnumerableOwnStringKeys(obj))
        {
            obj.TryGetProperty(key, receiver, out var value);
            values.Push(value);
        }

        return JsValue.FromJsArray(values);
    }

    [JsConstructorMethod("entries", Length = 1d)]
    private static JsValue Entries(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var obj = GetObjectForEnumeration(args, realm, out var realmState);
        if (obj is null)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        var entries = new JsArray(realmState);
        var receiver = JsValue.FromObjectUnsafe(obj);
        foreach (var key in EnumerateEnumerableOwnStringKeys(obj))
        {
            obj.TryGetProperty(key, receiver, out var value);
            var entry = new JsArray([key, value], realmState);
            entries.Push(entry);
        }

        return JsValue.FromJsArray(entries);
    }

    [JsConstructorMethod("assign", Length = 2d)]
    private static JsValue Assign(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var targetValue = args.GetArgument(0);

        // Object.assign throws on null/undefined targets but boxes primitives.
        if (!TryGetObject(targetValue, realmState, out var targetAccessor))
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
        }

        var targetJs = JsValue.FromObjectUnsafe(targetAccessor);
        for (var i = 1; i < args.Count; i++)
        {
            var sourceValue = args[i];
            if (!TryGetObject(sourceValue, realmState, out var sourceAccessor))
            {
                continue;
            }

            var sourceJs = JsValue.FromObjectUnsafe(sourceAccessor);
            foreach (var key in sourceAccessor.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
            {
                var descriptor = sourceAccessor.GetOwnPropertyDescriptor(key);
                if (descriptor?.Enumerable != true)
                {
                    continue;
                }

                if (sourceAccessor.TryGetProperty(key, sourceJs, out var value))
                {
                    // Per spec step 5.c.iii.3: Set(to, nextKey, propValue, true) - must throw on failure
                    SetPropertyOrThrow(targetAccessor, key, value, targetJs, realmState);
                }
            }
        }

        return targetJs;
    }

    [JsConstructorMethod("fromEntries", Length = 1d)]
    private static JsValue FromEntries(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var arg = args.GetArgument(0);

        // Per spec: Object.fromEntries throws TypeError for null/undefined
        if (arg.IsNullOrUndefined)
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
        }

        // Per spec: Let iteratorRecord be ? GetIterator(iterable, sync).
        var iteratorValue = GetIteratorObject(arg, realmState, "Object.fromEntries");
        if (!iteratorValue.TryGetObject<IJsPropertyAccessor>(out var iteratorAccessor))
        {
            throw ThrowTypeError("Object.fromEntries iterator must be an object", realm: realmState);
        }

        var iteratorReceiver = JsValue.FromObjectUnsafe(iteratorAccessor);
        if (!iteratorAccessor.TryGetProperty("next", iteratorReceiver, out var nextMethod) ||
            !nextMethod.TryGetObject<IJsCallable>(out var nextCallable))
        {
            throw ThrowTypeError("Object.fromEntries iterator must have a callable next method", realm: realmState);
        }

        var result = new JsObject(realmState.ObjectPrototype) { RealmState = realmState };

        while (true)
        {
            var nextResult = nextCallable.Invoke([], iteratorReceiver);
            if (!nextResult.TryGetObject<IJsPropertyAccessor>(out var resultAccessor))
            {
                throw ThrowTypeError("Object.fromEntries iterator result must be an object", realm: realmState);
            }

            var resultReceiver = JsValue.FromObjectUnsafe(resultAccessor);
            var done = resultAccessor.TryGetProperty("done", resultReceiver, out var doneValue) &&
                       JsOps.ToBoolean(doneValue);
            if (done)
            {
                break;
            }

            var entry = resultAccessor.TryGetProperty("value", resultReceiver, out var entryValue)
                ? entryValue
                : JsValue.Undefined;

            // Per spec step 4.d: If Type(nextItem) is not Object, then
            //   i. Let error be ThrowCompletion(a newly created TypeError object).
            //   ii. Return ? IteratorClose(iteratorRecord, error).
            if (!entry.IsObject)
            {
                IteratorClose(iteratorAccessor, realmState, "Object.fromEntries");
                throw ThrowTypeError("Iterator value is not an entry object", realm: realmState);
            }

            if (!entry.TryGetObject<IJsPropertyAccessor>(out var entryAccessor))
            {
                IteratorClose(iteratorAccessor, realmState, "Object.fromEntries");
                throw ThrowTypeError("Iterator value is not an entry object", realm: realmState);
            }

            var entryReceiver = JsValue.FromObjectUnsafe(entryAccessor);

            // Per spec step 4.e: Let k be Get(nextItem, "0").
            // Per spec step 4.f: If k is an abrupt completion, return ? IteratorClose(iteratorRecord, k).
            JsValue keyValue;
            try
            {
                keyValue = entryAccessor.TryGetProperty("0", entryReceiver, out var kv)
                    ? kv
                    : JsValue.Undefined;
            }
            catch (ThrowSignal)
            {
                IteratorClose(iteratorAccessor, realmState, "Object.fromEntries");
                throw;
            }

            // Per spec step 4.g: Let v be Get(nextItem, "1").
            // Per spec step 4.h: If v is an abrupt completion, return ? IteratorClose(iteratorRecord, v).
            JsValue value;
            try
            {
                value = entryAccessor.TryGetProperty("1", entryReceiver, out var v)
                    ? v
                    : JsValue.Undefined;
            }
            catch (ThrowSignal)
            {
                IteratorClose(iteratorAccessor, realmState, "Object.fromEntries");
                throw;
            }

            // Per spec step 4.i: Let propertyKey be ToPropertyKey(k).
            // Per spec step 4.j: If propertyKey is an abrupt completion, return ? IteratorClose(iteratorRecord, propertyKey).
            string key;
            try
            {
                key = JsOps.GetRequiredPropertyName(keyValue);
            }
            catch (ThrowSignal)
            {
                IteratorClose(iteratorAccessor, realmState, "Object.fromEntries");
                throw;
            }

            // Per spec step 4.k: Perform ! CreateDataPropertyOrThrow(obj, propertyKey, value).
            CreateDataPropertyOrThrowJsValue(result, key, value, realmState, "Object.fromEntries");
        }

        return JsValue.FromJsObject(result);
    }

    [JsConstructorMethod("hasOwn", Length = 2d)]
    private static JsValue HasOwn(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var targetValue = args.GetArgument(0);

        // Object.hasOwn follows ToObject, so null/undefined should throw here.
        if (!TryGetObject(targetValue, realmState, out var accessor))
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
        }

        var propName = JsOps.GetRequiredPropertyName(args.GetArgument(1));
        var hasOwn = accessor.GetOwnPropertyDescriptor(propName) is not null;
        return new JsValue(hasOwn);
    }

    [JsConstructorMethod("freeze", Length = 1d)]
    private static JsValue Freeze(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        var target = args[0].ObjectValue;
        if (target is ModuleNamespace)
        {
            throw ThrowTypeError("Cannot freeze module namespace", realm: realmState);
        }

        if (target is TypedArrayBase { Buffer.Resizable: true })
        {
            throw ThrowTypeError("Cannot freeze a typed array backed by a resizable ArrayBuffer", realm: realmState);
        }

        if (target is not IJsObjectLike objectLike)
        {
            // Per spec: If Type(O) is not Object, return O.
            return args[0];
        }

        // Per spec: Let status be ? SetIntegrityLevel(O, frozen).
        // If status is false, throw a TypeError exception.
        var status = SetIntegrityLevel(objectLike, freeze: true, realmState);
        if (!status)
        {
            throw ThrowTypeError("Cannot freeze object", realm: realmState);
        }

        return args[0];
    }

    [JsConstructorMethod("seal", Length = 1d)]
    private static JsValue Seal(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        var target = args[0].ObjectValue;
        if (target is not IJsObjectLike objectLike)
        {
            // Per spec: If Type(O) is not Object, return O.
            return args[0];
        }

        // Per spec: Let status be ? SetIntegrityLevel(O, sealed).
        // If status is false, throw a TypeError exception.
        var status = SetIntegrityLevel(objectLike, freeze: false, realmState);
        if (!status)
        {
            throw ThrowTypeError("Cannot seal object", realm: realmState);
        }

        return args[0];
    }

    [JsConstructorMethod("isFrozen", Length = 1d)]
    private static JsValue IsFrozen(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.True;
        }

        var target = args[0].ObjectValue;
        if (target is not IJsObjectLike objectLike)
        {
            // Per spec: If Type(O) is not Object, return true.
            return JsValue.True;
        }

        return new JsValue(TestIntegrityLevel(objectLike, frozen: true));
    }

    [JsConstructorMethod("isSealed", Length = 1d)]
    private static JsValue IsSealed(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.True;
        }

        var target = args[0].ObjectValue;
        if (target is not IJsObjectLike objectLike)
        {
            // Per spec: If Type(O) is not Object, return true.
            return JsValue.True;
        }

        return new JsValue(TestIntegrityLevel(objectLike, frozen: false));
    }

    [JsConstructorMethod("is", Length = 2d)]
    private static JsValue Is(IReadOnlyList<JsValue> args)
    {
        return new JsValue(JsOps.SameValue(args.GetArgument(0), args.GetArgument(1)));
    }

    [JsConstructorMethod("create", Length = 2d)]
    private static JsValue Create(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);

        if (args.Count == 0)
        {
            throw ThrowTypeError("Object prototype may only be an Object or null", realm: realmState);
        }

        var protoValue = args[0];
        IJsPropertyAccessor? protoAccessor = null;

        if (!protoValue.IsNull)
        {
            if (!protoValue.TryGetObjectLike(out var protoObjLike))
            {
                throw ThrowTypeError("Object prototype may only be an Object or null", realm: realmState);
            }
            protoAccessor = protoObjLike;
        }

        var obj = new JsObject { RealmState = realmState };

        // Set prototype (null is valid - creates an object with no prototype)
        if (protoValue.IsNull)
        {
            obj.SetPrototype(null);
        }
        else if (protoAccessor is not null)
        {
            obj.SetPrototype(protoAccessor);
        }

        // If Properties is not undefined, use ObjectDefineProperties (per spec step 4)
        if (args.Count > 1 && !args[1].IsUndefined)
        {
            var propsArg = args[1];
            if (!TryGetObject(propsArg, realmState, out var propsAccessor))
            {
                throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
            }

            // Per spec: ObjectDefineProperties(obj, Properties) uses own enumerable string-keyed properties
            foreach (var key in propsAccessor.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
            {
                var propDesc = propsAccessor.GetOwnPropertyDescriptor(key);
                if (propDesc?.Enumerable != true)
                {
                    continue;
                }

                if (!propsAccessor.TryGetProperty(key, out var descriptorValue))
                {
                    continue;
                }

                var descriptor = ToPropertyDescriptor(descriptorValue, realmState);
                TryDefinePropertyOnTarget(obj, key, descriptor, realmState, true);
            }
        }

        return JsValue.FromJsObject(obj);
    }

    [JsConstructorMethod("getOwnPropertyNames", Length = 1d)]
    private static JsValue GetOwnPropertyNames(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var targetValue = args.GetArgument(0);
        if (!TryGetObject(targetValue, realmState, out var obj))
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
        }

        var names = new JsArray(obj.GetOwnPropertyNames(), realmState);
        return JsValue.FromJsArray(names);
    }

    [JsConstructorMethod("getOwnPropertyDescriptor", Length = 2d)]
    private static JsValue GetOwnPropertyDescriptor(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var targetValue = args.GetArgument(0);
        if (targetValue.IsNullOrUndefined)
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
        }

        if (!TryGetObject(targetValue, realmState, out var obj))
        {
            return JsValue.Undefined;
        }

        var propName = JsOps.GetRequiredPropertyName(args.GetArgument(1));

        var desc = obj.GetOwnPropertyDescriptor(propName);
        if (desc is null)
        {
            return JsValue.Undefined;
        }

        var descriptorForResult = desc;
        if (string.Equals(propName, "name", StringComparison.Ordinal) && args[0].ObjectValue is IJsCallable)
        {
            descriptorForResult = desc.Clone();
            descriptorForResult.Configurable = true;
        }

        var result = FromPropertyDescriptor(descriptorForResult, realmState);
        return result is not null ? new JsValue(result) : JsValue.Undefined;
    }

    [JsConstructorMethod("getOwnPropertyDescriptors", Length = 1d)]
    private static JsValue GetOwnPropertyDescriptors(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !TryGetObject(args[0], realmState, out var obj))
        {
            throw ThrowTypeError("Object.getOwnPropertyDescriptors requires an object", realm: realmState);
        }

        var descriptors = new JsObject(realmState.ObjectPrototype) { RealmState = realmState };

        foreach (var key in obj.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
        {
            var descriptor = obj.GetOwnPropertyDescriptor(key);
            if (descriptor is null)
            {
                continue;
            }

            descriptors.SetProperty(key, (JsValue)(FromPropertyDescriptor(descriptor, realmState) ?? new JsObject()));
        }

        return JsValue.FromJsObject(descriptors);
    }

    [JsConstructorMethod("getPrototypeOf", Length = 1d)]
    private static JsValue GetPrototypeOf(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !TryGetObject(args[0], realmState, out var obj))
        {
            throw ThrowTypeError("Object.getPrototypeOf called on null or undefined", realm: realmState);
        }

        if (obj is ModuleNamespace)
        {
            return JsValue.Null;
        }

        if (obj is JsProxy proxy)
        {
            return JsValue.FromObjectUnsafe(proxy.GetPrototypeWithTrap());
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

        return proto is null ? JsValue.Null : JsValue.FromObjectUnsafe(proto);
    }

    [JsConstructorMethod("defineProperty", Length = 3d)]
    private static JsValue DefineProperty(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 3)
        {
            throw ThrowTypeError("Object.defineProperty requires a property descriptor", realm: realmState);
        }

        // Per spec: If Type(O) is not Object, throw a TypeError exception.
        // Must not box primitives - only accept actual objects.
        if (!args[0].IsObject || !args[0].TryGetObject<IJsObjectLike>(out var obj))
        {
            throw ThrowTypeError("Object.defineProperty called on non-object", realm: realmState);
        }

        var propName = JsOps.ToPropertyName(args[1]) ?? string.Empty;
        var descriptor = ToPropertyDescriptor(args[2], realmState);

        TryDefinePropertyOnTarget(obj, propName, descriptor, realmState, true);
        return JsValue.FromObjectUnsafe(obj);
    }

    [JsConstructorMethod("defineProperties", Length = 2d)]
    private static JsValue DefineProperties(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 2)
        {
            throw ThrowTypeError("Object.defineProperties requires both target and descriptors", realm: realmState);
        }

        // Per spec: If Type(O) is not Object, throw a TypeError exception.
        if (!args[0].IsObject || !args[0].TryGetObject<IJsObjectLike>(out var target))
        {
            throw ThrowTypeError("Object.defineProperties called on non-object", realm: realmState);
        }

        // Per spec: Let props be ? ToObject(Properties).
        var propsValue = args[1];
        if (!TryGetObject(propsValue, realmState, out var propsAccessor))
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
        }

        // Per spec: Let keys be ? props.[[OwnPropertyKeys]]().
        foreach (var key in propsAccessor.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
        {
            // Per spec: Let propDesc be ? props.[[GetOwnProperty]](nextKey).
            var propDesc = propsAccessor.GetOwnPropertyDescriptor(key);
            if (propDesc?.Enumerable != true)
            {
                continue;
            }

            if (!propsAccessor.TryGetProperty(key, out var descriptorValue))
            {
                continue;
            }

            var descriptor = ToPropertyDescriptor(descriptorValue, realmState);
            TryDefinePropertyOnTarget(target, key, descriptor, realmState, true);
        }

        return JsValue.FromObjectUnsafe(target);
    }

    [JsConstructorMethod("setPrototypeOf", Length = 2d)]
    private static JsValue SetPrototypeOf(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var targetValue = args.GetArgument(0);
        var protoValue = args.GetArgument(1);

        // Per spec: If Type(proto) is neither Object nor Null, throw a TypeError exception.
        IJsPropertyAccessor? protoAccessor = null;
        if (!protoValue.IsNull)
        {
            if (!protoValue.TryGetObjectLike(out var protoObjLike))
            {
                throw ThrowTypeError("Object prototype may only be an Object or null", realm: realmState);
            }
            protoAccessor = protoObjLike;
        }

        // Per spec step 4: If Type(O) is not Object, return O.
        var target = targetValue.ObjectValue;
        if (target is not IJsObjectLike targetObjectLike)
        {
            return targetValue;
        }

        // Per spec step 5: Let status be ? O.[[SetPrototypeOf]](proto).
        // Step 6: If status is false, throw a TypeError exception.
        switch (targetObjectLike)
        {
            case ModuleNamespace when protoAccessor is null:
                return JsValue.FromObjectUnsafe(target);
            case ModuleNamespace:
                throw ThrowTypeError("Cannot set prototype on module namespace", realm: realmState);
        }

        // For Proxy objects, SetPrototype calls the trap.
        // If the trap throws, the error propagates directly (not converted to TypeError).
        // Only if [[SetPrototypeOf]] returns false do we throw TypeError.
        try
        {
            targetObjectLike.SetPrototype(protoAccessor);
        }
        catch (ThrowSignal ex)
        {
            // Check if this is a "false return" signal from immutable prototype etc.
            // If the thrown value is undefined, it's a silent false-return indicator.
            // If it contains a real error (like from a Proxy trap), re-throw it.
            if (ex.ThrownValue.IsUndefined)
            {
                throw ThrowTypeError("Cannot set prototype of object", realm: realmState);
            }

            // If the thrown value is a TypeError from our engine about setPrototypeOf false,
            // wrap it. Otherwise re-throw the user's error directly.
            throw;
        }

        return JsValue.FromObjectUnsafe(target);
    }

    /// <summary>
    /// Checks if setting protoAccessor as the prototype of target would create a cycle.
    /// Per ES spec 10.4.7.2, we must check if target appears anywhere in the prototype chain of protoAccessor.
    /// </summary>
    private static bool WouldCreatePrototypeCycle(object target, IJsPropertyAccessor? protoAccessor)
    {
        if (protoAccessor is null)
        {
            return false;
        }

        // Walk the prototype chain of protoAccessor to see if target appears
        IJsPropertyAccessor? current = protoAccessor;
        for (var depth = 0; current is not null && depth < JsEngineConstants.MaxPrototypeChainDepth; depth++)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            // Get the prototype - need to handle different types
            current = current switch
            {
                JsObject obj => obj.Prototype,
                IJsObjectLike objLike => objLike.Prototype,
                _ => null
            };
        }

        return false;
    }

    [JsConstructorMethod("preventExtensions", Length = 1d)]
    private static JsValue PreventExtensions(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        // Per spec: If Type(O) is not Object, return O.
        if (!args[0].IsObject)
        {
            return args[0];
        }

        if (!TryGetObject(args[0], realmState, out var target))
        {
            return args[0];
        }

        // Per spec: Let status be ? O.[[PreventExtensions]]().
        // If status is false, throw a TypeError exception.
        if (!TryPreventExtensions(target, realmState))
        {
            throw ThrowTypeError("Cannot prevent extensions", realm: realmState);
        }

        return JsValue.FromObjectUnsafe(target);
    }

    [JsConstructorMethod("isExtensible", Length = 1d)]
    private static JsValue IsExtensible(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !TryGetObject(args[0], realmState, out var target))
        {
            return JsValue.False;
        }

        return new JsValue(IsTargetExtensible(target));
    }

    [JsConstructorMethod("getOwnPropertySymbols", Length = 1d)]
    private static JsValue GetOwnPropertySymbols(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var targetValue = args.GetArgument(0);
        if (!TryGetObject(targetValue, realmState, out var obj))
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
        }

        var symbols = new JsArray(realmState);
        if (obj is ModuleNamespace moduleNamespace)
        {
            foreach (var key in moduleNamespace.OwnKeys())
            {
                if (key is JsSymbol symbol)
                {
                    symbols.Push(symbol);
                }
            }

            return JsValue.FromJsArray(symbols);
        }

        foreach (var key in obj.GetOwnPropertyKeysInOrder())
        {
            if (JsSymbol.TryGetByInternalKey(key, out var symbol))
            {
                symbols.Push(symbol!);
            }
        }

        return JsValue.FromJsArray(symbols);
    }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var targetCtor = _constructor ?? ConstructFallback;
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
        {
            ApplyPrototype(constructing, targetCtor);
            return JsValue.FromObjectUnsafe(ConstructCore(args, targetCtor, constructing));
        }

        return JsValue.FromObjectUnsafe(ConstructCore(args, targetCtor, null));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.ObjectPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            var target = _constructor ?? constructor;
            if (newTarget.TryGetObject<IJsCallable>(out var newTargetCallable))
            {
                return JsValue.FromObjectUnsafe(ConstructCore(args, newTargetCallable, null));
            }

            return JsValue.FromObjectUnsafe(ConstructCore(args, target, null));
        });

        // Static methods are now registered via code generation from [JsConstructorMethod] attributes
        AttachPrototypeShortcut(constructor);
    }

    private object ConstructCore(IReadOnlyList<JsValue> args, IJsCallable newTarget, JsObject? existing)
    {
        var isSubclassConstruction = existing is not null
            ? !ReferenceEquals(existing.Prototype, Prototype)
            : !ReferenceEquals(newTarget, ConstructFallback);

        if (isSubclassConstruction)
        {
            return CreateBlank(newTarget, existing);
        }

        if (args.Count == 0 || args[0].IsUndefined || args[0].IsNull)
        {
            return CreateBlank(newTarget, existing);
        }

        var value = args[0];

        // Check if it's a TypedAstSymbol (stored in ObjectValue when Kind is Symbol)
        if (value is { IsSymbol: true, ObjectValue: JsSymbol typedSym })
        {
            return CreateSymbolWrapper(typedSym, realm: Realm);
        }

        if (value.TryGetBigInt(out var bigInt))
        {
            return CreateBigIntWrapper(bigInt, realm: Realm);
        }

        if (value.TryGetBoolean(out var boolValue))
        {
            return BooleanHelper.CreateBooleanWrapper(boolValue, realm: Realm);
        }

        if (value.TryGetString(out var strValue))
        {
            return StringHelper.CreateStringWrapper(strValue, realm: Realm);
        }

        if (value.TryGetDouble(out var numValue))
        {
            return NumberHelper.CreateNumberWrapper(numValue, realm: Realm);
        }

        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor;
        }

        return CreateBlank(newTarget, existing);
    }

    private JsObject CreateBlank(IJsCallable newTarget, JsObject? existing)
    {
        var targetCtor = _constructor ?? newTarget;
        var obj = existing ?? PrepareThisObject(JsValue.Undefined, false);
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        if (obj.Prototype is null)
        {
            obj.SetPrototype(proto);
        }

        obj.RealmState ??= Realm;
        return obj;
    }

    private void AttachPrototypeShortcut(HostFunction constructor)
    {
        if (Prototype.TryGetProperty("hasOwnProperty", out var hasOwn))
        {
            constructor.SetProperty("hasOwnProperty", hasOwn);
        }
    }

    [JsConstructorMethod("groupBy", Length = 2d)]
    private static JsValue GroupBy(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var items = args.GetArgument(0);
        var callbackFn = args.GetArgument(1);

        // Validate callback
        if (!callbackFn.TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("Object.groupBy callback must be a function", realm: realmState);
        }

        // Create result object
        var result = new JsObject { RealmState = realmState };

        // Group elements from any iterable.
        var index = 0;
        foreach (var element in EnumerateIteratorValues(items, realmState, "Object.groupBy"))
        {
            // Call callback with (element, index).
            var key = callback.Invoke([element, (double)index], JsValue.Undefined);

            // Convert key to a property key (string/symbol internal key).
            var propertyKey = JsOps.GetRequiredPropertyName(key);

            // Get or create array for this key.
            JsArray group;
            if (result.TryGetProperty(propertyKey, out var existingGroup) &&
                existingGroup.TryGetObject<JsArray>(out var existingArray))
            {
                group = existingArray;
            }
            else
            {
                group = new JsArray(realmState);
                result.SetProperty(propertyKey, JsValue.FromJsArray(group));
            }

            // Add element to group.
            group.SetElement((uint)group.Length, element);
            index++;
        }

        return JsValue.FromJsObject(result);
    }

    private static IEnumerable<JsValue> EnumerateIteratorValues(
        JsValue source,
        RealmState realm,
        string methodName)
    {
        var iteratorValue = GetIteratorObject(source, realm, methodName);
        if (!iteratorValue.TryGetObject<IJsPropertyAccessor>(out var iteratorAccessor))
        {
            throw ThrowTypeError($"{methodName} iterator must be an object", realm: realm);
        }

        var iteratorReceiver = JsValue.FromObjectUnsafe(iteratorAccessor);
        if (!iteratorAccessor.TryGetProperty("next", iteratorReceiver, out var nextMethod) ||
            !nextMethod.TryGetObject<IJsCallable>(out var nextCallable))
        {
            throw ThrowTypeError($"{methodName} iterator must have a callable next method", realm: realm);
        }

        while (true)
        {
            var nextResult = nextCallable.Invoke([], iteratorReceiver);
            if (!nextResult.TryGetObject<IJsPropertyAccessor>(out var resultAccessor))
            {
                throw ThrowTypeError($"{methodName} iterator result must be an object", realm: realm);
            }

            var resultReceiver = JsValue.FromObjectUnsafe(resultAccessor);
            var done = resultAccessor.TryGetProperty("done", resultReceiver, out var doneValue) &&
                       JsOps.ToBoolean(doneValue);
            if (done)
            {
                yield break;
            }

            if (resultAccessor.TryGetProperty("value", resultReceiver, out var value))
            {
                yield return value;
            }
            else
            {
                yield return JsValue.Undefined;
            }
        }
    }

    private static JsValue GetIteratorObject(JsValue source, RealmState realm, string methodName)
    {
        if (!TryGetObject(source, realm, out var accessor))
        {
            throw ThrowTypeError($"{methodName} requires an iterable object", realm: realm);
        }

        var receiver = JsValue.FromObjectUnsafe(accessor);
        // Iterator-like objects can be used directly if they expose a callable next.
        if (accessor.TryGetProperty("next", receiver, out var nextMethod) &&
            nextMethod.TryGetObject<IJsCallable>(out _))
        {
            return receiver;
        }

        // Otherwise, try Symbol.iterator to obtain the iterator.
        if (!accessor.TryGetProperty(SymbolKeys.Iterator, receiver, out var iteratorMethod) ||
            !iteratorMethod.TryGetObject<IJsCallable>(out var iteratorCallable))
        {
            throw ThrowTypeError($"{methodName} requires an iterable object", realm: realm);
        }

        var iterator = iteratorCallable.Invoke([], receiver);
        // Use TryGetObjectLike instead of TryGetObject to support iterator types like
        // JsArrayIterator and JsMapIterator that implement IJsObjectLike but are not JsObject
        if (!iterator.TryGetObjectLike(out _))
        {
            throw ThrowTypeError($"{methodName} Symbol.iterator must return an object", realm: realm);
        }

        return iterator;

    }
}
