#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine.JsTypes;

public sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    private ReferenceEqualityComparer()
    {
    }

    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    public bool Equals(T? x, T? y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(T obj)
    {
        return RuntimeHelpers.GetHashCode(obj);
    }
}
