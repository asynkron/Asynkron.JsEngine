using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
///     Base class for typed constructors that can be materialized into HostFunction instances.
///     Generated code wires the constructor to its prototype and calls <see cref="ConstructInstance" />.
/// </summary>
public abstract class JsConstructor(JsObject prototype, RealmState realm)
{
    protected JsObject Prototype { get; } = prototype ?? throw new ArgumentNullException(nameof(prototype));

    protected RealmState Realm { get; } = realm ?? throw new ArgumentNullException(nameof(realm));

    /// <summary>
    ///     Utility helper for derived classes to normalize the `this` value for constructor calls.
    ///     Unless <paramref name="assignPrototype" /> is false, the prototype passed to the constructor
    ///     will be installed on the resulting object.
    /// </summary>
    protected JsObject PrepareThisObject(object? thisValue, bool assignPrototype = true)
    {
        if (thisValue is JsObject { IsConstructing: true } existing)
        {
            if (assignPrototype && existing.Prototype is null && Prototype is not null)
            {
                existing.SetPrototype(Prototype);
            }

            return existing;
        }

        var instance = new JsObject();
        if (assignPrototype)
        {
            instance.SetPrototype(Prototype);
        }

        return instance;
    }

    protected abstract object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args);

    protected virtual void ConfigureConstructor(HostFunction constructor)
    {
    }
}
