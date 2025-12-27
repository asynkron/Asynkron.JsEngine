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
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1-12");

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
    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    /// <summary>
    ///     The number of days in this year.
    /// </summary>
    public int DaysInYear => DateTime.IsLeapYear(Year) ? 366 : 365;

    /// <summary>
    ///     The number of months in this year (always 12 for ISO calendar).
    /// </summary>
    public int MonthsInYear => 12;

    /// <summary>
    ///     Whether this year is a leap year.
    /// </summary>
    public bool InLeapYear => DateTime.IsLeapYear(Year);

    /// <summary>
    ///     Creates a PlainYearMonth from an ISO 8601 string (YYYY-MM).
    /// </summary>
    public static JsTemporalPlainYearMonth From(string isoString)
    {
        // Handle format: YYYY-MM or YYYY-MM-DD (ignore day)
        var parts = isoString.Split('-');
        if (parts.Length < 2)
            throw new FormatException($"Invalid PlainYearMonth string: {isoString}");

        var year = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var month = int.Parse(parts[1], CultureInfo.InvariantCulture);

        return new JsTemporalPlainYearMonth(year, month);
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
            throw new ArgumentOutOfRangeException(nameof(day), $"Day must be 1-{DaysInMonth} for {this}");

        return new JsTemporalPlainDate(Year, Month, day, Calendar);
    }

    public int CompareTo(JsTemporalPlainYearMonth? other)
    {
        if (other is null) return 1;
        var yearCompare = Year.CompareTo(other.Year);
        return yearCompare != 0 ? yearCompare : Month.CompareTo(other.Month);
    }

    public bool Equals(JsTemporalPlainYearMonth? other)
    {
        if (other is null) return false;
        return Year == other.Year && Month == other.Month && Calendar == other.Calendar;
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
    ///     Returns ISO 8601 year-month string (YYYY-MM).
    /// </summary>
    public override string ToString()
    {
        var result = $"{Year:D4}-{Month:D2}";
        if (Calendar != "iso8601")
        {
            result += $"[u-ca={Calendar}]";
        }
        return result;
    }

    public static bool operator ==(JsTemporalPlainYearMonth? left, JsTemporalPlainYearMonth? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(JsTemporalPlainYearMonth? left, JsTemporalPlainYearMonth? right) => !(left == right);
    public static bool operator <(JsTemporalPlainYearMonth left, JsTemporalPlainYearMonth right) => left.CompareTo(right) < 0;
    public static bool operator <=(JsTemporalPlainYearMonth left, JsTemporalPlainYearMonth right) => left.CompareTo(right) <= 0;
    public static bool operator >(JsTemporalPlainYearMonth left, JsTemporalPlainYearMonth right) => left.CompareTo(right) > 0;
    public static bool operator >=(JsTemporalPlainYearMonth left, JsTemporalPlainYearMonth right) => left.CompareTo(right) >= 0;
}
