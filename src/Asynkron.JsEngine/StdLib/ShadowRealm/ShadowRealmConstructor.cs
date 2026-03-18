#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("ShadowRealm", PrototypeType = typeof(ShadowRealmPrototype), Length = 0d, DisplayName = "ShadowRealm")]
public sealed partial class ShadowRealmConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    /// <summary>
    /// Internal slot key for storing the JsShadowRealm instance on the JsObject.
    /// </summary>
    internal const string ShadowRealmSlot = "#shadowRealm";

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Create the ShadowRealm instance
        var shadowRealm = new JsShadowRealm(Realm);

        // Create the JsObject to represent the ShadowRealm
        var instance = CreateDefaultInstance();
        if (instance.TryGetObject<JsObject>(out var jsObj))
        {
            // Store the internal shadow realm as a non-enumerable property
            jsObj.DefineProperty(ShadowRealmSlot, new PropertyDescriptor
            {
                Value = shadowRealm,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });
        }

        return instance;
    }
}
