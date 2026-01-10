using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.StdLib;
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

    [MethodImpl(JsEngineConstants.Inlining)]
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
        [MethodImpl(JsEngineConstants.Inlining)]
        get
        {
            AssertOwnership(nameof(Current));
            return _current;
        }
    }

    object IEnumerator.Current => _current;

    [MethodImpl(JsEngineConstants.Inlining)]
    public bool MoveNext()
    {
        AssertOwnership(nameof(MoveNext));
        var value = _value;
        if (value is null || _index >= value.Length)
        {
            return false;
        }

        _current = StringHelper.ReadCodePoint(value, ref _index);

        return true;
    }

    [Conditional("DEBUG")]
    internal void AssertOwnership(string usage) => PoolDebug.AssertOwned(this, usage);

    void IRentable.OnRent(ILogger? logger) { }

    void IRentable.OnReturn(ILogger? logger) => Reset();

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
