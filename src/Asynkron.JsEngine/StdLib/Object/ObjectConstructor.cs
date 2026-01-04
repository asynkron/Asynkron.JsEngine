#region

using System;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
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

    // Static methods registered via code generation

    [JsConstructorMethod("keys", Length = 1d)]
    public static JsValue Keys(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var obj = GetObjectForEnumeration(args, realm, out var realmState);
        if (obj is null)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
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

        return JsValue.FromJsArray(keys);
    }

    [JsConstructorMethod("values", Length = 1d)]
    public static JsValue Values(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var obj = GetObjectForEnumeration(args, realm, out var realmState);
        if (obj is null)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        var values = new JsArray(realmState);
        var receiver = JsValue.FromObjectUnsafe(obj);
        foreach (var key in obj.GetEnumerablePropertyNames())
        {
            if (obj.TryGetProperty(key, receiver, out var value))
            {
                values.Push(value);
            }
        }

        return JsValue.FromJsArray(values);
    }

    [JsConstructorMethod("entries", Length = 1d)]
    public static JsValue Entries(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var obj = GetObjectForEnumeration(args, realm, out var realmState);
        if (obj is null)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        var entries = new JsArray(realmState);
        var receiver = JsValue.FromObjectUnsafe(obj);
        foreach (var key in obj.GetEnumerablePropertyNames())
        {
            if (!obj.TryGetProperty(key, receiver, out var value))
            {
                continue;
            }

            var entry = new JsArray([key, value], realmState);
            entries.Push(entry);
        }

        return JsValue.FromJsArray(entries);
    }

    [JsConstructorMethod("assign", Length = 2d)]
    public static JsValue Assign(IReadOnlyList<JsValue> args, RealmState? realm)
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
                if (descriptor is null || descriptor.Enumerable != true)
                {
                    continue;
                }

                if (sourceAccessor.TryGetProperty(key, sourceJs, out var value))
                {
                    targetAccessor.SetProperty(key, value, targetJs);
                }
            }
        }

        return targetJs;
    }

    [JsConstructorMethod("fromEntries", Length = 1d)]
    public static JsValue FromEntries(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var arg = args.GetArgument(0);

        // Per spec: Object.fromEntries throws TypeError for null/undefined
        if (arg.IsNullOrUndefined)
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
        }

        var result = new JsObject(realmState.ObjectPrototype) { RealmState = realmState };
        foreach (var entry in EnumerateIteratorValues(arg, realmState, "Object.fromEntries"))
        {
            // Each entry should be an object (typically [key, value]).
            if (!TryGetObject(entry, realmState, out var entryAccessor))
            {
                throw ThrowTypeError("Iterator value is not an entry object", realm: realmState);
            }

            if (!entryAccessor.TryGetProperty("0", JsValue.FromObjectUnsafe(entryAccessor), out var keyValue))
            {
                keyValue = JsValue.Undefined;
            }

            if (!entryAccessor.TryGetProperty("1", JsValue.FromObjectUnsafe(entryAccessor), out var value))
            {
                value = JsValue.Undefined;
            }

            var key = JsOps.GetRequiredPropertyName(keyValue);
            result[key] = value;
        }

        return JsValue.FromJsObject(result);
    }

    [JsConstructorMethod("hasOwn", Length = 2d)]
    public static JsValue HasOwn(IReadOnlyList<JsValue> args, RealmState? realm)
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
    public static JsValue Freeze(IReadOnlyList<JsValue> args, RealmState? realm)
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

        switch (target)
        {
            case JsArray array:
                array.Freeze();
                return JsValue.FromJsArray(array);
            case JsObject obj:
                obj.Freeze();
                return JsValue.FromJsObject(obj);
            default:
                return args[0];
        }
    }

    [JsConstructorMethod("seal", Length = 1d)]
    public static JsValue Seal(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        var target = args[0].ObjectValue;
        switch (target)
        {
            case JsArray array:
                array.Seal();
                return JsValue.FromJsArray(array);
            case JsObject obj:
                obj.Seal();
                return JsValue.FromJsObject(obj);
            default:
                return args[0];
        }
    }

    [JsConstructorMethod("isFrozen", Length = 1d)]
    public static JsValue IsFrozen(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.True;
        }

        var target = args[0].ObjectValue;
        if (target is ModuleNamespace)
        {
            return JsValue.False;
        }

        if (target is not IJsObjectLike objectLike)
        {
            return JsValue.True;
        }

        return new JsValue(objectLike.IsFrozen);
    }

    [JsConstructorMethod("isSealed", Length = 1d)]
    public static JsValue IsSealed(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.True;
        }

        if (args[0].ObjectValue is not IJsObjectLike objectLike)
        {
            return JsValue.True;
        }

        return new JsValue(objectLike.IsSealed);
    }

    [JsConstructorMethod("is", Length = 2d)]
    public static JsValue Is(IReadOnlyList<JsValue> args)
    {
        return new JsValue(JsOps.SameValue(args.GetArgument(0), args.GetArgument(1)));
    }

    [JsConstructorMethod("create", Length = 2d)]
    public static JsValue Create(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var obj = new JsObject { RealmState = realmState };

        if (args.Count > 0)
        {
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

            if (!protoValue.IsNull || protoAccessor is not null)
            {
                obj.SetPrototype(protoAccessor);
            }
        }

        if (args.Count <= 1 || !args[1].TryGetObject(out var propsObj))
        {
            return JsValue.FromJsObject(obj);
        }

        foreach (var propName in propsObj.GetOwnPropertyNames())
        {
            if (!propsObj.TryGetProperty(propName, out var descriptorValue))
            {
                continue;
            }

            var descriptor = ToPropertyDescriptor(descriptorValue, realmState);
            TryDefinePropertyOnTarget(obj, propName, descriptor, realmState, true);
        }

        return JsValue.FromJsObject(obj);
    }

    [JsConstructorMethod("getOwnPropertyNames", Length = 1d)]
    public static JsValue GetOwnPropertyNames(IReadOnlyList<JsValue> args, RealmState? realm)
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
    public static JsValue GetOwnPropertyDescriptor(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 2 || !TryGetObject(args[0], realmState, out var obj))
        {
            return JsValue.Undefined;
        }

        var propName = JsOps.GetRequiredPropertyName(args[1]);

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
    public static JsValue GetOwnPropertyDescriptors(IReadOnlyList<JsValue> args, RealmState? realm)
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
    public static JsValue GetPrototypeOf(IReadOnlyList<JsValue> args, RealmState? realm)
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
    public static JsValue DefineProperty(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 3)
        {
            throw ThrowTypeError("Object.defineProperty requires a property descriptor", realm: realmState);
        }

        if (!TryGetObject(args[0], realmState, out var obj))
        {
            throw ThrowTypeError("Object.defineProperty called on non-object", realm: realmState);
        }

        var propName = JsOps.ToPropertyName(args[1]) ?? string.Empty;
        var descriptor = ToPropertyDescriptor(args[2], realmState);

        TryDefinePropertyOnTarget(obj, propName, descriptor, realmState, true);
        return JsValue.FromObjectUnsafe(obj);
    }

    [JsConstructorMethod("defineProperties", Length = 2d)]
    public static JsValue DefineProperties(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 2)
        {
            throw ThrowTypeError("Object.defineProperties requires both target and descriptors", realm: realmState);
        }

        if (!TryGetObject(args[0], realmState, out var target))
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

        return JsValue.FromObjectUnsafe(target);
    }

    [JsConstructorMethod("setPrototypeOf", Length = 2d)]
    public static JsValue SetPrototypeOf(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var targetValue = args.GetArgument(0);
        var protoValue = args.GetArgument(1);

        // Per spec: If Type(proto) is neither Object nor Null, throw a TypeError exception.
        // We check for null explicitly and then check if it's an object
        IJsPropertyAccessor? protoAccessor = null;
        if (!protoValue.IsNull)
        {
            if (!protoValue.TryGetObjectLike(out var protoObjLike))
            {
                throw ThrowTypeError("Object prototype may only be an Object or null", realm: realmState);
            }
            protoAccessor = protoObjLike;
        }

        var target = targetValue.ObjectValue;
        switch (target)
        {
            case ModuleNamespace when protoAccessor is null:
                return JsValue.FromObjectUnsafe(target);
            case ModuleNamespace:
                throw ThrowTypeError("Cannot set prototype on module namespace", realm: realmState);
            case JsArray array:
                array.SetPrototype(protoAccessor);
                break;
            case JsObject obj:
                obj.SetPrototype(protoAccessor);
                break;
            case IJsObjectLike objectLike:
                objectLike.SetPrototype(protoAccessor);
                break;
        }

        return target is null ? JsValue.Undefined : JsValue.FromObjectUnsafe(target);
    }

    [JsConstructorMethod("preventExtensions", Length = 1d)]
    public static JsValue PreventExtensions(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !TryGetObject(args[0], realmState, out var target))
        {
            throw ThrowTypeError("Object.preventExtensions requires an object", realm: realmState);
        }

        PreventExtensionsOnTarget(target);
        return JsValue.FromObjectUnsafe(target);
    }

    [JsConstructorMethod("isExtensible", Length = 1d)]
    public static JsValue IsExtensible(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !TryGetObject(args[0], realmState, out var target))
        {
            return JsValue.False;
        }

        return new JsValue(IsTargetExtensible(target));
    }

    [JsConstructorMethod("getOwnPropertySymbols", Length = 1d)]
    public static JsValue GetOwnPropertySymbols(IReadOnlyList<JsValue> args, RealmState? realm)
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

        foreach (var key in obj.GetOwnPropertyKeysInOrder(true, true))
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

    private void ApplyPrototype(JsObject instance, IJsCallable target)
    {
        if (instance.Prototype is not null)
        {
            return;
        }

        var proto = ResolveConstructPrototype(target, target, Realm) ?? Prototype;
        instance.SetPrototype(proto);
    }

    private void AttachPrototypeShortcut(HostFunction constructor)
    {
        if (Prototype.TryGetProperty("hasOwnProperty", out var hasOwn))
        {
            constructor.SetProperty("hasOwnProperty", hasOwn);
        }
    }

    [JsConstructorMethod("groupBy", Length = 2d)]
    public static JsValue GroupBy(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var items = args.GetArgument(0);
        var callbackFn = args.GetArgument(1);

        // Validate callback
        if (!callbackFn.TryGetObject<IJsCallable>(out var callback) || callback is null)
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
                existingGroup.TryGetObject<JsArray>(out var existingArray) &&
                existingArray is not null)
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
            !nextMethod.TryGetObject<IJsCallable>(out var nextCallable) ||
            nextCallable is null)
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
        if (accessor.TryGetProperty(SymbolKeys.Iterator, receiver, out var iteratorMethod) &&
            iteratorMethod.TryGetObject<IJsCallable>(out var iteratorCallable) &&
            iteratorCallable is not null)
        {
            var iterator = iteratorCallable.Invoke([], receiver);
            // Use TryGetObjectLike instead of TryGetObject to support iterator types like
            // JsArrayIterator and JsMapIterator that implement IJsObjectLike but are not JsObject
            if (!iterator.TryGetObjectLike(out var iteratorObj) || iteratorObj is null)
            {
                throw ThrowTypeError($"{methodName} Symbol.iterator must return an object", realm: realm);
            }

            return iterator;
        }

        throw ThrowTypeError($"{methodName} requires an iterable object", realm: realm);
    }
}
