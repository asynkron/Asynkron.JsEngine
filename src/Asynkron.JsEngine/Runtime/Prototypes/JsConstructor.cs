using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
///     Base class for typed constructors that can be materialized into HostFunction instances.
///     Generated code wires the constructor to its prototype and calls <see cref="ConstructInstance" />.
/// </summary>
public abstract class JsConstructor
{
    protected JsConstructor(JsObject prototype, RealmState realm)
    {
        Prototype = prototype ?? throw new ArgumentNullException(nameof(prototype));
        Realm = realm ?? throw new ArgumentNullException(nameof(realm));
    }

    protected JsObject Prototype { get; }

    protected RealmState Realm { get; }

    /// <summary>
    ///     Utility helper for derived classes to normalize the `this` value for constructor calls.
    ///     Unless <paramref name="assignPrototype" /> is false, the prototype passed to the constructor
    ///     will be installed on the resulting object.
    /// </summary>
    protected JsObject PrepareThisObject(object? thisValue, bool assignPrototype = true)
    {
        var instance = thisValue as JsObject ?? new JsObject();
        if (assignPrototype)
        {
            instance.SetPrototype(Prototype);
        }

        return instance;
    }

    protected abstract JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args);

    protected virtual void ConfigureConstructor(HostFunction constructor)
    {
    }
}
