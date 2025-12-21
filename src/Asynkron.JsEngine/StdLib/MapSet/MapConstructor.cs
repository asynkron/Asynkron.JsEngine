using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Map", PrototypeType = typeof(MapPrototype), Length = 0d, DisplayName = "Map")]
public sealed partial class MapConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
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
            if (wrapper.Prototype is null)
            {
                wrapper.SetPrototype(proto);
            }

            wrapper.SetProperty("_internalMap", backing);
            receiver = wrapper;
        }

        PopulateMap(backing, args);
        return receiver;
    }

    private static void PopulateMap(JsMap map, IReadOnlyList<JsValue> args)
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

            map.Set(key, value);
        }
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Map constructor not initialized");
}
