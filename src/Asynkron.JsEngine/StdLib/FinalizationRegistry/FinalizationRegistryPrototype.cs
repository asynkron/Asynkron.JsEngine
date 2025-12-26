#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("FinalizationRegistry", ToStringTag = "FinalizationRegistry")]
public sealed partial class FinalizationRegistryPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("register", Length = 2d)]
    public JsValue Register(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement FinalizationRegistry.prototype.register
        // Registers an object with the registry to be notified when it's garbage collected
        throw new NotImplementedException("FinalizationRegistry.prototype.register is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("unregister", Length = 1d)]
    public JsValue Unregister(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement FinalizationRegistry.prototype.unregister
        // Unregisters a token from the registry
        throw new NotImplementedException("FinalizationRegistry.prototype.unregister is not yet implemented");
    }
}
