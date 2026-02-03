#region

using System.Globalization;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using Asynkron.JsEngine.StdLib.Temporal;
using static Asynkron.JsEngine.StdLib.DateHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Date")]
[JsMethodAlias("toGMTString", "toUTCString")]
public sealed partial class DatePrototype
{
    [JsHostMethod("getTime", Length = 0d)]
    public JsValue GetTime(JsValue thisValue)
    {
        return RequireDateValue(thisValue, Realm, out _);
    }

    [JsHostMethod("setTime", Length = 1d)]
    public JsValue SetTime(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        RequireDateValue(thisValue, Realm, out var obj);
        var ms = args.GetArgument(0);
        var evalContext = Realm?.CreateContext();
        var clipped = TimeClip(JsOps.ToNumber(ms, evalContext));
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("getFullYear", Length = 0d)]
    public JsValue GetFullYear(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return JsValue.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return YearFromTime(local);
    }

    [JsHostMethod("getMonth", Length = 0d)]
    public JsValue GetMonth(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return JsValue.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return (double)MonthFromTime(local);
    }

    [JsHostMethod("getDate", Length = 0d)]
    public JsValue GetDate(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return JsValue.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return (double)DateFromTime(local);
    }

    [JsHostMethod("getDay", Length = 0d)]
    public JsValue GetDay(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return JsValue.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return WeekDayFromTime(local);
    }

    [JsHostMethod("getHours", Length = 0d)]
    public JsValue GetHours(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return JsValue.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return HourFromTime(local);
    }

    [JsHostMethod("getMinutes", Length = 0d)]
    public JsValue GetMinutes(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return JsValue.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return MinFromTime(local);
    }

    [JsHostMethod("getSeconds", Length = 0d)]
    public JsValue GetSeconds(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return JsValue.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return SecFromTime(local);
    }

    [JsHostMethod("getMilliseconds", Length = 0d)]
    public JsValue GetMilliseconds(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return JsValue.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return MsFromTime(local);
    }

    [JsHostMethod("getTimezoneOffset", Length = 0d)]
    public JsValue GetTimezoneOffset(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return JsValue.NaN;
        }

