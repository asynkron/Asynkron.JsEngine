namespace Asynkron.JsEngine.Ast;

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
