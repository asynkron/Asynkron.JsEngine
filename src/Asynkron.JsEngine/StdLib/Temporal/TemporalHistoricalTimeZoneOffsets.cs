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

    private static readonly TimeSpan NiuePreCutoverOffset = TimeSpan.FromSeconds(-(11 * 3600 + 19 * 60 + 40));
    private static readonly TimeSpan NiuePostCutoverOffset = TimeSpan.FromSeconds(-(11 * 3600 + 20 * 60));
    private static readonly DateTime NiueLocalCutover = new(1952, 10, 16, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateTimeOffset NiueUtcCutover = new(1952, 10, 16, 11, 19, 40, TimeSpan.Zero);
    private static readonly DateTime NiueAmbiguousWindowStart = new(1952, 10, 15, 23, 59, 40, DateTimeKind.Unspecified);
    private static readonly TimeSpan[] NiuePreCutoverOffsets = [NiuePreCutoverOffset];
    private static readonly TimeSpan[] NiueAmbiguousOffsets = [NiuePreCutoverOffset, NiuePostCutoverOffset];

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

        if (timeZone.IsAmbiguousTime(localDateTime))
        {
            return timeZone.GetAmbiguousTimeOffsets(localDateTime);
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

    private static bool TryGetUtcOffset(string timeZoneId, DateTime localDateTime, out TimeSpan offset)
    {
        switch (timeZoneId)
        {
            case "Africa/Monrovia" when localDateTime < MonroviaLocalCutover:
                offset = MonroviaOffset;
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
            case "Africa/Monrovia" when instant.ToUniversalTime() < MonroviaUtcCutover:
                offset = MonroviaOffset;
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
