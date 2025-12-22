#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Set", PrototypeType = typeof(SetPrototype), Length = 0d, DisplayName = "Set")]
public sealed partial class SetConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Set constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
        {
            return JsValue.FromObjectUnsafe(ConstructSet(args, _constructor ?? ConstructFallback,
                _constructor ?? ConstructFallback, constructing));
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
            if (wrapper.Prototype is null)
            {
                wrapper.SetPrototype(proto);
            }

            wrapper.SetProperty("_internalSet", backing);
            receiver = wrapper;
        }

        PopulateSet(backing, args);
        return receiver;
    }

    private static void PopulateSet(JsSet set, IReadOnlyList<JsValue> args)
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
            set.Add(value);
        }
    }
}
