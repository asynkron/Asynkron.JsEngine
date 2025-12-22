#region

using System.Text.Json;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlLikelySubtagsData
{
    private static readonly IReadOnlyDictionary<string, string> LikelySubtags = Load();

    public static bool TryResolve(string key, out string value)
    {
        return LikelySubtags.TryGetValue(key, out value!);
    }

    private static IReadOnlyDictionary<string, string> Load()
    {
        var assembly = typeof(IntlLikelySubtagsData).Assembly;
        const string resourceName = "Asynkron.JsEngine.StdLib.Intl.IntlLikelySubtags.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException(
                               "Could not load embedded Intl likely-subtags data.");

        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                   ?? throw new InvalidOperationException(
                       "Intl likely-subtags payload is missing.");

        return new Dictionary<string, string>(data, StringComparer.Ordinal);
    }
}
