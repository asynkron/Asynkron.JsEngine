#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.JsTypes;

internal sealed class JsSetIterator : JsIteratorBase
{
    private readonly JsSet _set;
    private readonly SetIterationKind _kind;
    private int _index;

    internal JsSetIterator(JsSet set, SetIterationKind kind, RealmState realm, JsObject? prototype)
        : base(realm, prototype)
    {
        _set = set;
        _kind = kind;
    }

    internal JsValue Next()
    {
        if (_done)
        {
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        if (_index >= _set.ValueCount)
        {
            _done = true;
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        var value = _set.GetValue(_index++);
        var result = _kind == SetIterationKind.Entries ? CreateEntryPair(value) : value;
        return IteratorResultObject.Create(result, false);
    }

    private JsValue CreateEntryPair(JsValue value)
    {
        var pair = new JsArray(_realm);
        pair.SetElement(0, value);
        pair.SetElement(1, value);
        return JsValue.FromJsArray(pair);
    }
}
