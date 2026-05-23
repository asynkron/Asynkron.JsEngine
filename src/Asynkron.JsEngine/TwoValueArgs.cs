#region

using System.Collections;
using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// A zero-allocation wrapper for two JsValue arguments that implements IReadOnlyList.
/// Used to avoid array allocations for common binary callable invocation.
/// </summary>
[method: MethodImpl(JsEngineConstants.Inlining)]
public readonly struct TwoValueArgs(JsValue first, JsValue second) : IReadOnlyList<JsValue>
{
    public int Count
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get => 2;
    }

    public JsValue this[int index]
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get
        {
            return index switch
            {
                0 => first,
                1 => second,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }
    }

    public IEnumerator<JsValue> GetEnumerator()
    {
        yield return first;
        yield return second;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
