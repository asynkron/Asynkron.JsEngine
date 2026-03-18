#region

using System.Globalization;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a Temporal.PlainDate - a calendar date without time or timezone.
///     Maps to DateOnly in .NET.
/// </summary>
public sealed class JsTemporalPlainDate(int year, int month, int day, string calendar = "iso8601")
    : IEquatable<JsTemporalPlainDate>, IComparable<JsTemporalPlainDate>
{
    public JsTemporalPlainDate(DateOnly date, string calendar = "iso8601")
        : this(date.Year, date.Month, date.Day, calendar)
    {
    }

    public int Year { get; } = year;
    public int Month { get; } = month;
    public int Day { get; } = day;
    public string Calendar { get; } = calendar;

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
    ///     The ISO 8601 week number.
    /// </summary>
    public int WeekOfYear
    {
        get
        {
            var dt = new DateTime(Year, Month, Day);
            return ISOWeek.GetWeekOfYear(dt);
        }
    }

    /// <summary>
    ///     The year that the ISO week belongs to.
    ///     Near year boundaries, this may differ from the calendar year.
    /// </summary>
    public int YearOfWeek
    {
        get
        {
            var dt = new DateTime(Year, Month, Day);
            return ISOWeek.GetYear(dt);
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
    ///     Creates a PlainDate from an ISO 8601 string.
    ///     Supports YYYY-MM-DD and extended year format (+/-YYYYYY-MM-DD).
    /// </summary>
    public static JsTemporalPlainDate From(string isoString)
    {
        var s = isoString;

        // Strip calendar annotation if present (e.g., [u-ca=iso8601])
        var bracketIdx = s.IndexOf('[');
        var calendar = "iso8601";
        if (bracketIdx >= 0)
        {
            var annotation = s[(bracketIdx + 1)..].TrimEnd(']');
            if (annotation.StartsWith("u-ca=", StringComparison.Ordinal))
            {
                calendar = annotation[5..];
            }
            s = s[..bracketIdx];
        }

        // Strip time portion if present (e.g., 2020-01-01T00:00:00)
        var tIdx = s.IndexOf('T');
        if (tIdx >= 0)
        {
            s = s[..tIdx];
        }

        // Try standard YYYY-MM-DD format first
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            return new JsTemporalPlainDate(date, calendar);
        }

        // Handle extended year format: +YYYYYY-MM-DD or -YYYYYY-MM-DD
        if (s.Length > 0 && (s[0] == '+' || s[0] == '-' || s[0] == '\u2212'))
        {
            var sign = s[0] == '-' || s[0] == '\u2212' ? -1 : 1;
            var rest = s[1..];
            // Find the month-day portion (last 6 chars: -MM-DD)
            var lastDash = rest.LastIndexOf('-');
            if (lastDash > 0)
            {
                var secondLastDash = rest.LastIndexOf('-', lastDash - 1);
                if (secondLastDash > 0)
                {
                    var yearStr = rest[..secondLastDash];
                    var monthStr = rest[(secondLastDash + 1)..lastDash];
                    var dayStr = rest[(lastDash + 1)..];

                    if (int.TryParse(yearStr, CultureInfo.InvariantCulture, out var year) &&
                        int.TryParse(monthStr, CultureInfo.InvariantCulture, out var month) &&
                        int.TryParse(dayStr, CultureInfo.InvariantCulture, out var day))
                    {
                        return new JsTemporalPlainDate(sign * year, month, day, calendar);
                    }
                }
            }
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
    ///     The duration's time units should already be balanced into days before calling.
    /// </summary>
    public JsTemporalPlainDate Add(JsTemporalDuration duration, string overflow = "constrain")
    {
        // Add years and months
        var newYear = Year + (long)duration.Years;
        var newMonth = Month + (long)duration.Months;

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

        var intYear = (int)newYear;
        var intMonth = (int)newMonth;

        // DaysInMonth needs a valid year for leap year calculation;
        // use the actual year but handle extreme ranges
        var daysInNewMonth = ISODaysInMonth(intYear, intMonth);

        if (string.Equals(overflow, "reject", StringComparison.Ordinal))
        {
            // In reject mode, if the original day doesn't fit in the new month, throw
            if (Day > daysInNewMonth)
            {
                throw new ArgumentException($"Day {Day} does not exist in month {intMonth} of year {intYear}");
            }
        }

        // Clamp day to valid range for new month (constrain behavior)
        var newDay = Math.Min(Day, daysInNewMonth);

        // Add weeks and days using epoch day arithmetic to support full Temporal range
        var totalDays = (long)(duration.Weeks * 7 + duration.Days);
        var epochDay = ISODateToEpochDay(intYear, intMonth, newDay) + totalDays;

        // Convert back to calendar date
        var (resultYear, resultMonth, resultDay) = EpochDayToISODate(epochDay);
        return new JsTemporalPlainDate(resultYear, resultMonth, resultDay, Calendar);
    }

    /// <summary>
    ///     Subtracts a duration from this date.
    ///     The duration's time units should already be balanced into days before calling.
    /// </summary>
    public JsTemporalPlainDate Subtract(JsTemporalDuration duration, string overflow = "constrain")
    {
        return Add(duration.Negated(), overflow);
    }

    /// <summary>
    ///     Returns the number of days in the given ISO month, handling any year.
    /// </summary>
    private static int ISODaysInMonth(int year, int month)
    {
        return month switch
        {
            1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
            4 or 6 or 9 or 11 => 30,
            2 => IsISOLeapYear(year) ? 29 : 28,
            _ => throw new ArgumentOutOfRangeException(nameof(month))
        };
    }

    /// <summary>
    ///     Returns whether the year is a leap year in the ISO 8601 calendar.
    /// </summary>
    private static bool IsISOLeapYear(int year)
    {
        return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
    }

    /// <summary>
    ///     Converts an ISO date to an epoch day number (days since 1970-01-01).
    ///     Uses a standard algorithm that supports the full Temporal date range.
    /// </summary>
    private static long ISODateToEpochDay(int year, int month, int day)
    {
        // Algorithm from https://howardhinnant.github.io/date_algorithms.html
        var y = (long)year;
        if (month <= 2)
        {
            y--;
        }

        var era = (y >= 0 ? y : y - 399) / 400;
        var yoe = y - era * 400;                                    // year of era [0, 399]
        var doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1; // day of year [0, 365]
        var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;          // day of era [0, 146096]
        return era * 146097 + doe - 719468;                         // epoch day
    }

    /// <summary>
    ///     Converts an epoch day number back to an ISO date (year, month, day).
    /// </summary>
    private static (int year, int month, int day) EpochDayToISODate(long epochDay)
    {
        // Algorithm from https://howardhinnant.github.io/date_algorithms.html
        var z = epochDay + 719468;
        var era = (z >= 0 ? z : z - 146096) / 146097;
        var doe = z - era * 146097;                                  // day of era [0, 146096]
        var yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365; // year of era [0, 399]
        var y = yoe + era * 400;
        var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);          // day of year [0, 365]
        var mp = (5 * doy + 2) / 153;                                // month index [0, 11]
        var d = (int)(doy - (153 * mp + 2) / 5 + 1);                // day [1, 31]
        var m = (int)(mp + (mp < 10 ? 3 : -9));                     // month [1, 12]
        var yr = (int)(y + (m <= 2 ? 1 : 0));
        return (yr, m, d);
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
        if (other is null)
        {
            return 1;
        }

        var c = Year.CompareTo(other.Year);
        if (c != 0)
        {
            return c;
        }

        c = Month.CompareTo(other.Month);
        return c != 0 ? c : Day.CompareTo(other.Day);
    }

    public bool Equals(JsTemporalPlainDate? other)
    {
        if (other is null)
        {
            return false;
        }

        return Year == other.Year && Month == other.Month && Day == other.Day &&
               string.Equals(Calendar, other.Calendar, StringComparison.Ordinal);
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
    ///     Returns basic ISO date string without calendar annotation.
    /// </summary>
    public string ToStringBasic()
    {
        return FormatYear() + $"-{Month:D2}-{Day:D2}";
    }

    /// <summary>
    ///     Returns ISO 8601 date string.
    ///     Standard years: YYYY-MM-DD. Extended years: +YYYYYY-MM-DD or -YYYYYY-MM-DD.
    ///     Includes calendar annotation for non-ISO calendars.
    /// </summary>
    public override string ToString()
    {
        var result = FormatYear() + $"-{Month:D2}-{Day:D2}";
        if (!string.Equals(Calendar, "iso8601", StringComparison.Ordinal))
        {
            result += $"[u-ca={Calendar}]";
        }
        return result;
    }

    /// <summary>
    ///     Returns ISO date string with calendar annotation (YYYY-MM-DD[u-ca=calendar]).
    ///     When critical is true, uses [!u-ca=calendar] format.
    /// </summary>
    public string ToStringWithCalendar(bool critical = false)
    {
        var prefix = critical ? "!" : "";
        return FormatYear() + $"-{Month:D2}-{Day:D2}[{prefix}u-ca={Calendar}]";
    }

    private string FormatYear()
    {
        if (Year is >= 0 and <= 9999)
        {
            return Year.ToString("D4", CultureInfo.InvariantCulture);
        }
        var absYear = Math.Abs(Year);
        return (Year < 0 ? "-" : "+") + absYear.ToString("D6", CultureInfo.InvariantCulture);
    }

    public static bool operator ==(JsTemporalPlainDate? left, JsTemporalPlainDate? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    public static bool operator !=(JsTemporalPlainDate? left, JsTemporalPlainDate? right) => !(left == right);
    public static bool operator <(JsTemporalPlainDate left, JsTemporalPlainDate right) => left.CompareTo(right) < 0;
    public static bool operator <=(JsTemporalPlainDate left, JsTemporalPlainDate right) => left.CompareTo(right) <= 0;
    public static bool operator >(JsTemporalPlainDate left, JsTemporalPlainDate right) => left.CompareTo(right) > 0;
    public static bool operator >=(JsTemporalPlainDate left, JsTemporalPlainDate right) => left.CompareTo(right) >= 0;
}
