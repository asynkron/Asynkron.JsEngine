#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// ShadowRealm provides a mechanism to execute JavaScript code in a separate realm
/// </summary>
[JsPrototype("ShadowRealm", ToStringTag = "ShadowRealm")]
public sealed partial class ShadowRealmPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("evaluate", Length = 1d)]
    public JsValue Evaluate(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement ShadowRealm.prototype.evaluate
        // Evaluates code in the shadow realm
        throw new NotImplementedException("ShadowRealm.prototype.evaluate is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("importValue", Length = 2d)]
    public JsValue ImportValue(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement ShadowRealm.prototype.importValue
        // Imports a value from a module in the shadow realm
        throw new NotImplementedException("ShadowRealm.prototype.importValue is not yet implemented");
    }
}
