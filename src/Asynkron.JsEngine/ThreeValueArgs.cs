using System.Collections;
using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

/// <summary>
/// An allocation-free wrapper for three JsValue arguments when carried through generic argument-list paths.
/// Do not pass this through an IReadOnlyList-typed hot path; that boxes the struct.
/// </summary>
[method: MethodImpl(JsEngineConstants.Inlining)]
public readonly struct ThreeValueArgs(JsValue first, JsValue second, JsValue third) : IReadOnlyList<JsValue>
{
    public int Count
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get => 3;
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
                2 => third,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }
    }

    public IEnumerator<JsValue> GetEnumerator()
    {
        yield return first;
        yield return second;
        yield return third;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
