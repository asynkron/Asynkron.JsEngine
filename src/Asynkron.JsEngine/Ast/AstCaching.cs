using System.Threading;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Marker for AST nodes that carry lazily computed caches.
/// </summary>
internal interface IAstCacheableNode
{
}

/// <summary>
/// Uniform cache contract for AST nodes that expose a single cached value of type <typeparamref name="TCache" />.
/// Implementations must be thread-safe and return an immutable cache instance.
/// </summary>
internal interface IAstCacheable<out TCache> : IAstCacheableNode where TCache : class
{
    TCache GetOrCreateCache();
}

internal static class AstCache
{
    internal static TCache GetOrCreate<TCache>(ref TCache? field, Func<TCache> factory) where TCache : class
    {
        var existing = Volatile.Read(ref field);
        if (existing is not null)
        {
            return existing;
        }

        var created = factory();
        var prior = Interlocked.CompareExchange(ref field, created, null);
        return prior ?? created;
    }

    internal static TCache GetOrCreate<TState, TCache>(ref TCache? field, TState state, Func<TState, TCache> factory)
        where TCache : class
    {
        var existing = Volatile.Read(ref field);
        if (existing is not null)
        {
            return existing;
        }

        var created = factory(state);
        var prior = Interlocked.CompareExchange(ref field, created, null);
        return prior ?? created;
    }
}
