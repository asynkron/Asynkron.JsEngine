using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("WeakRef", ToStringTag = "WeakRef")]
public sealed partial class WeakRefPrototype : JsPrototype
{
    [JsHostMethod("deref", Length = 0d)]
    public JsValue Deref(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        if (thisValue.AsObject() is JsObject obj && obj.TryGetProperty("_target", out var stored))
        {
            return stored;
        }

        return JsValue.Undefined;
    }
}
