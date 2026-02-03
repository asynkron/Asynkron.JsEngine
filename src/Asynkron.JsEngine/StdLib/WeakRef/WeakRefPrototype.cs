#region

using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("WeakRef", ToStringTag = "WeakRef")]
public sealed partial class WeakRefPrototype : JsPrototype
{
    internal const string TargetSlotName = "#WeakRef@target";

    [JsHostMethod("deref", Length = 0d)]
    public JsValue Deref(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        if (!thisValue.TryGetObject<JsObject>(out var obj))
        {
            throw ThrowTypeError("WeakRef.prototype.deref requires an object receiver");
        }

        if (!obj.HasPrivateField(TargetSlotName))
        {
            throw ThrowTypeError("WeakRef.prototype.deref requires a WeakRef receiver");
        }

        obj.TryGetProperty(TargetSlotName, out var stored);
        return stored;
    }
}
