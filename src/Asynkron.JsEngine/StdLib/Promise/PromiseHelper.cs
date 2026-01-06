#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class PromiseHelper
{
    internal static JsPromise CreatePromise(RealmState realm, IJsObjectLike? prototypeOverride = null)
    {
        var engine = realm.Engine ?? throw new InvalidOperationException("Promise creation requires an engine.");
        var promise = new JsPromise(engine);
        ApplyPromisePrototype(promise, realm, prototypeOverride);
        return promise;
    }

    internal static void ApplyPromisePrototype(JsPromise promise, RealmState realm,
        IJsObjectLike? prototypeOverride = null)
    {
        var prototype = prototypeOverride ?? realm.PromisePrototype;
        if (prototype is not null && promise.JsObject.Prototype is null)
        {
            promise.JsObject.SetPrototype(prototype);
        }

        promise.JsObject.RealmState ??= realm;
    }

    /// <summary>
    ///     Kept for existing call sites while promise methods live on Promise.prototype.
    ///     Ensures the promise carries the correct prototype and realm state.
    /// </summary>
    internal static void AddPromiseInstanceMethods(JsObject promiseObj, JsPromise promise, JsEngine engine)
    {
        var realm = engine.RealmState;
        ApplyPromisePrototype(promise, realm);

        if (ReferenceEquals(promiseObj, promise.JsObject))
        {
            return;
        }

        if (realm.PromisePrototype is not null && promiseObj.Prototype is null)
        {
            promiseObj.SetPrototype(realm.PromisePrototype);
        }

        promiseObj.RealmState ??= realm;
        promiseObj.SetPromiseSlot(promise);
    }
}
