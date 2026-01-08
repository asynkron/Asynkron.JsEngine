
namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
///     Base class for typed constructors that can be materialized into HostFunction instances.
///     Generated code wires the constructor to its prototype and calls <see cref="ConstructInstance" />.
/// </summary>
public abstract class JsConstructor(IJsObjectLike prototype, RealmState realm)
{
    protected IJsObjectLike Prototype { get; } = prototype ?? throw new ArgumentNullException(nameof(prototype));

    protected RealmState Realm { get; } = realm ?? throw new ArgumentNullException(nameof(realm));

    /// <summary>
    ///     Utility helper for derived classes to normalize the `this` value for constructor calls.
    ///     Unless <paramref name="assignPrototype" /> is false, the prototype passed to the constructor
    ///     will be installed on the resulting object.
    /// </summary>
    protected JsObject PrepareThisObject(JsValue thisValue, bool assignPrototype = true)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } existing)
        {
            if (assignPrototype && existing.Prototype is null && Prototype is not null)
            {
                existing.SetPrototype(Prototype);
            }

            return existing;
        }

        var instance = new JsObject();
        instance.RealmState = Realm;
        if (assignPrototype)
        {
            instance.SetPrototype(Prototype);
        }

        return instance;
    }

    /// <summary>
    ///     Builds a default instance when the constructor does not need to observe <c>thisValue</c>.
    ///     Derived constructors can call this to reuse the shared prototype wiring and realm initialization logic.
    /// </summary>
    protected JsValue CreateDefaultInstance()
    {
        var obj = PrepareThisObject(JsValue.Undefined, assignPrototype: false);
        if (obj.Prototype is null)
        {
            obj.SetPrototype(Prototype);
        }

        obj.RealmState ??= Realm;
        return new JsValue(obj);
    }

    protected abstract JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args);

    protected virtual void ConfigureConstructor(HostFunction constructor)
    {
    }

    /// <summary>
    /// Applies the prototype to the instance, resolving it from the target callable.
    /// </summary>
    protected void ApplyPrototype(JsObject instance, IJsCallable target)
    {
        if (instance.Prototype is not null)
        {
            return;
        }

        var proto = StdLib.ReflectHelper.ResolveConstructPrototype(target, target, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }
    }
}
