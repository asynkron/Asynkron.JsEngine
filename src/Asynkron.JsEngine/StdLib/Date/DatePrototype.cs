using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;
using static Asynkron.JsEngine.StdLib.DateHelper;
namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Date", ToStringTag = "Date")]
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
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var ms = args.GetArgument(0);
        var clipped = TimeClip(JsOps.ToNumber(ms));
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
        return -(offset / MsPerMinute);
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
        var ms = JsOps.ToNumber(args.GetArgument(0));
        var clipped = SetTimeComponents(time, Realm, millisecond: ms);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCMilliseconds", Length = 1d)]
    public JsValue SetUtcMilliseconds(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var ms = JsOps.ToNumber(args.GetArgument(0));
        var clipped = SetTimeComponents(timeValue, Realm, millisecond: ms, inputIsUtc: true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setSeconds", Length = 2d)]
    public JsValue SetSeconds(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var sec = JsOps.ToNumber(args.GetArgument(0));
        var ms = args.Count > 1 ? JsOps.ToNumber(args[1]) : MsFromTime(time);
        var clipped = SetTimeComponents(time, Realm, second: sec, millisecond: ms);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCSeconds", Length = 2d)]
    public JsValue SetUtcSeconds(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var sec = JsOps.ToNumber(args.GetArgument(0));
        var ms = args.Count > 1 ? JsOps.ToNumber(args[1]) : MsFromTime(timeValue);
        var clipped = SetTimeComponents(timeValue, Realm, second: sec, millisecond: ms, inputIsUtc: true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setMinutes", Length = 3d)]
    public JsValue SetMinutes(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var minute = JsOps.ToNumber(args.GetArgument(0));
        var sec = args.Count > 1 ? JsOps.ToNumber(args[1]) : SecFromTime(time);
        var ms = args.Count > 2 ? JsOps.ToNumber(args[2]) : MsFromTime(time);
        var clipped = SetTimeComponents(time, Realm, minute: minute, second: sec, millisecond: ms);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCMinutes", Length = 3d)]
    public JsValue SetUtcMinutes(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var minute = JsOps.ToNumber(args.GetArgument(0));
        var sec = args.Count > 1 ? JsOps.ToNumber(args[1]) : SecFromTime(timeValue);
        var ms = args.Count > 2 ? JsOps.ToNumber(args[2]) : MsFromTime(timeValue);
        var clipped = SetTimeComponents(timeValue, Realm, minute: minute, second: sec, millisecond: ms,
            inputIsUtc: true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setHours", Length = 4d)]
    public JsValue SetHours(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var hour = JsOps.ToNumber(args.GetArgument(0));
        var minute = args.Count > 1 ? JsOps.ToNumber(args[1]) : MinFromTime(time);
        var sec = args.Count > 2 ? JsOps.ToNumber(args[2]) : SecFromTime(time);
        var ms = args.Count > 3 ? JsOps.ToNumber(args[3]) : MsFromTime(time);
        var clipped = SetTimeComponents(time, Realm, hour: hour, minute: minute, second: sec, millisecond: ms);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCHours", Length = 4d)]
    public JsValue SetUtcHours(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var hour = JsOps.ToNumber(args.GetArgument(0));
        var minute = args.Count > 1 ? JsOps.ToNumber(args[1]) : MinFromTime(timeValue);
        var sec = args.Count > 2 ? JsOps.ToNumber(args[2]) : SecFromTime(timeValue);
        var ms = args.Count > 3 ? JsOps.ToNumber(args[3]) : MsFromTime(timeValue);
        var clipped = SetTimeComponents(timeValue, Realm, hour: hour, minute: minute, second: sec,
            millisecond: ms, inputIsUtc: true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setDate", Length = 1d)]
    public JsValue SetDate(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var newDt = JsOps.ToNumber(args.GetArgument(0));
        var day = MakeDay(YearFromTime(time), MonthFromTime(time), newDt);
        var clipped = ApplyTimeClip(day, time, Realm, false);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCDate", Length = 1d)]
    public JsValue SetUtcDate(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var newDt = JsOps.ToNumber(args.GetArgument(0));
        var day = MakeDay(YearFromTime(timeValue), MonthFromTime(timeValue), newDt);
        var clipped = ApplyTimeClip(day, timeValue, Realm, true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setMonth", Length = 2d)]
    public JsValue SetMonth(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var month = JsOps.ToNumber(args.GetArgument(0));
        var dt = args.Count > 1 ? JsOps.ToNumber(args[1]) : DateFromTime(time);
        var day = MakeDay(YearFromTime(time), month, dt);
        var clipped = ApplyTimeClip(day, time, Realm, false);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCMonth", Length = 2d)]
    public JsValue SetUtcMonth(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var month = JsOps.ToNumber(args.GetArgument(0));
        var dt = args.Count > 1 ? JsOps.ToNumber(args[1]) : DateFromTime(timeValue);
        var day = MakeDay(YearFromTime(timeValue), month, dt);
        var clipped = ApplyTimeClip(day, timeValue, Realm, true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setFullYear", Length = 3d)]
    public JsValue SetFullYear(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var year = JsOps.ToNumber(args.GetArgument(0));
        var month = args.Count > 1 ? JsOps.ToNumber(args[1]) : MonthFromTime(time);
        var date = args.Count > 2 ? JsOps.ToNumber(args[2]) : DateFromTime(time);
        var clipped = DateHelper.SetFullYear(year, month, date, time, Realm, false);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCFullYear", Length = 3d)]
    public JsValue SetUtcFullYear(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var year = JsOps.ToNumber(args.GetArgument(0));
        var month = args.Count > 1 ? JsOps.ToNumber(args[1]) : MonthFromTime(timeValue);
        var date = args.Count > 2 ? JsOps.ToNumber(args[2]) : DateFromTime(timeValue);
        var clipped = DateHelper.SetFullYear(year, month, date, timeValue, Realm, true);
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

        var utc = ConvertMillisecondsToUtc(timeValue);
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
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
        return local.ToString("HH:mm:ss 'GMT'zzz", CultureInfo.InvariantCulture);
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
        if (yearArg.Kind == JsValueKind.Symbol)
        {
            throw ThrowTypeError("Cannot convert a Symbol value to a number", realm: Realm);
        }

        var y = JsOps.ToNumber(yearArg);
        if (double.IsNaN(y))
        {
            StoreInternalDateValue(obj, double.NaN);
            return JsValue.NaN;
        }

        var fullYear = MakeFullYear(y);
        var tLocal = double.IsNaN(timeValue) ? 0d : LocalTimeMs(timeValue, Realm);
        var day = MakeDay(fullYear, MonthFromTime(tLocal), DateFromTime(tLocal));
        var newDate = MakeDate(day, TimeWithinDay(tLocal));
        var utc = UTCTimeFromLocal(newDate, Realm);
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

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.DatePrototype ??= Prototype as JsObject;

        if (Prototype is not JsObject prototype ||
            !prototype.TryGetProperty("toUTCString", out var toUtc) ||
            toUtc.IsNullOrUndefined)
        {
            return;
        }

        // Convert JsValue to object for PropertyDescriptor
        var toUtcObj = toUtc.TryGetObject<object>(out var obj) ? obj! : toUtc;
        prototype.DefineProperty("toGMTString",
            new PropertyDescriptor
            {
                Value = toUtcObj, Writable = true, Enumerable = false, Configurable = true
            });
    }
}
