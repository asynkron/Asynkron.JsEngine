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
    ///     The number of milliseconds since the Unix epoch, truncated toward zero.
    /// </summary>
    public long EpochMilliseconds => (long)(EpochNanoseconds / NanosecondsPerMillisecond);

    /// <summary>
    ///     The number of seconds since the Unix epoch, truncated toward zero.
    /// </summary>
    public long EpochSeconds => (long)(EpochNanoseconds / NanosecondsPerSecond);

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
        var dto = ToDateTimeOffset();
        var nanosPart = (int)(EpochNanoseconds % NanosecondsPerSecond);
        if (nanosPart < 0)
        {
            nanosPart += (int)NanosecondsPerSecond;
        }

        // Format: YYYY-MM-DDTHH:mm:ss.nnnnnnnnnZ
        if (nanosPart == 0)
        {
            return dto.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        var secondsPart = dto.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var nanosStr = nanosPart.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');
        return $"{secondsPart}.{nanosStr}Z";
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
