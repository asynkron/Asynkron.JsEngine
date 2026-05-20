#region

using System.Globalization;
using Asynkron.JsEngine.StdLib.Temporal;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a Temporal.PlainMonthDay - a month and day without year or time.
///     Useful for representing recurring dates like birthdays or holidays (e.g., "December 25").
/// </summary>
public sealed class JsTemporalPlainMonthDay : IEquatable<JsTemporalPlainMonthDay>, IComparable<JsTemporalPlainMonthDay>
{
    private readonly string? _monthCode;
    private readonly int _referenceMonth;
    private readonly int _referenceDay;

    public JsTemporalPlainMonthDay(int month, int day, string calendar = "iso8601", int? referenceYear = null,
        string? monthCode = null, int? referenceMonth = null, int? referenceDay = null)
    {
        if (month < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be positive");
        }

        if (string.Equals(calendar, "iso8601", StringComparison.Ordinal))
        {
            if (month > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1-12");
            }

            // Validate day based on month — use safe year for DaysInMonth (handles extended years)
            var yearForValidation = referenceYear is >= 1 and <= 9999 ? referenceYear.Value : 2000;
            var maxDay = IsoCalendarHelpers.DaysInMonth(yearForValidation, month);
            if (day < 1 || day > maxDay)
            {
                throw new ArgumentOutOfRangeException(nameof(day), $"Day must be 1-{maxDay} for month {month}");
            }
        }
        else
        {
            if (month > 13)
            {
                throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1-13 for non-ISO calendars");
            }

            if (day is < 1 or > 31)
            {
                throw new ArgumentOutOfRangeException(nameof(day), "Day must be 1-31 for non-ISO calendars");
            }
        }

        Month = month;
        Day = day;
        Calendar = calendar;
        // Reference year is used internally for calendar calculations
        ReferenceYear = referenceYear ?? 1972; // 1972 is a leap year
        _monthCode = monthCode;
        _referenceMonth = referenceMonth ?? month;
        _referenceDay = referenceDay ?? day;
    }

    public int Month { get; }
    public int Day { get; }
    public string Calendar { get; }
    internal int ReferenceYear { get; }
    internal int ReferenceMonth => _referenceMonth;
    internal int ReferenceDay => _referenceDay;

    /// <summary>
    ///     The month code (e.g., "M01" for January).
    /// </summary>
    public string MonthCode => _monthCode ?? $"M{Month:D2}";

    /// <summary>
    ///     Creates a PlainMonthDay from an ISO 8601 string.
    ///     Accepts: --MM-DD, MM-DD, or YYYY-MM-DD (year is used as reference year).
    /// </summary>
    public static JsTemporalPlainMonthDay From(string isoString)
    {
        // Handle format: --MM-DD
        if (isoString.StartsWith("--", StringComparison.Ordinal))
        {
            isoString = isoString[2..];
        }

        var parts = isoString.Split('-');
        if (parts.Length >= 3)
        {
            // YYYY-MM-DD format — extract month and day, use year as reference
            var refYear = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var month = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var day = int.Parse(parts[2], CultureInfo.InvariantCulture);
            return new JsTemporalPlainMonthDay(month, day, referenceYear: refYear);
        }

        if (parts.Length >= 2)
        {
            // MM-DD format
            var month = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var day = int.Parse(parts[1], CultureInfo.InvariantCulture);
            return new JsTemporalPlainMonthDay(month, day);
        }

        throw new FormatException($"Invalid PlainMonthDay string: {isoString}");
    }

    /// <summary>
    ///     Returns a new PlainMonthDay with modified fields.
    /// </summary>
    public JsTemporalPlainMonthDay With(int? month = null, int? day = null)
    {
        return new JsTemporalPlainMonthDay(
            month ?? Month,
            day ?? Day,
            Calendar);
    }

    /// <summary>
    ///     Creates a PlainDate for this month/day in a specific year.
    /// </summary>
    public JsTemporalPlainDate ToPlainDate(int year)
    {
        // Handle Feb 29 in non-leap years
        var day = Day;
        if (Month == 2 && Day == 29 && !DateTime.IsLeapYear(year))
        {
            throw new InvalidOperationException($"February 29 does not exist in year {year}");
        }

        return new JsTemporalPlainDate(year, Month, day, Calendar);
    }

