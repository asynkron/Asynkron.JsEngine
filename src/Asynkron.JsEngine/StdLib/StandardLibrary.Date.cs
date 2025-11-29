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

        dateConstructor = new HostFunction((thisValue, args) =>
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
                    var ms = JsOps.ToNumber(arg);
                    timeValue = TimeClip(ms);
                }
            }
            else
            {
                // Multiple arguments: year, month, day, hour, minute, second, millisecond
                var yearNum = MakeFullYear(JsOps.ToNumber(args[0]));
                var monthNum = args.Count > 1 ? JsOps.ToNumber(args[1]) : 0;
                var dayNum = args.Count > 2 ? JsOps.ToNumber(args[2]) : 1;
                var hourNum = args.Count > 3 ? JsOps.ToNumber(args[3]) : 0;
                var minuteNum = args.Count > 4 ? JsOps.ToNumber(args[4]) : 0;
                var secondNum = args.Count > 5 ? JsOps.ToNumber(args[5]) : 0;
                var millisecondNum = args.Count > 6 ? JsOps.ToNumber(args[6]) : 0;

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

            // Add instance methods
            dateInstance["getTime"] = new HostFunction((thisVal, args) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) &&
                    val is double ms)
                {
                    return ms;
                }

                return double.NaN;
            });

            dateInstance["setTime"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is not JsObject obj)
                {
                    throw ThrowTypeError("Date method called on incompatible receiver", realm: realm);
                }

                var ms = methodArgs.Count > 0 ? JsOps.ToNumber(methodArgs[0]) : double.NaN;
                var clipped = TimeClip(ms);
                StoreInternalDateValue(obj, clipped);
                return clipped;
            });

            dateInstance["getFullYear"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return YearFromTime(local);
            });

            var getYearFn = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return YearFromTime(local) - 1900;
            });
            DefineBuiltinFunction(dateInstance, "getYear", getYearFn, 0);

            var setYearFn = new HostFunction((thisVal, methodArgs) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);

                var yearArg = methodArgs.Count > 0 ? methodArgs[0] : Symbol.Undefined;
                if (yearArg is Symbol sym && !ReferenceEquals(sym, Symbol.Undefined))
                {
                    throw ThrowTypeError("Cannot convert a Symbol value to a number", realm: realm);
                }

                if (yearArg is TypedAstSymbol)
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
                if (double.IsNaN(fullYear) || double.IsInfinity(fullYear))
                {
                    StoreInternalDateValue(obj, double.NaN);
                    return double.NaN;
                }

                var tLocal = double.IsNaN(timeValue) ? 0d : LocalTimeMs(timeValue, realm);
                var month = MonthFromTime(tLocal);
                var date = DateFromTime(tLocal);
                var hour = HourFromTime(tLocal);
                var minute = MinFromTime(tLocal);
                var second = SecFromTime(tLocal);
                var millisecond = MsFromTime(tLocal);

                double clipped;
                if (fullYear is >= 1 and <= 9999 &&
                    month is >= 0 and < 12 &&
                    date is >= 1 and <= 31)
                {
                    try
                    {
                        var localDate = new DateTime(
                            (int)fullYear,
                            month + 1,
                            date,
                            (int)hour,
                            (int)minute,
                            (int)second,
                            (int)millisecond,
                            DateTimeKind.Local);
                        var utcMs = new DateTimeOffset(localDate).ToUniversalTime().ToUnixTimeMilliseconds();
                        clipped = TimeClip(utcMs);
                    }
                    catch
                    {
                        clipped = double.NaN;
                    }
                }
                else
                {
                    var d = MakeDay(fullYear, month, date);
                    var newDate = MakeDate(d, TimeWithinDay(tLocal));
                    var utc = UTCTimeFromLocal(newDate, realm);
                    clipped = TimeClip(utc);
                }

                StoreInternalDateValue(obj, clipped);
                return clipped;
            });
            DefineBuiltinFunction(dateInstance, "setYear", setYearFn, 1);

            dateInstance["getMonth"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return (double)MonthFromTime(local); // JS months are 0-indexed
            });

            dateInstance["getDate"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return (double)DateFromTime(local);
            });

            dateInstance["getDay"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return WeekDayFromTime(local);
            });

            dateInstance["getHours"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return HourFromTime(local);
            });

            dateInstance["getMinutes"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return MinFromTime(local);
            });

            dateInstance["getSeconds"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return SecFromTime(local);
            });

            dateInstance["getMilliseconds"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var local = LocalTimeMs(timeValue, realm);
                return MsFromTime(local);
            });

            dateInstance["getTimezoneOffset"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                var offset = GetLocalOffsetMs(timeValue, realm);
                return -(offset / MsPerMinute);
            });

            dateInstance["toISOString"] = new HostFunction((thisVal, args) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) &&
                    val is double ms && !double.IsNaN(ms))
                {
                    var dt = ConvertMillisecondsToUtc(ms);
                    return dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                }

                return double.NaN;
            });

            dateInstance["toString"] = new HostFunction((thisVal, args) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) &&
                    val is double ms && !double.IsNaN(ms))
                {
                    try
                    {
                        var local = ConvertMillisecondsToLocal(ms, realm);
                        return FormatDateToJsString(local, realm);
                    }
                    catch
                    {
                        return "Invalid Date";
                    }
                }

                return "Invalid Date";
            });

            // UTC-based accessors
            dateInstance["getUTCFullYear"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return YearFromTime(timeValue);
            });

            dateInstance["getUTCMonth"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return (double)MonthFromTime(timeValue);
            });

            dateInstance["getUTCDate"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return (double)DateFromTime(timeValue);
            });

            dateInstance["getUTCDay"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return WeekDayFromTime(timeValue);
            });

            dateInstance["getUTCHours"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return HourFromTime(timeValue);
            });

            dateInstance["getUTCMinutes"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return MinFromTime(timeValue);
            });

            dateInstance["getUTCSeconds"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return SecFromTime(timeValue);
            });

            dateInstance["getUTCMilliseconds"] = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return double.NaN;
                }

                return MsFromTime(timeValue);
            });

            // Formatting helpers
            var toUtcStringFn = new HostFunction((thisVal, args) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out _);
                if (double.IsNaN(timeValue))
                {
                    return "Invalid Date";
                }

                var utc = ConvertMillisecondsToUtc(timeValue);
                return FormatUtcToJsUtcString(utc);
            });
            DefineBuiltinFunction(dateInstance, "toUTCString", toUtcStringFn, 0);
            dateInstance.DefineProperty("toGMTString",
                new PropertyDescriptor
                {
                    Value = toUtcStringFn, Writable = true, Enumerable = false, Configurable = true
                });

            dateInstance["toJSON"] = new HostFunction((thisVal, args) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) &&
                    val is double ms && !double.IsNaN(ms))
                {
                    var dt = ConvertMillisecondsToUtc(ms);
                    return dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                }

                return null;
            });

            // valueOf() – mirrors getTime()
            dateInstance["valueOf"] = new HostFunction((thisVal, args) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    return ms;
                }

                return double.NaN;
            });

            return dateInstance;
        });

        dateConstructor.RealmState = realm;
        if (realm.FunctionPrototype is not null)
        {
            dateConstructor.Properties.SetPrototype(realm.FunctionPrototype);
        }

        datePrototype = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            datePrototype.SetPrototype(realm.ObjectPrototype);
        }

        dateConstructor.SetProperty("prototype", datePrototype);
        realm.DatePrototype ??= datePrototype;

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

        DefineBuiltinFunction(datePrototype, "setYear",
            new HostFunction((thisVal, methodArgs) =>
            {
                var timeValue = RequireDateValue(thisVal, realm, out var obj);

                var yearArg = methodArgs.Count > 0 ? methodArgs[0] : Symbol.Undefined;
                if (yearArg is Symbol sym && !ReferenceEquals(sym, Symbol.Undefined))
                {
                    throw ThrowTypeError("Cannot convert a Symbol value to a number", realm: realm);
                }

                if (yearArg is TypedAstSymbol)
                {
                    throw ThrowTypeError("Cannot convert a Symbol value to a number", realm: realm);
                }

                var y = JsOps.ToNumber(yearArg);
                var fullYear = MakeFullYear(y);

                var tLocal = double.IsNaN(timeValue) ? 0d : LocalTimeMs(timeValue, realm);
                var month = MonthFromTime(tLocal);
                var date = DateFromTime(tLocal);
                var hour = HourFromTime(tLocal);
                var minute = MinFromTime(tLocal);
                var second = SecFromTime(tLocal);
                var millisecond = MsFromTime(tLocal);

                var day = MakeDay(fullYear, month, date);
                var newDate = MakeDate(day, TimeWithinDay(tLocal));
                var utc = UTCTimeFromLocal(newDate, realm);
                var clipped = TimeClip(utc);

                StoreInternalDateValue(obj, clipped);
                return clipped;
            }), 1);

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

        dateConstructor.DefineProperty("name",
            new PropertyDescriptor { Value = "Date", Writable = false, Enumerable = false, Configurable = true });

        dateConstructor.DefineProperty("length",
            new PropertyDescriptor { Value = 7d, Writable = false, Enumerable = false, Configurable = true });

        return dateConstructor;

        static DateTimeOffset GetUtcTimeFromInternalDate(JsObject obj)
        {
            if (obj.TryGetProperty("_internalDate", out var stored) && stored is double storedMs)
            {
                return ConvertMillisecondsToUtc(storedMs);
            }

            return ConvertMillisecondsToUtc(0);
        }

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
