using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
///     Base class for typed prototypes that are materialized via source generators.
///     Derived types get access to the underlying <see cref="JsObject" /> and the owning realm.
/// </summary>
public abstract class JsPrototype
{
    protected JsPrototype(JsObject prototype, RealmState realm)
    {
        Prototype = prototype ?? throw new ArgumentNullException(nameof(prototype));
        Realm = realm ?? throw new ArgumentNullException(nameof(realm));
    }

    protected JsObject Prototype { get; }

    protected RealmState Realm { get; }

    /// <summary>
    ///     Optional hook for manual prototype customization that cannot be expressed via attributes.
    ///     The source generator calls this after all annotated members have been wired.
    /// </summary>
    protected virtual void ConfigurePrototype()
    {
    }
}
