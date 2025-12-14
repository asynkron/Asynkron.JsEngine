using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("WeakRef", PrototypeType = typeof(WeakRefPrototype), Length = 1d, DisplayName = "WeakRef")]
public sealed partial class WeakRefConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var targetCtor = _constructor ?? ConstructFallback;
        if (thisValue.IsObject && thisValue.AsObject() is JsObject { IsConstructing: true } constructing)
        {
            var target = RequireTargetObject(args);
            ApplyPrototype(constructing, targetCtor);
            InitializeWeakRef(constructing, target);
            return new JsValue(constructing);
        }

        throw ThrowTypeError("Constructor WeakRef requires 'new'", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!newTarget.IsObject)
            {
                throw ThrowTypeError("Constructor WeakRef requires 'new'", realm: Realm);
            }

            var obj = newTarget.AsObject();
            if (obj is not IJsCallable callableNewTarget)
            {
                throw ThrowTypeError("Constructor WeakRef requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return ConstructWithNewTarget(args, callableNewTarget, target);
        });
    }

    private JsValue ConstructWithNewTarget(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var target = RequireTargetObject(args);
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = PrepareThisObject(JsValue.Undefined, assignPrototype: false);
        if (proto is not null && instance.Prototype is null)
        {
            instance.SetPrototype(proto);
        }

        InitializeWeakRef(instance, target);
        return new JsValue(instance);
    }

    private JsValue RequireTargetObject(IReadOnlyList<JsValue> args)
    {
        var target = args.GetArgument(0);
        if (!target.IsObject || target.AsObject() is not IJsObjectLike)
        {
            throw ThrowTypeError("WeakRef target must be an object", realm: Realm);
        }

        return target;
    }

    private void InitializeWeakRef(JsObject instance, JsValue target)
    {
        instance.SetProperty("_target", target);
        instance.RealmState ??= Realm;
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("WeakRef constructor not initialized");

    private void ApplyPrototype(JsObject instance, IJsCallable target)
    {
        if (instance.Prototype is not null)
        {
            return;
        }

        var proto = ResolveConstructPrototype(target, target, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }
    }
}
