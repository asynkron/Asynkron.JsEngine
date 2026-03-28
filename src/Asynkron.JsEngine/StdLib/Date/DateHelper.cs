#region

using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class DateHelper
{
    private const double MsPerDay = 86400000d;
    private const double MsPerHour = 3600000d;
    internal const double MsPerMinute = 60000d;
    private const double MsPerSecond = 1000d;

    internal static double ComputeDateTimeValue(
        IReadOnlyList<JsValue> args,
        RealmState realm,
        EvaluationContext? context)
    {
        if (args.Count == 0)
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        if (args.Count == 1)
        {
            var arg = args[0];
            var ctx = context ?? realm.CreateContext();
            if (arg.IsObject &&
                arg.TryGetObject<JsObject>(out var dateObj) &&
                dateObj.GetOwnPropertyDescriptor("_internalDate") is { JsValue: var dateValue } &&
                dateValue.TryGetDouble(out var timeValue))
            {
                return timeValue;
            }

            var primitive = arg.IsObject
                ? JsOps.ToPrimitive(arg, ToPrimitiveHint.Default, ctx)
                : arg;
            if (ctx.IsThrow)
            {
                throw new ThrowSignal(ctx.FlowValue);
            }

            if (primitive.TryGetString(out var dateStr))
            {
                return ParseDateTimeString(dateStr, realm);
            }

            var ms = JsOps.ToNumber(primitive, ctx);
            if (ctx.IsThrow)
            {
                throw new ThrowSignal(ctx.FlowValue);
            }

            return TimeClip(ms);
        }

        var evalContext = context ?? realm.CreateContext();
        var yearNum = MakeFullYear(JsOps.ToNumber(args[0], evalContext));
        var monthNum = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : 0;
        var dayNum = args.Count > 2 ? JsOps.ToNumber(args[2], evalContext) : 1;
        var hourNum = args.Count > 3 ? JsOps.ToNumber(args[3], evalContext) : 0;
        var minuteNum = args.Count > 4 ? JsOps.ToNumber(args[4], evalContext) : 0;
        var secondNum = args.Count > 5 ? JsOps.ToNumber(args[5], evalContext) : 0;
        var millisecondNum = args.Count > 6 ? JsOps.ToNumber(args[6], evalContext) : 0;

        if (evalContext.IsThrow)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        if (double.IsNaN(yearNum) || double.IsNaN(monthNum) || double.IsNaN(dayNum) ||
            double.IsNaN(hourNum) || double.IsNaN(minuteNum) || double.IsNaN(secondNum) ||
            double.IsNaN(millisecondNum))
        {
            return double.NaN;
        }

        var day = MakeDay(yearNum, monthNum, dayNum);
        var hour = Math.Truncate(hourNum);
        var minute = Math.Truncate(minuteNum);
        var second = Math.Truncate(secondNum);
        var millisecond = Math.Truncate(millisecondNum);

        if (double.IsInfinity(hour) || double.IsInfinity(minute) ||
            double.IsInfinity(second) || double.IsInfinity(millisecond))
        {
            return double.NaN;
        }

        var timeWithinDay = hour * MsPerHour + minute * MsPerMinute + second * MsPerSecond + millisecond;
        var localDate = MakeDate(day, timeWithinDay);
        var utc = UtcTimeFromLocal(localDate, realm);
        return TimeClip(utc);
    }

    internal static void StoreInternalDateValue(JsObject obj, double timeValue)
    {
        if (BitConverter.DoubleToInt64Bits(timeValue) == BitConverter.DoubleToInt64Bits(-0d))
        {
            timeValue = 0d;
        }

        obj.SetProperty("_internalDate", new JsValue(timeValue));
    }

    internal static double RequireDateValue(JsValue thisVal, RealmState realm, out JsObject obj)
    {
        // Use TryGetObject<JsObject> to properly handle arrays and other non-JsObject types
        // JsArray implements IsObject but is not a JsObject subclass
        if (!thisVal.TryGetObject<JsObject>(out var candidate))
        {
            throw ThrowTypeError("Date method called on incompatible receiver", realm: realm);
        }

        if (candidate.GetOwnPropertyDescriptor("_internalDate") is not { JsValue: var jsValue } ||
            !jsValue.TryGetDouble(out var timeValue))
        {
            throw ThrowTypeError("Date method called on incompatible receiver", realm: realm);
        }

        obj = candidate;
        return timeValue;
    }

    private static JsObject RequireDateObject(JsValue thisVal, RealmState realm)
    {
        RequireDateValue(thisVal, realm, out var obj);
        return obj;
    }

    internal static double MakeFullYear(double year)
    {
        if (double.IsNaN(year))
        {
            return double.NaN;
        }

        var truncated = Math.Sign(year) * Math.Floor(Math.Abs(year));
        if (double.IsInfinity(truncated))
        {
            return truncated;
        }

        if (truncated is >= 0 and <= 99)
        {
            return 1900 + truncated;
        }

        return truncated;
    }

    internal static double TimeClip(double time)
    {
        if (double.IsNaN(time) || double.IsInfinity(time) || Math.Abs(time) > 8.64e15)
        {
            return double.NaN;
        }

        var truncated = Math.Truncate(time);
        return truncated == 0 ? 0 : truncated;
    }

    internal static double SetTimeComponents(double time, RealmState realmState, double? hour = null,
        double? minute = null, double? second = null, double? millisecond = null, bool inputIsUtc = false)
    {
        if (double.IsNaN(time))
        {
            return double.NaN;
        }

        if ((hour.HasValue && double.IsNaN(hour.Value)) ||
            (minute.HasValue && double.IsNaN(minute.Value)) ||
            (second.HasValue && double.IsNaN(second.Value)) ||
            (millisecond.HasValue && double.IsNaN(millisecond.Value)))
        {
            return double.NaN;
        }

        var h = ToIntegerOrInfinity(hour ?? HourFromTime(time));
        var m = ToIntegerOrInfinity(minute ?? MinFromTime(time));
        var s = ToIntegerOrInfinity(second ?? SecFromTime(time));
        var ms = ToIntegerOrInfinity(millisecond ?? MsFromTime(time));
        if (double.IsInfinity(h) || double.IsInfinity(m) || double.IsInfinity(s) || double.IsInfinity(ms))
        {
            return double.NaN;
        }

        var day = Day(time);
        var newTime = h * MsPerHour + m * MsPerMinute + s * MsPerSecond + ms;
        var newDate = MakeDate(day, newTime);
        var utc = inputIsUtc ? newDate : UtcTimeFromLocal(newDate, realmState);
        return TimeClip(utc);
    }

    internal static double ApplyTimeClip(double day, double time, RealmState realmState, bool inputIsUtc)
    {
        if (double.IsNaN(day) || double.IsNaN(time))
        {
            return double.NaN;
        }

        var newDate = MakeDate(day, TimeWithinDay(time));
        var utc = inputIsUtc ? newDate : UtcTimeFromLocal(newDate, realmState);
        return TimeClip(utc);
    }

    internal static double SetFullYear(double year, double month, double date, double time, RealmState realmState,
        bool inputIsUtc)
    {
        var timeValue = double.IsNaN(time) ? 0 : time;
        var day = MakeDay(year, month, date);
        var newDate = MakeDate(day, TimeWithinDay(timeValue));
        var utc = inputIsUtc ? newDate : UtcTimeFromLocal(newDate, realmState);
        return TimeClip(utc);
    }

    private static double Day(double t)
    {
        return Math.Floor(t / MsPerDay);
    }

    internal static double TimeWithinDay(double t)
    {
        var result = t % MsPerDay;
        if (result < 0)
        {
            result += MsPerDay;
        }

        return result;
    }

    private static bool IsLeapYear(double year)
    {
        var y = (long)Math.Truncate(year);
        return (y % 4 == 0 && y % 100 != 0) || y % 400 == 0;
    }

    private static double DayFromYear(double year)
    {
        var y = Math.Truncate(year);
        return 365 * (y - 1970) + Math.Floor((y - 1969) / 4) - Math.Floor((y - 1901) / 100) +
               Math.Floor((y - 1601) / 400);
    }

    private static double TimeFromYear(double year)
    {
        return MsPerDay * DayFromYear(year);
    }

    internal static double YearFromTime(double t)
    {
        if (double.IsNaN(t) || double.IsInfinity(t))
        {
            return double.NaN;
        }

        var y = 1970 + Math.Floor(t / (MsPerDay * 365.2425));
        while (TimeFromYear(y) > t)
        {
            y--;
        }

        while (TimeFromYear(y + 1) <= t)
        {
            y++;
        }

        return y;
    }

    private static double DayWithinYear(double t)
    {
        var y = YearFromTime(t);
        return Day(t) - DayFromYear(y);
    }

    internal static int MonthFromTime(double t)
    {
        var day = DayWithinYear(t);
        var leap = IsLeapYear(YearFromTime(t));
        var monthDayOffsets = leap
            ? new[] { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 }
            : new[] { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 };

        for (var m = 0; m < 12; m++)
        {
            if (day < monthDayOffsets[m + 1])
            {
                return m;
            }
        }

        return 11;
    }

    internal static int DateFromTime(double t)
    {
        var day = DayWithinYear(t);
        var leap = IsLeapYear(YearFromTime(t));
        var monthDayOffsets = leap
            ? new[] { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 }
            : new[] { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 };

        var month = MonthFromTime(t);
        return (int)(day - monthDayOffsets[month] + 1);
    }

    internal static double MakeDay(double year, double month, double date)
    {
        if (double.IsNaN(year) || double.IsNaN(month) || double.IsNaN(date) ||
            double.IsInfinity(year) || double.IsInfinity(month) || double.IsInfinity(date))
        {
            return double.NaN;
        }

        var y = Math.Truncate(year);
        var m = Math.Truncate(month);
        var dt = Math.Truncate(date);

        var ym = y + Math.Floor(m / 12);
        var mn = m % 12;
        if (mn < 0)
        {
            mn += 12;
        }

        var monthDayOffsets = IsLeapYear(ym)
            ? new[] { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335 }
            : new[] { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };

        var day = DayFromYear(ym) + monthDayOffsets[(int)mn] + dt - 1;
        return day;
    }

    internal static double MakeDate(double day, double time)
    {
        return day * MsPerDay + time;
    }

    internal static double MakeTime(double hour, double minute, double second, double millisecond)
    {
        if (double.IsNaN(hour) || double.IsNaN(minute) || double.IsNaN(second) || double.IsNaN(millisecond))
        {
            return double.NaN;
        }

        var h = Math.Truncate(hour);
        var m = Math.Truncate(minute);
        var s = Math.Truncate(second);
        var ms = Math.Truncate(millisecond);

        if (double.IsInfinity(h) || double.IsInfinity(m) || double.IsInfinity(s) || double.IsInfinity(ms))
        {
            return double.NaN;
        }

        return h * MsPerHour + m * MsPerMinute + s * MsPerSecond + ms;
    }

    internal static double HourFromTime(double t)
    {
        return Math.Floor(TimeWithinDay(t) / MsPerHour);
    }

    internal static double MinFromTime(double t)
    {
        return Math.Floor(TimeWithinDay(t) / MsPerMinute) % 60;
    }

    internal static double SecFromTime(double t)
    {
        return Math.Floor(TimeWithinDay(t) / MsPerSecond) % 60;
    }

    internal static double MsFromTime(double t)
    {
        return TimeWithinDay(t) % MsPerSecond;
    }

    internal static double WeekDayFromTime(double t)
    {
        var w = (Day(t) + 4) % 7;
        if (w < 0)
        {
            w += 7;
        }

        return w;
    }

    internal static double GetLocalOffsetMs(double utcTime, RealmState realmState)
    {
        if (double.IsNaN(utcTime) || double.IsInfinity(utcTime))
        {
            return 0;
        }

        try
        {
            var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Truncate(utcTime));
            var local = ConvertToConfiguredLocal(dto, realmState);
            return local.Offset.TotalMilliseconds;
        }
        catch
        {
            return ResolveTimeZone(realmState).BaseUtcOffset.TotalMilliseconds;
        }
    }

    internal static double LocalTimeMs(double utcTime, RealmState realmState)
    {
        return utcTime + GetLocalOffsetMs(utcTime, realmState);
    }

    internal static double UtcTimeFromLocal(double localTime, RealmState realmState)
    {
        var guess = localTime - GetLocalOffsetMs(localTime, realmState);
        var offset = GetLocalOffsetMs(guess, realmState);
        return localTime - offset;
    }

    internal static DateTimeOffset ConvertMillisecondsToLocal(double milliseconds, RealmState realmState)
    {
        var utc = ConvertMillisecondsToUtc(milliseconds);
        return ConvertToConfiguredLocal(utc, realmState);
    }

    private static DateTimeOffset ConvertToConfiguredLocal(DateTimeOffset utc, RealmState realmState)
    {
        var timeZone = ResolveTimeZone(realmState);
        return TimeZoneInfo.ConvertTime(utc, timeZone);
    }

    private static TimeZoneInfo ResolveTimeZone(RealmState realmState)
    {
        return realmState.Options.TimeZone ?? TimeZoneInfo.Utc;
    }

    private static readonly string[] WeekdayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    internal static string FormatDateToJsString(DateTimeOffset localTime, RealmState realmState)
    {
        var culture = CultureInfo.InvariantCulture;
        var weekday = localTime.ToString("ddd", culture);
        var month = localTime.ToString("MMM", culture);
        var day = localTime.ToString("dd", culture);
        var time = localTime.ToString("HH:mm:ss", culture);
        var year = localTime.ToString("yyyy", culture);

        var offset = localTime.ToString("zzz", culture).Replace(":", string.Empty, StringComparison.Ordinal);

        var timeZone = ResolveTimeZone(realmState);
        var timeZoneName = timeZone.IsDaylightSavingTime(localTime.DateTime)
            ? timeZone.DaylightName
            : timeZone.StandardName;

        return $"{weekday} {month} {day} {year} {time} GMT{offset} ({timeZoneName})";
    }

    internal static string FormatDateToJsStringFromTime(double utcTime, RealmState realmState)
    {
        var localTime = LocalTimeMs(utcTime, realmState);
        var weekday = WeekdayNames[(int)WeekDayFromTime(localTime)];
        var month = MonthNames[MonthFromTime(localTime)];
        var day = ((int)DateFromTime(localTime)).ToString("00", CultureInfo.InvariantCulture);
        var year = FormatYearString(YearFromTime(localTime));
        var time = $"{(int)HourFromTime(localTime):00}:{(int)MinFromTime(localTime):00}:{(int)SecFromTime(localTime):00}";

        var offsetMinutes = (int)Math.Round(GetLocalOffsetMs(utcTime, realmState) / MsPerMinute);
        var offsetSign = offsetMinutes < 0 ? "-" : "+";
        var offsetAbs = Math.Abs(offsetMinutes);
        var offset = $"{offsetSign}{offsetAbs / 60:00}{offsetAbs % 60:00}";

        var timeZone = ResolveTimeZone(realmState);
        var timeZoneName = timeZone.StandardName;

        return $"{weekday} {month} {day} {year} {time} GMT{offset} ({timeZoneName})";
    }

    internal static string FormatUtcToJsUtcString(DateTimeOffset utcTime)
    {
        var culture = CultureInfo.InvariantCulture;
        return utcTime.UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", culture);
    }

    internal static string FormatUtcToJsUtcStringFromTime(double utcTime)
    {
        var weekday = WeekdayNames[(int)WeekDayFromTime(utcTime)];
        var month = MonthNames[MonthFromTime(utcTime)];
        var day = ((int)DateFromTime(utcTime)).ToString("00", CultureInfo.InvariantCulture);
        var year = FormatYearString(YearFromTime(utcTime));
        var time = $"{(int)HourFromTime(utcTime):00}:{(int)MinFromTime(utcTime):00}:{(int)SecFromTime(utcTime):00}";
        return $"{weekday}, {day} {month} {year} {time} GMT";
    }

    internal static string FormatDateToJsDateStringFromTime(double utcTime, RealmState realmState)
    {
        var localTime = LocalTimeMs(utcTime, realmState);
        var weekday = WeekdayNames[(int)WeekDayFromTime(localTime)];
        var month = MonthNames[MonthFromTime(localTime)];
        var day = ((int)DateFromTime(localTime)).ToString("00", CultureInfo.InvariantCulture);
        var year = FormatYearString(YearFromTime(localTime));
        return $"{weekday} {month} {day} {year}";
    }

    internal static DateTimeOffset ConvertMillisecondsToUtc(double milliseconds)
    {
        if (double.IsNaN(milliseconds))
        {
            return DateTimeOffset.MinValue;
        }

        var truncated = (long)Math.Truncate(milliseconds);
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(truncated);
        }
        catch
        {
            return milliseconds < 0 ? DateTimeOffset.MinValue : DateTimeOffset.MaxValue;
        }
    }

    private static string FormatYearString(double year)
    {
        if (double.IsNaN(year) || double.IsInfinity(year))
        {
            return "NaN";
        }

        var integral = (long)Math.Truncate(year);
        var sign = integral < 0 ? "-" : string.Empty;
        var absYear = Math.Abs(integral);
        var digits = absYear.ToString(CultureInfo.InvariantCulture).PadLeft(4, '0');
        return $"{sign}{digits}";
    }

    internal static double ParseDateTimeString(string dateStr, RealmState realmState)
    {
        if (string.IsNullOrEmpty(dateStr))
        {
            return double.NaN;
        }

        var span = TrimAsciiWhitespace(dateStr.AsSpan());
        if (span.Length == 0)
        {
            return double.NaN;
        }

        if (TryParseEcmaDateTimeString(span, realmState, out var isoTimeValue))
        {
            return isoTimeValue;
        }

        if (TryParseJsDateToString(span, out var jsToStringTimeValue))
        {
            return jsToStringTimeValue;
        }

        if (TryParseJsDateToUtcString(span, out var jsToUtcTimeValue))
        {
            return jsToUtcTimeValue;
        }

        return double.NaN;
    }

    internal static string FormatUtcToIsoString(double timeValue)
    {
        var year = (int)YearFromTime(timeValue);
        var month = MonthFromTime(timeValue) + 1;
        var day = DateFromTime(timeValue);
        var hour = (int)HourFromTime(timeValue);
        var minute = (int)MinFromTime(timeValue);
        var second = (int)SecFromTime(timeValue);
        var millisecond = (int)MsFromTime(timeValue);

        var extendedYear = year is < 0 or > 9999;
        var length = extendedYear ? 27 : 24;

        return string.Create(length,
            (year, month, day, hour, minute, second, millisecond, extendedYear),
            static (span, state) =>
            {
                var i = 0;

                if (state.extendedYear)
                {
                    span[i++] = state.year < 0 ? '-' : '+';
                    var absYear = Math.Abs(state.year);
                    WriteFixedDigits(span.Slice(i, 6), absYear, 6);
                    i += 6;
                }
                else
                {
                    WriteFixedDigits(span.Slice(i, 4), state.year, 4);
                    i += 4;
                }

                span[i++] = '-';
                WriteFixedDigits(span.Slice(i, 2), state.month, 2);
                i += 2;
                span[i++] = '-';
                WriteFixedDigits(span.Slice(i, 2), state.day, 2);
                i += 2;
                span[i++] = 'T';
                WriteFixedDigits(span.Slice(i, 2), state.hour, 2);
                i += 2;
                span[i++] = ':';
                WriteFixedDigits(span.Slice(i, 2), state.minute, 2);
                i += 2;
                span[i++] = ':';
                WriteFixedDigits(span.Slice(i, 2), state.second, 2);
                i += 2;
                span[i++] = '.';
                WriteFixedDigits(span.Slice(i, 3), state.millisecond, 3);
                i += 3;
                span[i] = 'Z';
            });
    }

    private static void WriteFixedDigits(Span<char> destination, int value, int digits)
    {
        for (var i = digits - 1; i >= 0; i--)
        {
            destination[i] = (char)('0' + (value % 10));
            value /= 10;
        }
    }

    private static ReadOnlySpan<char> TrimAsciiWhitespace(ReadOnlySpan<char> value)
    {
        var start = 0;
        while (start < value.Length && IsAsciiWhitespace(value[start]))
        {
            start++;
        }

        var end = value.Length - 1;
        while (end >= start && IsAsciiWhitespace(value[end]))
        {
            end--;
        }

        return value.Slice(start, end - start + 1);
    }

    private static bool IsAsciiWhitespace(char c)
        => c is ' ' or '\t' or '\r' or '\n' or '\f';

    private static bool TryParseEcmaDateTimeString(ReadOnlySpan<char> value, RealmState realmState, out double timeValue)
    {
        timeValue = double.NaN;

        if (value.Length < 4)
        {
            return false;
        }

        var index = 0;
        int year;
        if (value[index] is '+' or '-')
        {
            var sign = value[index++];
            if (value.Length - index < 6)
            {
                return false;
            }

            if (!TryParseFixedDigits(value.Slice(index, 6), out var yearDigits))
            {
                return false;
            }

            if (sign == '-' && yearDigits == 0)
            {
                timeValue = double.NaN;
                return true;
            }

            year = sign == '-' ? -yearDigits : yearDigits;
            index += 6;
        }
        else
        {
            if (!TryParseFixedDigits(value.Slice(index, 4), out var yearDigits))
            {
                return false;
            }

            year = yearDigits;
            index += 4;
        }

        var hasTime = false;

        if (index == value.Length)
        {
            var utcYearOnly = MakeDate(MakeDay(year, 0, 1), 0);
            timeValue = TimeClip(utcYearOnly);
            return true;
        }

        if (value[index] != '-')
        {
            return false;
        }

        index++;

        int month;
        if (index + 2 > value.Length || !TryParseTwoDigits(value.Slice(index, 2), out month))
        {
            timeValue = double.NaN;
            return true;
        }

        index += 2;
        if (index == value.Length)
        {
            var utcYearMonth = MakeDate(MakeDay(year, month - 1, 1), 0);
            timeValue = TimeClip(utcYearMonth);
            return true;
        }

        if (value[index] != '-')
        {
            timeValue = double.NaN;
            return true;
        }

        index++;
        int day;
        if (index + 2 > value.Length || !TryParseTwoDigits(value.Slice(index, 2), out day))
        {
            timeValue = double.NaN;
            return true;
        }

        index += 2;

        var hour = 0;
        var minute = 0;
        var second = 0;
        var millisecond = 0;
        var offsetMinutes = 0;
        var hasOffset = false;

        if (index < value.Length && value[index] == 'T')
        {
            hasTime = true;
            index++;

            if (index + 5 > value.Length ||
                !TryParseTwoDigits(value.Slice(index, 2), out hour) ||
                value[index + 2] != ':' ||
                !TryParseTwoDigits(value.Slice(index + 3, 2), out minute))
            {
                timeValue = double.NaN;
                return true;
            }

            index += 5;

            if (index < value.Length && value[index] == ':')
            {
                index++;
                if (index + 2 > value.Length || !TryParseTwoDigits(value.Slice(index, 2), out second))
                {
                    timeValue = double.NaN;
                    return true;
                }

                index += 2;
            }

            if (index < value.Length && value[index] == '.')
            {
                index++;
                if (index >= value.Length || !IsAsciiDigit(value[index]))
                {
                    timeValue = double.NaN;
                    return true;
                }

                var digitsStart = index;
                var digitsCount = 0;
                while (index < value.Length && IsAsciiDigit(value[index]))
                {
                    index++;
                    digitsCount++;
                }

                var fraction = value.Slice(digitsStart, digitsCount);
                millisecond = ParseMilliseconds(fraction);
            }

            if (index < value.Length)
            {
                if (value[index] is 'Z' or 'z')
                {
                    hasOffset = true;
                    offsetMinutes = 0;
                    index++;
                }
                else if (value[index] is '+' or '-')
                {
                    hasOffset = true;
                    if (!TryParseTimeZoneOffset(value.Slice(index), out offsetMinutes, out var offsetChars))
                    {
                        timeValue = double.NaN;
                        return true;
                    }

                    index += offsetChars;
                }
            }
        }

        if (index != value.Length)
        {
            timeValue = double.NaN;
            return true;
        }

        if (!TryGetDaysInMonth(year, month, out var daysInMonth) || day < 1 || day > daysInMonth)
        {
            timeValue = double.NaN;
            return true;
        }

        if (!IsValidTime(hour, minute, second, millisecond))
        {
            timeValue = double.NaN;
            return true;
        }

        var dayNumber = MakeDay(year, month - 1, day);
        var timeWithinDay = hour * MsPerHour + minute * MsPerMinute + second * MsPerSecond + millisecond;
        var date = MakeDate(dayNumber, timeWithinDay);

        double utc;
        if (hasTime)
        {
            if (hasOffset)
            {
                utc = date - offsetMinutes * MsPerMinute;
            }
            else
            {
                utc = UtcTimeFromLocal(date, realmState);
            }
        }
        else
        {
            utc = date;
        }

        timeValue = TimeClip(utc);
        return true;
    }

    private static bool TryGetDaysInMonth(int year, int month, out int daysInMonth)
    {
        daysInMonth = 0;
        if ((uint)(month - 1) >= 12)
        {
            return false;
        }

        var leap = IsLeapYear(year);
        daysInMonth = month switch
        {
            1 => 31,
            2 => leap ? 29 : 28,
            3 => 31,
            4 => 30,
            5 => 31,
            6 => 30,
            7 => 31,
            8 => 31,
            9 => 30,
            10 => 31,
            11 => 30,
            12 => 31,
            _ => 0
        };

        return daysInMonth != 0;
    }

    private static bool IsValidTime(int hour, int minute, int second, int millisecond)
    {
        if (hour == 24)
        {
            return minute == 0 && second == 0 && millisecond == 0;
        }

        return (uint)hour <= 23 &&
               (uint)minute <= 59 &&
               (uint)second <= 59 &&
               (uint)millisecond <= 999;
    }

    private static int ParseMilliseconds(ReadOnlySpan<char> fraction)
    {
        // ECMAScript milliseconds are 3 digits; fractional seconds are truncated/padded right.
        var ms = 0;
        for (var i = 0; i < 3; i++)
        {
            ms *= 10;
            if (i < fraction.Length)
            {
                ms += fraction[i] - '0';
            }
        }

        return ms;
    }

    private static bool TryParseTimeZoneOffset(
        ReadOnlySpan<char> value,
        out int offsetMinutes,
        out int charsConsumed)
    {
        offsetMinutes = 0;
        charsConsumed = 0;

        if (value.Length < 5 || value[0] is not ('+' or '-'))
        {
            return false;
        }

        var sign = value[0] == '-' ? -1 : 1;

        if (!TryParseTwoDigits(value.Slice(1, 2), out var hours))
        {
            return false;
        }

        var index = 3;
        if (index < value.Length && value[index] == ':')
        {
            index++;
        }

        if (index + 2 > value.Length || !TryParseTwoDigits(value.Slice(index, 2), out var minutes))
        {
            return false;
        }

        if ((uint)hours > 23 || (uint)minutes > 59)
        {
            return false;
        }

        charsConsumed = index + 2;
        offsetMinutes = sign * (hours * 60 + minutes);
        return true;
    }

    private static bool TryParseJsDateToString(ReadOnlySpan<char> value, out double timeValue)
    {
        timeValue = double.NaN;

        var index = 0;
        if (!TryReadToken(value, ref index, out _))
        {
            return false;
        }

        if (!TryReadToken(value, ref index, out var monthToken) ||
            !TryReadToken(value, ref index, out var dayToken) ||
            !TryReadToken(value, ref index, out var yearToken) ||
            !TryReadToken(value, ref index, out var timeToken) ||
            !TryReadToken(value, ref index, out var gmtToken))
        {
            return false;
        }

        if (!TryParseMonthAbbreviation(monthToken, out var month) ||
            !TryParseFixedDigits(dayToken, out var day) ||
            !TryParseFixedDigits(yearToken, out var year) ||
            !TryParseTimeHms(timeToken, out var hour, out var minute, out var second) ||
            !TryParseGmtOffsetToken(gmtToken, out var offsetMinutes))
        {
            return false;
        }

        if (!TryGetDaysInMonth(year, month, out var daysInMonth) || day < 1 || day > daysInMonth)
        {
            timeValue = double.NaN;
            return true;
        }

        var dayNumber = MakeDay(year, month - 1, day);
        var timeWithinDay = hour * MsPerHour + minute * MsPerMinute + second * MsPerSecond;
        var date = MakeDate(dayNumber, timeWithinDay);
        timeValue = TimeClip(date - offsetMinutes * MsPerMinute);
        return true;
    }

    private static bool TryParseJsDateToUtcString(ReadOnlySpan<char> value, out double timeValue)
    {
        timeValue = double.NaN;

        var index = 0;
        if (!TryReadToken(value, ref index, out var weekdayToken))
        {
            return false;
        }

        if (weekdayToken.Length == 0 || weekdayToken[^1] != ',')
        {
            return false;
        }

        if (!TryReadToken(value, ref index, out var dayToken) ||
            !TryReadToken(value, ref index, out var monthToken) ||
            !TryReadToken(value, ref index, out var yearToken) ||
            !TryReadToken(value, ref index, out var timeToken) ||
            !TryReadToken(value, ref index, out var gmtToken))
        {
            return false;
        }

        if (!gmtToken.Equals("GMT", StringComparison.Ordinal) ||
            !TryParseMonthAbbreviation(monthToken, out var month) ||
            !TryParseFixedDigits(dayToken, out var day) ||
            !TryParseFixedDigits(yearToken, out var year) ||
            !TryParseTimeHms(timeToken, out var hour, out var minute, out var second))
        {
            return false;
        }

        if (!TryGetDaysInMonth(year, month, out var daysInMonth) || day < 1 || day > daysInMonth)
        {
            timeValue = double.NaN;
            return true;
        }

        var dayNumber = MakeDay(year, month - 1, day);
        var timeWithinDay = hour * MsPerHour + minute * MsPerMinute + second * MsPerSecond;
        var date = MakeDate(dayNumber, timeWithinDay);
        timeValue = TimeClip(date);
        return true;
    }

    private static bool TryReadToken(ReadOnlySpan<char> value, ref int index, out ReadOnlySpan<char> token)
    {
        while (index < value.Length && value[index] == ' ')
        {
            index++;
        }

        if (index >= value.Length)
        {
            token = default;
            return false;
        }

        var start = index;
        while (index < value.Length && value[index] != ' ')
        {
            index++;
        }

        token = value.Slice(start, index - start);
        return true;
    }

    private static bool TryParseMonthAbbreviation(ReadOnlySpan<char> value, out int month)
    {
        month = value switch
        {
            _ when value.Equals("Jan", StringComparison.Ordinal) => 1,
            _ when value.Equals("Feb", StringComparison.Ordinal) => 2,
            _ when value.Equals("Mar", StringComparison.Ordinal) => 3,
            _ when value.Equals("Apr", StringComparison.Ordinal) => 4,
            _ when value.Equals("May", StringComparison.Ordinal) => 5,
            _ when value.Equals("Jun", StringComparison.Ordinal) => 6,
            _ when value.Equals("Jul", StringComparison.Ordinal) => 7,
            _ when value.Equals("Aug", StringComparison.Ordinal) => 8,
            _ when value.Equals("Sep", StringComparison.Ordinal) => 9,
            _ when value.Equals("Oct", StringComparison.Ordinal) => 10,
            _ when value.Equals("Nov", StringComparison.Ordinal) => 11,
            _ when value.Equals("Dec", StringComparison.Ordinal) => 12,
            _ => 0
        };

        return month != 0;
    }

    private static bool TryParseTimeHms(ReadOnlySpan<char> value, out int hour, out int minute, out int second)
    {
        hour = 0;
        minute = 0;
        second = 0;

        if (value.Length != 8 || value[2] != ':' || value[5] != ':')
        {
            return false;
        }

        return TryParseTwoDigits(value.Slice(0, 2), out hour) &&
               TryParseTwoDigits(value.Slice(3, 2), out minute) &&
               TryParseTwoDigits(value.Slice(6, 2), out second) &&
               IsValidTime(hour, minute, second, 0);
    }

    private static bool TryParseGmtOffsetToken(ReadOnlySpan<char> value, out int offsetMinutes)
    {
        offsetMinutes = 0;
        if (value.Length != 8 || !value.StartsWith("GMT", StringComparison.Ordinal))
        {
            return false;
        }

        var sign = value[3] == '-' ? -1 : 1;
        if (value[3] is not ('+' or '-'))
        {
            return false;
        }

        if (!TryParseTwoDigits(value.Slice(4, 2), out var hours) ||
            !TryParseTwoDigits(value.Slice(6, 2), out var minutes))
        {
            return false;
        }

        if ((uint)hours > 23 || (uint)minutes > 59)
        {
            return false;
        }

        offsetMinutes = sign * (hours * 60 + minutes);
        return true;
    }

    private static bool TryParseFixedDigits(ReadOnlySpan<char> value, out int result)
    {
        result = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var digit = value[i] - '0';
            if ((uint)digit > 9)
            {
                return false;
            }

            result = result * 10 + digit;
        }

        return true;
    }

    private static bool TryParseTwoDigits(ReadOnlySpan<char> value, out int result)
    {
        result = 0;
        if (value.Length != 2)
        {
            return false;
        }

        var digit0 = value[0] - '0';
        var digit1 = value[1] - '0';
        if ((uint)digit0 > 9 || (uint)digit1 > 9)
        {
            return false;
        }

        result = digit0 * 10 + digit1;
        return true;
    }

    private static bool IsAsciiDigit(char c)
        => (uint)(c - '0') <= 9;

    internal static JsValue FormatWithIntlDateTime(
        JsValue dateThis,
        JsValue localesArg,
        JsValue optionsArg,
        RealmState realm,
        string required = "any",
        string defaults = "all")
    {
        var dateObj = RequireDateObject(dateThis, realm);
        var effectiveOptionsArg = ToDateTimeOptions(optionsArg, required, defaults, realm);

        if (realm.Engine?.GlobalObject is not { } global ||
            !global.TryGetProperty("Intl", out var intlVal) || !intlVal.TryGetObject<JsObject>(out var intlObj) ||
            !intlObj.TryGetProperty("DateTimeFormat", out var ctorVal) ||
            !ctorVal.TryGetObject<IJsCallable>(out var ctor))
        {
            return new JsValue("Invalid Date");
        }

        var ctorArgs = new[] { localesArg, effectiveOptionsArg };
        var instance = new JsObject();
        if (ctorVal.TryGetObject<IJsPropertyAccessor>(out var ctorAccessor) &&
            ctorAccessor.TryGetProperty("prototype", out var proto) &&
            proto.TryGetObject<IJsPropertyAccessor>(out var protoAccessor))
        {
            instance.SetPrototype(protoAccessor);
        }

        instance.BeginConstruction();
        JsValue constructed;
        try
        {
            constructed = ctor.Invoke(ctorArgs, new JsValue(instance));
        }
        finally
        {
            instance.EndConstruction();
        }

        JsValue formatter;
        if (constructed.IsObject)
        {
            var constructedObj = constructed.AsObject();
            formatter = constructedObj is not null ? constructed : new JsValue(instance);
        }
        else
        {
            formatter = new JsValue(instance);
        }

        if (!formatter.IsObject || !formatter.TryGetObject<IJsPropertyAccessor>(out var accessor) ||
            !accessor.TryGetProperty("format", formatter, out var formatVal) ||
            !formatVal.TryGetObject<IJsCallable>(out var formatCallable))
        {
            return new JsValue("Invalid Date");
        }

        return formatCallable.Invoke(new SingleValueArgs(new JsValue(dateObj)), formatter);
    }

    internal static JsObject CreateDefaultDateTimeOptions(RealmState realm)
    {
        var opts = new JsObject(realm.ObjectPrototype);
        opts.SetProperty("year", "numeric");
        opts.SetProperty("month", "numeric");
        opts.SetProperty("day", "numeric");
        opts.SetProperty("hour", "numeric");
        opts.SetProperty("minute", "numeric");
        opts.SetProperty("second", "numeric");
        return opts;
    }

    internal static JsObject CreateDefaultDateOptions(RealmState realm)
    {
        var opts = new JsObject(realm.ObjectPrototype);
        opts.SetProperty("year", "numeric");
        opts.SetProperty("month", "numeric");
        opts.SetProperty("day", "numeric");
        return opts;
    }

    internal static JsObject CreateDefaultTimeOptions(RealmState realm)
    {
        var opts = new JsObject(realm.ObjectPrototype);
        opts.SetProperty("hour", "numeric");
        opts.SetProperty("minute", "numeric");
        opts.SetProperty("second", "numeric");
        return opts;
    }

    private static readonly string[] DateComponents =
        ["weekday", "year", "month", "day"];

    private static readonly string[] TimeComponents =
        ["dayPeriod", "hour", "minute", "second", "fractionalSecondDigits"];

    /// <summary>
    /// ECMA-402 ToDateTimeOptions(options, required, defaults).
    /// Merges default date/time components into options when the relevant components are missing.
    /// </summary>
    internal static JsValue ToDateTimeOptions(JsValue optionsArg, string required, string defaults, RealmState realm)
    {
        JsObject options;
        if (optionsArg.IsUndefined)
        {
            options = new JsObject();
        }
        else if (optionsArg.TryGetObject<JsObject>(out var obj))
        {
            // Copy properties to a new object to avoid mutating the caller's object
            options = new JsObject();
            foreach (var key in obj.GetOwnPropertyKeysInOrder(includeSymbols: false))
            {
                if (obj.TryGetProperty(key, out var val))
                {
                    options.SetProperty(key, val);
                }
            }
        }
        else
        {
            options = new JsObject();
        }

        var needDefaults = true;

        // If required is "date" or "any", check for date components
        if (required is "date" or "any")
        {
            foreach (var comp in DateComponents)
            {
                if (options.TryGetProperty(comp, out var v) && !v.IsUndefined)
                {
                    needDefaults = false;
                    break;
                }
            }
        }

        // If still need defaults and required is "time" or "any", check for time components
        if (needDefaults && required is "time" or "any")
        {
            foreach (var comp in TimeComponents)
            {
                if (options.TryGetProperty(comp, out var v) && !v.IsUndefined)
                {
                    needDefaults = false;
                    break;
                }
            }
        }

        // Check for dateStyle/timeStyle
        if (needDefaults)
        {
            if ((required is "date" or "any") && options.TryGetProperty("dateStyle", out var ds) && !ds.IsUndefined)
            {
                needDefaults = false;
            }

            if (needDefaults && (required is "time" or "any") && options.TryGetProperty("timeStyle", out var ts) && !ts.IsUndefined)
            {
                needDefaults = false;
            }
        }

        // Apply defaults
        if (needDefaults)
        {
            if (defaults is "date" or "all")
            {
                options.SetProperty("year", "numeric");
                options.SetProperty("month", "numeric");
                options.SetProperty("day", "numeric");
            }

            if (defaults is "time" or "all")
            {
                options.SetProperty("hour", "numeric");
                options.SetProperty("minute", "numeric");
                options.SetProperty("second", "numeric");
            }
        }

        return new JsValue(options);
    }
}
