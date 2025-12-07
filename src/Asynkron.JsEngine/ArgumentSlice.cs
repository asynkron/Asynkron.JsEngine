using System.Collections;

namespace Asynkron.JsEngine;

/// <summary>
/// A zero-copy slice over an IReadOnlyList that implements IReadOnlyList itself.
/// Avoids allocating new arrays for .call(), .apply(), .bind() scenarios.
/// </summary>
public readonly struct ArgumentSlice : IReadOnlyList<object?>
{
    private readonly IReadOnlyList<object?>? _source;
    private readonly int _offset;
    private readonly int _count;

    public ArgumentSlice(IReadOnlyList<object?> source, int offset)
    {
        _source = source;
        _offset = Math.Min(offset, source.Count);
        _count = Math.Max(0, source.Count - _offset);
    }

    public ArgumentSlice(IReadOnlyList<object?> source, int offset, int count)
    {
        _source = source;
        _offset = Math.Min(offset, source.Count);
        _count = Math.Min(count, Math.Max(0, source.Count - _offset));
    }

    public int Count => _count;

    public object? this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _source![_offset + index];
        }
    }

    public IEnumerator<object?> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
            yield return _source![_offset + i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Static empty instance for zero-arg cases.
    /// </summary>
    public static readonly ArgumentSlice Empty = new(Array.Empty<object?>(), 0);
}
