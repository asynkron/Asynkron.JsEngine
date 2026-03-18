#region

using System.Globalization;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a Temporal.PlainYearMonth - a year and month without day or time.
///     Useful for representing things like "December 2024" or credit card expiration dates.
/// </summary>
public sealed class JsTemporalPlainYearMonth : IEquatable<JsTemporalPlainYearMonth>, IComparable<JsTemporalPlainYearMonth>
{
    public JsTemporalPlainYearMonth(int year, int month, string calendar = "iso8601", int? referenceDay = null)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1-12");
        }

        Year = year;
        Month = month;
        Calendar = calendar;
        // Reference day is used internally for calendar calculations
        ReferenceDay = referenceDay ?? 1;
    }

    public int Year { get; }
    public int Month { get; }
    public string Calendar { get; }
    internal int ReferenceDay { get; }

    /// <summary>
    ///     The month code (e.g., "M01" for January).
    /// </summary>
    public string MonthCode => $"M{Month:D2}";

    /// <summary>
    ///     The number of days in this month.
    /// </summary>
    public int DaysInMonth => IsoCalendarHelpers.DaysInMonth(Year, Month);

    /// <summary>
    ///     The number of days in this year.
    /// </summary>
    public int DaysInYear => IsoCalendarHelpers.IsLeapYear(Year) ? 366 : 365;

    /// <summary>
    ///     The number of months in this year (always 12 for ISO calendar).
    /// </summary>
    public int MonthsInYear => 12;

    /// <summary>
    ///     Whether this year is a leap year.
    /// </summary>
    public bool InLeapYear => IsoCalendarHelpers.IsLeapYear(Year);

    /// <summary>
    ///     Creates a PlainYearMonth from an ISO 8601 string (YYYY-MM).
    ///     Supports extended year format: +YYYYYY-MM or -YYYYYY-MM.
    /// </summary>
    public static JsTemporalPlainYearMonth From(string isoString)
    {
        var s = isoString;

        // Strip calendar annotation if present
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

        // Strip time portion if present
        var tIdx = s.IndexOf('T');
        if (tIdx >= 0)
        {
            s = s[..tIdx];
        }

        // Handle extended year format: +YYYYYY-MM[-DD] or -YYYYYY-MM[-DD]
        if (s.Length > 0 && (s[0] == '+' || s[0] == '-' || s[0] == '\u2212'))
        {
            var sign = s[0] == '-' || s[0] == '\u2212' ? -1 : 1;
            var rest = s[1..];
            // Find month separator: last '-' that separates month (or second-to-last if DD present)
            var lastDash = rest.LastIndexOf('-');
            if (lastDash > 0)
            {
                var secondLastDash = rest.LastIndexOf('-', lastDash - 1);
                if (secondLastDash > 0)
                {
                    // Format: YYYYYY-MM-DD — use year and month
                    var yearStr = rest[..secondLastDash];
                    var monthStr = rest[(secondLastDash + 1)..lastDash];
                    if (int.TryParse(yearStr, CultureInfo.InvariantCulture, out var year) &&
                        int.TryParse(monthStr, CultureInfo.InvariantCulture, out var month))
                    {
                        return new JsTemporalPlainYearMonth(sign * year, month, calendar);
                    }
                }
                else
                {
                    // Format: YYYYYY-MM
                    var yearStr = rest[..lastDash];
                    var monthStr = rest[(lastDash + 1)..];
                    if (int.TryParse(yearStr, CultureInfo.InvariantCulture, out var year) &&
                        int.TryParse(monthStr, CultureInfo.InvariantCulture, out var month))
                    {
                        return new JsTemporalPlainYearMonth(sign * year, month, calendar);
                    }
                }
            }
        }

        // Standard format: YYYY-MM or YYYY-MM-DD
        var parts = s.Split('-');
        if (parts.Length < 2)
        {
            throw new FormatException($"Invalid PlainYearMonth string: {isoString}");
        }

        var stdYear = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var stdMonth = int.Parse(parts[1], CultureInfo.InvariantCulture);

        return new JsTemporalPlainYearMonth(stdYear, stdMonth, calendar);
    }

    /// <summary>
    ///     Returns a new PlainYearMonth with modified fields.
    /// </summary>
    public JsTemporalPlainYearMonth With(int? year = null, int? month = null)
    {
        return new JsTemporalPlainYearMonth(
            year ?? Year,
            month ?? Month,
            Calendar);
    }

    /// <summary>
    ///     Adds a duration (years and months) to this YearMonth.
    /// </summary>
    public JsTemporalPlainYearMonth Add(JsTemporalDuration duration)
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

        return new JsTemporalPlainYearMonth(newYear, newMonth, Calendar);
    }

    /// <summary>
    ///     Subtracts a duration from this YearMonth.
    /// </summary>
    public JsTemporalPlainYearMonth Subtract(JsTemporalDuration duration)
    {
        return Add(duration.Negated());
    }

    /// <summary>
    ///     Returns the duration until another YearMonth.
    /// </summary>
    public JsTemporalDuration Until(JsTemporalPlainYearMonth other)
    {
        var totalMonths = (other.Year - Year) * 12 + (other.Month - Month);
        var years = totalMonths / 12;
        var months = totalMonths % 12;
        return JsTemporalDuration.From(years: years, months: months);
    }

    /// <summary>
    ///     Returns the duration since another YearMonth.
    /// </summary>
    public JsTemporalDuration Since(JsTemporalPlainYearMonth other)
    {
        return other.Until(this);
    }

    /// <summary>
    ///     Creates a PlainDate on a specific day of this month.
    /// </summary>
    public JsTemporalPlainDate ToPlainDate(int day)
    {
        if (day < 1 || day > DaysInMonth)
        {
            throw new ArgumentOutOfRangeException(nameof(day), $"Day must be 1-{DaysInMonth} for {this}");
        }

        return new JsTemporalPlainDate(Year, Month, day, Calendar);
    }

    public int CompareTo(JsTemporalPlainYearMonth? other)
    {
        if (other is null)
        {
            return 1;
        }

        var yearCompare = Year.CompareTo(other.Year);
        return yearCompare != 0 ? yearCompare : Month.CompareTo(other.Month);
    }

    public bool Equals(JsTemporalPlainYearMonth? other)
    {
        if (other is null)
        {
            return false;
        }

        return Year == other.Year && Month == other.Month && string.Equals(Calendar, other.Calendar, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsTemporalPlainYearMonth other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Year, Month, Calendar);
    }

    /// <summary>
    ///     Returns basic year-month string (YYYY-MM) without calendar annotation.
    /// </summary>
    public string ToStringBasic()
    {
        return FormatYear() + $"-{Month:D2}";
    }

    /// <summary>
    ///     Returns ISO 8601 year-month string (YYYY-MM), with calendar annotation for non-ISO calendars.
    /// </summary>
    public override string ToString()
    {
        var result = FormatYear() + $"-{Month:D2}";
        if (!string.Equals(Calendar, "iso8601", StringComparison.Ordinal))
        {
            result += $"-{ReferenceDay:D2}[u-ca={Calendar}]";
        }
        return result;
    }

    /// <summary>
    ///     Returns year-month string with reference day and calendar annotation (YYYY-MM-DD[u-ca=calendar]).
    /// </summary>
    public string ToStringWithCalendar()
    {
        return FormatYear() + $"-{Month:D2}-{ReferenceDay:D2}[u-ca={Calendar}]";
    }

    private string FormatYear()
    {
        if (Year is >= 0 and <= 9999)
        {
            return Year.ToString("D4", CultureInfo.InvariantCulture);
        }
        // Extended year format with sign prefix and 6 digits
        var absYear = Math.Abs(Year);
        return (Year < 0 ? "-" : "+") + absYear.ToString("D6", CultureInfo.InvariantCulture);
    }

    public static bool operator ==(JsTemporalPlainYearMonth? left, JsTemporalPlainYearMonth? right)
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

    public static bool operator !=(JsTemporalPlainYearMonth? left, JsTemporalPlainYearMonth? right) => !(left == right);
    public static bool operator <(JsTemporalPlainYearMonth left, JsTemporalPlainYearMonth right) => left.CompareTo(right) < 0;
    public static bool operator <=(JsTemporalPlainYearMonth left, JsTemporalPlainYearMonth right) => left.CompareTo(right) <= 0;
    public static bool operator >(JsTemporalPlainYearMonth left, JsTemporalPlainYearMonth right) => left.CompareTo(right) > 0;
    public static bool operator >=(JsTemporalPlainYearMonth left, JsTemporalPlainYearMonth right) => left.CompareTo(right) >= 0;
}
