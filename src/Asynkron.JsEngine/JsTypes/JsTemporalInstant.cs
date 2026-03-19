#region

using System.Globalization;
using System.Numerics;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a Temporal.Instant - an exact point in time (nanoseconds from Unix epoch).
///     Maps to DateTimeOffset in .NET but with nanosecond precision.
/// </summary>
public sealed class JsTemporalInstant(BigInteger epochNanoseconds)
    : IEquatable<JsTemporalInstant>, IComparable<JsTemporalInstant>
{
    private const long NanosecondsPerMillisecond = 1_000_000L;
    private const long NanosecondsPerSecond = 1_000_000_000L;

    // Unix epoch: 1970-01-01T00:00:00Z
    private static readonly DateTimeOffset UnixEpoch = DateTimeOffset.UnixEpoch;

    public JsTemporalInstant(long epochMilliseconds)
        : this(new BigInteger(epochMilliseconds) * NanosecondsPerMillisecond)
    {
    }

    public JsTemporalInstant(DateTimeOffset dateTimeOffset)
        : this(DateTimeOffsetToNanoseconds(dateTimeOffset))
    {
    }

    /// <summary>
    ///     The number of nanoseconds since the Unix epoch (1970-01-01T00:00:00Z).
    ///     Can be negative for dates before the epoch.
    /// </summary>
    public BigInteger EpochNanoseconds { get; } = epochNanoseconds;

    /// <summary>
    ///     The number of milliseconds since the Unix epoch, floored (toward negative infinity).
    /// </summary>
    public long EpochMilliseconds => (long)FloorDiv(EpochNanoseconds, NanosecondsPerMillisecond);

    /// <summary>
    ///     The number of seconds since the Unix epoch, floored (toward negative infinity).
    /// </summary>
    public long EpochSeconds => (long)FloorDiv(EpochNanoseconds, NanosecondsPerSecond);

    /// <summary>
    ///     Floor division for BigInteger (rounds toward negative infinity instead of toward zero).
    /// </summary>
    private static BigInteger FloorDiv(BigInteger a, long b)
    {
        var (quotient, remainder) = BigInteger.DivRem(a, b);
        // If remainder is non-zero and signs differ, adjust toward negative infinity
        if (remainder != 0 && (a < 0) != (b < 0))
        {
            quotient--;
        }
        return quotient;
    }

    /// <summary>
    ///     Creates a Temporal.Instant representing the current moment.
    /// </summary>
    public static JsTemporalInstant Now()
    {
        return new JsTemporalInstant(DateTimeOffset.UtcNow);
    }

    /// <summary>
    ///     Creates a Temporal.Instant from epoch milliseconds (same as Date.now()).
    /// </summary>
    public static JsTemporalInstant FromEpochMilliseconds(long epochMilliseconds)
    {
        return new JsTemporalInstant(epochMilliseconds);
    }

    /// <summary>
    ///     Creates a Temporal.Instant from epoch nanoseconds.
    /// </summary>
    public static JsTemporalInstant FromEpochNanoseconds(BigInteger epochNanoseconds)
    {
        return new JsTemporalInstant(epochNanoseconds);
    }

    private static BigInteger DateTimeOffsetToNanoseconds(DateTimeOffset dto)
    {
        var ticks = (dto - UnixEpoch).Ticks;
        // 1 tick = 100 nanoseconds
        return new BigInteger(ticks) * 100;
    }

    /// <summary>
    ///     Converts to a .NET DateTimeOffset (loses precision beyond 100ns).
    /// </summary>
    public DateTimeOffset ToDateTimeOffset()
    {
        // Convert nanoseconds to ticks (1 tick = 100ns)
        var ticks = (long)(EpochNanoseconds / 100);
        return UnixEpoch.AddTicks(ticks);
    }

    public int CompareTo(JsTemporalInstant? other)
    {
        if (other is null)
        {
            return 1;
        }

        return EpochNanoseconds.CompareTo(other.EpochNanoseconds);
    }

    public bool Equals(JsTemporalInstant? other)
    {
        return other is not null && EpochNanoseconds.Equals(other.EpochNanoseconds);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsTemporalInstant other && Equals(other);
    }

    public override int GetHashCode()
    {
        return EpochNanoseconds.GetHashCode();
    }

    /// <summary>
    ///     Returns ISO 8601 string representation with Z suffix.
    /// </summary>
    public override string ToString()
    {
        // Compute ISO date/time components directly from epoch nanoseconds
        // to support years outside the .NET DateTimeOffset range.
        const long nsPerDay = 86_400_000_000_000L;

        // Split into day number and time-of-day nanoseconds
        var epochNs = EpochNanoseconds;
        var dayNs = epochNs >= 0
            ? epochNs % nsPerDay
            : nsPerDay - 1 - ((-epochNs - 1) % nsPerDay);
        var dayNumber = (long)((epochNs - dayNs) / nsPerDay);

        // Convert day number to ISO date
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
        var second = (int)(timeNs / NanosecondsPerSecond);
        var nanosPart = (int)(timeNs % NanosecondsPerSecond);

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

        var datePart = string.Create(CultureInfo.InvariantCulture,
            $"{yearStr}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:{second:D2}");

        if (nanosPart == 0)
        {
            return $"{datePart}Z";
        }

        var nanosStr = nanosPart.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');
        return $"{datePart}.{nanosStr}Z";
    }

    public static bool operator ==(JsTemporalInstant? left, JsTemporalInstant? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.EpochNanoseconds == right.EpochNanoseconds;
    }

    public static bool operator !=(JsTemporalInstant? left, JsTemporalInstant? right) => !(left == right);
    public static bool operator <(JsTemporalInstant left, JsTemporalInstant right) => left.CompareTo(right) < 0;
    public static bool operator <=(JsTemporalInstant left, JsTemporalInstant right) => left.CompareTo(right) <= 0;
    public static bool operator >(JsTemporalInstant left, JsTemporalInstant right) => left.CompareTo(right) > 0;
    public static bool operator >=(JsTemporalInstant left, JsTemporalInstant right) => left.CompareTo(right) >= 0;
}
