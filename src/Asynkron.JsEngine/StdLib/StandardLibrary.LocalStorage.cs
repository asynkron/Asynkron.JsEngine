using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateLocalStorageObject()
    {
        var storage = new JsObject();
        var backing = new Dictionary<string, string?>(StringComparer.Ordinal);

        storage.SetProperty("getItem", new HostFunction((_, args) =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            return backing.GetValueOrDefault(key);
        }));

        storage.SetProperty("setItem", new HostFunction((_, args) =>
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

        storage.SetProperty("removeItem", new HostFunction((_, args) =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            backing.Remove(key);
            return null;
        }));

        storage.SetProperty("clear", new HostFunction((_, _) =>
        {
            backing.Clear();
            return null;
        }));

        return storage;
    }
}
