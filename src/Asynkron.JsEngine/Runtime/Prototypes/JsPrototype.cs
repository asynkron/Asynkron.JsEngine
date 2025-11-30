using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
///     Base class for typed prototypes that are materialized via source generators.
///     Derived types get access to the underlying <see cref="JsObject" /> and the owning realm.
/// </summary>
public abstract class JsPrototype(JsObject prototype, RealmState realm)
{
    protected JsObject Prototype { get; } = prototype ?? throw new ArgumentNullException(nameof(prototype));

    protected RealmState Realm { get; } = realm ?? throw new ArgumentNullException(nameof(realm));

    /// <summary>
    ///     Optional hook for manual prototype customization that cannot be expressed via attributes.
    ///     The source generator calls this after all annotated members have been wired.
    /// </summary>
    protected virtual void ConfigurePrototype()
    {
    }
}
