using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("WeakMap", PrototypeType = typeof(WeakMapPrototype), Length = 0d, DisplayName = "WeakMap")]
public sealed partial class WeakMapConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true })
        {
            return JsValue.FromObjectUnsafe(ConstructWeakMap(args, _constructor ?? ConstructFallback));
        }

        throw StandardLibrary.ThrowTypeError("Constructor WeakMap requires 'new'", realm: Realm);
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
                throw StandardLibrary.ThrowTypeError("Constructor WeakMap requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return JsValue.FromObjectUnsafe(ConstructWeakMap(args, callable, target));
        });
    }

    private object ConstructWeakMap(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable? targetCtor = null)
    {
        var proto = StandardLibrary.ResolveConstructPrototype(newTarget, targetCtor ?? newTarget, Realm) ?? Prototype;
        var instance = new JsWeakMap();
        instance.SetPrototype(proto);
        PopulateWeakMap(instance, args);
        return instance;
    }

    private static void PopulateWeakMap(JsWeakMap map, IReadOnlyList<JsValue> args)
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
