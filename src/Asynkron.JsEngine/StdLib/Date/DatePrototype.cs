using System.Collections.Generic;
using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Date", ToStringTag = "Date")]
public sealed partial class DatePrototype : JsPrototype
{
    [JsHostMethod("getTime", Length = 0d)]
    public object? GetTime(object? thisValue, IReadOnlyList<object?> args)
    {
        return RequireDateValue(thisValue, Realm, out _);
    }

    [JsHostMethod("setTime", Length = 1d)]
    public object? SetTime(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var ms = args.GetArgument(0);
        var clipped = TimeClip(JsOps.ToNumber(ms));
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("getFullYear", Length = 0d)]
    public object? GetFullYear(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return YearFromTime(local);
    }

    [JsHostMethod("getMonth", Length = 0d)]
    public object? GetMonth(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return (double)MonthFromTime(local);
    }

    [JsHostMethod("getDate", Length = 0d)]
    public object? GetDate(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return (double)DateFromTime(local);
    }

    [JsHostMethod("getDay", Length = 0d)]
    public object? GetDay(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return WeekDayFromTime(local);
    }

    [JsHostMethod("getHours", Length = 0d)]
    public object? GetHours(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return HourFromTime(local);
    }

    [JsHostMethod("getMinutes", Length = 0d)]
    public object? GetMinutes(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return MinFromTime(local);
    }

    [JsHostMethod("getSeconds", Length = 0d)]
    public object? GetSeconds(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return SecFromTime(local);
    }

    [JsHostMethod("getMilliseconds", Length = 0d)]
    public object? GetMilliseconds(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return MsFromTime(local);
    }

    [JsHostMethod("getTimezoneOffset", Length = 0d)]
    public object? GetTimezoneOffset(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var offset = GetLocalOffsetMs(timeValue, Realm);
        return -(offset / MsPerMinute);
    }

    [JsHostMethod("getUTCFullYear", Length = 0d)]
    public object? GetUtcFullYear(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? double.NaN : YearFromTime(timeValue);
    }

    [JsHostMethod("getUTCMonth", Length = 0d)]
    public object? GetUtcMonth(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? double.NaN : (double)MonthFromTime(timeValue);
    }

    [JsHostMethod("getUTCDate", Length = 0d)]
    public object? GetUtcDate(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? double.NaN : (double)DateFromTime(timeValue);
    }

    [JsHostMethod("getUTCDay", Length = 0d)]
    public object? GetUtcDay(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? double.NaN : WeekDayFromTime(timeValue);
    }

    [JsHostMethod("getUTCHours", Length = 0d)]
    public object? GetUtcHours(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? double.NaN : HourFromTime(timeValue);
    }

    [JsHostMethod("getUTCMinutes", Length = 0d)]
    public object? GetUtcMinutes(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? double.NaN : MinFromTime(timeValue);
    }

    [JsHostMethod("getUTCSeconds", Length = 0d)]
    public object? GetUtcSeconds(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? double.NaN : SecFromTime(timeValue);
    }

    [JsHostMethod("getUTCMilliseconds", Length = 0d)]
    public object? GetUtcMilliseconds(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        return double.IsNaN(timeValue) ? double.NaN : MsFromTime(timeValue);
    }

