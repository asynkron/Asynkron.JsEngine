#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Boolean", PrototypeType = typeof(BooleanPrototype), Length = 1d, DisplayName = "Boolean")]
public sealed partial class BooleanConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Boolean constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!thisValue.IsObject || thisValue.AsObject() is not { IsConstructing: true } constructing)
        {
            return new JsValue(JsOps.ToBoolean(args.GetArgument(0)));
        }

        ApplyPrototype(constructing, _constructor ?? ConstructFallback);
        InitializeBooleanWrapper(constructing, args);
        return new JsValue(constructing);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.BooleanPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                return JsOps.ToBoolean(args.GetArgument(0));
            }

            var target = _constructor ?? constructor;
            var newTargetCallable = newTarget.TryGetObject<IJsCallable>(out var callable) ? callable : target;
            return JsValue.FromObjectUnsafe(ConstructWithNewTarget(args, newTargetCallable));
        });
    }

    private object ConstructWithNewTarget(IReadOnlyList<JsValue> args, IJsCallable newTarget)
    {
        var targetCtor = _constructor ?? newTarget;
        var resolvedProto =
            ResolveConstructPrototype(newTarget, targetCtor, Realm) ??
            Prototype;

        var instance = PrepareThisObject(JsValue.Undefined, false);
        if (instance.Prototype is null)
        {
            instance.SetPrototype(resolvedProto);
        }

        InitializeBooleanWrapper(instance, args);
        return instance;
    }

    private static void InitializeBooleanWrapper(JsObject wrapper, IReadOnlyList<JsValue> args)
    {
        var value = JsOps.ToBoolean(args.GetArgument(0));
        wrapper.SetProperty("__value__", value);
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
}
