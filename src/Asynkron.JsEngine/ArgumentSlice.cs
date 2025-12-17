using System.Collections;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
/// A zero-copy slice over an IReadOnlyList that implements IReadOnlyList itself.
/// Avoids allocating new arrays for .call(), .apply(), .bind() scenarios.
/// </summary>
public readonly struct ArgumentSlice : IReadOnlyList<JsValue>
{
    private readonly IReadOnlyList<JsValue>? _source;
    private readonly int _offset;
    private readonly int _count;

    public ArgumentSlice(IReadOnlyList<JsValue> source, int offset)
    {
        _source = source;
        _offset = Math.Min(offset, source.Count);
        _count = Math.Max(0, source.Count - _offset);
    }

    public ArgumentSlice(IReadOnlyList<JsValue> source, int offset, int count)
    {
        _source = source;
        _offset = Math.Min(offset, source.Count);
        _count = Math.Min(count, Math.Max(0, source.Count - _offset));
    }

    public int Count => _count;

    public JsValue this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _source![_offset + index];
        }
    }

    public IEnumerator<JsValue> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
            yield return _source![_offset + i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Static empty instance for zero-arg cases.
    /// </summary>
    public static readonly ArgumentSlice Empty = new([], 0);
}
