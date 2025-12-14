using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateLocalStorageObject()
    {
        var storage = new JsObject();
        var backing = new Dictionary<string, string?>(StringComparer.Ordinal);

        storage.SetHostedProperty("getItem", GetItem);
        storage.SetHostedProperty("setItem", SetItem);
        storage.SetHostedProperty("removeItem", RemoveItem);
        storage.SetHostedProperty("clear", Clear);

        return storage;

        object? GetItem(object? _, IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            return backing.GetValueOrDefault(key);
        }

        object? SetItem(object? _, IReadOnlyList<object?> args)
        {
            if (args.Count < 2)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            var value = args[1]?.ToString() ?? string.Empty;
            backing[key] = value;
            return null;
        }

        object? RemoveItem(object? _, IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            backing.Remove(key);
            return null;
        }

        object? Clear(object? _, IReadOnlyList<object?> __)
        {
            backing.Clear();
            return null;
        }
    }
}