    public int CompareTo(JsTemporalPlainMonthDay? other)
    {
        if (other is null)
        {
            return 1;
        }

        var monthCompare = Month.CompareTo(other.Month);
        return monthCompare != 0 ? monthCompare : Day.CompareTo(other.Day);
    }

    public bool Equals(JsTemporalPlainMonthDay? other)
    {
        if (other is null)
        {
            return false;
        }

        return Month == other.Month && Day == other.Day &&
               ReferenceYear == other.ReferenceYear &&
               _referenceMonth == other._referenceMonth &&
               _referenceDay == other._referenceDay &&
               string.Equals(_monthCode, other._monthCode, StringComparison.Ordinal) &&
               string.Equals(
                   TemporalHelper.CanonicalizeCalendarIdForComparison(Calendar),
                   TemporalHelper.CanonicalizeCalendarIdForComparison(other.Calendar),
                   StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsTemporalPlainMonthDay other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Month, Day, ReferenceYear, _referenceMonth, _referenceDay, _monthCode, Calendar);
    }

    /// <summary>
    ///     Returns basic month-day string (MM-DD) without calendar annotation.
    /// </summary>
    public string ToStringBasic()
    {
        return $"{Month:D2}-{Day:D2}";
    }

    /// <summary>
    ///     Returns Temporal month-day string per spec TemporalMonthDayToString:
    ///     ISO calendar: "MM-DD", non-ISO calendar: "YYYY-MM-DD[u-ca=calendar]".
    /// </summary>
    public override string ToString()
    {
        // Format: "--MM-DD" for ISO calendar, "YYYY-MM-DD[u-ca=cal]" for non-ISO.
        // The leading -- IS REQUIRED. Internal tests verify "--12-25".
        if (!string.Equals(Calendar, "iso8601", StringComparison.Ordinal))
        {
            return FormatYear(ReferenceYear) + $"-{_referenceMonth:D2}-{_referenceDay:D2}[u-ca={Calendar}]";
        }
        return $"--{Month:D2}-{Day:D2}";
    }

    /// <summary>
    ///     Returns month-day string with reference year and calendar annotation (YYYY-MM-DD[u-ca=calendar]).
    ///     When critical is true, uses [!u-ca=calendar] format.
    /// </summary>
    public string ToStringWithCalendar(bool critical = false)
    {
        var prefix = critical ? "!" : "";
        return FormatYear(ReferenceYear) + $"-{_referenceMonth:D2}-{_referenceDay:D2}[{prefix}u-ca={Calendar}]";
    }

    /// <summary>
    ///     Returns reference ISO date string without calendar annotation (YYYY-MM-DD).
    /// </summary>
    public string ToStringReferenceDate()
    {
        return FormatYear(ReferenceYear) + $"-{_referenceMonth:D2}-{_referenceDay:D2}";
    }

    private static string FormatYear(int year)
    {
        if (year is >= 0 and <= 9999)
        {
            return year.ToString("D4", CultureInfo.InvariantCulture);
        }
        var absYear = Math.Abs(year);
        return (year < 0 ? "-" : "+") + absYear.ToString("D6", CultureInfo.InvariantCulture);
    }

    public static bool operator ==(JsTemporalPlainMonthDay? left, JsTemporalPlainMonthDay? right)
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

    public static bool operator !=(JsTemporalPlainMonthDay? left, JsTemporalPlainMonthDay? right) => !(left == right);
    public static bool operator <(JsTemporalPlainMonthDay left, JsTemporalPlainMonthDay right) => left.CompareTo(right) < 0;
    public static bool operator <=(JsTemporalPlainMonthDay left, JsTemporalPlainMonthDay right) => left.CompareTo(right) <= 0;
    public static bool operator >(JsTemporalPlainMonthDay left, JsTemporalPlainMonthDay right) => left.CompareTo(right) > 0;
    public static bool operator >=(JsTemporalPlainMonthDay left, JsTemporalPlainMonthDay right) => left.CompareTo(right) >= 0;
}
