#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Boolean", ToStringTag = "Boolean")]
public sealed partial class BooleanPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(RequireBooleanReceiver(thisValue) ? "true" : "false");
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public JsValue ValueOf(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(RequireBooleanReceiver(thisValue));
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.BooleanPrototype ??= Prototype as JsObject;
    }

    private bool RequireBooleanReceiver(JsValue receiver)
    {
        if (receiver.TryGetBoolean(out var flag))
        {
            return flag;
        }

        if (receiver.TryGetObject<JsObject>(out var obj) && obj is not null &&
            obj.TryGetProperty("__value__", out var inner))
        {
            if (inner.TryGetBoolean(out var boolVal))
            {
                return boolVal;
            }
        }

        if (receiver.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty("__value__", out var innerVal))
        {
            if (innerVal.TryGetBoolean(out var boolVal))
            {
                return boolVal;
            }
        }

        throw ThrowTypeError("Boolean method called on non-boolean object", realm: Realm);
    }
}
