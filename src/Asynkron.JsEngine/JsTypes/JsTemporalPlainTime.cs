#region

using System.Globalization;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a Temporal.PlainTime - a wall-clock time without date or timezone.
///     Maps to TimeOnly in .NET but with nanosecond precision.
/// </summary>
public sealed class JsTemporalPlainTime : IEquatable<JsTemporalPlainTime>, IComparable<JsTemporalPlainTime>
{
    private const long NanosecondsPerMicrosecond = 1000L;
    private const long NanosecondsPerMillisecond = 1_000_000L;
    private const long NanosecondsPerSecond = 1_000_000_000L;
    private const long NanosecondsPerMinute = 60L * NanosecondsPerSecond;
    private const long NanosecondsPerHour = 60L * NanosecondsPerMinute;
    private const long NanosecondsPerDay = 24L * NanosecondsPerHour;

    public JsTemporalPlainTime(
        int hour = 0,
        int minute = 0,
        int second = 0,
        int millisecond = 0,
        int microsecond = 0,
        int nanosecond = 0)
    {
        // Validate ranges
        if (hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), "Hour must be 0-23");
        }

        if (minute is < 0 or > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), "Minute must be 0-59");
        }

        if (second is < 0 or > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(second), "Second must be 0-59");
        }

        if (millisecond is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(millisecond), "Millisecond must be 0-999");
        }

        if (microsecond is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(microsecond), "Microsecond must be 0-999");
        }

        if (nanosecond is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(nanosecond), "Nanosecond must be 0-999");
        }

        Hour = hour;
        Minute = minute;
        Second = second;
        Millisecond = millisecond;
        Microsecond = microsecond;
        Nanosecond = nanosecond;
    }

    public JsTemporalPlainTime(TimeOnly time)
        : this(time.Hour, time.Minute, time.Second, time.Millisecond, time.Microsecond)
    {
    }

    public int Hour { get; }
    public int Minute { get; }
    public int Second { get; }
    public int Millisecond { get; }
    public int Microsecond { get; }
    public int Nanosecond { get; }

    /// <summary>
    ///     Total nanoseconds since midnight.
    /// </summary>
    public long TotalNanoseconds =>
        (long)Hour * NanosecondsPerHour +
        (long)Minute * NanosecondsPerMinute +
        (long)Second * NanosecondsPerSecond +
        (long)Millisecond * NanosecondsPerMillisecond +
        (long)Microsecond * NanosecondsPerMicrosecond +
        Nanosecond;

    /// <summary>
    ///     Midnight (00:00:00).
    /// </summary>
    public static JsTemporalPlainTime Midnight => new();

    /// <summary>
    ///     Creates a PlainTime for the current time.
    /// </summary>
    public static JsTemporalPlainTime Now(TimeZoneInfo? timeZone = null)
    {
        timeZone ??= TimeZoneInfo.Local;
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        return new JsTemporalPlainTime(
            now.Hour, now.Minute, now.Second,
            now.Millisecond, now.Microsecond);
    }

    /// <summary>
    ///     Creates a PlainTime from an ISO 8601 time string (HH:mm:ss.sssssssss).
    /// </summary>
    public static JsTemporalPlainTime From(string isoString)
    {
        // Simple parsing - full implementation would handle more formats
        var parts = isoString.Split(':');
        if (parts.Length < 2)
        {
            throw new FormatException($"Invalid PlainTime string: {isoString}");
        }

        var hour = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var minute = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var second = 0;
        var millisecond = 0;
        var microsecond = 0;
        var nanosecond = 0;

        if (parts.Length >= 3)
        {
            var secondPart = parts[2];
            var dotIndex = secondPart.IndexOf('.');
            if (dotIndex >= 0)
            {
                second = int.Parse(secondPart[..dotIndex], CultureInfo.InvariantCulture);
                var fractionStr = secondPart[(dotIndex + 1)..].PadRight(9, '0');

                millisecond = int.Parse(fractionStr[..3], CultureInfo.InvariantCulture);
                microsecond = int.Parse(fractionStr.Substring(3, 3), CultureInfo.InvariantCulture);
                nanosecond = int.Parse(fractionStr.Substring(6, 3), CultureInfo.InvariantCulture);
            }
            else
            {
                second = int.Parse(secondPart, CultureInfo.InvariantCulture);
            }
        }

        return new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond);
    }

    /// <summary>
    ///     Converts to .NET TimeOnly (loses nanosecond precision).
    /// </summary>
    public TimeOnly ToTimeOnly()
    {
        return new TimeOnly(Hour, Minute, Second, Millisecond, Microsecond);
    }

    /// <summary>
    ///     Returns a new PlainTime with modified fields.
    /// </summary>
    public JsTemporalPlainTime With(
        int? hour = null,
        int? minute = null,
        int? second = null,
        int? millisecond = null,
        int? microsecond = null,
        int? nanosecond = null)
    {
        return new JsTemporalPlainTime(
            hour ?? Hour,
            minute ?? Minute,
            second ?? Second,
            millisecond ?? Millisecond,
            microsecond ?? Microsecond,
            nanosecond ?? Nanosecond);
    }

    /// <summary>
    ///     Adds a duration to this time, wrapping around midnight.
    /// </summary>
    public JsTemporalPlainTime Add(JsTemporalDuration duration)
    {
        var totalNanos = TotalNanoseconds +
                         (long)(duration.Hours * NanosecondsPerHour) +
                         (long)(duration.Minutes * NanosecondsPerMinute) +
                         (long)(duration.Seconds * NanosecondsPerSecond) +
                         (long)(duration.Milliseconds * NanosecondsPerMillisecond) +
                         (long)(duration.Microseconds * NanosecondsPerMicrosecond) +
                         (long)duration.Nanoseconds;

        // Wrap around to stay within 24 hours
        totalNanos = ((totalNanos % NanosecondsPerDay) + NanosecondsPerDay) % NanosecondsPerDay;

        return FromNanoseconds(totalNanos);
    }

    /// <summary>
    ///     Subtracts a duration from this time, wrapping around midnight.
    /// </summary>
    public JsTemporalPlainTime Subtract(JsTemporalDuration duration)
    {
        return Add(duration.Negated());
    }

    /// <summary>
    ///     Returns the duration until another time (within the same day).
    /// </summary>
    public JsTemporalDuration Until(JsTemporalPlainTime other)
    {
        var diffNanos = other.TotalNanoseconds - TotalNanoseconds;
        return JsTemporalDuration.From(nanoseconds: diffNanos);
    }

    /// <summary>
    ///     Returns the duration since another time (within the same day).
    /// </summary>
    public JsTemporalDuration Since(JsTemporalPlainTime other)
    {
        return other.Until(this);
    }

    /// <summary>
    ///     Rounds the time to the specified unit.
    /// </summary>
    public JsTemporalPlainTime Round(string smallestUnit)
    {
        var nanos = TotalNanoseconds;
        long divisor = smallestUnit switch
        {
            "hour" or "hours" => NanosecondsPerHour,
            "minute" or "minutes" => NanosecondsPerMinute,
            "second" or "seconds" => NanosecondsPerSecond,
            "millisecond" or "milliseconds" => NanosecondsPerMillisecond,
            "microsecond" or "microseconds" => NanosecondsPerMicrosecond,
            _ => 1L // nanosecond
        };
        var rounded = (nanos / divisor) * divisor;
        // Wrap around if needed (shouldn't be, but be safe)
        rounded = ((rounded % NanosecondsPerDay) + NanosecondsPerDay) % NanosecondsPerDay;
        return FromNanoseconds(rounded);
    }

    private static JsTemporalPlainTime FromNanoseconds(long totalNanoseconds)
    {
        var hours = (int)(totalNanoseconds / NanosecondsPerHour);
        totalNanoseconds %= NanosecondsPerHour;

        var minutes = (int)(totalNanoseconds / NanosecondsPerMinute);
        totalNanoseconds %= NanosecondsPerMinute;

        var seconds = (int)(totalNanoseconds / NanosecondsPerSecond);
        totalNanoseconds %= NanosecondsPerSecond;

        var milliseconds = (int)(totalNanoseconds / NanosecondsPerMillisecond);
        totalNanoseconds %= NanosecondsPerMillisecond;

        var microseconds = (int)(totalNanoseconds / NanosecondsPerMicrosecond);
        var nanoseconds = (int)(totalNanoseconds % NanosecondsPerMicrosecond);

        return new JsTemporalPlainTime(hours, minutes, seconds, milliseconds, microseconds, nanoseconds);
    }

    public int CompareTo(JsTemporalPlainTime? other)
    {
        if (other is null)
        {
            return 1;
        }

        return TotalNanoseconds.CompareTo(other.TotalNanoseconds);
    }

    public bool Equals(JsTemporalPlainTime? other)
    {
        if (other is null)
        {
            return false;
        }

        return TotalNanoseconds == other.TotalNanoseconds;
    }

    public override bool Equals(object? obj)
    {
        return obj is JsTemporalPlainTime other && Equals(other);
    }

    public override int GetHashCode()
    {
        return TotalNanoseconds.GetHashCode();
    }

    /// <summary>
    ///     Returns ISO 8601 time string (HH:mm:ss or HH:mm:ss.sssssssss).
    /// </summary>
    public override string ToString()
    {
        var baseTime = $"{Hour:D2}:{Minute:D2}:{Second:D2}";

        if (Millisecond == 0 && Microsecond == 0 && Nanosecond == 0)
        {
            return baseTime;
        }

        var totalSubSecondNanos = Millisecond * 1_000_000L + Microsecond * 1_000L + Nanosecond;
        var fractionStr = totalSubSecondNanos.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');

        return $"{baseTime}.{fractionStr}";
    }

    public static bool operator ==(JsTemporalPlainTime? left, JsTemporalPlainTime? right)
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

    public static bool operator !=(JsTemporalPlainTime? left, JsTemporalPlainTime? right) => !(left == right);
    public static bool operator <(JsTemporalPlainTime left, JsTemporalPlainTime right) => left.CompareTo(right) < 0;
    public static bool operator <=(JsTemporalPlainTime left, JsTemporalPlainTime right) => left.CompareTo(right) <= 0;
    public static bool operator >(JsTemporalPlainTime left, JsTemporalPlainTime right) => left.CompareTo(right) > 0;
    public static bool operator >=(JsTemporalPlainTime left, JsTemporalPlainTime right) => left.CompareTo(right) >= 0;
}
