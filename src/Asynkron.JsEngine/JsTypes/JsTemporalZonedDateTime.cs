#region

using System.Globalization;
using System.Numerics;

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
        int nanosecond,
        string timeZoneId,
        string calendar = "iso8601")
    {
        TimeZoneId = timeZoneId;
        Calendar = calendar;
        TimeZone = ResolveTimeZone(timeZoneId, out var fixedOffset);
        FixedOffset = fixedOffset;

        // Determine timezone offset — only create DateTime for non-fixed timezones
        // (DateTime can't handle extreme years outside 1-9999)
        TimeSpan offset;
        if (FixedOffset.HasValue)
        {
            offset = FixedOffset.Value;
        }
        else
        {
            var localDateTime = new DateTime(
                Math.Clamp(year, 1, 9999), month, day,
                hour, minute, second, millisecond, microsecond);
            offset = TimeZone.GetUtcOffset(localDateTime);
        }

        // Compute epoch nanoseconds with full nanosecond precision
        var epochDays = IsoCalendarHelpers.DateToEpochDays(year, month, day);
        var localEpochNanos = new BigInteger(epochDays) * 86_400_000_000_000L
            + (long)hour * 3_600_000_000_000L
            + (long)minute * 60_000_000_000L
            + (long)second * 1_000_000_000L
            + (long)millisecond * 1_000_000L
            + (long)microsecond * 1_000L
            + nanosecond;

        // Subtract timezone offset (ticks are 100ns units) to get UTC epoch nanoseconds
        var offsetNanos = offset.Ticks * 100L;
        Instant = new JsTemporalInstant(localEpochNanos - offsetNanos);
    }

    internal static TimeZoneInfo ResolveTimeZone(string timeZoneId, out TimeSpan? fixedOffset)
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
            // Per Temporal spec, offset time zone identifiers only support ±HH:MM (no seconds)
            if (parts.Length != 2)
            {
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
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
    ///     The ISO 8601 week number.
    /// </summary>
    public int WeekOfYear
    {
        get
        {
            var dt = LocalDateTimeOffset.DateTime;
            return ISOWeek.GetWeekOfYear(dt);
        }
    }

    /// <summary>
    ///     The year that the ISO week belongs to.
    /// </summary>
    public int YearOfWeek
    {
        get
        {
            var dt = LocalDateTimeOffset.DateTime;
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
        var remaining = isoString;
        var bracketIndex = remaining.IndexOf('[');
        string? timeZoneId = null;
        var calendar = "iso8601";

        if (bracketIndex >= 0)
        {
            // Extract all bracket annotations
            var annotations = remaining[bracketIndex..];
            remaining = remaining[..bracketIndex];

            // Parse timezone and calendar from bracket annotations
            var pos = 0;
            while (pos < annotations.Length)
            {
                var open = annotations.IndexOf('[', pos);
                if (open < 0) break;
                var close = annotations.IndexOf(']', open);
                if (close < 0) break;
                var content = annotations[(open + 1)..close];
                if (content.StartsWith("u-ca=", StringComparison.Ordinal))
                {
                    calendar = content[5..];
                }
                else
                {
                    timeZoneId = content;
                }
                pos = close + 1;
            }
        }

        // Try custom parsing first (preserves nanosecond precision, handles all years)
        var parsed = ParseIsoDateTimeWithOffset(remaining);
        if (parsed is not null)
        {
            timeZoneId ??= TimeZoneInfo.Local.Id;

            // If no explicit offset in the string, the parsed instant represents local time
            // in the given timezone — we need to subtract the timezone offset to get UTC
            if (!HasExplicitOffset(remaining))
            {
                var tz = ResolveTimeZone(timeZoneId, out var fixedOff);
                TimeSpan tzOffset;
                if (fixedOff.HasValue)
                {
                    tzOffset = fixedOff.Value;
                }
                else
                {
                    // Use the parsed instant as approximate local time to determine offset
                    var approxLocal = parsed.ToDateTimeOffset().DateTime;
                    tzOffset = tz.GetUtcOffset(approxLocal);
                }
                var offsetNanosTz = tzOffset.Ticks * 100L;
                parsed = JsTemporalInstant.FromEpochNanoseconds(parsed.EpochNanoseconds - offsetNanosTz);
            }

            return new JsTemporalZonedDateTime(parsed, timeZoneId, calendar);
        }

        // Fall back to standard DateTimeOffset parsing
        if (DateTimeOffset.TryParse(remaining, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            timeZoneId ??= TimeZoneInfo.Local.Id;
            var instant = new JsTemporalInstant(dto);
            return new JsTemporalZonedDateTime(instant, timeZoneId, calendar);
        }

        throw new FormatException($"Invalid ZonedDateTime string: {isoString}");
    }

    /// <summary>
    ///     Parses an ISO datetime string with offset into a JsTemporalInstant.
    ///     Handles extended year format, year 0, and nanosecond precision.
    /// </summary>
    internal static JsTemporalInstant? ParseIsoDateTimeWithOffset(string str)
    {
        int year, sign = 1;
        ReadOnlySpan<char> afterDate;

        if (str.Length > 0 && (str[0] == '+' || str[0] == '-' || str[0] == '\u2212'))
        {
            // Extended year format: +YYYYYY-MM-DD or -YYYYYY-MM-DD
            sign = str[0] == '-' || str[0] == '\u2212' ? -1 : 1;
            var rest = str[1..];

            var tIdx = FindDateTimeSeparator(rest);
            if (tIdx < 0)
            {
                // Date-only: treat as midnight
                var datePart2 = rest;
                var lastDash2 = datePart2.LastIndexOf('-');
                if (lastDash2 <= 0) return null;
                var secondLastDash2 = datePart2.LastIndexOf('-', lastDash2 - 1);
                if (secondLastDash2 <= 0) return null;

                if (!int.TryParse(datePart2[..secondLastDash2], CultureInfo.InvariantCulture, out var yearAbs2) ||
                    !int.TryParse(datePart2[(secondLastDash2 + 1)..lastDash2], CultureInfo.InvariantCulture, out var month2) ||
                    !int.TryParse(datePart2[(lastDash2 + 1)..], CultureInfo.InvariantCulture, out var day2))
                    return null;

                // Reject negative zero year (-000000)
                if (sign == -1 && yearAbs2 == 0) return null;

                year = sign * yearAbs2;
                return ComputeInstant(year, month2, day2, "00:00:00".AsSpan());
            }

            var datePart = rest[..tIdx];
            afterDate = rest.AsSpan()[(tIdx + 1)..];

            int yearAbs, month, day;
            if (!datePart.Contains('-') && datePart.Length >= 8)
            {
                // Basic format: YYYYYYMMDD (6+ year digits + 2 month + 2 day)
                var yearLen = datePart.Length - 4; // last 4 chars are MMDD
                if (!int.TryParse(datePart[..yearLen], CultureInfo.InvariantCulture, out yearAbs) ||
                    !int.TryParse(datePart[yearLen..(yearLen + 2)], CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart[(yearLen + 2)..], CultureInfo.InvariantCulture, out day))
                    return null;
            }
            else
            {
                var lastDash = datePart.LastIndexOf('-');
                if (lastDash <= 0) return null;
                var secondLastDash = datePart.LastIndexOf('-', lastDash - 1);
                if (secondLastDash <= 0) return null;

                if (!int.TryParse(datePart[..secondLastDash], CultureInfo.InvariantCulture, out yearAbs) ||
                    !int.TryParse(datePart[(secondLastDash + 1)..lastDash], CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart[(lastDash + 1)..], CultureInfo.InvariantCulture, out day))
                    return null;
            }

            // Reject negative zero year (-000000)
            if (sign == -1 && yearAbs == 0) return null;

            year = sign * yearAbs;
            return ComputeInstant(year, month, day, afterDate);
        }

        // Standard format with year 0000: YYYY-MM-DDTHH:mm:ss...
        {
            var tIdx = FindDateTimeSeparator(str);
            if (tIdx < 0)
            {
                // Date-only: treat as midnight (must be exactly YYYY-MM-DD, no trailing offset)
                var dashParts2 = str.Split('-');
                if (dashParts2.Length != 3) return null;

                if (!int.TryParse(dashParts2[0], CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(dashParts2[1], CultureInfo.InvariantCulture, out var month2) ||
                    !int.TryParse(dashParts2[2], CultureInfo.InvariantCulture, out var day2))
                    return null;

                return ComputeInstant(year, month2, day2, "00:00:00".AsSpan());
            }

            var datePart = str[..tIdx];
            afterDate = str.AsSpan()[(tIdx + 1)..];

            int month, day;
            if (!datePart.Contains('-') && datePart.Length == 8)
            {
                // Basic date format: YYYYMMDD
                if (!int.TryParse(datePart[..4], CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(datePart[4..6], CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart[6..8], CultureInfo.InvariantCulture, out day))
                    return null;
            }
            else
            {
                var dashParts = datePart.Split('-');
                if (dashParts.Length < 3) return null;

                if (!int.TryParse(dashParts[0], CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(dashParts[1], CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(dashParts[2], CultureInfo.InvariantCulture, out day))
                    return null;
            }

            return ComputeInstant(year, month, day, afterDate);
        }
    }

    /// <summary>
    ///     Finds the T, t, or space separator between date and time portions.
    /// </summary>
    private static int FindDateTimeSeparator(string str)
    {
        for (var i = 0; i < str.Length; i++)
        {
            if (str[i] is 'T' or 't' or ' ')
                return i;
        }

        return -1;
    }

    /// <summary>
    ///     Computes epoch nanoseconds from parsed date components and a time+offset span.
    /// </summary>
    private static JsTemporalInstant? ComputeInstant(int year, int month, int day, ReadOnlySpan<char> timeAndOffset)
    {
        // Parse time portion and offset from: HH:mm:ss[.fffffffff][Z|+HH:MM|-HH:MM]
        var timePart = timeAndOffset.ToString();
        var offsetNanos = 0L;

        if (timePart.EndsWith('Z') || timePart.EndsWith('z'))
        {
            timePart = timePart[..^1];
        }
        else
        {
            // Find offset: last + or - that's preceded by digit/colon and followed by digit
            for (var i = timePart.Length - 1; i >= 2; i--)
            {
                if ((timePart[i] == '+' || timePart[i] == '-') && i + 1 < timePart.Length && char.IsDigit(timePart[i + 1]))
                {
                    offsetNanos = ParseOffsetNanos(timePart[i..]);
                    timePart = timePart[..i];
                    break;
                }
            }
        }

        // Normalize comma decimal separator to period (ISO 8601 allows both)
        timePart = timePart.Replace(',', '.');

        // Parse HH:mm:ss[.fffffffff] or compact HHMMSS[.f]
        int hour, minute;
        var second = 0;
        long subSecondNanos = 0;

        if (timePart.Contains(':'))
        {
            var timeParts = timePart.Split(':');
            hour = timeParts.Length > 0 ? int.Parse(timeParts[0], CultureInfo.InvariantCulture) : 0;
            minute = timeParts.Length > 1 ? int.Parse(timeParts[1], CultureInfo.InvariantCulture) : 0;

            if (timeParts.Length > 2)
            {
                var secStr = timeParts[2];
                var dotIdx = secStr.IndexOf('.');
                if (dotIdx >= 0)
                {
                    second = int.Parse(secStr[..dotIdx], CultureInfo.InvariantCulture);
                    var frac = secStr[(dotIdx + 1)..];
                    frac = frac.PadRight(9, '0');
                    if (frac.Length > 9) frac = frac[..9];
                    subSecondNanos = long.Parse(frac, CultureInfo.InvariantCulture);
                }
                else
                {
                    second = int.Parse(secStr, CultureInfo.InvariantCulture);
                }
            }
        }
        else
        {
            // Compact format: HH or HHMM or HHMMSS[.f]
            var dotIdx = timePart.IndexOf('.');
            string digits;
            if (dotIdx >= 0)
            {
                digits = timePart[..dotIdx];
                var frac = timePart[(dotIdx + 1)..];
                frac = frac.PadRight(9, '0');
                if (frac.Length > 9) frac = frac[..9];
                subSecondNanos = long.Parse(frac, CultureInfo.InvariantCulture);
            }
            else
            {
                digits = timePart;
            }

            hour = digits.Length >= 2 ? int.Parse(digits[..2], CultureInfo.InvariantCulture) : 0;
            minute = digits.Length >= 4 ? int.Parse(digits[2..4], CultureInfo.InvariantCulture) : 0;
            second = digits.Length >= 6 ? int.Parse(digits[4..6], CultureInfo.InvariantCulture) : 0;
        }

        // Clamp leap second (60) to 59 per Temporal spec
        if (second == 60)
            second = 59;

        // Compute epoch nanoseconds
        var epochDays = IsoCalendarHelpers.DateToEpochDays(year, month, day);
        var epochNanos = new BigInteger(epochDays) * 86400L * 1_000_000_000L;
        epochNanos += (long)hour * 3_600_000_000_000L;
        epochNanos += (long)minute * 60_000_000_000L;
        epochNanos += (long)second * 1_000_000_000L;
        epochNanos += subSecondNanos;
        epochNanos -= offsetNanos;

        // Validate epoch nanos range: -8.64e21 to 8.64e21
        var maxNanos = new BigInteger(8_640_000_000_000_000_000L) * 1000L;
        if (epochNanos < -maxNanos || epochNanos > maxNanos) return null;

        return new JsTemporalInstant(epochNanos);
    }

    /// <summary>
    ///     Parses an offset string like "+01:00", "-05:30", "+0100" to nanoseconds.
    /// </summary>
    private static long ParseOffsetNanos(string offsetStr)
    {
        var sign = offsetStr[0] == '-' ? -1L : 1L;
        var body = offsetStr[1..];
        var parts = body.Split(':');
        int hours, minutes = 0;

        if (parts.Length == 1)
        {
            // Compact format: HHMM or HH
            if (body.Length == 4)
            {
                hours = int.Parse(body[..2], CultureInfo.InvariantCulture);
                minutes = int.Parse(body[2..], CultureInfo.InvariantCulture);
            }
            else
            {
                hours = int.Parse(body, CultureInfo.InvariantCulture);
            }
        }
        else
        {
            hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
            minutes = int.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        return sign * ((long)hours * 3_600_000_000_000L + (long)minutes * 60_000_000_000L);
    }

    /// <summary>
    ///     Checks if an ISO datetime string has an explicit UTC offset (Z, +HH:MM, -HH:MM, etc.).
    /// </summary>
    internal static bool HasExplicitOffset(string str)
    {
        // Check for trailing Z/z
        if (str.Length > 0 && (str[^1] == 'Z' || str[^1] == 'z'))
            return true;

        // Find the T/t separator
        var tIdx = FindDateTimeSeparator(str);
        if (tIdx < 0)
            return false;

        var timePart = str[(tIdx + 1)..];

        // Look for +/- followed by digits in the time part (offset indicator)
        for (var i = timePart.Length - 1; i >= 2; i--)
        {
            if ((timePart[i] == '+' || timePart[i] == '-') &&
                i + 1 < timePart.Length && char.IsDigit(timePart[i + 1]))
                return true;
        }

        return false;
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
    ///     Computes date/time components directly from BigInteger epoch nanoseconds to support
    ///     years outside the .NET DateTimeOffset range (1-9999).
    /// </summary>
    public override string ToString()
    {
        const long nsPerDay = 86_400_000_000_000L;
        const long nsPerSecond = 1_000_000_000L;

        // Get the timezone offset
        var offset = GetOffsetTimeSpan();
        var offsetNanos = (BigInteger)offset.Ticks * 100;

        // Convert UTC epoch nanoseconds to local epoch nanoseconds
        var localEpochNs = Instant.EpochNanoseconds + offsetNanos;

        // Split into day number and time-of-day nanoseconds
        var dayNs = localEpochNs >= 0
            ? localEpochNs % nsPerDay
            : nsPerDay - 1 - ((-localEpochNs - 1) % nsPerDay);
        var dayNumber = (long)((localEpochNs - dayNs) / nsPerDay);

        // Convert day number to ISO date (civil calendar algorithm)
        var z = dayNumber + 719468L;
        var era = (z >= 0 ? z : z - 146096) / 146097;
        var doe = z - era * 146097;
        var yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        var y = yoe + era * 400;
        var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        var mp = (5 * doy + 2) / 153;
        var d = doy - (153 * mp + 2) / 5 + 1;
        var m = mp + (mp < 10 ? 3 : -9);
        y += m <= 2 ? 1 : 0;

        var year = (int)y;
        var month = (int)m;
        var day = (int)d;

        // Convert time-of-day nanoseconds to components
        var timeNs = (long)dayNs;
        var hour = (int)(timeNs / 3_600_000_000_000L);
        timeNs %= 3_600_000_000_000L;
        var minute = (int)(timeNs / 60_000_000_000L);
        timeNs %= 60_000_000_000L;
        var second = (int)(timeNs / nsPerSecond);
        var nanosPart = (int)(timeNs % nsPerSecond);

        // Format year: 4 digits for 0000-9999, 6 digits with sign otherwise
        string yearStr;
        if (year >= 0 && year <= 9999)
        {
            yearStr = year.ToString("D4", CultureInfo.InvariantCulture);
        }
        else if (year >= 0)
        {
            yearStr = "+" + year.ToString("D6", CultureInfo.InvariantCulture);
        }
        else
        {
            yearStr = "-" + (-year).ToString("D6", CultureInfo.InvariantCulture);
        }

        var baseStr = string.Create(CultureInfo.InvariantCulture,
            $"{yearStr}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:{second:D2}");

        // Add fractional seconds if needed
        if (nanosPart > 0)
        {
            var fractionStr = nanosPart.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');
            baseStr += $".{fractionStr}";
        }

        // Format offset string
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absOffset = offset.Duration();
        baseStr += $"{sign}{absOffset.Hours:D2}:{absOffset.Minutes:D2}";

        baseStr += $"[{TimeZoneId}]";

        if (!string.Equals(Calendar, "iso8601", StringComparison.Ordinal))
        {
            baseStr += $"[u-ca={Calendar}]";
        }

        return baseStr;
    }

    /// <summary>
    ///     Gets the timezone offset as a TimeSpan, handling extreme years gracefully.
    /// </summary>
    private TimeSpan GetOffsetTimeSpan()
    {
        if (FixedOffset.HasValue)
        {
            return FixedOffset.Value;
        }

        try
        {
            return TimeZone.GetUtcOffset(LocalDateTimeOffset.DateTime);
        }
        catch (OverflowException)
        {
            // For extreme years outside .NET's range, fall back to UTC offset
            return TimeSpan.Zero;
        }
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
