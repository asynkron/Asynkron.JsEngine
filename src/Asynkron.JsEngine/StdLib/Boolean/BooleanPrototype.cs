using System.Collections.Generic;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Boolean", ToStringTag = "Boolean")]
public sealed partial class BooleanPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    public object? ToString(object? thisValue, IReadOnlyList<object?> _)
    {
        return RequireBooleanReceiver(thisValue) ? "true" : "false";
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public object? ValueOf(object? thisValue, IReadOnlyList<object?> _)
    {
        return RequireBooleanReceiver(thisValue);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.BooleanPrototype ??= Prototype as JsObject;
    }

    private bool RequireBooleanReceiver(object? receiver)
    {
        return receiver switch
        {
            bool flag => flag,
            JsObject obj when obj.TryGetProperty("__value__", out var inner) && inner is bool b => b,
            IJsPropertyAccessor accessor when accessor.TryGetProperty("__value__", out var inner) &&
                                              inner is bool b => b,
            _ => throw ThrowTypeError("Boolean method called on non-boolean object", realm: Realm)
        };
    }
}