        var offset = GetLocalOffsetMs(timeValue, Realm);
        var minutes = -(offset / MsPerMinute);
        return minutes == 0 ? 0 : minutes;
    }

    [JsHostMethod("getUTCFullYear", Length = 0d)]
    public JsValue GetUtcFullYear(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? JsValue.NaN : YearFromTime(timeValue);
    }

    [JsHostMethod("getUTCMonth", Length = 0d)]
    public JsValue GetUtcMonth(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? JsValue.NaN : MonthFromTime(timeValue);
    }

    [JsHostMethod("getUTCDate", Length = 0d)]
    public JsValue GetUtcDate(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? JsValue.NaN : DateFromTime(timeValue);
    }

    [JsHostMethod("getUTCDay", Length = 0d)]
    public JsValue GetUtcDay(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? JsValue.NaN : WeekDayFromTime(timeValue);
    }

    [JsHostMethod("getUTCHours", Length = 0d)]
    public JsValue GetUtcHours(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? JsValue.NaN : HourFromTime(timeValue);
    }

    [JsHostMethod("getUTCMinutes", Length = 0d)]
    public JsValue GetUtcMinutes(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? JsValue.NaN : MinFromTime(timeValue);
    }

    [JsHostMethod("getUTCSeconds", Length = 0d)]
    public JsValue GetUtcSeconds(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? JsValue.NaN : SecFromTime(timeValue);
    }

    [JsHostMethod("getUTCMilliseconds", Length = 0d)]
    public JsValue GetUtcMilliseconds(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? JsValue.NaN : MsFromTime(timeValue);
    }

    [JsHostMethod("setMilliseconds", Length = 1d)]
    public JsValue SetMilliseconds(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var evalContext = Realm?.CreateContext();
        var ms = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var clipped = SetTimeComponents(time, Realm!, millisecond: ms);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCMilliseconds", Length = 1d)]
    public JsValue SetUtcMilliseconds(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var evalContext = Realm?.CreateContext();
        var ms = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var clipped = SetTimeComponents(timeValue, Realm!, millisecond: ms, inputIsUtc: true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setSeconds", Length = 2d)]
    public JsValue SetSeconds(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var evalContext = Realm?.CreateContext();
        var sec = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var ms = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : MsFromTime(time);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var clipped = SetTimeComponents(time, Realm!, second: sec, millisecond: ms);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCSeconds", Length = 2d)]
    public JsValue SetUtcSeconds(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var evalContext = Realm?.CreateContext();
        var sec = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var ms = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : MsFromTime(timeValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var clipped = SetTimeComponents(timeValue, Realm!, second: sec, millisecond: ms, inputIsUtc: true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setMinutes", Length = 3d)]
    public JsValue SetMinutes(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var evalContext = Realm?.CreateContext();
        var minute = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var sec = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : SecFromTime(time);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var ms = args.Count > 2 ? JsOps.ToNumber(args[2], evalContext) : MsFromTime(time);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var clipped = SetTimeComponents(time, Realm!, minute: minute, second: sec, millisecond: ms);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCMinutes", Length = 3d)]
    public JsValue SetUtcMinutes(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var evalContext = Realm?.CreateContext();
        var minute = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var sec = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : SecFromTime(timeValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var ms = args.Count > 2 ? JsOps.ToNumber(args[2], evalContext) : MsFromTime(timeValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var clipped = SetTimeComponents(timeValue, Realm!, minute: minute, second: sec, millisecond: ms,
            inputIsUtc: true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setHours", Length = 4d)]
    public JsValue SetHours(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var evalContext = Realm?.CreateContext();
        var hour = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var minute = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : MinFromTime(time);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var sec = args.Count > 2 ? JsOps.ToNumber(args[2], evalContext) : SecFromTime(time);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var ms = args.Count > 3 ? JsOps.ToNumber(args[3], evalContext) : MsFromTime(time);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var clipped = SetTimeComponents(time, Realm!, hour, minute, sec, ms);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCHours", Length = 4d)]
    public JsValue SetUtcHours(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var evalContext = Realm?.CreateContext();
        var hour = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var minute = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : MinFromTime(timeValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var sec = args.Count > 2 ? JsOps.ToNumber(args[2], evalContext) : SecFromTime(timeValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var ms = args.Count > 3 ? JsOps.ToNumber(args[3], evalContext) : MsFromTime(timeValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var clipped = SetTimeComponents(timeValue, Realm!, hour, minute, sec,
            ms, true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setDate", Length = 1d)]
    public JsValue SetDate(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var evalContext = Realm?.CreateContext();
        var newDt = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var time = LocalTimeMs(timeValue, Realm!);
        var day = MakeDay(YearFromTime(time), MonthFromTime(time), newDt);
        var clipped = ApplyTimeClip(day, time, Realm!, false);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCDate", Length = 1d)]
    public JsValue SetUtcDate(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var evalContext = Realm?.CreateContext();
        var newDt = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var day = MakeDay(YearFromTime(timeValue), MonthFromTime(timeValue), newDt);
        var clipped = ApplyTimeClip(day, timeValue, Realm!, true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setMonth", Length = 2d)]
    public JsValue SetMonth(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var evalContext = Realm?.CreateContext();
        var month = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var dt = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : DateFromTime(time);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var day = MakeDay(YearFromTime(time), month, dt);
        var clipped = ApplyTimeClip(day, time, Realm!, false);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCMonth", Length = 2d)]
    public JsValue SetUtcMonth(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var evalContext = Realm?.CreateContext();
        var month = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var dt = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : DateFromTime(timeValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var day = MakeDay(YearFromTime(timeValue), month, dt);
        var clipped = ApplyTimeClip(day, timeValue, Realm!, true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setFullYear", Length = 3d)]
    public JsValue SetFullYear(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = double.IsNaN(timeValue) ? 0d : LocalTimeMs(timeValue, Realm);
        var evalContext = Realm?.CreateContext();
        var year = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var month = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : MonthFromTime(time);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var date = args.Count > 2 ? JsOps.ToNumber(args[2], evalContext) : DateFromTime(time);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var clipped = DateHelper.SetFullYear(year, month, date, time, Realm!, false);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCFullYear", Length = 3d)]
    public JsValue SetUtcFullYear(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var evalContext = Realm?.CreateContext();
        var year = JsOps.ToNumber(args.GetArgument(0), evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var month = args.Count > 1 ? JsOps.ToNumber(args[1], evalContext) : MonthFromTime(timeValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var date = args.Count > 2 ? JsOps.ToNumber(args[2], evalContext) : DateFromTime(timeValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var clipped = DateHelper.SetFullYear(year, month, date, timeValue, Realm!, true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("toISOString", Length = 0d)]
    public JsValue ToIsoString(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue) || double.IsInfinity(timeValue))
        {
            throw ThrowRangeError("Invalid time value", realm: Realm);
        }

        return FormatUtcToIsoString(timeValue);
    }

    [JsHostMethod("toJSON", Length = 1d)]
    public JsValue ToJson(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        if (double.IsNaN(timeValue) || double.IsInfinity(timeValue))
        {
            return JsValue.Null;
        }

        if (!obj.TryGetProperty("toISOString", out var method))
        {
            throw ThrowTypeError("toISOString is not callable", realm: Realm);
        }

        // method is already a JsValue from TryGetProperty
        if (!method.TryGetObject<IJsCallable>(out var fn))
        {
            throw ThrowTypeError("toISOString is not callable", realm: Realm);
        }

        return fn.Invoke([], new JsValue(obj));
    }

    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return "Invalid Date";
        }

        var local = ConvertMillisecondsToLocal(timeValue, Realm);
        return FormatDateToJsString(local, Realm);
    }

    [JsHostMethod("toDateString", Length = 0d)]
    public JsValue ToDateString(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return "Invalid Date";
        }

        var local = ConvertMillisecondsToLocal(timeValue, Realm);
        return local.ToString("ddd MMM dd yyyy", CultureInfo.InvariantCulture);
    }

    [JsHostMethod("toTimeString", Length = 0d)]
    public JsValue ToTimeString(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return "Invalid Date";
        }

        var local = ConvertMillisecondsToLocal(timeValue, Realm);
        var culture = CultureInfo.InvariantCulture;
        var time = local.ToString("HH:mm:ss", culture);
        var offset = local.ToString("zzz", culture).Replace(":", string.Empty, StringComparison.Ordinal);
        var timeZone = Realm?.Options.TimeZone ?? TimeZoneInfo.Utc;
        var timeZoneName = timeZone.IsDaylightSavingTime(local.DateTime)
            ? timeZone.DaylightName
            : timeZone.StandardName;
        return $"{time} GMT{offset} ({timeZoneName})";
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public JsValue ValueOf(JsValue thisValue)
    {
        return RequireDateValue(thisValue, Realm, out _);
    }

    [JsHostMethod("getYear", Length = 0d)]
    public JsValue GetYear(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return JsValue.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return YearFromTime(local) - 1900;
    }

    [JsHostMethod("setYear", Length = 1d)]
    public JsValue SetYear(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);

        var yearArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var evalContext = Realm?.CreateContext();
        var y = JsOps.ToNumber(yearArg, evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        if (double.IsNaN(y))
        {
            StoreInternalDateValue(obj, double.NaN);
            return JsValue.NaN;
        }

        var fullYear = MakeFullYear(y);
        var tLocal = double.IsNaN(timeValue) ? 0d : LocalTimeMs(timeValue, Realm!);
        var day = MakeDay(fullYear, MonthFromTime(tLocal), DateFromTime(tLocal));
        var newDate = MakeDate(day, TimeWithinDay(tLocal));
        var utc = UtcTimeFromLocal(newDate, Realm!);
        var clipped = TimeClip(utc);

        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("toUTCString", Length = 0d)]
    public JsValue ToUtcString(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return "Invalid Date";
        }

        var utc = ConvertMillisecondsToUtc(timeValue);
        return FormatUtcToJsUtcString(utc);
    }

    [JsHostMethod("toLocaleString", Length = 0d)]
    public JsValue ToLocaleString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return "Invalid Date";
        }

        return FormatWithIntlDateTime(thisValue, args.GetArgument(0), args.GetArgument(1),
            Realm, () => CreateDefaultDateTimeOptions(Realm));
    }

    [JsHostMethod("toLocaleDateString", Length = 0d)]
    public JsValue ToLocaleDateString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return "Invalid Date";
        }

        return FormatWithIntlDateTime(thisValue, args.GetArgument(0), args.GetArgument(1),
            Realm, () => CreateDefaultDateOptions(Realm));
    }

    [JsHostMethod("toLocaleTimeString", Length = 0d)]
    public JsValue ToLocaleTimeString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return "Invalid Date";
        }

        return FormatWithIntlDateTime(thisValue, args.GetArgument(0), args.GetArgument(1),
            Realm, () => CreateDefaultTimeOptions(Realm));
    }

    /* FLAKY */
    [JsHostMethod("toTemporalInstant", Length = 0d)]
    public JsValue ToTemporalInstant(JsValue thisValue)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue) || double.IsInfinity(timeValue))
        {
            throw ThrowRangeError("Invalid time value", realm: Realm);
        }

        // Date -> Temporal.Instant is millisecond-based, so wrap the epoch milliseconds directly.
        return TemporalHelper.CreateInstantFromEpochMilliseconds(Realm, timeValue);
    }

    [JsSymbolMethod("toPrimitive", Length = 1d)]
    public JsValue ToPrimitive(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        RequireDateValue(thisValue, Realm, out var obj);

        if (!args.GetArgument(0).TryGetString(out var hint))
        {
            throw ThrowTypeError("Cannot convert object to primitive value", realm: Realm);
        }

        // Date prefers string conversion for the "default" hint.
        var preferString = hint is "string" or "default";
        if (!preferString && !string.Equals(hint, "number", StringComparison.Ordinal))
        {
            throw ThrowTypeError("Cannot convert object to primitive value", realm: Realm);
        }

        if (preferString)
        {
            if (TryInvokeAndReturnPrimitive(obj, "toString", out var result) ||
                TryInvokeAndReturnPrimitive(obj, "valueOf", out result))
            {
                return result;
            }
        }
        else
        {
            if (TryInvokeAndReturnPrimitive(obj, "valueOf", out var result) ||
                TryInvokeAndReturnPrimitive(obj, "toString", out result))
            {
                return result;
            }
        }

        throw ThrowTypeError("Cannot convert object to primitive value", realm: Realm);
    }

    private static bool TryInvokeAndReturnPrimitive(JsObject obj, string methodName, out JsValue result)
    {
        result = JsValue.Undefined;
        if (!obj.TryGetProperty(methodName, out var method) ||
            !method.TryGetObject<IJsCallable>(out var callable))
        {
            return false;
        }

        // Follow OrdinaryToPrimitive: only return if the result is not an object.
        var value = callable.Invoke([], new JsValue(obj));
        if (value.IsObject)
        {
            return false;
        }

        result = value;
        return true;
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.DatePrototype ??= Prototype as JsObject;

        // toGMTString is registered via code generation from [JsMethodAlias] attribute
    }
}
