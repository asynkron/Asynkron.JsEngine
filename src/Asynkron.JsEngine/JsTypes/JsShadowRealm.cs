#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents the internal state of a ShadowRealm instance.
///     Holds an isolated JsEngine with its own global environment.
///     Uses composition rather than inheritance since JsObject is sealed.
/// </summary>
public sealed class JsShadowRealm
{
    /// <summary>
    ///     The isolated engine instance for this ShadowRealm.
    /// </summary>
    internal Asynkron.JsEngine.JsEngine InnerEngine { get; }

    /// <summary>
    ///     The realm state of the caller (outer) realm that created this ShadowRealm.
    /// </summary>
    internal RealmState CallerRealmState { get; }

    public JsShadowRealm(RealmState callerRealmState)
    {
        CallerRealmState = callerRealmState ?? throw new ArgumentNullException(nameof(callerRealmState));

        // Create a fully isolated engine for the shadow realm
        InnerEngine = new Asynkron.JsEngine.JsEngine();
    }
}
