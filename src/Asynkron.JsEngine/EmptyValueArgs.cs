#region

using System.Collections;
using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// A zero-allocation wrapper for an empty argument list that implements IReadOnlyList.
/// Used to avoid interface-based argument setup when invoking zero-argument callables.
/// </summary>
internal readonly struct EmptyValueArgs : IReadOnlyList<JsValue>
{
    public static EmptyValueArgs Instance { get; } = new();

    public int Count
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get => 0;
    }

    public JsValue this[int index]
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get => throw new ArgumentOutOfRangeException(nameof(index));
    }

    public IEnumerator<JsValue> GetEnumerator()
    {
        yield break;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
