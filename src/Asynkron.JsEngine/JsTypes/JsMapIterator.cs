#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.JsTypes;

internal sealed class JsMapIterator : JsIteratorBase
{
    private readonly JsMap _map;
    private readonly MapIterationKind _kind;
    private int _index;

    internal JsMapIterator(JsMap map, MapIterationKind kind, RealmState realm, JsObject? prototype)
        : base(realm, prototype)
    {
        _map = map;
        _kind = kind;
    }

    internal JsValue Next()
    {
        if (_done)
        {
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        if (_index >= _map.EntryCount)
        {
            _done = true;
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        var entry = _map.GetEntry(_index++);
        var key = JsValue.FromObjectUnsafe(entry.Key);
        var value = entry.Value;

        var result = _kind switch
        {
            MapIterationKind.Keys => key,
            MapIterationKind.Values => value,
            _ => CreateEntryPair(key, value)
        };

        return IteratorResultObject.Create(result, false);
    }

    private JsValue CreateEntryPair(JsValue key, JsValue value)
    {
        var pair = new JsArray(_realm);
        pair.SetElement(0, key);
        pair.SetElement(1, value);
        return JsValue.FromJsArray(pair);
    }
}
