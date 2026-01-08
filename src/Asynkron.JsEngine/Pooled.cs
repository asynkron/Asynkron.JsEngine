using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

[Obsolete("Do not use. ", false)]
internal readonly struct Pooled<T> : IDisposable where T : class
{
    public readonly T? Value;
    private readonly Action<T>? _returnAction;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Pooled(T value, Action<T> returnAction)
    {
        Value = value;
        _returnAction = returnAction;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        // No-op for default struct (Value is null)
        if (Value is not null)
        {
            _returnAction?.Invoke(Value);
        }
    }

    // Implicit conversion for convenience when passing to methods expecting T
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator T(Pooled<T> pooled) => pooled.Value!;
}
