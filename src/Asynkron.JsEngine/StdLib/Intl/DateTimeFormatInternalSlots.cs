using System.Globalization;

namespace Asynkron.JsEngine.StdLib.Intl;

internal sealed class DateTimeFormatInternalSlots
{
    public static readonly string[] ComponentNames =
    [
        "weekday", "era", "year", "month", "day", "hour", "minute", "second", "timeZoneName"
    ];

    public string Locale { get; init; } = CultureInfo.CurrentCulture.Name;
    public string TimeZone { get; init; } = TimeZoneInfo.Utc.Id;
    public string Calendar { get; init; } = "gregory";
    public string NumberingSystem { get; init; } = "latn";
    public string HourCycle { get; init; } = "h23";
    public string LocaleMatcher { get; init; } = "best fit";
    public string FormatMatcher { get; init; } = "best fit";
    public string? DateStyle { get; init; }
    public string? TimeStyle { get; init; }
    public Dictionary<string, string> Components { get; } = new();
}
