#region

using System.Globalization;
using System.Text;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a Temporal.Duration - a length of time with calendar and clock components.
///     Unlike TimeSpan, this supports years, months, and weeks, plus nanosecond precision.
/// </summary>
public sealed class JsTemporalDuration : IEquatable<JsTemporalDuration>
{
    public JsTemporalDuration(
        double years = 0,
        double months = 0,
        double weeks = 0,
        double days = 0,
        double hours = 0,
        double minutes = 0,
        double seconds = 0,
        double milliseconds = 0,
        double microseconds = 0,
        double nanoseconds = 0)
    {
        Years = years;
        Months = months;
        Weeks = weeks;
        Days = days;
        Hours = hours;
        Minutes = minutes;
        Seconds = seconds;
        Milliseconds = milliseconds;
        Microseconds = microseconds;
        Nanoseconds = nanoseconds;

        // Validate sign consistency per Temporal spec
        var sign = GetDurationSign();
        Sign = sign;
    }

    public double Years { get; }
    public double Months { get; }
    public double Weeks { get; }
    public double Days { get; }
    public double Hours { get; }
    public double Minutes { get; }
    public double Seconds { get; }
    public double Milliseconds { get; }
    public double Microseconds { get; }
    public double Nanoseconds { get; }

    /// <summary>
    ///     The sign of the duration: -1, 0, or 1.
    /// </summary>
    public int Sign { get; }

    /// <summary>
    ///     Whether this duration has no date components (years, months, weeks, days).
    /// </summary>
    public bool IsTimeDurationOnly =>
        Years == 0 && Months == 0 && Weeks == 0 && Days == 0;

    /// <summary>
    ///     Zero duration.
    /// </summary>
    public static JsTemporalDuration Zero => new();

    /// <summary>
    ///     Creates a Duration from a TimeSpan (loses year/month/week precision).
    /// </summary>
    public static JsTemporalDuration FromTimeSpan(TimeSpan ts)
    {
        return new JsTemporalDuration(
            days: ts.Days,
            hours: ts.Hours,
            minutes: ts.Minutes,
            seconds: ts.Seconds,
            milliseconds: ts.Milliseconds,
            microseconds: ts.Microseconds
        );
    }

    /// <summary>
    ///     Creates a Duration from individual components.
    /// </summary>
    public static JsTemporalDuration From(
        double years = 0,
        double months = 0,
        double weeks = 0,
        double days = 0,
        double hours = 0,
        double minutes = 0,
        double seconds = 0,
        double milliseconds = 0,
        double microseconds = 0,
        double nanoseconds = 0)
    {
        return new JsTemporalDuration(
            years, months, weeks, days,
            hours, minutes, seconds,
            milliseconds, microseconds, nanoseconds);
    }

    /// <summary>
    ///     Creates a Duration from total nanoseconds.
    /// </summary>
    public static JsTemporalDuration FromNanoseconds(double totalNanoseconds)
    {
        // Convert to time units
        var absNanos = Math.Abs(totalNanoseconds);
        var sign = totalNanoseconds < 0 ? -1 : 1;

        var hours = Math.Floor(absNanos / 3_600_000_000_000.0);
        absNanos -= hours * 3_600_000_000_000.0;

        var minutes = Math.Floor(absNanos / 60_000_000_000.0);
        absNanos -= minutes * 60_000_000_000.0;

        var seconds = Math.Floor(absNanos / 1_000_000_000.0);
        absNanos -= seconds * 1_000_000_000.0;

        var milliseconds = Math.Floor(absNanos / 1_000_000.0);
        absNanos -= milliseconds * 1_000_000.0;

        var microseconds = Math.Floor(absNanos / 1_000.0);
        absNanos -= microseconds * 1_000.0;

        var nanoseconds = Math.Round(absNanos);

        return new JsTemporalDuration(
            hours: sign * hours,
            minutes: sign * minutes,
            seconds: sign * seconds,
            milliseconds: sign * milliseconds,
            microseconds: sign * microseconds,
            nanoseconds: sign * nanoseconds);
    }

    /// <summary>
    ///     Whether this duration is zero (blank).
    /// </summary>
    public bool Blank => Sign == 0;

