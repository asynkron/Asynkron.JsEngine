#region

using System;
using Asynkron.JsEngine.JsTypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

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
    ///     Creates a new instance of the collection type.
    /// </summary>
    protected abstract TInstance CreateInstance();

    /// <summary>
    ///     Populates the instance with data from the constructor arguments.
    /// </summary>
    protected abstract void PopulateInstance(TInstance instance, IReadOnlyList<JsValue> args);

    /// <summary>
    ///     Configures realm-specific properties (e.g., Realm.XxxConstructor, Realm.XxxPrototype).
    /// </summary>
    protected abstract void ConfigureRealmProperties(HostFunction constructor);

    protected sealed override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true })
        {
            return JsValue.FromObjectUnsafe(ConstructCollection(args, _constructor ?? ConstructFallback));
        }

        throw ThrowTypeError($"Constructor {ConstructorName} requires 'new'", realm: Realm);
    }

    protected sealed override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        ConfigureRealmProperties(constructor);

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!newTarget.TryGetObject<IJsCallable>(out var callable))
            {
                throw ThrowTypeError($"Constructor {ConstructorName} requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return JsValue.FromObjectUnsafe(ConstructCollection(args, callable, target));
        });
    }

    private object ConstructCollection(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable? targetCtor = null)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor ?? newTarget, Realm) ?? Prototype;
        var instance = CreateInstance();
        instance.SetPrototype(proto);
        PopulateInstance(instance, args);
        return instance;
    }
}
