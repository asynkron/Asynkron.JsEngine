#region

using System.Globalization;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a Temporal.PlainDate - a calendar date without time or timezone.
///     Maps to DateOnly in .NET.
/// </summary>
public sealed class JsTemporalPlainDate : IEquatable<JsTemporalPlainDate>, IComparable<JsTemporalPlainDate>
{
    public JsTemporalPlainDate(int year, int month, int day, string calendar = "iso8601")
    {
        Year = year;
        Month = month;
        Day = day;
        Calendar = calendar;
    }

    public JsTemporalPlainDate(DateOnly date, string calendar = "iso8601")
        : this(date.Year, date.Month, date.Day, calendar)
    {
    }

    public int Year { get; }
    public int Month { get; }
    public int Day { get; }
    public string Calendar { get; }

    /// <summary>
    ///     The month code (e.g., "M01" for January).
    /// </summary>
    public string MonthCode => $"M{Month:D2}";

    /// <summary>
    ///     The day of the week (1 = Monday, 7 = Sunday per ISO 8601).
    /// </summary>
    public int DayOfWeek => ((int)ToDateOnly().DayOfWeek + 6) % 7 + 1;

    /// <summary>
    ///     The day of the year (1-366).
    /// </summary>
    public int DayOfYear => ToDateOnly().DayOfYear;

    /// <summary>
    ///     The ISO week number.
    /// </summary>
    public int WeekOfYear
    {
        get
        {
            var date = ToDateOnly();
            var culture = CultureInfo.InvariantCulture;
            return culture.Calendar.GetWeekOfYear(
                date.ToDateTime(TimeOnly.MinValue),
                CalendarWeekRule.FirstFourDayWeek,
                System.DayOfWeek.Monday);
        }
    }

    /// <summary>
    ///     The number of days in the current month.
    /// </summary>
    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    /// <summary>
    ///     The number of days in the current year.
    /// </summary>
    public int DaysInYear => DateTime.IsLeapYear(Year) ? 366 : 365;

    /// <summary>
    ///     The number of months in the current year (always 12 for ISO calendar).
    /// </summary>
    public int MonthsInYear => 12;

    /// <summary>
    ///     Whether the current year is a leap year.
    /// </summary>
    public bool InLeapYear => DateTime.IsLeapYear(Year);

    /// <summary>
    ///     Creates a PlainDate for today in the system time zone.
    /// </summary>
    public static JsTemporalPlainDate Today(TimeZoneInfo? timeZone = null)
    {
        timeZone ??= TimeZoneInfo.Local;
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        return new JsTemporalPlainDate(now.Year, now.Month, now.Day);
    }

