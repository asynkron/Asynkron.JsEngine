using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
///     Shared constructor workflow for constructors that must be invoked with <c>new</c> and
///     materialize a typed object with prototype resolution from <c>newTarget</c>.
/// </summary>
public abstract class ConstructingInstanceConstructorBase<TInstance>(
    IJsObjectLike prototype,
    RealmState realm,
    string constructorName)
    : JsConstructor(prototype, realm)
    where TInstance : IJsObjectLike
{
    private HostFunction? _constructor;

    protected string ConstructorName { get; } = constructorName ?? throw new ArgumentNullException(nameof(constructorName));

    protected HostFunction ConstructorFunction =>
        _constructor ?? throw new InvalidOperationException($"{ConstructorName} constructor not initialized");

    protected abstract TInstance CreateInstance();

    protected abstract void ConfigureRealmProperties(HostFunction constructor);

    protected abstract bool TryGetNewTargetCallable(JsValue newTarget, out IJsCallable callable);

    protected abstract JsValue BuildReturnValue(TInstance instance, IReadOnlyList<JsValue> args);

    protected sealed override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true })
        {
            return ConstructWithNewTarget(args, ConstructorFunction, ConstructorFunction);
        }

        throw ThrowTypeError($"Constructor {ConstructorName} requires 'new'", realm: Realm);
    }

    protected sealed override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        ConfigureRealmProperties(constructor);

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!TryGetNewTargetCallable(newTarget, out var callable))
            {
                throw ThrowTypeError($"Constructor {ConstructorName} requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return ConstructWithNewTarget(args, callable, target);
        });
    }

    private JsValue ConstructWithNewTarget(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = CreateInstance();
        instance.SetPrototype(proto);
        return BuildReturnValue(instance, args);
    }
}
