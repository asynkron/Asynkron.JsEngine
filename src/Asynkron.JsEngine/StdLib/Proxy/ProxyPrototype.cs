#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

// Proxy has no public prototype surface; this placeholder satisfies generator wiring.
[JsPrototype("Proxy", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class ProxyPrototype : JsPrototype
{
    protected override void ConfigurePrototype()
    {
        // Proxy.prototype is undefined in userland; see constructor configuration.
    }
}
