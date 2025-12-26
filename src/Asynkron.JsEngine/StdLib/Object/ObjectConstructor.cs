#region

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
    // Static methods registered via code generation

    /* FLAKY */
    [JsConstructorMethod("keys", Length = 1d)]
    public static JsValue Keys(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        IJsPropertyAccessor? obj = null;
        if (!args[0].TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            if (TryGetObject(args[0], realmState, out var coerced))
            {
                obj = coerced;
            }
        }
        else
        {
            obj = accessor;
        }

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

    /* FLAKY */
    [JsConstructorMethod("values", Length = 1d)]
    public static JsValue Values(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        IJsPropertyAccessor? obj = null;
        if (!args[0].TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            if (TryGetObject(args[0], realmState, out var coerced))
            {
                obj = coerced;
            }
        }
        else
        {
            obj = accessor;
        }

        if (obj is null)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        var values = new JsArray(realmState);
        foreach (var key in obj.GetEnumerablePropertyNames())
        {
            if (obj.TryGetProperty(key, out var value))
            {
                values.Push(value);
            }
        }

        return JsValue.FromJsArray(values);
    }

    /* FLAKY */
    [JsConstructorMethod("entries", Length = 1d)]
    public static JsValue Entries(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        IJsPropertyAccessor? obj = null;
        if (!args[0].TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            if (TryGetObject(args[0], realmState, out var coerced))
            {
                obj = coerced;
            }
        }
        else
        {
            obj = accessor;
        }

        if (obj is null)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        var entries = new JsArray(realmState);
        foreach (var key in obj.GetEnumerablePropertyNames())
        {
            if (!obj.TryGetProperty(key, out var value))
            {
                continue;
            }

            var entry = new JsArray([key, value], realmState);
            entries.Push(entry);
        }

        return JsValue.FromJsArray(entries);
    }

    /* FLAKY */
    [JsConstructorMethod("assign", Length = 2d)]
    public static JsValue Assign(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetObject<IJsPropertyAccessor>(out var targetAccessor))
        {
            return args.GetArgument(0);
        }

        for (var i = 1; i < args.Count; i++)
        {
            if (!args[i].TryGetObject(out var source))
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

    /* FLAKY */
    [JsConstructorMethod("fromEntries", Length = 1d)]
    public static JsValue FromEntries(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !args[0].TryGetObject(out JsArray? entries))
        {
            return JsValue.FromJsObject(new JsObject(realmState.ObjectPrototype) { RealmState = realmState });
        }

        var result = new JsObject(realmState.ObjectPrototype) { RealmState = realmState };
        foreach (var entry in entries.Items)
        {
            if (!entry.TryGetObject<JsArray>(out var entryArray) || entryArray.Items.Count < 2)
            {
                continue;
            }

            var keyValue = entryArray.GetElement(0);
            var key = JsOps.ToJsString(keyValue);
            var value = entryArray.GetElement(1);
            result[key] = value;
        }

        return JsValue.FromJsObject(result);
    }

    /* FLAKY */
    [JsConstructorMethod("hasOwn", Length = 2d)]
    public static JsValue HasOwn(IReadOnlyList<JsValue> args)
    {
        if (args.Count < 2)
        {
            return JsValue.False;
        }

        var propName = JsOps.ToPropertyName(args[1]);
        if (propName is null)
        {
            return JsValue.False;
        }

        var hasOwn = args[0].ObjectValue switch
        {
            JsObject obj => obj.GetOwnPropertyDescriptor(propName) is not null,
            JsArray array => array.GetOwnPropertyDescriptor(propName) is not null,
            IJsObjectLike accessor => accessor.GetOwnPropertyDescriptor(propName) is not null,
            _ => false
        };
        return new JsValue(hasOwn);
    }

    /* FLAKY */
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

        if (target is not JsObject obj)
        {
            return args[0];
        }

        obj.Freeze();
        return JsValue.FromJsObject(obj);
    }

    /* FLAKY */
    [JsConstructorMethod("seal", Length = 1d)]
    public static JsValue Seal(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        if (args[0].ObjectValue is not JsObject obj)
        {
            return args[0];
        }

        obj.Seal();
        return JsValue.FromJsObject(obj);
    }

    /* FLAKY */
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

        if (target is not JsObject obj)
        {
            return JsValue.True;
        }

        return new JsValue(obj.IsFrozen);
    }

    /* FLAKY */
    [JsConstructorMethod("isSealed", Length = 1d)]
    public static JsValue IsSealed(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.True;
        }

        if (args[0].ObjectValue is not JsObject obj)
        {
            return JsValue.True;
        }

        return new JsValue(obj.IsSealed);
    }

    /* FLAKY */
    [JsConstructorMethod("is", Length = 2d)]
    public static JsValue Is(IReadOnlyList<JsValue> args)
    {
        return new JsValue(JsOps.SameValue(args.GetArgument(0), args.GetArgument(1)));
    }

    /* FLAKY */
    [JsConstructorMethod("create", Length = 2d)]
    public static JsValue Create(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        var obj = new JsObject { RealmState = realmState };
        if (args.Count > 0 && !args[0].IsNull && args[0].TryGetObject<IJsPropertyAccessor>(out var protoValue))
        {
            obj.SetPrototype(protoValue);
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

    /* FLAKY */
    [JsConstructorMethod("getOwnPropertyNames", Length = 1d)]
    public static JsValue GetOwnPropertyNames(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        IJsPropertyAccessor? obj = null;
        if (!args[0].TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            if (TryGetObject(args[0], realmState, out var coerced))
            {
                obj = coerced;
            }
        }
        else
        {
            obj = accessor;
        }

        if (obj is null)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        var names = new JsArray(obj.GetOwnPropertyNames(), realmState);
        return JsValue.FromJsArray(names);
    }

    /* FLAKY */
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

    /* FLAKY */
    [JsConstructorMethod("getOwnPropertyDescriptors", Length = 1d)]
    public static JsValue GetOwnPropertyDescriptors(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0 || !TryGetObject(args[0], realmState, out var obj))
        {
            throw ThrowTypeError("Object.getOwnPropertyDescriptors requires an object", realm: realmState);
        }

        var descriptors = new JsObject(realmState.ObjectPrototype) { RealmState = realmState };

        foreach (var key in obj.GetOwnPropertyNames())
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

    /* FLAKY */
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

    /* FLAKY */
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

    /* FLAKY */
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

    /* FLAKY */
    [JsConstructorMethod("setPrototypeOf", Length = 2d)]
    public static JsValue SetPrototypeOf(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count < 2)
        {
            return args.GetArgument(0);
        }

        var targetValue = args[0];
        var protoAccessor = args[1].IsNull ? null : args[1].ObjectValue as IJsPropertyAccessor;

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

    /* FLAKY */
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

    /* FLAKY */
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

    /* FLAKY */
    [JsConstructorMethod("getOwnPropertySymbols", Length = 1d)]
    public static JsValue GetOwnPropertySymbols(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var realmState = RequireRealm(realm);
        if (args.Count == 0)
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        if (!TryGetObject(args[0], realmState, out var obj))
        {
            return JsValue.FromJsArray(new JsArray(realmState));
        }

        var symbols = new JsArray(realmState);
        if (obj is ModuleNamespace moduleNamespace)
        {
            foreach (var key in moduleNamespace.OwnKeys())
            {
                if (key is TypedAstSymbol symbol)
                {
                    symbols.Push(symbol);
                }
            }

            return JsValue.FromJsArray(symbols);
        }

        foreach (var key in obj.GetOwnPropertyKeysInOrder(true, true))
        {
            if (TypedAstSymbol.TryGetByInternalKey(key, out var symbol))
            {
                symbols.Push(symbol);
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
        if (value is { IsSymbol: true, ObjectValue: TypedAstSymbol typedSym })
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

    /* FLAKY */
    [JsConstructorMethod("groupBy", Length = 2d)]
    public static JsValue GroupBy(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        // TODO: Implement Object.groupBy
        // Groups array elements by the result of a callback function
        throw new NotImplementedException("Object.groupBy is not yet implemented");
    }
}
