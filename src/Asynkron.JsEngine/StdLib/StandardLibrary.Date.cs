using System;
using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateDateObject()
    {
        var date = new JsObject();

        // Date.now() - returns milliseconds since epoch
        date["now"] = new HostFunction(_ => (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        // Date.UTC(...) - returns time value (ms since epoch) for the given UTC date/time components.
        date["UTC"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            double ToNumberOrNaN(object? v)
            {
                return v is double d ? d : double.NaN;
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
                return double.NaN;
            }

            // ECMAScript: years 0–99 are interpreted as 1900–1999.
            var year = (int)y;
            if (year is >= 0 and <= 99)
            {
                year += 1900;
            }

            var month = (int)m + 1; // JS months are 0-based
            var day = (int)dt;
            var hour = (int)h;
            var minute = (int)min;
            var second = (int)s;
            var millisecond = (int)ms;

            try
            {
                var utcDate = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
                var dto = new DateTimeOffset(utcDate);
                return (double)dto.ToUnixTimeMilliseconds();
            }
            catch
            {
                return double.NaN;
            }
        });

        // Date.parse() - parses a date string
        date["parse"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not string dateStr)
            {
                return double.NaN;
            }

            if (DateTimeOffset.TryParse(dateStr, out var parsed))
            {
                return (double)parsed.ToUnixTimeMilliseconds();
            }

            return double.NaN;
        });

        return date;
    }

    /// <summary>
    ///     Creates a Date instance constructor.
    /// </summary>
    public static HostFunction CreateDateConstructor(RealmState realm)
    {
        HostFunction? dateConstructor = null;
        JsObject? datePrototype = null;
        const double MsPerDay = 86400000d;
        const double MsPerHour = 3600000d;
        const double MsPerMinute = 60000d;
        const double MsPerSecond = 1000d;

        var setYearFn = new HostFunction((thisVal, methodArgs) =>
        {
            var timeValue = RequireDateValue(thisVal, realm, out var obj);

            var yearArg = methodArgs.Count > 0 ? methodArgs[0] : Symbol.Undefined;
            if (yearArg is Symbol sym && !ReferenceEquals(sym, Symbol.Undefined) || yearArg is TypedAstSymbol)
            {
                throw ThrowTypeError("Cannot convert a Symbol value to a number", realm: realm);
            }

            var y = JsOps.ToNumber(yearArg);
            if (double.IsNaN(y))
            {
                StoreInternalDateValue(obj, double.NaN);
                return double.NaN;
            }

            var fullYear = MakeFullYear(y);
            var tLocal = double.IsNaN(timeValue) ? 0d : LocalTimeMs(timeValue, realm);
            var day = MakeDay(fullYear, MonthFromTime(tLocal), DateFromTime(tLocal));
            var newDate = MakeDate(day, TimeWithinDay(tLocal));
            var utc = UTCTimeFromLocal(newDate, realm);
            var clipped = TimeClip(utc);

            StoreInternalDateValue(obj, clipped);
            return clipped;
        }, realm);

        object? DateCtorCore(object? thisValue, IReadOnlyList<object?> args, EvaluationContext? context)
        {
            // For `new Date(...)`, the typed evaluator creates the instance
            // object and passes it as `thisValue`. Reuse that object so it
            // keeps the correct prototype chain (Date.prototype).
            var dateInstance = thisValue as JsObject ?? new JsObject();

            if (dateInstance.Prototype is null && datePrototype is not null)
            {
                dateInstance.SetPrototype(datePrototype);
            }

            double timeValue;

            if (args.Count == 0)
            {
                // No arguments: current date/time
                timeValue = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            else if (args.Count == 1)
            {
                // Single argument: milliseconds since epoch or date string
                var arg = args[0];
                if (arg is string dateStr && DateTimeOffset.TryParse(dateStr, out var parsed))
                {
                    timeValue = TimeClip(parsed.ToUnixTimeMilliseconds());
                }
                else
                {
                    var ms = JsOps.ToNumber(arg, context);
                    if (context?.IsThrow == true)
                    {
                        return context.FlowValue;
                    }

                    timeValue = TimeClip(ms);
                }
            }
            else
            {
                // Multiple arguments: year, month, day, hour, minute, second, millisecond
                var yearNum = MakeFullYear(JsOps.ToNumber(args[0], context));
                var monthNum = args.Count > 1 ? JsOps.ToNumber(args[1], context) : 0;
                var dayNum = args.Count > 2 ? JsOps.ToNumber(args[2], context) : 1;
                var hourNum = args.Count > 3 ? JsOps.ToNumber(args[3], context) : 0;
                var minuteNum = args.Count > 4 ? JsOps.ToNumber(args[4], context) : 0;
                var secondNum = args.Count > 5 ? JsOps.ToNumber(args[5], context) : 0;
                var millisecondNum = args.Count > 6 ? JsOps.ToNumber(args[6], context) : 0;

                if (context?.IsThrow == true)
                {
                    return context.FlowValue;
                }

                if (double.IsNaN(yearNum) || double.IsNaN(monthNum) || double.IsNaN(dayNum) ||
                    double.IsNaN(hourNum) || double.IsNaN(minuteNum) || double.IsNaN(secondNum) ||
                    double.IsNaN(millisecondNum))
                {
                    timeValue = double.NaN;
                }
                else
                {
                    var day = MakeDay(yearNum, monthNum, dayNum);
                    var hour = Math.Truncate(hourNum);
                    var minute = Math.Truncate(minuteNum);
                    var second = Math.Truncate(secondNum);
                    var millisecond = Math.Truncate(millisecondNum);

                    if (double.IsInfinity(hour) || double.IsInfinity(minute) ||
                        double.IsInfinity(second) || double.IsInfinity(millisecond))
                    {
                        timeValue = double.NaN;
                    }
                    else
                    {
                        var timeWithinDay = hour * MsPerHour + minute * MsPerMinute + second * MsPerSecond +
                                            millisecond;
                        var localDate = MakeDate(day, timeWithinDay);
                        var utc = UTCTimeFromLocal(localDate, realm);
                        timeValue = TimeClip(utc);
                    }
                }
            }

            // Store the internal date value
            StoreInternalDateValue(dateInstance, timeValue);

            return dateInstance;
        }

        dateConstructor = new HostFunction((thisValue, args) => DateCtorCore(thisValue, args, null),
            isConstructor: true);
        dateConstructor.SetInvokeWithContext((arguments, thisValue, context, _) =>
            DateCtorCore(thisValue, arguments, context));

        dateConstructor.RealmState = realm;
        if (realm.FunctionPrototype is not null)
        {
            dateConstructor.Properties.SetPrototype(realm.FunctionPrototype);
        }

        datePrototype = new JsObject(realm.ObjectPrototype);

        dateConstructor.SetProperty("prototype", datePrototype);
        realm.DatePrototype ??= datePrototype;

        datePrototype.DefineProperty("constructor",
            new PropertyDescriptor
            {
                Value = dateConstructor, Writable = true, Enumerable = false, Configurable = true
            });

        DefineBuiltinFunction(datePrototype, "getTime",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                return timeValue;
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "setTime",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var ms = args.GetArgument(0);
                var clipped = TimeClip(JsOps.ToNumber(ms));
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 1);

        DefineBuiltinFunction(datePrototype, "getFullYear",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return YearFromTime(local);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getMonth",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return (double)MonthFromTime(local);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getDate",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return (double)DateFromTime(local);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getDay",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return WeekDayFromTime(local);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getHours",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return HourFromTime(local);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getMinutes",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return MinFromTime(local);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getSeconds",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return SecFromTime(local);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getMilliseconds",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return MsFromTime(local);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getTimezoneOffset",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var offset = GetLocalOffsetMs(timeValue, realm);
                return -(offset / MsPerMinute);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getUTCFullYear",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return YearFromTime(timeValue);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getUTCMonth",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return (double)MonthFromTime(timeValue);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getUTCDate",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return (double)DateFromTime(timeValue);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getUTCDay",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return WeekDayFromTime(timeValue);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getUTCHours",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return HourFromTime(timeValue);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getUTCMinutes",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return MinFromTime(timeValue);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getUTCSeconds",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return SecFromTime(timeValue);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "getUTCMilliseconds",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return MsFromTime(timeValue);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "setMilliseconds",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var time = LocalTimeMs(timeValue, realm);
                var ms = JsOps.ToNumber(args.GetArgument(0));
                var clipped = SetTimeComponents(time, realm, millisecond: ms);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 1);

        DefineBuiltinFunction(datePrototype, "setUTCMilliseconds",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var ms = JsOps.ToNumber(args.GetArgument(0));
                var clipped = SetTimeComponents(timeValue, realm, millisecond: ms, inputIsUtc: true);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 1);

        DefineBuiltinFunction(datePrototype, "setSeconds",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var time = LocalTimeMs(timeValue, realm);
                var sec = JsOps.ToNumber(args.GetArgument(0));
                var ms = args.Count > 1 ? JsOps.ToNumber(args[1]) : MsFromTime(time);
                var clipped = SetTimeComponents(time, realm, second: sec, millisecond: ms);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 2);

        DefineBuiltinFunction(datePrototype, "setUTCSeconds",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var sec = JsOps.ToNumber(args.GetArgument(0));
                var ms = args.Count > 1 ? JsOps.ToNumber(args[1]) : MsFromTime(timeValue);
                var clipped = SetTimeComponents(timeValue, realm, second: sec, millisecond: ms, inputIsUtc: true);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 2);

        DefineBuiltinFunction(datePrototype, "setMinutes",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var time = LocalTimeMs(timeValue, realm);
                var minute = JsOps.ToNumber(args.GetArgument(0));
                var sec = args.Count > 1 ? JsOps.ToNumber(args[1]) : SecFromTime(time);
                var ms = args.Count > 2 ? JsOps.ToNumber(args[2]) : MsFromTime(time);
                var clipped = SetTimeComponents(time, realm, minute: minute, second: sec, millisecond: ms);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 3);

        DefineBuiltinFunction(datePrototype, "setUTCMinutes",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var minute = JsOps.ToNumber(args.GetArgument(0));
                var sec = args.Count > 1 ? JsOps.ToNumber(args[1]) : SecFromTime(timeValue);
                var ms = args.Count > 2 ? JsOps.ToNumber(args[2]) : MsFromTime(timeValue);
                var clipped = SetTimeComponents(timeValue, realm, minute: minute, second: sec, millisecond: ms,
                    inputIsUtc: true);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 3);

        DefineBuiltinFunction(datePrototype, "setHours",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var time = LocalTimeMs(timeValue, realm);
                var hour = JsOps.ToNumber(args.GetArgument(0));
                var minute = args.Count > 1 ? JsOps.ToNumber(args[1]) : MinFromTime(time);
                var sec = args.Count > 2 ? JsOps.ToNumber(args[2]) : SecFromTime(time);
                var ms = args.Count > 3 ? JsOps.ToNumber(args[3]) : MsFromTime(time);
                var clipped = SetTimeComponents(time, realm, hour: hour, minute: minute, second: sec,
                    millisecond: ms);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 4);

        DefineBuiltinFunction(datePrototype, "setUTCHours",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var hour = JsOps.ToNumber(args.GetArgument(0));
                var minute = args.Count > 1 ? JsOps.ToNumber(args[1]) : MinFromTime(timeValue);
                var sec = args.Count > 2 ? JsOps.ToNumber(args[2]) : SecFromTime(timeValue);
                var ms = args.Count > 3 ? JsOps.ToNumber(args[3]) : MsFromTime(timeValue);
                var clipped = SetTimeComponents(timeValue, realm, hour: hour, minute: minute, second: sec,
                    millisecond: ms, inputIsUtc: true);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 4);

        DefineBuiltinFunction(datePrototype, "setDate",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var time = LocalTimeMs(timeValue, realm);
                var newDt = JsOps.ToNumber(args.GetArgument(0));
                var day = MakeDay(YearFromTime(time), MonthFromTime(time), newDt);
                var clipped = ApplyTimeClip(day, time, realm, false);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 1);

        DefineBuiltinFunction(datePrototype, "setUTCDate",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var newDt = JsOps.ToNumber(args.GetArgument(0));
                var day = MakeDay(YearFromTime(timeValue), MonthFromTime(timeValue), newDt);
                var clipped = ApplyTimeClip(day, timeValue, realm, true);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 1);

        DefineBuiltinFunction(datePrototype, "setMonth",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var time = LocalTimeMs(timeValue, realm);
                var month = JsOps.ToNumber(args.GetArgument(0));
                var dt = args.Count > 1 ? JsOps.ToNumber(args[1]) : DateFromTime(time);
                var day = MakeDay(YearFromTime(time), month, dt);
                var clipped = ApplyTimeClip(day, time, realm, false);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 2);

        DefineBuiltinFunction(datePrototype, "setUTCMonth",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var month = JsOps.ToNumber(args.GetArgument(0));
                var dt = args.Count > 1 ? JsOps.ToNumber(args[1]) : DateFromTime(timeValue);
                var day = MakeDay(YearFromTime(timeValue), month, dt);
                var clipped = ApplyTimeClip(day, timeValue, realm, true);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 2);

        DefineBuiltinFunction(datePrototype, "setFullYear",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var time = LocalTimeMs(timeValue, realm);
                var year = JsOps.ToNumber(args.GetArgument(0));
                var month = args.Count > 1 ? JsOps.ToNumber(args[1]) : MonthFromTime(time);
                var date = args.Count > 2 ? JsOps.ToNumber(args[2]) : DateFromTime(time);
                var clipped = SetFullYear(year, month, date, time, realm, false);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 3);

        DefineBuiltinFunction(datePrototype, "setUTCFullYear",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                var year = JsOps.ToNumber(args.GetArgument(0));
                var month = args.Count > 1 ? JsOps.ToNumber(args[1]) : MonthFromTime(timeValue);
                var date = args.Count > 2 ? JsOps.ToNumber(args[2]) : DateFromTime(timeValue);
                var clipped = SetFullYear(year, month, date, timeValue, realm, true);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            }, realm, isConstructor: false), 3);

        DefineBuiltinFunction(datePrototype, "toISOString",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue) || double.IsInfinity(timeValue))
                {
                    throw ThrowRangeError("Invalid time value", realm: realm);
                }

                var utc = ConvertMillisecondsToUtc(timeValue);
                return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "toJSON",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);
                if (double.IsNaN(timeValue) || double.IsInfinity(timeValue))
                {
                    return null;
                }

                if (!obj.TryGetProperty("toISOString", out var method) || method is not IJsCallable fn)
                {
                    throw ThrowTypeError("toISOString is not callable", realm: realm);
                }

                return fn.Invoke(Array.Empty<object?>(), obj);
            }, realm, isConstructor: false), 1);

        DefineBuiltinFunction(datePrototype, "toString",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return "Invalid Date";
                }

                var local = ConvertMillisecondsToLocal(timeValue, realm);
                return FormatDateToJsString(local, realm);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "toDateString",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return "Invalid Date";
                }

                var local = ConvertMillisecondsToLocal(timeValue, realm);
                return local.ToString("ddd MMM dd yyyy", CultureInfo.InvariantCulture);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "toTimeString",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return "Invalid Date";
                }

                var local = ConvertMillisecondsToLocal(timeValue, realm);
                return local.ToString("HH:mm:ss 'GMT'zzz", CultureInfo.InvariantCulture);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "valueOf",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                return timeValue;
            }, realm, isConstructor: false), 0);

        // Annex B legacy methods and shared prototype methods
        DefineBuiltinFunction(datePrototype, "getYear",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return YearFromTime(local) - 1900;
            }), 0);

        DefineBuiltinFunction(datePrototype, "setYear", setYearFn, 1);

        var protoToUtcStringFn = new HostFunction((thisVal, args) =>
        {
            var timeValue = RequireDateValue(thisVal, realm, out _);
            if (double.IsNaN(timeValue))
            {
                return "Invalid Date";
            }

            var utc = ConvertMillisecondsToUtc(timeValue);
            return FormatUtcToJsUtcString(utc);
        });
        DefineBuiltinFunction(datePrototype, "toUTCString", protoToUtcStringFn, 0);
        datePrototype.DefineProperty("toGMTString",
            new PropertyDescriptor
            {
                Value = protoToUtcStringFn, Writable = true, Enumerable = false, Configurable = true
            });

        DefineBuiltinFunction(datePrototype, "toLocaleString",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return "Invalid Date";
                }

                return FormatWithIntlDateTime(thisVal, args.GetArgument(0), args.GetArgument(1),
                    CreateDefaultDateTimeOptions);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "toLocaleDateString",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return "Invalid Date";
                }

                return FormatWithIntlDateTime(thisVal, args.GetArgument(0), args.GetArgument(1),
                    CreateDefaultDateOptions);
            }, realm, isConstructor: false), 0);

        DefineBuiltinFunction(datePrototype, "toLocaleTimeString",
            new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return "Invalid Date";
                }

                return FormatWithIntlDateTime(thisVal, args.GetArgument(0), args.GetArgument(1),
                    CreateDefaultTimeOptions);
            }, realm, isConstructor: false), 0);

        dateConstructor.DefineProperty("name",
            new PropertyDescriptor { Value = "Date", Writable = false, Enumerable = false, Configurable = true });

        dateConstructor.DefineProperty("length",
            new PropertyDescriptor { Value = 7d, Writable = false, Enumerable = false, Configurable = true });

        return dateConstructor;

        static DateTimeOffset ConvertMillisecondsToLocal(double milliseconds, RealmState realmState)
        {
            var utc = ConvertMillisecondsToUtc(milliseconds);
            return ConvertToConfiguredLocal(utc, realmState);
        }

        static DateTimeOffset ConvertToConfiguredLocal(DateTimeOffset utc, RealmState realmState)
        {
            var timeZone = ResolveTimeZone(realmState);
            return TimeZoneInfo.ConvertTime(utc, timeZone);
        }

        static TimeZoneInfo ResolveTimeZone(RealmState realmState)
        {
            return realmState.Options.TimeZone ?? TimeZoneInfo.Utc;
        }

        object FormatWithIntlDateTime(object? dateThis, object? localesArg, object? optionsArg,
            Func<JsObject>? defaultOptionsFactory)
        {
            if (dateThis is not JsObject dateObj)
            {
                throw ThrowTypeError("Date method called on incompatible receiver", realm: realm);
            }

            if (defaultOptionsFactory is not null &&
                optionsArg is Symbol sym &&
                ReferenceEquals(sym, Symbol.Undefined))
            {
                optionsArg = defaultOptionsFactory();
            }

            if (realm.Engine?.GlobalObject is not JsObject global ||
                !global.TryGetProperty("Intl", out var intlVal) || intlVal is not JsObject intlObj ||
                !intlObj.TryGetProperty("DateTimeFormat", out var ctorVal) ||
                ctorVal is not IJsCallable ctor)
            {
                return "Invalid Date";
            }

            var ctorArgs = new object?[] { localesArg, optionsArg };
            var instance = new JsObject();
            if (ctorVal is IJsPropertyAccessor ctorAccessor &&
                ctorAccessor.TryGetProperty("prototype", out var proto) &&
                proto is IJsPropertyAccessor protoAccessor)
            {
                instance.SetPrototype(protoAccessor);
            }

            instance.BeginConstruction();
            object? constructed;
            try
            {
                constructed = ctor.Invoke(ctorArgs, instance);
            }
            finally
            {
                instance.EndConstruction();
            }

            var formatter = constructed switch
            {
                IJsPropertyAccessor => constructed,
                IJsCallable => constructed,
                _ => (object?)instance
            };

            if (formatter is not IJsPropertyAccessor accessor ||
                !accessor.TryGetProperty("format", formatter, out var formatVal) ||
                formatVal is not IJsCallable formatCallable)
            {
                return "Invalid Date";
            }

            return formatCallable.Invoke(new object?[] { dateObj }, formatter) ?? Symbol.Undefined;
        }

        JsObject CreateDefaultDateTimeOptions()
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

        JsObject CreateDefaultDateOptions()
        {
            var opts = new JsObject(realm.ObjectPrototype);
            opts.SetProperty("year", "numeric");
            opts.SetProperty("month", "numeric");
            opts.SetProperty("day", "numeric");
            return opts;
        }

        JsObject CreateDefaultTimeOptions()
        {
            var opts = new JsObject(realm.ObjectPrototype);
            opts.SetProperty("hour", "numeric");
            opts.SetProperty("minute", "numeric");
            opts.SetProperty("second", "numeric");
            return opts;
        }

        static void StoreInternalDateValue(JsObject obj, double timeValue)
        {
            obj.SetProperty("_internalDate", timeValue);
        }

        static double RequireDateValue(object? thisVal, RealmState realm, out JsObject obj)
        {
            if (thisVal is JsObject candidate &&
                candidate.GetOwnPropertyDescriptor("_internalDate") is { Value: double timeValue })
            {
                obj = candidate;
                return timeValue;
            }

            throw ThrowTypeError("Date method called on incompatible receiver", realm: realm);
        }

        static double MakeFullYear(double year)
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

        static double TimeClip(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || Math.Abs(time) > 8.64e15)
            {
                return double.NaN;
            }

            return Math.Truncate(time);
        }

        static double SetTimeComponents(double time, RealmState realmState, double? hour = null, double? minute = null,
            double? second = null, double? millisecond = null, bool inputIsUtc = false)
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

        static double ApplyTimeClip(double day, double time, RealmState realmState, bool inputIsUtc)
        {
            if (double.IsNaN(day) || double.IsNaN(time))
            {
                return double.NaN;
            }

            var newDate = MakeDate(day, TimeWithinDay(time));
            var utc = inputIsUtc ? newDate : UTCTimeFromLocal(newDate, realmState);
            return TimeClip(utc);
        }

        static double SetFullYear(double year, double month, double date, double time, RealmState realmState,
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

        static double Day(double t)
        {
            return Math.Floor(t / MsPerDay);
        }

        static double TimeWithinDay(double t)
        {
            var result = t % MsPerDay;
            if (result < 0)
            {
                result += MsPerDay;
            }

            return result;
        }

        static bool IsLeapYear(double year)
        {
            var y = (long)Math.Truncate(year);
            return (y % 4 == 0 && y % 100 != 0) || y % 400 == 0;
        }

        static double DayFromYear(double year)
        {
            var y = Math.Truncate(year);
            return 365 * (y - 1970) + Math.Floor((y - 1969) / 4) - Math.Floor((y - 1901) / 100) +
                   Math.Floor((y - 1601) / 400);
        }

        static double TimeFromYear(double year)
        {
            return MsPerDay * DayFromYear(year);
        }

        static double YearFromTime(double t)
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

        static double DayWithinYear(double t)
        {
            var y = YearFromTime(t);
            return Day(t) - DayFromYear(y);
        }

        static int MonthFromTime(double t)
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

        static int DateFromTime(double t)
        {
            var day = DayWithinYear(t);
            var leap = IsLeapYear(YearFromTime(t));
            var monthDayOffsets = leap
                ? new[] { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 }
                : new[] { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 };

            var month = MonthFromTime(t);
            return (int)(day - monthDayOffsets[month] + 1);
        }

        static double MakeDay(double year, double month, double date)
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

        static double MakeDate(double day, double time)
        {
            return day * MsPerDay + time;
        }

        static double HourFromTime(double t)
        {
            return Math.Floor(TimeWithinDay(t) / MsPerHour);
        }

        static double MinFromTime(double t)
        {
            return Math.Floor(TimeWithinDay(t) / MsPerMinute) % 60;
        }

        static double SecFromTime(double t)
        {
            return Math.Floor(TimeWithinDay(t) / MsPerSecond) % 60;
        }

        static double MsFromTime(double t)
        {
            return TimeWithinDay(t) % MsPerSecond;
        }

        static double WeekDayFromTime(double t)
        {
            var w = (Day(t) + 4) % 7;
            if (w < 0)
            {
                w += 7;
            }

            return w;
        }

        static double GetLocalOffsetMs(double utcTime, RealmState realmState)
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

        static double LocalTimeMs(double utcTime, RealmState realmState)
        {
            return utcTime + GetLocalOffsetMs(utcTime, realmState);
        }

        static double UTCTimeFromLocal(double localTime, RealmState realmState)
        {
            var guess = localTime - GetLocalOffsetMs(localTime, realmState);
            var offset = GetLocalOffsetMs(guess, realmState);
            return localTime - offset;
        }

        static string FormatDateToJsString(DateTimeOffset localTime, RealmState realmState)
        {
            // Match the typical "Wed Jan 02 2008 00:00:00 GMT+0100 (Central European Standard Time)" output.
            var culture = CultureInfo.InvariantCulture;
            var weekday = localTime.ToString("ddd", culture);
            var month = localTime.ToString("MMM", culture);
            var day = localTime.ToString("dd", culture);
            var time = localTime.ToString("HH:mm:ss", culture);
            var year = localTime.ToString("yyyy", culture);

            // ECMAScript requires the GMT offset in the form GMT+HHMM.
            var offset = localTime.ToString("zzz", culture).Replace(":", string.Empty);

            var timeZone = ResolveTimeZone(realmState);
            var timeZoneName = timeZone.IsDaylightSavingTime(localTime.DateTime)
                ? timeZone.DaylightName
                : timeZone.StandardName;

            return $"{weekday} {month} {day} {year} {time} GMT{offset} ({timeZoneName})";
        }

        static string FormatUtcToJsUtcString(DateTimeOffset utcTime)
        {
            // Match Node/ECMAScript style: "Thu, 01 Jan 1970 00:00:00 GMT"
            var culture = CultureInfo.InvariantCulture;
            return utcTime.UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", culture);
        }

        static DateTimeOffset ConvertMillisecondsToUtc(double milliseconds)
        {
            if (double.IsNaN(milliseconds))
            {
                return DateTimeOffset.MinValue;
            }

            // JavaScript stores Date values as milliseconds since Unix epoch in UTC.
            // The input can be fractional, but DateTimeOffset only accepts long, so
            // truncate toward zero like ECMAScript's ToIntegerOrInfinity.
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
    }
}
