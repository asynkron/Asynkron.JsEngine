#region

using System.Globalization;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a Temporal.PlainDateTime - a calendar date and wall-clock time without timezone.
///     Combines PlainDate and PlainTime.
/// </summary>
public sealed class JsTemporalPlainDateTime : IEquatable<JsTemporalPlainDateTime>, IComparable<JsTemporalPlainDateTime>
{
    public JsTemporalPlainDateTime(
        int year, int month, int day,
        int hour = 0, int minute = 0, int second = 0,
        int millisecond = 0, int microsecond = 0, int nanosecond = 0,
        string calendar = "iso8601")
    {
        Date = new JsTemporalPlainDate(year, month, day, calendar);
        Time = new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond);
    }

    public JsTemporalPlainDateTime(JsTemporalPlainDate date, JsTemporalPlainTime time)
    {
        Date = date;
        Time = time;
    }

    public JsTemporalPlainDateTime(DateTime dateTime, string calendar = "iso8601")
        : this(dateTime.Year, dateTime.Month, dateTime.Day,
            dateTime.Hour, dateTime.Minute, dateTime.Second,
            dateTime.Millisecond, dateTime.Microsecond, 0, calendar)
    {
    }

    public JsTemporalPlainDate Date { get; }
    public JsTemporalPlainTime Time { get; }

    // Date properties
    public int Year => Date.Year;
    public int Month => Date.Month;
    public int Day => Date.Day;
    public string Calendar => Date.Calendar;
    public string MonthCode => Date.MonthCode;
    public int DayOfWeek => Date.DayOfWeek;
    public int DayOfYear => Date.DayOfYear;
    public int WeekOfYear => Date.WeekOfYear;
    public int DaysInMonth => Date.DaysInMonth;
    public int DaysInYear => Date.DaysInYear;
    public int MonthsInYear => Date.MonthsInYear;
    public bool InLeapYear => Date.InLeapYear;

    // Time properties
    public int Hour => Time.Hour;
    public int Minute => Time.Minute;
    public int Second => Time.Second;
    public int Millisecond => Time.Millisecond;
    public int Microsecond => Time.Microsecond;
    public int Nanosecond => Time.Nanosecond;

    /// <summary>
    ///     Creates a PlainDateTime for now in the system time zone.
    /// </summary>
    public static JsTemporalPlainDateTime Now(TimeZoneInfo? timeZone = null)
    {
        timeZone ??= TimeZoneInfo.Local;
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        return new JsTemporalPlainDateTime(now.DateTime);
    }

    /// <summary>
    ///     Creates a PlainDateTime from an ISO 8601 string.
    /// </summary>
    public static JsTemporalPlainDateTime From(string isoString)
    {
        // Simple parsing - handle YYYY-MM-DDTHH:mm:ss format
        var tIndex = isoString.IndexOf('T');
        if (tIndex < 0)
        {
            // Date only
            var date = JsTemporalPlainDate.From(isoString);
            return new JsTemporalPlainDateTime(date, JsTemporalPlainTime.Midnight);
        }

        var datePart = isoString[..tIndex];
        var timePart = isoString[(tIndex + 1)..];

        // Remove timezone suffix if present
        var bracketIndex = timePart.IndexOf('[');
        if (bracketIndex >= 0)
        {
            timePart = timePart[..bracketIndex];
        }

        if (timePart.EndsWith('Z'))
        {
            timePart = timePart[..^1];
        }

        var plusIndex = timePart.IndexOf('+');
        if (plusIndex >= 0)
        {
            timePart = timePart[..plusIndex];
        }

        var minusIndex = timePart.LastIndexOf('-');
        if (minusIndex > 0 && timePart[minusIndex - 1] != 'T')
        {
            // Check if it's an offset like -05:00
            if (minusIndex + 3 <= timePart.Length && timePart.Substring(minusIndex + 1).Contains(':'))
            {
                timePart = timePart[..minusIndex];
            }
        }

        var date2 = JsTemporalPlainDate.From(datePart);
        var time = JsTemporalPlainTime.From(timePart);

        return new JsTemporalPlainDateTime(date2, time);
    }

    /// <summary>
    ///     Converts to .NET DateTime (loses nanosecond precision).
    /// </summary>
    public DateTime ToDateTime()
    {
        return new DateTime(
            Year, Month, Day,
            Hour, Minute, Second,
            Millisecond, Microsecond);
    }

    /// <summary>
    ///     Returns a new PlainDateTime with modified fields.
    /// </summary>
    public JsTemporalPlainDateTime With(
        int? year = null, int? month = null, int? day = null,
        int? hour = null, int? minute = null, int? second = null,
        int? millisecond = null, int? microsecond = null, int? nanosecond = null)
    {
        return new JsTemporalPlainDateTime(
            Date.With(year, month, day),
            Time.With(hour, minute, second, millisecond, microsecond, nanosecond));
    }

    /// <summary>
    ///     Returns a new PlainDateTime with a different time.
    /// </summary>
    public JsTemporalPlainDateTime WithPlainTime(JsTemporalPlainTime time)
    {
        return new JsTemporalPlainDateTime(Date, time);
    }

    /// <summary>
    ///     Returns a new PlainDateTime with a different date.
    /// </summary>
    public JsTemporalPlainDateTime WithPlainDate(JsTemporalPlainDate date)
    {
        return new JsTemporalPlainDateTime(date, Time);
    }

    /// <summary>
    ///     Adds a duration to this datetime.
    /// </summary>
    public JsTemporalPlainDateTime Add(JsTemporalDuration duration)
    {
        // Add date part first (years, months, weeks, days)
        var newDate = Date.Add(JsTemporalDuration.From(
            years: duration.Years,
            months: duration.Months,
            weeks: duration.Weeks,
            days: duration.Days));

        // Add time part and handle day overflow
        var totalNanos = Time.TotalNanoseconds +
                         (long)(duration.Hours * 3_600_000_000_000.0) +
                         (long)(duration.Minutes * 60_000_000_000.0) +
                         (long)(duration.Seconds * 1_000_000_000.0) +
                         (long)(duration.Milliseconds * 1_000_000.0) +
                         (long)(duration.Microseconds * 1_000.0) +
                         (long)duration.Nanoseconds;

        const long nanosPerDay = 24L * 60 * 60 * 1_000_000_000;
        var dayOverflow = (int)(totalNanos / nanosPerDay);
        totalNanos = ((totalNanos % nanosPerDay) + nanosPerDay) % nanosPerDay;

        if (dayOverflow != 0)
        {
            newDate = newDate.Add(JsTemporalDuration.From(days: dayOverflow));
        }

        var newTime = JsTemporalPlainTime.From(
            new TimeSpan(totalNanos / 100).ToString(@"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture));

        return new JsTemporalPlainDateTime(newDate, newTime);
    }

    /// <summary>
    ///     Subtracts a duration from this datetime.
    /// </summary>
    public JsTemporalPlainDateTime Subtract(JsTemporalDuration duration)
    {
        return Add(duration.Negated());
    }

    /// <summary>
    ///     Returns the duration until another datetime.
    /// </summary>
    public JsTemporalDuration Until(JsTemporalPlainDateTime other)
    {
        var days = other.Date.ToDateOnly().DayNumber - Date.ToDateOnly().DayNumber;
        var nanos = other.Time.TotalNanoseconds - Time.TotalNanoseconds;

        return JsTemporalDuration.From(days: days, nanoseconds: nanos);
    }

    /// <summary>
    ///     Returns the duration since another datetime.
    /// </summary>
    public JsTemporalDuration Since(JsTemporalPlainDateTime other)
    {
        return other.Until(this);
    }

    /// <summary>
    ///     Rounds the datetime to the specified unit.
    /// </summary>
    public JsTemporalPlainDateTime Round(string smallestUnit)
    {
        // Round the time component only (date components are not rounded)
        var roundedTime = Time.Round(smallestUnit);
        return new JsTemporalPlainDateTime(Date, roundedTime);
    }

    /// <summary>
    ///     Extracts just the date component.
    /// </summary>
    public JsTemporalPlainDate ToPlainDate() => Date;

    /// <summary>
    ///     Extracts just the time component.
    /// </summary>
    public JsTemporalPlainTime ToPlainTime() => Time;

    public int CompareTo(JsTemporalPlainDateTime? other)
    {
        if (other is null)
        {
            return 1;
        }

        var dateCompare = Date.CompareTo(other.Date);
        return dateCompare != 0 ? dateCompare : Time.CompareTo(other.Time);
    }

    public bool Equals(JsTemporalPlainDateTime? other)
    {
        if (other is null)
        {
            return false;
        }

        return Date.Equals(other.Date) && Time.Equals(other.Time);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsTemporalPlainDateTime other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Date, Time);
    }

    /// <summary>
    ///     Returns ISO 8601 datetime string (YYYY-MM-DDTHH:mm:ss.sssssssss).
    /// </summary>
    public override string ToString()
    {
        var datePart = Date.ToString();
        var timePart = Time.ToString();

        // Remove calendar annotation from date if present (we'll add it at the end)
        var bracketIndex = datePart.IndexOf('[');
        var calendarAnnotation = bracketIndex >= 0 ? datePart[bracketIndex..] : "";
        if (bracketIndex >= 0)
        {
            datePart = datePart[..bracketIndex];
        }

        return $"{datePart}T{timePart}{calendarAnnotation}";
    }

    public static bool operator ==(JsTemporalPlainDateTime? left, JsTemporalPlainDateTime? right)
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

    public static bool operator !=(JsTemporalPlainDateTime? left, JsTemporalPlainDateTime? right) => !(left == right);
    public static bool operator <(JsTemporalPlainDateTime left, JsTemporalPlainDateTime right) => left.CompareTo(right) < 0;
    public static bool operator <=(JsTemporalPlainDateTime left, JsTemporalPlainDateTime right) => left.CompareTo(right) <= 0;
    public static bool operator >(JsTemporalPlainDateTime left, JsTemporalPlainDateTime right) => left.CompareTo(right) > 0;
    public static bool operator >=(JsTemporalPlainDateTime left, JsTemporalPlainDateTime right) => left.CompareTo(right) >= 0;
}
