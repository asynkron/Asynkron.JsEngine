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

        // Skip deleted entries (tombstones left in _insertionOrder)
        while (_index < _map.EntryCount)
        {
            var entry = _map.GetEntry(_index++);
            if (!_map.IsEntryAlive(entry.Key))
            {
                continue;
            }

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

        _done = true;
        return IteratorResultObject.DoneUndefined.AsJsValue;
    }

    private JsValue CreateEntryPair(JsValue key, JsValue value)
    {
        var pair = new JsArray(_realm);
        pair.SetElement(0, key);
        pair.SetElement(1, value);
        return JsValue.FromJsArray(pair);
    }
}
