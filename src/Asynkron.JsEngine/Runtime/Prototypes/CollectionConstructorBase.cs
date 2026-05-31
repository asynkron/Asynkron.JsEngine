using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
///     Base class for collection constructors that create instances and populate them from arguments.
///     Consolidates boilerplate for WeakMap, WeakSet, Map, Set constructors.
/// </summary>
/// <typeparam name="TInstance">The type of collection instance created by this constructor.</typeparam>
public abstract class CollectionConstructorBase<TInstance>(
    IJsObjectLike prototype,
    RealmState realm,
    string constructorName)
    : ConstructingInstanceConstructorBase<TInstance>(prototype, realm, constructorName)
    where TInstance : IJsObjectLike
{
    /// <summary>
    ///     Creates a new instance of the collection type.
    /// </summary>
    protected abstract override TInstance CreateInstance();

    /// <summary>
    ///     Populates the instance with data from the constructor arguments.
    /// </summary>
    protected abstract void PopulateInstance(TInstance instance, IReadOnlyList<JsValue> args);

    /// <summary>
    ///     Populates a collection instance from an iterable using a named adder method.
    /// </summary>
    protected void PopulateCollectionFromIterable(
        TInstance instance,
        IReadOnlyList<JsValue> args,
        string adderPropertyName,
        string adderNotCallableMessage,
        string operation,
        Action<IJsCallable, JsValue, JsValue> populateEntry)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return;
        }

        if (!instance.TryGetProperty(adderPropertyName, out var adderValue) ||
            !adderValue.TryGetObject<IJsCallable>(out var adder))
        {
            throw StandardLibrary.ThrowTypeError(adderNotCallableMessage, realm: Realm);
        }

        var instanceValue = JsValue.FromObjectUnsafe(instance);
        MapSetIterationHelper.Iterate(args[0], Realm, operation, entry => populateEntry(adder, entry, instanceValue));
    }

    /// <summary>
    ///     Configures realm-specific properties (e.g., Realm.XxxConstructor, Realm.XxxPrototype).
    /// </summary>
    protected abstract override void ConfigureRealmProperties(HostFunction constructor);

    protected sealed override bool TryGetNewTargetCallable(JsValue newTarget, out IJsCallable callable)
    {
        if (newTarget.TryGetObject<IJsCallable>(out var resolvedCallable) && resolvedCallable is not null)
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
        PopulateInstance(instance, args);
        return JsValue.FromObjectUnsafe(instance);
    }
}
