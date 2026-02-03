#region

using System.Globalization;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a Temporal.ZonedDateTime - a date/time with timezone awareness.
///     Combines an Instant with a TimeZone to provide wall-clock time in a specific timezone.
/// </summary>
public sealed class JsTemporalZonedDateTime : IEquatable<JsTemporalZonedDateTime>, IComparable<JsTemporalZonedDateTime>
{
    public JsTemporalZonedDateTime(
        JsTemporalInstant instant,
        string timeZoneId,
        string calendar = "iso8601")
    {
        Instant = instant;
        TimeZoneId = timeZoneId;
        Calendar = calendar;
        TimeZone = ResolveTimeZone(timeZoneId, out var fixedOffset);
        FixedOffset = fixedOffset;
    }

    public JsTemporalZonedDateTime(
        int year, int month, int day,
        int hour, int minute, int second,

        int millisecond, int microsecond,
#pragma warning disable RCS1163
        // ReSharper disable once UnusedParameter.Local
        int nanosecond,
#pragma warning restore RCS1163
        string timeZoneId,
        string calendar = "iso8601")
    {
        TimeZoneId = timeZoneId;
        Calendar = calendar;
        TimeZone = ResolveTimeZone(timeZoneId, out var fixedOffset);
        FixedOffset = fixedOffset;

        // Create a DateTime in the specified timezone and convert to Instant
        var localDateTime = new DateTime(year, month, day, hour, minute, second, millisecond, microsecond);
        var offset = FixedOffset ?? TimeZone.GetUtcOffset(localDateTime);
        var utcDateTime = localDateTime - offset;
        Instant = new JsTemporalInstant(new DateTimeOffset(utcDateTime, TimeSpan.Zero));
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId, out TimeSpan? fixedOffset)
    {
        fixedOffset = null;
        if (TryParseOffsetTimeZone(timeZoneId, out var offset))
        {
            fixedOffset = offset;
            return TimeZoneInfo.Utc;
        }

        if (string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            fixedOffset = TimeSpan.Zero;
            return TimeZoneInfo.Utc;
        }

        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    private static bool TryParseOffsetTimeZone(string timeZoneId, out TimeSpan offset)
    {
        offset = default;
        if (string.IsNullOrEmpty(timeZoneId))
        {
            return false;
        }

        var offsetId = timeZoneId;
        if (string.Equals(offsetId, "Z", StringComparison.OrdinalIgnoreCase))
        {
            offset = TimeSpan.Zero;
            return true;
        }

        if (offsetId.StartsWith("UTC", StringComparison.OrdinalIgnoreCase))
        {
            if (offsetId.Length == 3)
            {
                offset = TimeSpan.Zero;
                return true;
            }

            offsetId = offsetId[3..];
        }

        if (offsetId.Length < 3 || (offsetId[0] != '+' && offsetId[0] != '-'))
        {
            return false;
        }

        var sign = offsetId[0] == '-' ? -1 : 1;
        var offsetBody = offsetId[1..];
        var hours = 0;
        var minutes = 0;
        var seconds = 0;

        var parts = offsetBody.Split(':');
        if (parts.Length == 1)
        {
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours))
            {
                return false;
            }
        }
        else
        {
            if (parts.Length < 2 || parts.Length > 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
            {
                return false;
            }

            if (parts.Length == 3 &&
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
            {
                return false;
            }
        }

        if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59 || seconds < 0 || seconds > 59)
        {
            return false;
        }

        var totalSeconds = sign * (hours * 3600 + minutes * 60 + seconds);
        offset = TimeSpan.FromSeconds(totalSeconds);
        return true;
    }

    public JsTemporalInstant Instant { get; }
    public string TimeZoneId { get; }
    public string Calendar { get; }
    public TimeZoneInfo TimeZone { get; }
    public TimeSpan? FixedOffset { get; }

    /// <summary>
    ///     Gets the wall-clock datetime in the timezone.
    /// </summary>
    private DateTimeOffset LocalDateTimeOffset
    {
        get
        {
            var utc = Instant.ToDateTimeOffset();
            return FixedOffset.HasValue ? utc.ToOffset(FixedOffset.Value) : TimeZoneInfo.ConvertTime(utc, TimeZone);
        }
    }

    // Date/time components in wall-clock time
    public int Year => LocalDateTimeOffset.Year;
    public int Month => LocalDateTimeOffset.Month;
    public int Day => LocalDateTimeOffset.Day;
    public int Hour => LocalDateTimeOffset.Hour;
    public int Minute => LocalDateTimeOffset.Minute;
    public int Second => LocalDateTimeOffset.Second;
    public int Millisecond => LocalDateTimeOffset.Millisecond;
    public int Microsecond => LocalDateTimeOffset.Microsecond;

