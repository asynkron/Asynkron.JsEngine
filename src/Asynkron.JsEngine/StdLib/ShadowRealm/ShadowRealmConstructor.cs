#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("ShadowRealm", PrototypeType = typeof(ShadowRealmPrototype), Length = 0d, DisplayName = "ShadowRealm")]
public sealed partial class ShadowRealmConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement ShadowRealm constructor
        // Creates a new ShadowRealm with an isolated global environment
        var obj = PrepareThisObject(JsValue.Undefined, false);
        if (Prototype is not null && obj.Prototype is null)
        {
            obj.SetPrototype(Prototype);
        }
        obj.RealmState ??= Realm;
        return new JsValue(obj);
    }
}
