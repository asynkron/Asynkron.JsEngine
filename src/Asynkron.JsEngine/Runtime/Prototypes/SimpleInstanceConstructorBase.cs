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
    : ConstructingInstanceConstructorBase<TInstance>(prototype, realm, constructorName)
    where TInstance : IJsObjectLike
{
    /// <summary>
    ///     Creates a new instance of the target type.
    /// </summary>
    protected abstract override TInstance CreateInstance();

    /// <summary>
    ///     Configures realm-specific properties (e.g., Realm.XxxConstructor, Realm.XxxPrototype).
    /// </summary>
    protected abstract override void ConfigureRealmProperties(HostFunction constructor);

    protected sealed override bool TryGetNewTargetCallable(JsValue newTarget, out IJsCallable callable)
    {
        if (newTarget.TryGetCallable(out var resolvedCallable) && resolvedCallable is not null)
        {
            callable = resolvedCallable;
            return true;
        }

        callable = default!;
        return false;
    }

    protected sealed override JsValue BuildReturnValue(TInstance instance, IReadOnlyList<JsValue> args)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return JsValue.FromObjectUnsafe(instance);
    }
}
