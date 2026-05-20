using System;
using System.Numerics;

namespace Asynkron.JsEngine.StdLib.Temporal;

internal static class TemporalHistoricalTimeZoneOffsets
{
    private static readonly DateTimeOffset UnixEpoch = DateTimeOffset.UnixEpoch;

    private static readonly TimeSpan MonroviaOffset = TimeSpan.FromSeconds(-2670);
    private static readonly DateTime MonroviaLocalCutover = new(1972, 1, 7, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateTimeOffset MonroviaUtcCutover = new(1972, 1, 7, 0, 44, 30, TimeSpan.Zero);
    private static readonly TimeSpan[] MonroviaOffsets = [MonroviaOffset];

    private static readonly TimeSpan LondonPermanentStandardOffset = TimeSpan.FromHours(1);
    private static readonly DateTimeOffset LondonPermanentStandardStartUtc = new(1968, 2, 18, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LondonPermanentStandardEndUtc = new(1971, 10, 31, 2, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan AnchorageStandardOffset = TimeSpan.FromHours(-10);
    private static readonly DateTimeOffset AnchorageStandardStartUtc = new(1945, 9, 30, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AnchorageStandardEndUtc = new(1969, 4, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan NiuePreCutoverOffset = TimeSpan.FromSeconds(-(11 * 3600 + 19 * 60 + 40));
    private static readonly TimeSpan NiuePostCutoverOffset = TimeSpan.FromSeconds(-(11 * 3600 + 20 * 60));
    private static readonly DateTime NiueLocalCutover = new(1952, 10, 16, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateTimeOffset NiueUtcCutover = new(1952, 10, 16, 11, 19, 40, TimeSpan.Zero);
    private static readonly DateTime NiueAmbiguousWindowStart = new(1952, 10, 15, 23, 59, 40, DateTimeKind.Unspecified);
    private static readonly TimeSpan[] NiuePreCutoverOffsets = [NiuePreCutoverOffset];
    private static readonly TimeSpan[] NiueAmbiguousOffsets = [NiuePreCutoverOffset, NiuePostCutoverOffset];

    private static readonly TimeSpan ParisPreCutoverOffset = TimeSpan.FromSeconds(9 * 60 + 21);
    private static readonly DateTime ParisLocalCutover = new(1911, 3, 11, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateTimeOffset ParisUtcCutover = new(1911, 3, 10, 23, 50, 39, TimeSpan.Zero);
    private static readonly TimeSpan[] ParisPreCutoverOffsets = [ParisPreCutoverOffset];

    internal static TimeSpan GetUtcOffset(TimeZoneInfo timeZone, DateTime localDateTime)
    {
        if (TryGetUtcOffset(timeZone.Id, localDateTime, out var offset))
        {
            return offset;
        }

        return timeZone.GetUtcOffset(localDateTime);
    }

    internal static TimeSpan GetUtcOffset(string requestedTimeZoneId, TimeZoneInfo timeZone, DateTime localDateTime)
    {
        if (TryGetUtcOffset(requestedTimeZoneId, localDateTime, out var offset))
        {
            return offset;
        }

        if (TryGetUtcOffset(timeZone.Id, localDateTime, out offset))
        {
            return offset;
        }

        return timeZone.GetUtcOffset(localDateTime);
    }

    internal static TimeSpan GetUtcOffset(TimeZoneInfo timeZone, DateTimeOffset instant)
    {
        if (TryGetUtcOffset(timeZone.Id, instant, out var offset))
        {
            return offset;
        }

        return timeZone.GetUtcOffset(instant);
    }

    internal static TimeSpan GetUtcOffset(string requestedTimeZoneId, TimeZoneInfo timeZone, DateTimeOffset instant)
    {
        if (TryGetUtcOffset(requestedTimeZoneId, instant, out var offset))
        {
            return offset;
        }

        if (TryGetUtcOffset(timeZone.Id, instant, out offset))
        {
            return offset;
        }

        return timeZone.GetUtcOffset(instant);
    }

    internal static TimeSpan GetUtcOffset(string requestedTimeZoneId, TimeZoneInfo timeZone, BigInteger epochNanoseconds)
    {
        var instant = ToDateTimeOffsetFloor(epochNanoseconds);
        return GetUtcOffset(requestedTimeZoneId, timeZone, instant);
    }

    internal static TimeSpan[] GetPossibleUtcOffsets(string requestedTimeZoneId, TimeZoneInfo timeZone, DateTime localDateTime)
    {
        if (TryGetPossibleUtcOffsets(requestedTimeZoneId, localDateTime, out var offsets))
        {
            return offsets;
        }

        if (TryGetPossibleUtcOffsets(timeZone.Id, localDateTime, out offsets))
        {
            return offsets;
        }

        return [timeZone.GetUtcOffset(localDateTime)];
    }

    internal static DateTimeOffset ConvertTime(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        return instant.ToOffset(GetUtcOffset(timeZone, instant));
    }

    internal static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        var totalHours = absolute.Days * 24 + absolute.Hours;

        return absolute.Seconds == 0
            ? $"{sign}{totalHours:D2}:{absolute.Minutes:D2}"
            : $"{sign}{totalHours:D2}:{absolute.Minutes:D2}:{absolute.Seconds:D2}";
    }

    /// <summary>
    /// Returns the UTC instants that mark the start or end of a synthetic override window for the
    /// given timezone. The expansion phase of the binary search uses these to avoid stepping over
    /// a boundary where both sides coincidentally share the same offset value through different
    /// mechanisms (e.g., the override window and native summer DST).
    /// </summary>
    internal static DateTimeOffset[] GetSyntheticBoundaries(string timeZoneId) =>
        timeZoneId switch
        {
            "Europe/London" => [LondonPermanentStandardStartUtc, LondonPermanentStandardEndUtc],
            "America/Anchorage" => [AnchorageStandardStartUtc, AnchorageStandardEndUtc],
            _ => []
        };

    private static bool TryGetUtcOffset(string timeZoneId, DateTime localDateTime, out TimeSpan offset)
    {
        switch (timeZoneId)
        {
            case "Africa/Monrovia" when localDateTime < MonroviaLocalCutover:
                offset = MonroviaOffset;
                return true;
            case "Europe/Paris" when localDateTime < ParisLocalCutover:
                offset = ParisPreCutoverOffset;
                return true;
            case "Pacific/Niue" when localDateTime < NiueLocalCutover:
                offset = NiuePreCutoverOffset;
                return true;
            default:
                offset = default;
                return false;
        }
    }

    private static bool TryGetPossibleUtcOffsets(string timeZoneId, DateTime localDateTime, out TimeSpan[] offsets)
    {
        switch (timeZoneId)
        {
            case "Africa/Monrovia" when localDateTime < MonroviaLocalCutover:
                offsets = MonroviaOffsets;
                return true;
            case "Europe/Paris" when localDateTime < ParisLocalCutover:
                offsets = ParisPreCutoverOffsets;
                return true;
            case "Pacific/Niue" when localDateTime >= NiueAmbiguousWindowStart && localDateTime < NiueLocalCutover:
                offsets = NiueAmbiguousOffsets;
                return true;
            case "Pacific/Niue" when localDateTime < NiueLocalCutover:
                offsets = NiuePreCutoverOffsets;
                return true;
            default:
                offsets = [];
                return false;
        }
    }

    private static bool TryGetUtcOffset(string timeZoneId, DateTimeOffset instant, out TimeSpan offset)
    {
        switch (timeZoneId)
        {
            case "Europe/London" when instant.ToUniversalTime() >= LondonPermanentStandardStartUtc &&
                                      instant.ToUniversalTime() < LondonPermanentStandardEndUtc:
                offset = LondonPermanentStandardOffset;
                return true;
            case "America/Anchorage" when instant.ToUniversalTime() >= AnchorageStandardStartUtc &&
                                          instant.ToUniversalTime() < AnchorageStandardEndUtc:
                offset = AnchorageStandardOffset;
                return true;
            case "Africa/Monrovia" when instant.ToUniversalTime() < MonroviaUtcCutover:
                offset = MonroviaOffset;
                return true;
            case "Europe/Paris" when instant.ToUniversalTime() < ParisUtcCutover:
                offset = ParisPreCutoverOffset;
                return true;
            case "Pacific/Niue" when instant.ToUniversalTime() < NiueUtcCutover:
                offset = NiuePreCutoverOffset;
                return true;
            default:
                offset = default;
                return false;
        }
    }

    private static DateTimeOffset ToDateTimeOffsetFloor(BigInteger epochNanoseconds)
    {
        var ticks = FloorDiv(epochNanoseconds, 100);
        if (ticks < long.MinValue || ticks > long.MaxValue)
        {
            throw new OverflowException("Epoch nanoseconds are outside DateTimeOffset range.");
        }

        return UnixEpoch.AddTicks((long)ticks);
    }

    private static BigInteger FloorDiv(BigInteger value, int divisor)
    {
        var quotient = BigInteger.DivRem(value, divisor, out var remainder);
        if (remainder != 0 && value.Sign < 0)
        {
            quotient--;
        }

        return quotient;
    }
}
