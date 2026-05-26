#region

using System.Collections;
using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// An allocation-free wrapper for four JsValue arguments when carried through generic argument-list paths.
/// Do not pass this through an IReadOnlyList-typed hot path; that boxes the struct.
/// </summary>
[method: MethodImpl(JsEngineConstants.Inlining)]
public readonly struct FourValueArgs(JsValue first, JsValue second, JsValue third, JsValue fourth) : IReadOnlyList<JsValue>
{
    public int Count
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get => 4;
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
                3 => fourth,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }
    }

    public IEnumerator<JsValue> GetEnumerator()
    {
        yield return first;
        yield return second;
        yield return third;
        yield return fourth;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
