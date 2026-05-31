using System.Collections;
using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

/// <summary>
/// An allocation-free wrapper for two JsValue arguments when carried through generic argument-list paths.
/// Do not pass this through an IReadOnlyList-typed hot path; that boxes the struct.
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
