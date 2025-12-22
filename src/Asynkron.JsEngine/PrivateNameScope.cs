#region

using System.Collections.Concurrent;

#endregion

namespace Asynkron.JsEngine;

public sealed class PrivateNameScope
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, PrivateNameScope> _scopes = new();
    private readonly int _id = Interlocked.Increment(ref _nextId);
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public PrivateNameScope()
    {
        _scopes[_id] = this;
    }

    public object BrandToken { get; } = new();

    public bool TryGetKey(string lexeme, out string key)
    {
        return _map.TryGetValue(lexeme, out key!);
    }

    public string GetKey(string lexeme)
    {
        if (_map.TryGetValue(lexeme, out var key))
        {
            return key;
        }

        key = $"{lexeme}@{_id}";
        _map[lexeme] = key;
        return key;
    }

    public static bool TryResolveScope(string key, out PrivateNameScope? scope)
    {
        scope = null;
        var separator = key.LastIndexOf('@');
        if (separator < 0)
        {
            return false;
        }

        if (!int.TryParse(key.AsSpan(separator + 1), out var id))
        {
            return false;
        }

        return _scopes.TryGetValue(id, out scope);
    }
}