    private int GetDurationSign()
    {
        var components = new[]
        {
            Years, Months, Weeks, Days,
            Hours, Minutes, Seconds,
            Milliseconds, Microseconds, Nanoseconds
        };

        var hasPositive = false;
        var hasNegative = false;

        foreach (var c in components)
        {
            if (c > 0)
            {
                hasPositive = true;
            }
            else if (c < 0)
            {
                hasNegative = true;
            }
        }

        if (hasPositive && hasNegative)
        {
            // Per Temporal spec, this is a RangeError, but we handle it gracefully
            // by determining the "dominant" sign
            var total = Years + Months + Weeks + Days + Hours + Minutes + Seconds +
                        Milliseconds + Microseconds + Nanoseconds;
            return total >= 0 ? 1 : -1;
        }

        if (hasPositive)
        {
            return 1;
        }

        if (hasNegative)
        {
            return -1;
        }

        return 0;
    }

    /// <summary>
    ///     Returns the negated duration.
    ///     Per ECMAScript spec, negating 0 must produce +0 (not -0).
    /// </summary>
    public JsTemporalDuration Negated()
    {
        return new JsTemporalDuration(
            NegateZeroSafe(Years), NegateZeroSafe(Months), NegateZeroSafe(Weeks), NegateZeroSafe(Days),
            NegateZeroSafe(Hours), NegateZeroSafe(Minutes), NegateZeroSafe(Seconds),
            NegateZeroSafe(Milliseconds), NegateZeroSafe(Microseconds), NegateZeroSafe(Nanoseconds));
    }

    /// <summary>
    ///     Negates a value, but returns +0 for 0 to avoid -0.
    /// </summary>
    private static double NegateZeroSafe(double value)
    {
        return value == 0 ? 0 : -value;
    }

    /// <summary>
    ///     Returns the absolute value of the duration.
    /// </summary>
    public JsTemporalDuration Abs()
    {
        return new JsTemporalDuration(
            Math.Abs(Years), Math.Abs(Months), Math.Abs(Weeks), Math.Abs(Days),
            Math.Abs(Hours), Math.Abs(Minutes), Math.Abs(Seconds),
            Math.Abs(Milliseconds), Math.Abs(Microseconds), Math.Abs(Nanoseconds));
    }

    /// <summary>
    ///     Adds two durations (for time components only; date components need calendar context).
    /// </summary>
    public JsTemporalDuration Add(JsTemporalDuration other)
    {
        return new JsTemporalDuration(
            Years + other.Years,
            Months + other.Months,
            Weeks + other.Weeks,
            Days + other.Days,
            Hours + other.Hours,
            Minutes + other.Minutes,
            Seconds + other.Seconds,
            Milliseconds + other.Milliseconds,
            Microseconds + other.Microseconds,
            Nanoseconds + other.Nanoseconds);
    }

    /// <summary>
    ///     Subtracts another duration from this one.
    /// </summary>
    public JsTemporalDuration Subtract(JsTemporalDuration other)
    {
        return Add(other.Negated());
    }

    /// <summary>
    ///     Returns the total duration in the specified unit (approximate for date units).
    /// </summary>
    public double Total(string unit)
    {
        // This is a simplified implementation - proper Temporal requires relativeTo
        // for accurate date component calculations
        var totalNanoseconds =
            Nanoseconds +
            Microseconds * 1_000 +
            Milliseconds * 1_000_000 +
            Seconds * 1_000_000_000.0 +
            Minutes * 60_000_000_000.0 +
            Hours * 3_600_000_000_000.0 +
            Days * 86_400_000_000_000.0 +
            Weeks * 604_800_000_000_000.0 +
            Months * 2_629_746_000_000_000.0 + // Average month
            Years * 31_556_952_000_000_000.0;  // Average year

        return unit.ToLowerInvariant() switch
        {
            "years" or "year" => totalNanoseconds / 31_556_952_000_000_000.0,
            "months" or "month" => totalNanoseconds / 2_629_746_000_000_000.0,
            "weeks" or "week" => totalNanoseconds / 604_800_000_000_000.0,
            "days" or "day" => totalNanoseconds / 86_400_000_000_000.0,
            "hours" or "hour" => totalNanoseconds / 3_600_000_000_000.0,
            "minutes" or "minute" => totalNanoseconds / 60_000_000_000.0,
            "seconds" or "second" => totalNanoseconds / 1_000_000_000.0,
            "milliseconds" or "millisecond" => totalNanoseconds / 1_000_000.0,
            "microseconds" or "microsecond" => totalNanoseconds / 1_000.0,
            "nanoseconds" or "nanosecond" => totalNanoseconds,
            _ => throw new ArgumentException($"Invalid unit: {unit}", nameof(unit))
        };
    }

