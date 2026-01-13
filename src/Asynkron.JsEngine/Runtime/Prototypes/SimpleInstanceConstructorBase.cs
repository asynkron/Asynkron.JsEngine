#region

using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
///     Base class for simple constructors that create instances without processing arguments.
///     Consolidates boilerplate for DisposableStack, AsyncDisposableStack, and similar constructors.
/// </summary>
/// <typeparam name="TInstance">The type of instance created by this constructor.</typeparam>
public abstract class SimpleInstanceConstructorBase<TInstance>(
    IJsObjectLike prototype,
    RealmState realm,
    string constructorName)
    : JsConstructor(prototype, realm)
    where TInstance : IJsObjectLike
{
    private HostFunction? _constructor;

    /// <summary>
    ///     Gets the constructor name for error messages.
    /// </summary>
    protected string ConstructorName { get; } = constructorName;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException($"{ConstructorName} constructor not initialized");

    /// <summary>
    ///     Creates a new instance of the target type.
    /// </summary>
    protected abstract TInstance CreateInstance();

    /// <summary>
    ///     Configures realm-specific properties (e.g., Realm.XxxConstructor, Realm.XxxPrototype).
    /// </summary>
    protected abstract void ConfigureRealmProperties(HostFunction constructor);

    protected sealed override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true })
        {
            var target = _constructor ?? ConstructFallback;
            return JsValue.FromObjectUnsafe(ConstructInstanceInternal(target, target));
        }

        throw ThrowTypeError($"Constructor {ConstructorName} requires 'new'", realm: Realm);
    }

    protected sealed override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        ConfigureRealmProperties(constructor);

        constructor.SetInvokeWithContext((_, _, _, newTarget) =>
        {
            if (!newTarget.TryGetCallable(out var callable))
            {
                throw ThrowTypeError($"Constructor {ConstructorName} requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return JsValue.FromObjectUnsafe(ConstructInstanceInternal(callable, target));
        });
    }

    private object ConstructInstanceInternal(IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = CreateInstance();

        instance.SetPrototype(proto);

        return instance;
    }
}
