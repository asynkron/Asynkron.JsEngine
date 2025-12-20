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

    internal static TCache GetOrCreate<TState, TCache>(
        ref TCache? field,
        TState state,
        Func<TState, TCache> factory,
        out bool created)
        where TCache : class
    {
        var existing = Volatile.Read(ref field);
        if (existing is not null)
        {
            created = false;
            return existing;
        }

        var newValue = factory(state);
        var prior = Interlocked.CompareExchange(ref field, newValue, null);
        if (prior is null)
        {
            created = true;
            return newValue;
        }

        created = false;
        return prior;
    }
}
