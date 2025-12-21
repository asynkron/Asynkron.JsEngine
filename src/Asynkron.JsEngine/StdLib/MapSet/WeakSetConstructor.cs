using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("WeakSet", PrototypeType = typeof(WeakSetPrototype), Length = 0d, DisplayName = "WeakSet")]
public sealed partial class WeakSetConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true })
        {
            return JsValue.FromObjectUnsafe(ConstructWeakSet(args, _constructor ?? ConstructFallback));
        }

        throw StandardLibrary.ThrowTypeError("Constructor WeakSet requires 'new'", realm: Realm);
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
                throw StandardLibrary.ThrowTypeError("Constructor WeakSet requires 'new'", realm: Realm);
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

    private static void PopulateWeakSet(JsWeakSet set, IReadOnlyList<JsValue> args)
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
                set.Add(value);
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