    /// <summary>
    ///     Nanoseconds component (0-999). Note: .NET doesn't track nanoseconds, so we derive from Instant.
    /// </summary>
    public int Nanosecond
    {
        get
        {
            var nanos = (int)(Instant.EpochNanoseconds % 1000);
            return nanos < 0 ? nanos + 1000 : nanos;
        }
    }

    public long EpochMilliseconds => Instant.EpochMilliseconds;
    public long EpochSeconds => Instant.EpochSeconds;

    /// <summary>
    ///     The month code (e.g., "M01" for January).
    /// </summary>
    public string MonthCode => $"M{Month:D2}";

    /// <summary>
    ///     The day of the week (1 = Monday, 7 = Sunday per ISO 8601).
    /// </summary>
    public int DayOfWeek
    {
        get
        {
            var dow = LocalDateTimeOffset.DayOfWeek;
            return dow == System.DayOfWeek.Sunday ? 7 : (int)dow;
        }
    }

    /// <summary>
    ///     The day of the year (1-366).
    /// </summary>
    public int DayOfYear => LocalDateTimeOffset.DayOfYear;

    /// <summary>
    ///     The ISO week number.
    /// </summary>
    public int WeekOfYear
    {
        get
        {
            var culture = CultureInfo.InvariantCulture;
            return culture.Calendar.GetWeekOfYear(
                LocalDateTimeOffset.DateTime,
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
    ///     Whether the current year is a leap year.
    /// </summary>
    public bool InLeapYear => DateTime.IsLeapYear(Year);

    /// <summary>
    ///     The timezone offset in nanoseconds.
    /// </summary>
    public long OffsetNanoseconds
    {
        get
        {
            var offset = FixedOffset ?? TimeZone.GetUtcOffset(LocalDateTimeOffset.DateTime);
            return (long)offset.TotalMilliseconds * 1_000_000;
        }
    }

    /// <summary>
    ///     The timezone offset as a string (e.g., "+01:00").
    /// </summary>
    public string Offset
    {
        get
        {
            var offset = FixedOffset ?? TimeZone.GetUtcOffset(LocalDateTimeOffset.DateTime);
            var sign = offset >= TimeSpan.Zero ? "+" : "-";
            var absOffset = offset.Duration();
            return $"{sign}{absOffset.Hours:D2}:{absOffset.Minutes:D2}";
        }
    }

    /// <summary>
    ///     Creates a ZonedDateTime for now in the specified timezone.
    /// </summary>
    public static JsTemporalZonedDateTime Now(string? timeZoneId = null)
    {
        timeZoneId ??= TimeZoneInfo.Local.Id;
        return new JsTemporalZonedDateTime(JsTemporalInstant.Now(), timeZoneId);
    }

    /// <summary>
    ///     Creates a ZonedDateTime from an ISO 8601 string.
    /// </summary>
    public static JsTemporalZonedDateTime From(string isoString)
    {
        // Parse format: 2024-12-25T10:30:00+01:00[Europe/Paris]
        var bracketIndex = isoString.IndexOf('[');
        string? timeZoneId = null;

        if (bracketIndex >= 0)
        {
            var closeBracket = isoString.IndexOf(']', bracketIndex);
            if (closeBracket > bracketIndex)
            {
                timeZoneId = isoString.Substring(bracketIndex + 1, closeBracket - bracketIndex - 1);
            }
            isoString = isoString[..bracketIndex];
        }

        // Parse the datetime with offset
        if (DateTimeOffset.TryParse(isoString, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            timeZoneId ??= TimeZoneInfo.Local.Id;
            var instant = new JsTemporalInstant(dto);
            return new JsTemporalZonedDateTime(instant, timeZoneId);
        }

        throw new FormatException($"Invalid ZonedDateTime string: {isoString}");
    }

    /// <summary>
    ///     Returns a new ZonedDateTime with modified fields.
    /// </summary>
    public JsTemporalZonedDateTime With(
        int? year = null, int? month = null, int? day = null,
        int? hour = null, int? minute = null, int? second = null,
        int? millisecond = null, int? microsecond = null, int? nanosecond = null)
    {
        return new JsTemporalZonedDateTime(
            year ?? Year, month ?? Month, day ?? Day,
            hour ?? Hour, minute ?? Minute, second ?? Second,
            millisecond ?? Millisecond, microsecond ?? Microsecond, nanosecond ?? Nanosecond,
            TimeZoneId, Calendar);
    }

    /// <summary>
    ///     Returns a new ZonedDateTime in a different timezone (same instant).
    /// </summary>
    public JsTemporalZonedDateTime WithTimeZone(string timeZoneId)
    {
        return new JsTemporalZonedDateTime(Instant, timeZoneId, Calendar);
    }

    /// <summary>
    ///     Adds a duration to this ZonedDateTime.
    /// </summary>
    public JsTemporalZonedDateTime Add(JsTemporalDuration duration)
    {
        // Convert to PlainDateTime, add, then convert back
        var pdt = ToPlainDateTime();
        var newPdt = pdt.Add(duration);

        return new JsTemporalZonedDateTime(
            newPdt.Year, newPdt.Month, newPdt.Day,
            newPdt.Hour, newPdt.Minute, newPdt.Second,
            newPdt.Millisecond, newPdt.Microsecond, newPdt.Nanosecond,
            TimeZoneId, Calendar);
    }

    /// <summary>
    ///     Subtracts a duration from this ZonedDateTime.
    /// </summary>
    public JsTemporalZonedDateTime Subtract(JsTemporalDuration duration)
    {
        return Add(duration.Negated());
    }

    /// <summary>
    ///     Extracts the PlainDateTime (wall-clock time).
    /// </summary>
    public JsTemporalPlainDateTime ToPlainDateTime()
    {
        return new JsTemporalPlainDateTime(
            Year, Month, Day,
            Hour, Minute, Second,
            Millisecond, Microsecond, Nanosecond,
            Calendar);
    }

    /// <summary>
    ///     Extracts just the date component.
    /// </summary>
    public JsTemporalPlainDate ToPlainDate()
    {
        return new JsTemporalPlainDate(Year, Month, Day, Calendar);
    }

    /// <summary>
    ///     Extracts just the time component.
    /// </summary>
    public JsTemporalPlainTime ToPlainTime()
    {
        return new JsTemporalPlainTime(Hour, Minute, Second, Millisecond, Microsecond, Nanosecond);
    }

    /// <summary>
    ///     Gets the underlying Instant.
    /// </summary>
    public JsTemporalInstant ToInstant() => Instant;

    public int CompareTo(JsTemporalZonedDateTime? other)
    {
        if (other is null)
        {
            return 1;
        }

        return Instant.CompareTo(other.Instant);
    }

    public bool Equals(JsTemporalZonedDateTime? other)
    {
        if (other is null)
        {
            return false;
        }

        return Instant.Equals(other.Instant) && string.Equals(TimeZoneId, other.TimeZoneId, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsTemporalZonedDateTime other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Instant, TimeZoneId);
    }

    /// <summary>
    ///     Returns ISO 8601 string with timezone (e.g., "2024-12-25T10:30:00+01:00[Europe/Paris]").
    /// </summary>
    public override string ToString()
    {
        var dt = LocalDateTimeOffset;
        var baseStr = $"{dt.Year:D4}-{dt.Month:D2}-{dt.Day:D2}T{dt.Hour:D2}:{dt.Minute:D2}:{dt.Second:D2}";

        // Add fractional seconds if needed
        var totalSubSecondNanos = Millisecond * 1_000_000L + Microsecond * 1_000L + Nanosecond;
        if (totalSubSecondNanos > 0)
        {
            var fractionStr = totalSubSecondNanos.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');
            baseStr += $".{fractionStr}";
        }

        baseStr += Offset;
        baseStr += $"[{TimeZoneId}]";

        if (!string.Equals(Calendar, "iso8601", StringComparison.Ordinal))
        {
            baseStr += $"[u-ca={Calendar}]";
        }

        return baseStr;
    }

    public static bool operator ==(JsTemporalZonedDateTime? left, JsTemporalZonedDateTime? right)
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

    public static bool operator !=(JsTemporalZonedDateTime? left, JsTemporalZonedDateTime? right) => !(left == right);
    public static bool operator <(JsTemporalZonedDateTime left, JsTemporalZonedDateTime right) => left.CompareTo(right) < 0;
    public static bool operator <=(JsTemporalZonedDateTime left, JsTemporalZonedDateTime right) => left.CompareTo(right) <= 0;
    public static bool operator >(JsTemporalZonedDateTime left, JsTemporalZonedDateTime right) => left.CompareTo(right) > 0;
    public static bool operator >=(JsTemporalZonedDateTime left, JsTemporalZonedDateTime right) => left.CompareTo(right) >= 0;
}
