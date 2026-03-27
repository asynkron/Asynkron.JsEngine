#region

using System.Globalization;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

internal sealed class DateTimeFormatInternalSlots
{
    public static readonly string[] ComponentNames =
    [
        "weekday", "era", "year", "month", "day", "dayPeriod", "hour", "minute", "second",
        "fractionalSecondDigits", "timeZoneName"
    ];

    public string Locale { get; init; } = CultureInfo.CurrentCulture.Name;
    public string TimeZone { get; init; } = TimeZoneInfo.Utc.Id;
    public string? DisplayTimeZone { get; init; }
    public string Calendar { get; init; } = "gregory";
    public string NumberingSystem { get; init; } = "latn";
    public string HourCycle { get; init; } = "h23";
    public bool? Hour12 { get; init; }
    public string? DateStyle { get; init; }
    public string? TimeStyle { get; init; }
    public Dictionary<string, string> Components { get; init; } = new(StringComparer.Ordinal);
}
