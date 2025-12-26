#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    /* FLAKY */
    /* FLAKY */
    [JsHostMethod("entries", Length = 0d)]
    public JsValue Entries(JsValue thisValue)
    {
        return JsValue.FromObjectUnsafe(CreateArrayIterator(thisValue, "Array.prototype.entries", Realm,
            (accessor, _) => idx =>
            {
                var pair = new JsArray(Realm);
                pair.Push((double)idx);
                pair.Push(GetElementOrUndefinedJsValue(accessor, idx));
                return JsValue.FromJsArray(pair);
            }));
    }

    /* FLAKY */
    /* FLAKY */
    [JsHostMethod("keys", Length = 0d)]
    public JsValue Keys(JsValue thisValue)
    {
        return JsValue.FromObjectUnsafe(CreateArrayIterator(thisValue, "Array.prototype.keys", Realm,
            (_, _) => idx => new JsValue((double)idx)));
    }

    /* FLAKY */
    /* FLAKY */
    [JsHostMethod("values", Length = 0d)]
    public JsValue Values(JsValue thisValue)
    {
        return JsValue.FromObjectUnsafe(CreateArrayIterator(thisValue, "Array.prototype.values", Realm,
            (accessor, _) => idx => GetElementOrUndefinedJsValue(accessor, idx)));
    }
}
