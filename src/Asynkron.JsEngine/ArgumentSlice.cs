#region

using System.Collections;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// A zero-copy slice over an IReadOnlyList that implements IReadOnlyList itself.
/// Avoids allocating new arrays for .call(), .apply(), .bind() scenarios.
/// </summary>
public readonly struct ArgumentSlice : IReadOnlyList<JsValue>
{
    private readonly IReadOnlyList<JsValue>? _source;
    private readonly int _offset;

    public ArgumentSlice(IReadOnlyList<JsValue> source, int offset)
    {
        _source = source;
        _offset = Math.Min(offset, source.Count);
        Count = Math.Max(0, source.Count - _offset);
    }

    public ArgumentSlice(IReadOnlyList<JsValue> source, int offset, int count)
    {
        _source = source;
        _offset = Math.Min(offset, source.Count);
        Count = Math.Min(count, Math.Max(0, source.Count - _offset));
    }

    public int Count { get; }

    public JsValue this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _source![_offset + index];
        }
    }

    public IEnumerator<JsValue> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return _source![_offset + i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Static empty instance for zero-arg cases.
    /// </summary>
    public static readonly ArgumentSlice Empty = new([], 0);
}
