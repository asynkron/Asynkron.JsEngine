using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Pooled enumerator for string character iteration (Unicode code points).
/// Handles surrogate pairs correctly per ECMAScript spec.
/// </summary>
internal sealed class StringPooledEnumerator : IEnumerator<JsValue>, IRentable
{
    private static readonly ObjectPool<StringPooledEnumerator> Pool = new(32, () => new StringPooledEnumerator());

    private string? _value;
    private int _index;
    private JsValue _current;

    private StringPooledEnumerator()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringPooledEnumerator Rent(string value)
    {
        var enumerator = Pool.Rent();
        enumerator._value = value;
        enumerator._index = 0;
        enumerator._current = default;
        return enumerator;
    }

    public JsValue Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current;
    }

    object IEnumerator.Current => _current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        var value = _value;
        if (value is null || _index >= value.Length)
        {
            return false;
        }

        var ch = value[_index];
        if (char.IsHighSurrogate(ch) && _index + 1 < value.Length && char.IsLowSurrogate(value[_index + 1]))
        {
            // Surrogate pair - yield both chars as one string
            _current = value.Substring(_index, 2);
            _index += 2;
        }
        else
        {
            // Single character (BMP or unpaired surrogate)
            _current = ch.ToString();
            _index++;
        }

        return true;
    }

    void IRentable.Activate(ILogger? logger) { }

    void IRentable.Reset(ILogger? logger) => Reset();

    public void Reset()
    {
        _value = null;
        _index = 0;
        _current = default;
    }

    public void Dispose()
    {
        Pool.Return(this);
    }
}
