using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

public static partial class StandardLibrary
{
    public static JsObject CreateLocalStorageObject()
    {
        var storage = new JsObject();
        var backing = new Dictionary<string, string?>(StringComparer.Ordinal);

        storage.SetProperty("getItem", new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            return backing.GetValueOrDefault(key);
        }));

        storage.SetProperty("setItem", new HostFunction((thisValue, args) =>
        {
            if (args.Count < 2)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            var value = args[1]?.ToString() ?? string.Empty;
            backing[key] = value;
            return null;
        }));

        storage.SetProperty("removeItem", new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            backing.Remove(key);
            return null;
        }));

        storage.SetProperty("clear", new HostFunction((thisValue, args) =>
        {
            backing.Clear();
            return null;
        }));

        return storage;
    }

    /// <summary>
    /// Creates a Console object with common logging methods.
    /// </summary>

}
