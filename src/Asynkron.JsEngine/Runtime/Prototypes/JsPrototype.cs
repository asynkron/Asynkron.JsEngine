#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

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

    /// <summary>
    ///     Helper for iterator prototypes to set up the iterator prototype chain
    ///     and assign to the appropriate Realm property.
    /// </summary>
    protected void ConfigureAsIteratorPrototype(Action<JsObject?> assignToRealm)
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        var iteratorPrototype = Realm.IteratorPrototype ??=
            (JsObject)StdLib.IteratorPrototype.CreatePrototype(Realm);
        if (!ReferenceEquals(Prototype.Prototype, iteratorPrototype))
        {
            Prototype.SetPrototype(iteratorPrototype);
        }

        assignToRealm(Prototype as JsObject);
    }

    /// <summary>
    ///     Ensures that a function object has a specific metadata property (like "length" or "name")
    ///     with the correct descriptor attributes.
    /// </summary>
    protected static void EnsureFunctionMetadata(IJsObjectLike target, string propertyName, object value)
    {
        if (target.GetOwnPropertyDescriptor(propertyName) is not null)
        {
            return;
        }

        target.DefineProperty(propertyName, new PropertyDescriptor
        {
            Value = value,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
    }
}