    [JsHostMethod("setMilliseconds", Length = 1d)]
    public object? SetMilliseconds(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var ms = JsOps.ToNumber(args.GetArgument(0));
        var clipped = SetTimeComponents(time, Realm, millisecond: ms);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCMilliseconds", Length = 1d)]
    public object? SetUtcMilliseconds(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var ms = JsOps.ToNumber(args.GetArgument(0));
        var clipped = SetTimeComponents(timeValue, Realm, millisecond: ms, inputIsUtc: true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setSeconds", Length = 2d)]
    public object? SetSeconds(object? thisValue, IReadOnlyList<object?> args)
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
    public object? SetUtcSeconds(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var sec = JsOps.ToNumber(args.GetArgument(0));
        var ms = args.Count > 1 ? JsOps.ToNumber(args[1]) : MsFromTime(timeValue);
        var clipped = SetTimeComponents(timeValue, Realm, second: sec, millisecond: ms, inputIsUtc: true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setMinutes", Length = 3d)]
    public object? SetMinutes(object? thisValue, IReadOnlyList<object?> args)
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
    public object? SetUtcMinutes(object? thisValue, IReadOnlyList<object?> args)
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
    public object? SetHours(object? thisValue, IReadOnlyList<object?> args)
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
    public object? SetUtcHours(object? thisValue, IReadOnlyList<object?> args)
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
    public object? SetDate(object? thisValue, IReadOnlyList<object?> args)
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
    public object? SetUtcDate(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var newDt = JsOps.ToNumber(args.GetArgument(0));
        var day = MakeDay(YearFromTime(timeValue), MonthFromTime(timeValue), newDt);
        var clipped = ApplyTimeClip(day, timeValue, Realm, true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setMonth", Length = 2d)]
    public object? SetMonth(object? thisValue, IReadOnlyList<object?> args)
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
    public object? SetUtcMonth(object? thisValue, IReadOnlyList<object?> args)
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
    public object? SetFullYear(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var time = LocalTimeMs(timeValue, Realm);
        var year = JsOps.ToNumber(args.GetArgument(0));
        var month = args.Count > 1 ? JsOps.ToNumber(args[1]) : MonthFromTime(time);
        var date = args.Count > 2 ? JsOps.ToNumber(args[2]) : DateFromTime(time);
        var clipped = StandardLibrary.SetFullYear(year, month, date, time, Realm, false);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("setUTCFullYear", Length = 3d)]
    public object? SetUtcFullYear(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        var year = JsOps.ToNumber(args.GetArgument(0));
        var month = args.Count > 1 ? JsOps.ToNumber(args[1]) : MonthFromTime(timeValue);
        var date = args.Count > 2 ? JsOps.ToNumber(args[2]) : DateFromTime(timeValue);
        var clipped = StandardLibrary.SetFullYear(year, month, date, timeValue, Realm, true);
        StoreInternalDateValue(obj, clipped);
        return clipped;
    }

    [JsHostMethod("toISOString", Length = 0d)]
    public object? ToIsoString(object? thisValue, IReadOnlyList<object?> args)
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
    public object? ToJson(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);
        if (double.IsNaN(timeValue) || double.IsInfinity(timeValue))
        {
            return null;
        }

        if (!obj.TryGetProperty("toISOString", out var method) || method is not IJsCallable fn)
        {
            throw ThrowTypeError("toISOString is not callable", realm: Realm);
        }

        return fn.Invoke(Array.Empty<object?>(), obj);
    }

    [JsHostMethod("toString", Length = 0d)]
    public object ToString(object? thisValue, IReadOnlyList<object?> args)
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
    public object ToDateString(object? thisValue, IReadOnlyList<object?> args)
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
    public object ToTimeString(object? thisValue, IReadOnlyList<object?> args)
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
    public object? ValueOf(object? thisValue, IReadOnlyList<object?> args)
    {
        return RequireDateValue(thisValue, Realm, out _);
    }

    [JsHostMethod("getYear", Length = 0d)]
    public object? GetYear(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out _);
        if (double.IsNaN(timeValue))
        {
            return double.NaN;
        }

        var local = LocalTimeMs(timeValue, Realm);
        return YearFromTime(local) - 1900;
    }

    [JsHostMethod("setYear", Length = 1d)]
    public object? SetYear(object? thisValue, IReadOnlyList<object?> args)
    {
        var timeValue = RequireDateValue(thisValue, Realm, out var obj);

        var yearArg = args.Count > 0 ? args[0] : Symbol.Undefined;
        if (yearArg is Symbol sym && !ReferenceEquals(sym, Symbol.Undefined) || yearArg is TypedAstSymbol)
        {
            throw ThrowTypeError("Cannot convert a Symbol value to a number", realm: Realm);
        }

        var y = JsOps.ToNumber(yearArg);
        if (double.IsNaN(y))
        {
            StoreInternalDateValue(obj, double.NaN);
            return double.NaN;
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
    public object ToUtcString(object? thisValue, IReadOnlyList<object?> args)
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
    public object ToLocaleString(object? thisValue, IReadOnlyList<object?> args)
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
    public object ToLocaleDateString(object? thisValue, IReadOnlyList<object?> args)
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
    public object ToLocaleTimeString(object? thisValue, IReadOnlyList<object?> args)
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
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.DatePrototype ??= Prototype as JsObject;

        if (Prototype is JsObject prototype &&
            prototype.TryGetProperty("toUTCString", out var toUtc) &&
            toUtc is not null)
        {
            prototype.DefineProperty("toGMTString",
                new PropertyDescriptor
                {
                    Value = toUtc, Writable = true, Enumerable = false, Configurable = true
                });
        }
    }
}