    /// <summary>
    ///     Creates a PlainDate from an ISO 8601 string (YYYY-MM-DD).
    /// </summary>
    public static JsTemporalPlainDate From(string isoString)
    {
        // Simple parsing - full implementation would handle more formats
        if (DateOnly.TryParseExact(isoString, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            return new JsTemporalPlainDate(date);
        }

        throw new FormatException($"Invalid PlainDate string: {isoString}");
    }

    /// <summary>
    ///     Creates a PlainDate from individual components.
    /// </summary>
    public static JsTemporalPlainDate From(int year, int month, int day, string calendar = "iso8601")
    {
        return new JsTemporalPlainDate(year, month, day, calendar);
    }

    /// <summary>
    ///     Converts to .NET DateOnly.
    /// </summary>
    public DateOnly ToDateOnly()
    {
        return new DateOnly(Year, Month, Day);
    }

    /// <summary>
    ///     Returns a new PlainDate with modified fields.
    /// </summary>
    public JsTemporalPlainDate With(int? year = null, int? month = null, int? day = null)
    {
        return new JsTemporalPlainDate(
            year ?? Year,
            month ?? Month,
            day ?? Day,
            Calendar);
    }

    /// <summary>
    ///     Adds a duration to this date.
    /// </summary>
    public JsTemporalPlainDate Add(JsTemporalDuration duration)
    {
        var date = ToDateOnly();

        // Add years and months
        if (duration.Years != 0 || duration.Months != 0)
        {
            var newYear = Year + (int)duration.Years;
            var newMonth = Month + (int)duration.Months;

            // Normalize months
            while (newMonth > 12)
            {
                newMonth -= 12;
                newYear++;
            }
            while (newMonth < 1)
            {
                newMonth += 12;
                newYear--;
            }

            // Clamp day to valid range for new month
            var daysInNewMonth = DateTime.DaysInMonth(newYear, newMonth);
            var newDay = Math.Min(Day, daysInNewMonth);

            date = new DateOnly(newYear, newMonth, newDay);
        }

        // Add weeks and days
        var totalDays = (int)(duration.Weeks * 7 + duration.Days);
        if (totalDays != 0)
        {
            date = date.AddDays(totalDays);
        }

        return new JsTemporalPlainDate(date, Calendar);
    }

    /// <summary>
    ///     Subtracts a duration from this date.
    /// </summary>
    public JsTemporalPlainDate Subtract(JsTemporalDuration duration)
    {
        return Add(duration.Negated());
    }

    /// <summary>
    ///     Returns the duration until another date.
    /// </summary>
    public JsTemporalDuration Until(JsTemporalPlainDate other)
    {
        var days = other.ToDateOnly().DayNumber - ToDateOnly().DayNumber;
        return JsTemporalDuration.From(days: days);
    }

    /// <summary>
    ///     Returns the duration since another date.
    /// </summary>
    public JsTemporalDuration Since(JsTemporalPlainDate other)
    {
        return other.Until(this);
    }

    /// <summary>
    ///     Combines this date with a time to create a PlainDateTime.
    /// </summary>
    public JsTemporalPlainDateTime ToPlainDateTime(JsTemporalPlainTime time)
    {
        return new JsTemporalPlainDateTime(this, time);
    }

    /// <summary>
    ///     Extracts the year and month from this date.
    /// </summary>
    public JsTemporalPlainYearMonth ToPlainYearMonth()
    {
        return new JsTemporalPlainYearMonth(Year, Month, Calendar);
    }

    /// <summary>
    ///     Extracts the month and day from this date.
    /// </summary>
    public JsTemporalPlainMonthDay ToPlainMonthDay()
    {
        return new JsTemporalPlainMonthDay(Month, Day, Calendar);
    }

    public int CompareTo(JsTemporalPlainDate? other)
    {
        if (other is null) return 1;
        return ToDateOnly().CompareTo(other.ToDateOnly());
    }

    public bool Equals(JsTemporalPlainDate? other)
    {
        if (other is null) return false;
        return Year == other.Year && Month == other.Month && Day == other.Day &&
               Calendar == other.Calendar;
    }

    public override bool Equals(object? obj)
    {
        return obj is JsTemporalPlainDate other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Year, Month, Day, Calendar);
    }

    /// <summary>
    ///     Returns ISO 8601 date string (YYYY-MM-DD).
    /// </summary>
    public override string ToString()
    {
        var result = $"{Year:D4}-{Month:D2}-{Day:D2}";
        if (Calendar != "iso8601")
        {
            result += $"[u-ca={Calendar}]";
        }
        return result;
    }

    public static bool operator ==(JsTemporalPlainDate? left, JsTemporalPlainDate? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(JsTemporalPlainDate? left, JsTemporalPlainDate? right) => !(left == right);
    public static bool operator <(JsTemporalPlainDate left, JsTemporalPlainDate right) => left.CompareTo(right) < 0;
    public static bool operator <=(JsTemporalPlainDate left, JsTemporalPlainDate right) => left.CompareTo(right) <= 0;
    public static bool operator >(JsTemporalPlainDate left, JsTemporalPlainDate right) => left.CompareTo(right) > 0;
    public static bool operator >=(JsTemporalPlainDate left, JsTemporalPlainDate right) => left.CompareTo(right) >= 0;
}
