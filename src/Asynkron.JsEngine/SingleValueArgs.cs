using System.Collections;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
/// A zero-allocation wrapper for a single JsValue that implements IReadOnlyList.
/// Used to avoid array allocations when invoking callables with a single argument.
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct SingleValueArgs(JsValue value) : IReadOnlyList<JsValue>
{
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
            ArgumentOutOfRangeException.ThrowIfNotEqual(index, 0);
            return value;
        }
    }

    public IEnumerator<JsValue> GetEnumerator()
    {
        yield return value;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
