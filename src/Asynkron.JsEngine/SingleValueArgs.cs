using System.Collections;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
/// A zero-allocation wrapper for a single JsValue that implements IReadOnlyList.
/// Used to avoid array allocations when invoking callables with a single argument.
/// </summary>
public readonly struct SingleValueArgs : IReadOnlyList<JsValue>
{
    private readonly JsValue _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SingleValueArgs(JsValue value)
    {
        _value = value;
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => 1;
    }

    public JsValue this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _value;
        }
    }

    public IEnumerator<JsValue> GetEnumerator()
    {
        yield return _value;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
