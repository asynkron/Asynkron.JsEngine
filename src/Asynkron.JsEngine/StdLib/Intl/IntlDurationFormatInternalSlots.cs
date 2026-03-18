namespace Asynkron.JsEngine.StdLib.Intl;

internal sealed class IntlDurationFormatInternalSlots
{
    public required string Locale { get; init; }
    public required string NumberingSystem { get; init; }
    public required string Style { get; init; }

    // Unit styles
    public required string YearsStyle { get; init; }
    public required string YearsDisplay { get; init; }
    public required string MonthsStyle { get; init; }
    public required string MonthsDisplay { get; init; }
    public required string WeeksStyle { get; init; }
    public required string WeeksDisplay { get; init; }
    public required string DaysStyle { get; init; }
    public required string DaysDisplay { get; init; }
    public required string HoursStyle { get; init; }
    public required string HoursDisplay { get; init; }
    public required string MinutesStyle { get; init; }
    public required string MinutesDisplay { get; init; }
    public required string SecondsStyle { get; init; }
    public required string SecondsDisplay { get; init; }
    public required string MillisecondsStyle { get; init; }
    public required string MillisecondsDisplay { get; init; }
    public required string MicrosecondsStyle { get; init; }
    public required string MicrosecondsDisplay { get; init; }
    public required string NanosecondsStyle { get; init; }
    public required string NanosecondsDisplay { get; init; }

    // FractionalDigits: undefined in JS mapped to null here
    public int? FractionalDigits { get; init; }
}
