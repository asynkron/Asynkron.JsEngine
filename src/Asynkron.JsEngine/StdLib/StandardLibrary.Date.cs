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

        static DateTimeOffset GetLocalTimeFromInternalDate(JsObject obj)
        {
            var utc = GetUtcTimeFromInternalDate(obj);
            return utc.ToLocalTime();
        }

        static DateTimeOffset GetUtcTimeFromInternalDate(JsObject obj)
        {
            if (obj.TryGetProperty("_internalDate", out var stored) && stored is double storedMs)
            {
                return ConvertMillisecondsToUtc(storedMs);
            }

            return ConvertMillisecondsToUtc(0);
        }

        static void StoreInternalDate(JsObject obj, DateTimeOffset dateTime)
        {
            obj.SetProperty("_internalDate", (double)dateTime.ToUnixTimeMilliseconds());
        }

        static string FormatDateToJsString(DateTimeOffset localTime)
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

            var timeZone = TimeZoneInfo.Local.IsDaylightSavingTime(localTime.DateTime)
                ? TimeZoneInfo.Local.DaylightName
                : TimeZoneInfo.Local.StandardName;

            return $"{weekday} {month} {day} {year} {time} GMT{offset} ({timeZone})";
        }

        static string FormatUtcToJsUtcString(DateTimeOffset utcTime)
        {
            // Match Node/ECMAScript style: "Thu, 01 Jan 1970 00:00:00 GMT"
            var culture = CultureInfo.InvariantCulture;
            return utcTime.UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", culture);
        }

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

            DateTimeOffset dateTime;

            if (args.Count == 0)
            {
                // No arguments: current date/time
                dateTime = DateTimeOffset.UtcNow;
            }
            else if (args.Count == 1)
            {
                // Single argument: milliseconds since epoch or date string
                if (args[0] is double ms)
                {
                    dateTime = ConvertMillisecondsToUtc(ms);
                }
                else if (args[0] is string dateStr && DateTimeOffset.TryParse(dateStr, out var parsed))
                {
                    dateTime = parsed;
                }
                else
                {
                    dateTime = DateTimeOffset.UtcNow;
                }
            }
            else
            {
                // Multiple arguments: year, month, day, hour, minute, second, millisecond
                var year = args[0] is double y ? (int)y : 1970;
                var month = args.Count > 1 && args[1] is double m ? (int)m + 1 : 1; // JS months are 0-indexed
                var day = args.Count > 2 && args[2] is double d ? (int)d : 1;
                var hour = args.Count > 3 && args[3] is double h ? (int)h : 0;
                var minute = args.Count > 4 && args[4] is double min ? (int)min : 0;
                var second = args.Count > 5 && args[5] is double s ? (int)s : 0;
                var millisecond = args.Count > 6 && args[6] is double ms ? (int)ms : 0;

                try
                {
                    var localDate = new DateTime(year, month, day, hour, minute, second, millisecond,
                        DateTimeKind.Utc);
                    dateTime = new DateTimeOffset(localDate);
                }
                catch
                {
                    dateTime = DateTimeOffset.UtcNow;
                }
            }

            // Store the internal date value
            StoreInternalDate(dateInstance, dateTime);

            // Add instance methods
            dateInstance["getTime"] = new HostFunction((thisVal, _) =>
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
                if (thisVal is JsObject obj && methodArgs.Count > 0 && methodArgs[0] is double ms)
                {
                    var utc = ConvertMillisecondsToUtc(ms);
                    StoreInternalDate(obj, utc);
                    return (double)utc.ToUnixTimeMilliseconds();
                }

                return double.NaN;
            });

            dateInstance["getFullYear"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Year;
                }

                return double.NaN;
            });

            dateInstance["getYear"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    var year = dt.Year;
                    return (double)(year >= 1900 ? year - 1900 : year);
                }

                return double.NaN;
            });

            dateInstance["setYear"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is not JsObject obj)
                {
                    return double.NaN;
                }

                var yearArg = methodArgs.Count > 0 ? methodArgs[0] : Symbols.Undefined;
                var y = JsOps.ToNumber(yearArg);
                if (double.IsNaN(y))
                {
                    obj.SetProperty("_internalDate", double.NaN);
                    return double.NaN;
                }

                var t = obj.TryGetProperty("_internalDate", out var storedVal) && storedVal is double storedMs
                    ? storedMs
                    : double.NaN;

                var local = double.IsNaN(t)
                    ? ConvertMillisecondsToUtc(0).ToLocalTime()
                    : ConvertMillisecondsToUtc(t).ToLocalTime();

                var yInt = (int)Math.Truncate(y);
                if (yInt is >= 0 and <= 99)
                {
                    yInt += 1900;
                }

                DateTimeOffset newLocal;
                try
                {
                    newLocal = new DateTimeOffset(
                        yInt,
                        local.Month,
                        local.Day,
                        local.Hour,
                        local.Minute,
                        local.Second,
                        local.Millisecond,
                        local.Offset);
                }
                catch
                {
                    obj.SetProperty("_internalDate", double.NaN);
                    return double.NaN;
                }

                var utc = newLocal.ToUniversalTime();
                StoreInternalDate(obj, utc);
                return (double)utc.ToUnixTimeMilliseconds();
            });

            dateInstance["getMonth"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)(local.Month - 1); // JS months are 0-indexed
                }

                return double.NaN;
            });

            dateInstance["getDate"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Day;
                }

                return double.NaN;
            });

            dateInstance["getDay"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.DayOfWeek;
                }

                return double.NaN;
            });

            dateInstance["getHours"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Hour;
                }

                return double.NaN;
            });

            dateInstance["getMinutes"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Minute;
                }

                return double.NaN;
            });

            dateInstance["getSeconds"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Second;
                }

                return double.NaN;
            });

            dateInstance["getMilliseconds"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Millisecond;
                }

                return double.NaN;
            });

            dateInstance["getTimezoneOffset"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return -local.Offset.TotalMinutes;
                }

                return double.NaN;
            });

            dateInstance["toISOString"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = ConvertMillisecondsToUtc(ms);
                    return dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                }

                return "";
            });

            dateInstance["toString"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return FormatDateToJsString(local);
                }

                return "Invalid Date";
            });

            // UTC-based accessors
            dateInstance["getUTCFullYear"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Year;
                }

                return double.NaN;
            });

            dateInstance["getUTCMonth"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)(utc.Month - 1); // JS months are 0-indexed
                }

                return double.NaN;
            });

            dateInstance["getUTCDate"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Day;
                }

                return double.NaN;
            });

            dateInstance["getUTCDay"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.DayOfWeek;
                }

                return double.NaN;
            });

            dateInstance["getUTCHours"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Hour;
                }

                return double.NaN;
            });

            dateInstance["getUTCMinutes"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Minute;
                }

                return double.NaN;
            });

            dateInstance["getUTCSeconds"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Second;
                }

                return double.NaN;
            });

            dateInstance["getUTCMilliseconds"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Millisecond;
                }

                return double.NaN;
            });

            // Formatting helpers
            dateInstance["toUTCString"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var utc = ConvertMillisecondsToUtc(ms);
                    return FormatUtcToJsUtcString(utc);
                }

                return "Invalid Date";
            });

            dateInstance["toJSON"] = new HostFunction((thisVal, _) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = ConvertMillisecondsToUtc(ms);
                    return dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                }

                return null;
            });

            // valueOf() – mirrors getTime()
            dateInstance["valueOf"] = new HostFunction((thisVal, _) =>
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

        dateConstructor.DefineProperty("name",
            new PropertyDescriptor { Value = "Date", Writable = false, Enumerable = false, Configurable = true });

        dateConstructor.DefineProperty("length",
            new PropertyDescriptor { Value = 7d, Writable = false, Enumerable = false, Configurable = true });

        return dateConstructor;

        static DateTimeOffset ConvertMillisecondsToUtc(double milliseconds)
        {
            // JavaScript stores Date values as milliseconds since Unix epoch in UTC.
            // The input can be fractional, but DateTimeOffset only accepts long, so
            // truncate toward zero like ECMAScript's ToIntegerOrInfinity.
            var truncated = (long)Math.Truncate(milliseconds);
            return DateTimeOffset.FromUnixTimeMilliseconds(truncated);
        }
    }
}