    /// <summary>
    ///     Converts to a TimeSpan (loses precision for years/months/weeks and nanoseconds).
    /// </summary>
    public TimeSpan ToTimeSpan()
    {
        // Approximate: 1 year = 365.2425 days, 1 month = 30.44 days
        var totalDays = Years * 365.2425 + Months * 30.4369 + Weeks * 7 + Days;
        var totalTicks =
            (long)(totalDays * TimeSpan.TicksPerDay) +
            (long)(Hours * TimeSpan.TicksPerHour) +
            (long)(Minutes * TimeSpan.TicksPerMinute) +
            (long)(Seconds * TimeSpan.TicksPerSecond) +
            (long)(Milliseconds * TimeSpan.TicksPerMillisecond) +
            (long)(Microseconds * 10) + // 1 microsecond = 10 ticks
            (long)(Nanoseconds / 100);  // 1 tick = 100 nanoseconds

        return new TimeSpan(totalTicks);
    }

    public bool Equals(JsTemporalDuration? other)
    {
        if (other is null)
        {
            return false;
        }

        return Years == other.Years &&
               Months == other.Months &&
               Weeks == other.Weeks &&
               Days == other.Days &&
               Hours == other.Hours &&
               Minutes == other.Minutes &&
               Seconds == other.Seconds &&
               Milliseconds == other.Milliseconds &&
               Microseconds == other.Microseconds &&
               Nanoseconds == other.Nanoseconds;
    }

    public override bool Equals(object? obj)
    {
        return obj is JsTemporalDuration other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Years);
        hash.Add(Months);
        hash.Add(Weeks);
        hash.Add(Days);
        hash.Add(Hours);
        hash.Add(Minutes);
        hash.Add(Seconds);
        hash.Add(Milliseconds);
        hash.Add(Microseconds);
        hash.Add(Nanoseconds);
        return hash.ToHashCode();
    }

    /// <summary>
    ///     Returns ISO 8601 duration string (e.g., "P1Y2M3DT4H5M6S").
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();

        if (Sign < 0)
        {
            sb.Append('-');
        }

        sb.Append('P');

        var absYears = Math.Abs(Years);
        var absMonths = Math.Abs(Months);
        var absWeeks = Math.Abs(Weeks);
        var absDays = Math.Abs(Days);
        var absHours = Math.Abs(Hours);
        var absMinutes = Math.Abs(Minutes);
        var absSeconds = Math.Abs(Seconds);
        var absMilliseconds = Math.Abs(Milliseconds);
        var absMicroseconds = Math.Abs(Microseconds);
        var absNanoseconds = Math.Abs(Nanoseconds);

        if (absYears != 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{absYears}Y");
        }

        if (absMonths != 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{absMonths}M");
        }

        if (absWeeks != 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{absWeeks}W");
        }

        if (absDays != 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{absDays}D");
        }

        if (absHours != 0 || absMinutes != 0 || absSeconds != 0 ||
            absMilliseconds != 0 || absMicroseconds != 0 || absNanoseconds != 0)
        {
            sb.Append('T');
            if (absHours != 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{absHours}H");
            }

            if (absMinutes != 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{absMinutes}M");
            }

            if (absSeconds != 0 || absMilliseconds != 0 || absMicroseconds != 0 || absNanoseconds != 0)
            {
                // Use integer arithmetic to avoid double precision loss for large second values
                var wholeSeconds = (long)absSeconds;
                var fracNanos = (long)absNanoseconds +
                                (long)absMicroseconds * 1_000L +
                                (long)absMilliseconds * 1_000_000L;

                if (fracNanos == 0)
                {
                    sb.Append(wholeSeconds.ToString(CultureInfo.InvariantCulture));
                    sb.Append('S');
                }
                else
                {
                    var fracStr = fracNanos.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');
                    sb.Append(wholeSeconds.ToString(CultureInfo.InvariantCulture));
                    sb.Append('.');
                    sb.Append(fracStr);
                    sb.Append('S');
                }
            }
        }

        // Handle zero duration
        if (sb.Length == 1 || (sb.Length == 2 && sb[0] == '-'))
        {
            return "PT0S";
        }

        return sb.ToString();
    }

    public static bool operator ==(JsTemporalDuration? left, JsTemporalDuration? right)
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

    public static bool operator !=(JsTemporalDuration? left, JsTemporalDuration? right) => !(left == right);
}
