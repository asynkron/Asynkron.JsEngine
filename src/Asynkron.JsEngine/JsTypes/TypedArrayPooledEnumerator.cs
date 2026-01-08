#region

using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Pooled enumerator for TypedArray values.
/// Checks buffer validity on each iteration per ECMAScript spec.
/// </summary>
internal sealed class TypedArrayPooledEnumerator : IEnumerator<JsValue>, IRentable
{
    private static readonly ObjectPool<TypedArrayPooledEnumerator> Pool = new(16, () => new TypedArrayPooledEnumerator());

    private TypedArrayBase? _typedArray;
    private int _index;
    private JsValue _current;

    private TypedArrayPooledEnumerator()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TypedArrayPooledEnumerator Rent(TypedArrayBase typedArray)
    {
        var enumerator = Pool.Rent();
        enumerator._typedArray = typedArray;
        enumerator._index = 0;
        enumerator._current = default;
        return enumerator;
    }

    public JsValue Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            AssertOwnership(nameof(Current));
            return _current;
        }
    }

    object IEnumerator.Current => _current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        AssertOwnership(nameof(MoveNext));
        var typedArray = _typedArray;
        if (typedArray is null)
        {
            return false;
        }

        typedArray.AssertInvariants(nameof(MoveNext));

        // Check length and bounds on each iteration - buffer may be resized during iteration
        if (_index >= typedArray.Length)
        {
            return false;
        }

        // Check if buffer is still valid on each iteration
        if (typedArray.IsDetachedOrOutOfBounds())
        {
            return false;
        }

        _current = typedArray.GetValueForIndex(_index);
        _index++;
        return true;

    }

    [Conditional("DEBUG")]
    internal void AssertOwnership(string usage) => PoolDebug.AssertOwned(this, usage);

    void IRentable.OnRent(ILogger? logger) { }

    void IRentable.OnReturn(ILogger? logger) => Reset();

    public void Reset()
    {
        _typedArray = null;
        _index = 0;
        _current = default;
    }

    public void Dispose()
    {
        Pool.Return(this);
    }
}
