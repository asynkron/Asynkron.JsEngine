#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine;

public sealed class PrivateNameScope
{
    private static int _nextId;
    private readonly int _id = Interlocked.Increment(ref _nextId);
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public PrivateNameScope(RealmState realm)
    {
        realm.PrivateNameScopes[_id] = this;
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

    public static bool TryResolveScope(RealmState realm, string key, out PrivateNameScope? scope)
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

        return realm.PrivateNameScopes.TryGetValue(id, out scope);
    }
}
