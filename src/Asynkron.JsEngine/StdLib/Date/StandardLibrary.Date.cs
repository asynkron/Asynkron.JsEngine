using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateDateConstructor(RealmState realm)
    {
        return DateConstructor.CreateConstructor(realm);
    }

    internal const double MsPerDay = 86400000d;
    internal const double MsPerHour = 3600000d;
    internal const double MsPerMinute = 60000d;
    internal const double MsPerSecond = 1000d;

    internal static JsValue DateParse(IReadOnlyList<JsValue> args, RealmState realm)
    {
        if (args.Count == 0 || !args[0].TryGetString(out var dateStr))
        {
            return JsValue.NaN;
        }

        return DateTimeOffset.TryParse(dateStr, out var parsed)
            ? new JsValue((double)parsed.ToUnixTimeMilliseconds())
            : JsValue.NaN;
    }

    internal static JsValue DateUtc(IReadOnlyList<JsValue> args, RealmState realm)
    {
        if (args.Count == 0)
        {
            return JsValue.NaN;
        }

        double ToNumberOrNaN(JsValue v)
        {
            return v.TryGetDouble(out var d) ? d : double.NaN;
        }

        var y = ToNumberOrNaN(args[0]);
        var m = args.Count > 1 ? ToNumberOrNaN(args[1]) : 0;
        var dt = args.Count > 2 ? ToNumberOrNaN(args[2]) : 1;
        var h = args.Count > 3 ? ToNumberOrNaN(args[3]) : 0;
        var min = args.Count > 4 ? ToNumberOrNaN(args[4]) : 0;
        var s = args.Count > 5 ? ToNumberOrNaN(args[5]) : 0;
        var ms = args.Count > 6 ? ToNumberOrNaN(args[6]) : 0;

        if (double.IsNaN(y) || double.IsNaN(m) || double.IsNaN(dt) ||
            double.IsNaN(h) || double.IsNaN(min) || double.IsNaN(s) || double.IsNaN(ms))
        {
            return JsValue.NaN;
        }

        var year = (int)y;
        if (year is >= 0 and <= 99)
        {
            year += 1900;
        }

        var month = (int)m + 1;
        var day = (int)dt;
        var hour = (int)h;
        var minute = (int)min;
        var second = (int)s;
        var millisecond = (int)ms;

        try
        {
            var utcDate = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
            var dto = new DateTimeOffset(utcDate);
            return new JsValue((double)dto.ToUnixTimeMilliseconds());
        }
        catch
        {
            return JsValue.NaN;
        }
    }

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
            if (arg.TryGetString(out var dateStr) && DateTimeOffset.TryParse(dateStr, out var parsed))
            {
                return TimeClip(parsed.ToUnixTimeMilliseconds());
            }

            var ctx = context ?? realm.CreateContext();
            var ms = JsOps.ToNumberWithContext(arg, ctx);
            if (ctx.IsThrow)
            {
                throw new ThrowSignal(ctx.FlowValue);
            }

            return TimeClip(ms);
        }

        var evalContext = context ?? realm.CreateContext();
        var yearNum = MakeFullYear(JsOps.ToNumberWithContext(args[0], evalContext));
        var monthNum = args.Count > 1 ? JsOps.ToNumberWithContext(args[1], evalContext) : 0;
        var dayNum = args.Count > 2 ? JsOps.ToNumberWithContext(args[2], evalContext) : 1;
        var hourNum = args.Count > 3 ? JsOps.ToNumberWithContext(args[3], evalContext) : 0;
        var minuteNum = args.Count > 4 ? JsOps.ToNumberWithContext(args[4], evalContext) : 0;
        var secondNum = args.Count > 5 ? JsOps.ToNumberWithContext(args[5], evalContext) : 0;
        var millisecondNum = args.Count > 6 ? JsOps.ToNumberWithContext(args[6], evalContext) : 0;

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
        var utc = UTCTimeFromLocal(localDate, realm);
        return TimeClip(utc);
    }

    internal static void StoreInternalDateValue(JsObject obj, double timeValue)
    {
        obj.SetProperty("_internalDate", new JsValue(timeValue));
    }

    internal static double RequireDateValue(JsValue thisVal, RealmState realm, out JsObject obj)
    {
        if (thisVal.IsObject)
        {
            var candidate = thisVal.AsObject();
            if (candidate.GetOwnPropertyDescriptor("_internalDate") is { Value: double timeValue })
            {
                obj = candidate;
                return timeValue;
            }
        }

        throw ThrowTypeError("Date method called on incompatible receiver", realm: realm);
    }

    internal static JsObject RequireDateObject(JsValue thisVal, RealmState realm)
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

        return Math.Truncate(time);
    }

    internal static double SetTimeComponents(double time, RealmState realmState, double? hour = null,
        double? minute = null, double? second = null, double? millisecond = null, bool inputIsUtc = false)
    {
        if (double.IsNaN(time))
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
        var utc = inputIsUtc ? newDate : UTCTimeFromLocal(newDate, realmState);
        return TimeClip(utc);
    }

    internal static double ApplyTimeClip(double day, double time, RealmState realmState, bool inputIsUtc)
    {
        if (double.IsNaN(day) || double.IsNaN(time))
        {
            return double.NaN;
        }

        var newDate = MakeDate(day, TimeWithinDay(time));
        var utc = inputIsUtc ? newDate : UTCTimeFromLocal(newDate, realmState);
        return TimeClip(utc);
    }

    internal static double SetFullYear(double year, double month, double date, double time, RealmState realmState,
        bool inputIsUtc)
    {
        var timeValue = double.IsNaN(time) ? 0 : time;
        var y = ToIntegerOrInfinity(year);
        var m = ToIntegerOrInfinity(month);
        var dt = ToIntegerOrInfinity(date);
        if (double.IsInfinity(y) || double.IsInfinity(m) || double.IsInfinity(dt))
        {
            return double.NaN;
        }

        var day = MakeDay(y, m, dt);
        var newDate = MakeDate(day, TimeWithinDay(timeValue));
        var utc = inputIsUtc ? newDate : UTCTimeFromLocal(newDate, realmState);
        return TimeClip(utc);
    }

    internal static double Day(double t)
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

    internal static bool IsLeapYear(double year)
    {
        var y = (long)Math.Truncate(year);
        return (y % 4 == 0 && y % 100 != 0) || y % 400 == 0;
    }

    internal static double DayFromYear(double year)
    {
        var y = Math.Truncate(year);
        return 365 * (y - 1970) + Math.Floor((y - 1969) / 4) - Math.Floor((y - 1901) / 100) +
               Math.Floor((y - 1601) / 400);
    }

    internal static double TimeFromYear(double year)
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

    internal static double DayWithinYear(double t)
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
            ym -= 1;
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

    internal static double UTCTimeFromLocal(double localTime, RealmState realmState)
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

    internal static DateTimeOffset ConvertToConfiguredLocal(DateTimeOffset utc, RealmState realmState)
    {
        var timeZone = ResolveTimeZone(realmState);
        return TimeZoneInfo.ConvertTime(utc, timeZone);
    }

    internal static TimeZoneInfo ResolveTimeZone(RealmState realmState)
    {
        return realmState.Options.TimeZone ?? TimeZoneInfo.Utc;
    }

    internal static string FormatDateToJsString(DateTimeOffset localTime, RealmState realmState)
    {
        var culture = CultureInfo.InvariantCulture;
        var weekday = localTime.ToString("ddd", culture);
        var month = localTime.ToString("MMM", culture);
        var day = localTime.ToString("dd", culture);
        var time = localTime.ToString("HH:mm:ss", culture);
        var year = localTime.ToString("yyyy", culture);

        var offset = localTime.ToString("zzz", culture).Replace(":", string.Empty);

        var timeZone = ResolveTimeZone(realmState);
        var timeZoneName = timeZone.IsDaylightSavingTime(localTime.DateTime)
            ? timeZone.DaylightName
            : timeZone.StandardName;

        return $"{weekday} {month} {day} {year} {time} GMT{offset} ({timeZoneName})";
    }

    internal static string FormatUtcToJsUtcString(DateTimeOffset utcTime)
    {
        var culture = CultureInfo.InvariantCulture;
        return utcTime.UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", culture);
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

    internal static JsValue FormatWithIntlDateTime(
        JsValue dateThis,
        JsValue localesArg,
        JsValue optionsArg,
        RealmState realm,
        Func<JsObject>? defaultOptionsFactory)
    {
        var dateObj = RequireDateObject(dateThis, realm);
        var effectiveOptionsArg = optionsArg;
        if (defaultOptionsFactory is not null &&
            optionsArg.IsSymbol &&
            ReferenceEquals(optionsArg.AsSymbol(), Symbol.Undefined))
        {
            effectiveOptionsArg = new JsValue(defaultOptionsFactory());
        }

        if (realm.Engine?.GlobalObject is not JsObject global ||
            !global.TryGetProperty("Intl", out var intlVal) || !intlVal.TryGetObject<JsObject>(out var intlObj) ||
            !intlObj.TryGetProperty("DateTimeFormat", out var ctorVal) ||
            !ctorVal.TryGetObject<IJsCallable>(out var ctor))
        {
            return new JsValue("Invalid Date");
        }

        var ctorArgs = new JsValue[] { localesArg, effectiveOptionsArg };
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
            if (constructedObj is IJsPropertyAccessor || constructedObj is IJsCallable)
            {
                formatter = constructed;
            }
            else
            {
                formatter = new JsValue(instance);
            }
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

        return formatCallable.Invoke([new JsValue(dateObj)], formatter);
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
}
