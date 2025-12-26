using System.Runtime.CompilerServices;

namespace Asynkron.JsParser;

/// <summary>
/// An equality comparer that uses reference equality for comparison.
/// </summary>
/// <typeparam name="T">The type to compare.</typeparam>
public sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
{
    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    private ReferenceEqualityComparer() { }

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
