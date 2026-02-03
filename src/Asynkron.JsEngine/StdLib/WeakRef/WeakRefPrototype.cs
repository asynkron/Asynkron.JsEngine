#region

using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("WeakRef", ToStringTag = "WeakRef")]
public sealed partial class WeakRefPrototype : JsPrototype
{
    [JsHostMethod("deref", Length = 0d)]
    public JsValue Deref(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        if (!thisValue.TryGetObject<JsObject>(out var obj))
        {
            throw ThrowTypeError("WeakRef.prototype.deref requires an object receiver");
        }

        if (!obj.TryGetProperty("_target", out var stored))
        {
            throw ThrowTypeError("WeakRef.prototype.deref requires a WeakRef receiver");
        }

        return stored;
    }
}
