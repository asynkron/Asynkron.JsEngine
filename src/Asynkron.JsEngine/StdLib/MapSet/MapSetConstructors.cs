using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Map", PrototypeType = typeof(MapPrototype), Length = 0d, DisplayName = "Map")]
public sealed partial class MapConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is JsObject { IsConstructing: true } constructing)
        {
            return JsValue.FromObjectUnsafe(ConstructMap(args, _constructor ?? ConstructFallback, _constructor ?? ConstructFallback, constructing));
        }

        throw ThrowTypeError("Constructor Map requires 'new'", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.MapConstructor ??= constructor;
        Realm.MapPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!newTarget.TryGetObject<IJsCallable>(out var callable))
            {
                throw ThrowTypeError("Constructor Map requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return JsValue.FromObjectUnsafe(ConstructMap(args, callable, target));
        });
    }

    private object ConstructMap(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor,
        JsObject? providedThis = null)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var backing = new JsMap();
        object receiver;

        if (ReferenceEquals(newTarget, targetCtor))
        {
            backing.SetPrototype(proto);
            receiver = backing;
        }
        else
        {
            var wrapper = providedThis ?? new JsObject { RealmState = Realm };
            if (wrapper.Prototype is null && proto is not null)
            {
                wrapper.SetPrototype(proto);
            }

            wrapper.SetProperty("_internalMap", backing);
            receiver = wrapper;
        }

        PopulateMap(backing, args);
        return receiver;
    }

    private void PopulateMap(JsMap map, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return;
        }

        if (!args[0].TryGetObject<JsArray>(out var entries))
        {
            return;
        }

        foreach (var entry in entries.Items)
        {
            JsValue key;
            JsValue value;
            if (entry.TryGetObject<JsArray>(out var pair))
            {
                key = pair.GetElement(0);
                value = pair.GetElement(1);
            }
            else if (entry.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                accessor.TryGetProperty("0", out key);
                accessor.TryGetProperty("1", out value);
            }
            else
            {
                continue;
            }

            map.Set(key.ToObject(), value.ToObject());
        }
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Map constructor not initialized");
}

[JsConstructor("Set", PrototypeType = typeof(SetPrototype), Length = 0d, DisplayName = "Set")]
public sealed partial class SetConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is JsObject { IsConstructing: true } constructing)
        {
            return JsValue.FromObjectUnsafe(ConstructSet(args, _constructor ?? ConstructFallback, _constructor ?? ConstructFallback, constructing));
        }

        throw ThrowTypeError("Constructor Set requires 'new'", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.SetConstructor ??= constructor;
        Realm.SetPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!newTarget.TryGetObject<IJsCallable>(out var callable))
            {
                throw ThrowTypeError("Constructor Set requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return JsValue.FromObjectUnsafe(ConstructSet(args, callable, target));
        });
    }

    private object ConstructSet(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor,
        JsObject? providedThis = null)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var backing = new JsSet();
        object receiver;

        if (ReferenceEquals(newTarget, targetCtor))
        {
            backing.SetPrototype(proto);
            receiver = backing;
        }
        else
        {
            var wrapper = providedThis ?? new JsObject { RealmState = Realm };
            if (wrapper.Prototype is null && proto is not null)
            {
                wrapper.SetPrototype(proto);
            }

            wrapper.SetProperty("_internalSet", backing);
            receiver = wrapper;
        }

        PopulateSet(backing, args);
        return receiver;
    }

    private void PopulateSet(JsSet set, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return;
        }

        if (!args[0].TryGetObject<JsArray>(out var values))
        {
            return;
        }

        foreach (var value in values.Items)
        {
            set.Add(value.ToObject());
        }
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Set constructor not initialized");
}

[JsConstructor("WeakMap", PrototypeType = typeof(WeakMapPrototype), Length = 0d, DisplayName = "WeakMap")]
public sealed partial class WeakMapConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is JsObject { IsConstructing: true })
        {
            return JsValue.FromObjectUnsafe(ConstructWeakMap(args, _constructor ?? ConstructFallback));
        }

        throw ThrowTypeError("Constructor WeakMap requires 'new'", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.WeakMapConstructor ??= constructor;
        Realm.WeakMapPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!newTarget.TryGetObject<IJsCallable>(out var callable))
            {
                throw ThrowTypeError("Constructor WeakMap requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return JsValue.FromObjectUnsafe(ConstructWeakMap(args, callable, target));
        });
    }

    private object ConstructWeakMap(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable? targetCtor = null)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor ?? newTarget, Realm) ?? Prototype;
        var instance = new JsWeakMap();
        instance.SetPrototype(proto);
        PopulateWeakMap(instance, args);
        return instance;
    }

    private void PopulateWeakMap(JsWeakMap map, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return;
        }

        if (!args[0].TryGetObject<JsArray>(out var entries))
        {
            return;
        }

        foreach (var entry in entries.Items)
        {
            if (!entry.TryGetObject<JsArray>(out var pair) || pair.Items.Count < 2)
            {
                continue;
            }

            try
            {
                map.Set(pair.GetElement(0), pair.GetElement(1));
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("WeakMap constructor not initialized");
}

[JsConstructor("WeakSet", PrototypeType = typeof(WeakSetPrototype), Length = 0d, DisplayName = "WeakSet")]
public sealed partial class WeakSetConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is JsObject { IsConstructing: true })
        {
            return JsValue.FromObjectUnsafe(ConstructWeakSet(args, _constructor ?? ConstructFallback));
        }

        throw ThrowTypeError("Constructor WeakSet requires 'new'", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.WeakSetConstructor ??= constructor;
        Realm.WeakSetPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!newTarget.TryGetObject<IJsCallable>(out var callable))
            {
                throw ThrowTypeError("Constructor WeakSet requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return JsValue.FromObjectUnsafe(ConstructWeakSet(args, callable, target));
        });
    }

    private object ConstructWeakSet(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable? targetCtor = null)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor ?? newTarget, Realm) ?? Prototype;
        var instance = new JsWeakSet();
        instance.SetPrototype(proto);
        PopulateWeakSet(instance, args);
        return instance;
    }

    private void PopulateWeakSet(JsWeakSet set, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return;
        }

        if (!args[0].TryGetObject<JsArray>(out var values))
        {
            return;
        }

        foreach (var value in values.Items)
        {
            try
            {
                // Handle case where value is already a boxed JsValue
                var jsVal = value is JsValue jv ? jv : value;
                set.Add(jsVal);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("WeakSet constructor not initialized");
}
