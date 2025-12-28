#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    [JsHostMethod("entries", Length = 0d)]
    public JsValue Entries(JsValue thisValue)
    {
        return JsValue.FromObjectUnsafe(CreateArrayIterator(thisValue, "Array.prototype.entries", Realm,
            ArrayIteratorKind.Entries));
    }

    [JsHostMethod("keys", Length = 0d)]
    public JsValue Keys(JsValue thisValue)
    {
        return JsValue.FromObjectUnsafe(CreateArrayIterator(thisValue, "Array.prototype.keys", Realm,
            ArrayIteratorKind.Keys));
    }

    [JsHostMethod("values", Length = 0d)]
    public JsValue Values(JsValue thisValue)
    {
        return JsValue.FromObjectUnsafe(CreateArrayIterator(thisValue, "Array.prototype.values", Realm,
            ArrayIteratorKind.Values));
    }
}
