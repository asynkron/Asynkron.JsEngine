namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Uniform cache contract for AST nodes that expose a single cached value of type <typeparamref name="TCache" />.
/// Implementations must be thread-safe and return an immutable cache instance.
/// </summary>
internal interface IAstCacheable<out TCache> : IAstCacheableNode where TCache : class
{
    TCache GetOrCreateCache();
}
