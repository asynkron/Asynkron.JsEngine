using System.Text.Json;

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlWeekData
{
    private static readonly WeekDataPayload Payload = Load();

    public static string GetFirstDay(string? region)
    {
        return Lookup(Payload.FirstDay, region) ?? "mon";
    }

    public static (string Start, string End)? GetWeekend(string? region)
    {
        var start = Lookup(Payload.WeekendStart, region);
        var end = Lookup(Payload.WeekendEnd, region);
        if (start is null || end is null)
        {
            return null;
        }

        return (start, end);
    }

    public static int GetMinimalDays(string? region)
    {
        var token = Lookup(Payload.MinDays, region);
        if (token is null || !int.TryParse(token, out var value))
        {
            return 1;
        }

        if (value < 1)
        {
            return 1;
        }

        return value > 7 ? 7 : value;
    }

    private static string? Lookup(IReadOnlyDictionary<string, string> map, string? region)
    {
        if (!string.IsNullOrEmpty(region) && map.TryGetValue(region, out var value))
        {
            return value;
        }

        if (map.TryGetValue("001", out var defaultValue))
        {
            return defaultValue;
        }

        return null;
    }

    private static WeekDataPayload Load()
    {
        var assembly = typeof(IntlWeekData).Assembly;
        const string resourceName = "Asynkron.JsEngine.StdLib.Intl.IntlWeekData.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
                        ?? throw new InvalidOperationException("Could not load embedded Intl week data.");
        var payload = JsonSerializer.Deserialize<WeekDataPayload>(stream)
                      ?? throw new InvalidOperationException("Intl week data payload is missing.");
        return payload with
        {
            FirstDay = new Dictionary<string, string>(payload.FirstDay, StringComparer.Ordinal),
            WeekendStart = new Dictionary<string, string>(payload.WeekendStart, StringComparer.Ordinal),
            WeekendEnd = new Dictionary<string, string>(payload.WeekendEnd, StringComparer.Ordinal),
            MinDays = new Dictionary<string, string>(payload.MinDays, StringComparer.Ordinal),
        };
    }

    private sealed record WeekDataPayload
    {
        public Dictionary<string, string> FirstDay { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> WeekendStart { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> WeekendEnd { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> MinDays { get; init; } = new(StringComparer.Ordinal);
    }
}
