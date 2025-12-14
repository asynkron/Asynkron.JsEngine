using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    [JsHostMethod("entries", Length = 0d)]
    public object? Entries(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return CreateArrayIterator(thisValue, "Array.prototype.entries", Realm, (accessor, _) => idx =>
        {
            var pair = new JsArray(Realm);
            pair.Push((double)idx);
            pair.Push(GetElementOrUndefined(accessor, ToIndexString(idx)));
            return pair;
        });
    }

    [JsHostMethod("keys", Length = 0d)]
    public object? Keys(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return CreateArrayIterator(thisValue, "Array.prototype.keys", Realm, (_, _) => idx => (double)idx);
    }

    [JsHostMethod("values", Length = 0d)]
    public object? Values(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return CreateArrayIterator(thisValue, "Array.prototype.values", Realm,
            (accessor, _) => idx => GetElementOrUndefined(accessor, ToIndexString(idx)));
    }
}
