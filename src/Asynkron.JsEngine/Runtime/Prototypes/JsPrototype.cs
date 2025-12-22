#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Runtime.Prototypes;

/// <summary>
///     Base class for typed prototypes that are materialized via source generators.
///     Derived types get access to the underlying <see cref="IJsObjectLike" /> and the owning realm.
/// </summary>
public abstract class JsPrototype
{
    protected JsPrototype(IJsObjectLike prototype, RealmState realm)
    {
        Prototype = prototype ?? throw new ArgumentNullException(nameof(prototype));
        Realm = realm ?? throw new ArgumentNullException(nameof(realm));

        if (Prototype.Prototype is null && Realm.ObjectPrototype is not null)
        {
            Prototype.SetPrototype(Realm.ObjectPrototype);
        }

        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }
    }

    protected IJsObjectLike Prototype { get; }

    protected RealmState Realm { get; }

    /// <summary>
    ///     Optional hook for manual prototype customization that cannot be expressed via attributes.
    ///     The source generator calls this after all annotated members have been wired.
    /// </summary>
    protected virtual void ConfigurePrototype()
    {
    }
}
