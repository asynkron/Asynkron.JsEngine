#region

using System.Globalization;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a Temporal.PlainMonthDay - a month and day without year or time.
///     Useful for representing recurring dates like birthdays or holidays (e.g., "December 25").
/// </summary>
public sealed class JsTemporalPlainMonthDay : IEquatable<JsTemporalPlainMonthDay>, IComparable<JsTemporalPlainMonthDay>
{
    public JsTemporalPlainMonthDay(int month, int day, string calendar = "iso8601", int? referenceYear = null)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1-12");
        }

        // Validate day based on month (use reference year for leap year handling)
        var yearForValidation = referenceYear ?? 2000; // 2000 is a leap year
        var maxDay = DateTime.DaysInMonth(yearForValidation, month);
        if (day < 1 || day > maxDay)
        {
            throw new ArgumentOutOfRangeException(nameof(day), $"Day must be 1-{maxDay} for month {month}");
        }

        Month = month;
        Day = day;
        Calendar = calendar;
        // Reference year is used internally for calendar calculations
        ReferenceYear = referenceYear ?? 1972; // 1972 is a leap year
    }

    public int Month { get; }
    public int Day { get; }
    public string Calendar { get; }
    internal int ReferenceYear { get; }

    /// <summary>
    ///     The month code (e.g., "M01" for January).
    /// </summary>
    public string MonthCode => $"M{Month:D2}";

    /// <summary>
    ///     Creates a PlainMonthDay from an ISO 8601 string (--MM-DD or MM-DD).
    /// </summary>
    public static JsTemporalPlainMonthDay From(string isoString)
    {
        // Handle format: --MM-DD or MM-DD
        if (isoString.StartsWith("--", StringComparison.Ordinal))
        {
            isoString = isoString[2..];
        }

        var parts = isoString.Split('-');
        if (parts.Length < 2)
        {
            throw new FormatException($"Invalid PlainMonthDay string: {isoString}");
        }

        var month = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var day = int.Parse(parts[1], CultureInfo.InvariantCulture);

        return new JsTemporalPlainMonthDay(month, day);
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

        return Month == other.Month && Day == other.Day && string.Equals(Calendar, other.Calendar, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsTemporalPlainMonthDay other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Month, Day, Calendar);
    }

    /// <summary>
    ///     Returns ISO 8601 month-day string (--MM-DD).
    /// </summary>
    public override string ToString()
    {
        var result = $"--{Month:D2}-{Day:D2}";
        if (!string.Equals(Calendar, "iso8601", StringComparison.Ordinal))
        {
            result += $"[u-ca={Calendar}]";
        }
        return result;
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
