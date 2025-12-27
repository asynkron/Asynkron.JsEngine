#region

using System.Numerics;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
///     Provides the Temporal API constructors and static methods.
/// </summary>
public static class TemporalHelper
{
    private const string TemporalInstantSlot = "[[TemporalInstant]]";
    private const string TemporalDurationSlot = "[[TemporalDuration]]";
    private const string TemporalPlainDateSlot = "[[TemporalPlainDate]]";
    private const string TemporalPlainTimeSlot = "[[TemporalPlainTime]]";
    private const string TemporalPlainDateTimeSlot = "[[TemporalPlainDateTime]]";

    public static JsObject CreateTemporalObject(RealmState realm)
    {
        var temporal = new JsObject(realm.ObjectPrototype);

        // Set @@toStringTag
        temporal.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal", Writable = false, Enumerable = false, Configurable = true });

        // Temporal.Now namespace
        var now = CreateTemporalNow(realm);
        temporal.DefineProperty("Now",
            new PropertyDescriptor { Value = now, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Instant constructor
        var instantCtor = CreateInstantConstructor(realm);
        temporal.DefineProperty("Instant",
            new PropertyDescriptor { Value = instantCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Duration constructor
        var durationCtor = CreateDurationConstructor(realm);
        temporal.DefineProperty("Duration",
            new PropertyDescriptor { Value = durationCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.PlainDate constructor
        var plainDateCtor = CreatePlainDateConstructor(realm);
        temporal.DefineProperty("PlainDate",
            new PropertyDescriptor { Value = plainDateCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.PlainTime constructor
        var plainTimeCtor = CreatePlainTimeConstructor(realm);
        temporal.DefineProperty("PlainTime",
            new PropertyDescriptor { Value = plainTimeCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.PlainDateTime constructor
        var plainDateTimeCtor = CreatePlainDateTimeConstructor(realm);
        temporal.DefineProperty("PlainDateTime",
            new PropertyDescriptor { Value = plainDateTimeCtor, Writable = true, Enumerable = false, Configurable = true });

        return temporal;
    }

    private static JsObject CreateTemporalNow(RealmState realm)
    {
        var now = new JsObject(realm.ObjectPrototype);

        now.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal.Now", Writable = false, Enumerable = false, Configurable = true });

        // Temporal.Now.instant()
        var instantFn = CreateFunction(realm, "instant", 0, (_, _) =>
        {
            var instant = JsTemporalInstant.Now();
            return WrapInstant(instant, realm);
        });
        now.DefineProperty("instant",
            new PropertyDescriptor { Value = instantFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.timeZoneId()
        var timeZoneIdFn = CreateFunction(realm, "timeZoneId", 0, (_, _) =>
        {
            var tzId = TimeZoneInfo.Local.Id;
            // Convert Windows timezone ID to IANA if needed
            if (OperatingSystem.IsWindows() && TimeZoneInfo.TryConvertWindowsIdToIanaId(tzId, out var ianaId))
            {
                tzId = ianaId;
            }
            return new JsValue(tzId);
        });
        now.DefineProperty("timeZoneId",
            new PropertyDescriptor { Value = timeZoneIdFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.plainDateISO()
        var plainDateISOFn = CreateFunction(realm, "plainDateISO", 0, (_, _) =>
        {
            var date = JsTemporalPlainDate.Today();
            return WrapPlainDate(date, realm);
        });
        now.DefineProperty("plainDateISO",
            new PropertyDescriptor { Value = plainDateISOFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.plainTimeISO()
        var plainTimeISOFn = CreateFunction(realm, "plainTimeISO", 0, (_, _) =>
        {
            var time = JsTemporalPlainTime.Now();
            return WrapPlainTime(time, realm);
        });
        now.DefineProperty("plainTimeISO",
            new PropertyDescriptor { Value = plainTimeISOFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.plainDateTimeISO()
        var plainDateTimeISOFn = CreateFunction(realm, "plainDateTimeISO", 0, (_, _) =>
        {
            var dt = JsTemporalPlainDateTime.Now();
            return WrapPlainDateTime(dt, realm);
        });
        now.DefineProperty("plainDateTimeISO",
            new PropertyDescriptor { Value = plainDateTimeISOFn, Writable = true, Enumerable = false, Configurable = true });

        return now;
    }

    private static HostFunction CreateInstantConstructor(RealmState realm)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototype.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal.Instant", Writable = false, Enumerable = false, Configurable = true });

        // Prototype methods
        AddPrototypeGetter(prototype, realm, "epochMilliseconds", thisValue =>
        {
            var instant = GetInstant(thisValue);
            return new JsValue((double)instant.EpochMilliseconds);
        });

        AddPrototypeGetter(prototype, realm, "epochNanoseconds", thisValue =>
        {
            var instant = GetInstant(thisValue);
            return JsValue.FromObjectUnsafe(new JsBigInt(instant.EpochNanoseconds));
        });

        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, _) =>
        {
            var instant = GetInstant(thisValue);
            return new JsValue(instant.ToString());
        });

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
        {
            var instant = GetInstant(thisValue);
            return new JsValue(instant.ToString());
        });

        AddPrototypeMethod(prototype, realm, "valueOf", 0, (_, _) =>
            throw StandardLibrary.ThrowTypeError("Temporal.Instant.prototype.valueOf does not support implicit conversion", realm: realm));

        AddPrototypeMethod(prototype, realm, "equals", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var otherArg = args.GetArgument(0);
            var other = ToTemporalInstant(otherArg, realm);
            return new JsValue(instant.Equals(other));
        });

        // Constructor
        var ctor = new HostFunction((thisValue, args) =>
        {
            var epochNanoseconds = args.GetArgument(0);

            JsTemporalInstant instant;
            if (epochNanoseconds.TryGetObject<JsBigInt>(out var bigInt))
            {
                instant = new JsTemporalInstant(bigInt.Value);
            }
            else
            {
                var ms = JsOps.ToNumber(epochNanoseconds);
                instant = JsTemporalInstant.FromEpochMilliseconds((long)ms);
            }

            return WrapInstant(instant, realm, prototype);
        }, realm) { IsConstructor = true };

        ctor.DefineProperty("prototype",
            new PropertyDescriptor { Value = prototype, Writable = false, Enumerable = false, Configurable = false });
        prototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = ctor, Writable = true, Enumerable = false, Configurable = true });

        // Static methods
        var fromEpochMilliseconds = CreateFunction(realm, "fromEpochMilliseconds", 1, (_, args) =>
        {
            var ms = JsOps.ToNumber(args.GetArgument(0));
            var instant = JsTemporalInstant.FromEpochMilliseconds((long)ms);
            return WrapInstant(instant, realm, prototype);
        });
        ctor.DefineProperty("fromEpochMilliseconds",
            new PropertyDescriptor { Value = fromEpochMilliseconds, Writable = true, Enumerable = false, Configurable = true });

        var fromEpochNanoseconds = CreateFunction(realm, "fromEpochNanoseconds", 1, (_, args) =>
        {
            var arg = args.GetArgument(0);
            BigInteger nanos;
            if (arg.TryGetObject<JsBigInt>(out var bigInt))
            {
                nanos = bigInt.Value;
            }
            else
            {
                nanos = new BigInteger(JsOps.ToNumber(arg));
            }
            var instant = JsTemporalInstant.FromEpochNanoseconds(nanos);
            return WrapInstant(instant, realm, prototype);
        });
        ctor.DefineProperty("fromEpochNanoseconds",
            new PropertyDescriptor { Value = fromEpochNanoseconds, Writable = true, Enumerable = false, Configurable = true });

        return ctor;
    }

    private static HostFunction CreateDurationConstructor(RealmState realm)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototype.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal.Duration", Writable = false, Enumerable = false, Configurable = true });

        // Prototype getters
        AddPrototypeGetter(prototype, realm, "years", tv => new JsValue(GetDuration(tv).Years));
        AddPrototypeGetter(prototype, realm, "months", tv => new JsValue(GetDuration(tv).Months));
        AddPrototypeGetter(prototype, realm, "weeks", tv => new JsValue(GetDuration(tv).Weeks));
        AddPrototypeGetter(prototype, realm, "days", tv => new JsValue(GetDuration(tv).Days));
        AddPrototypeGetter(prototype, realm, "hours", tv => new JsValue(GetDuration(tv).Hours));
        AddPrototypeGetter(prototype, realm, "minutes", tv => new JsValue(GetDuration(tv).Minutes));
        AddPrototypeGetter(prototype, realm, "seconds", tv => new JsValue(GetDuration(tv).Seconds));
        AddPrototypeGetter(prototype, realm, "milliseconds", tv => new JsValue(GetDuration(tv).Milliseconds));
        AddPrototypeGetter(prototype, realm, "microseconds", tv => new JsValue(GetDuration(tv).Microseconds));
        AddPrototypeGetter(prototype, realm, "nanoseconds", tv => new JsValue(GetDuration(tv).Nanoseconds));
        AddPrototypeGetter(prototype, realm, "sign", tv => new JsValue(GetDuration(tv).Sign));

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, _) =>
        {
            var duration = GetDuration(thisValue);
            return new JsValue(duration.ToString());
        });

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
        {
            var duration = GetDuration(thisValue);
            return new JsValue(duration.ToString());
        });

        AddPrototypeMethod(prototype, realm, "valueOf", 0, (_, _) =>
            throw StandardLibrary.ThrowTypeError("Temporal.Duration.prototype.valueOf does not support implicit conversion", realm: realm));

        AddPrototypeMethod(prototype, realm, "negated", 0, (thisValue, _) =>
        {
            var duration = GetDuration(thisValue);
            return WrapDuration(duration.Negated(), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "abs", 0, (thisValue, _) =>
        {
            var duration = GetDuration(thisValue);
            return WrapDuration(duration.Abs(), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "add", 1, (thisValue, args) =>
        {
            var duration = GetDuration(thisValue);
            var other = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapDuration(duration.Add(other), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var duration = GetDuration(thisValue);
            var other = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapDuration(duration.Subtract(other), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "total", 1, (thisValue, args) =>
        {
            var duration = GetDuration(thisValue);
            var unitArg = args.GetArgument(0);
            var unit = JsOps.ToJsString(unitArg);
            return new JsValue(duration.Total(unit));
        });

        // Constructor
        var ctor = new HostFunction((thisValue, args) =>
        {
            var duration = new JsTemporalDuration(
                GetNumberArg(args, 0),  // years
                GetNumberArg(args, 1),  // months
                GetNumberArg(args, 2),  // weeks
                GetNumberArg(args, 3),  // days
                GetNumberArg(args, 4),  // hours
                GetNumberArg(args, 5),  // minutes
                GetNumberArg(args, 6),  // seconds
                GetNumberArg(args, 7),  // milliseconds
                GetNumberArg(args, 8),  // microseconds
                GetNumberArg(args, 9)); // nanoseconds

            return WrapDuration(duration, realm, prototype);
        }, realm) { IsConstructor = true };

        ctor.DefineProperty("prototype",
            new PropertyDescriptor { Value = prototype, Writable = false, Enumerable = false, Configurable = false });
        prototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = ctor, Writable = true, Enumerable = false, Configurable = true });

        // Static methods
        var from = CreateFunction(realm, "from", 1, (_, args) =>
        {
            var arg = args.GetArgument(0);
            var duration = ToTemporalDuration(arg, realm);
            return WrapDuration(duration, realm, prototype);
        });
        ctor.DefineProperty("from",
            new PropertyDescriptor { Value = from, Writable = true, Enumerable = false, Configurable = true });

        var compare = CreateFunction(realm, "compare", 2, (_, args) =>
        {
            var d1 = ToTemporalDuration(args.GetArgument(0), realm);
            var d2 = ToTemporalDuration(args.GetArgument(1), realm);
            // Simple comparison by total nanoseconds (ignores relativeTo)
            var total1 = d1.Total("nanoseconds");
            var total2 = d2.Total("nanoseconds");
            return new JsValue(Math.Sign(total1 - total2));
        });
        ctor.DefineProperty("compare",
            new PropertyDescriptor { Value = compare, Writable = true, Enumerable = false, Configurable = true });

        return ctor;
    }

    private static HostFunction CreatePlainDateConstructor(RealmState realm)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototype.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal.PlainDate", Writable = false, Enumerable = false, Configurable = true });

        // Prototype getters
        AddPrototypeGetter(prototype, realm, "year", tv => new JsValue(GetPlainDate(tv).Year));
        AddPrototypeGetter(prototype, realm, "month", tv => new JsValue(GetPlainDate(tv).Month));
        AddPrototypeGetter(prototype, realm, "day", tv => new JsValue(GetPlainDate(tv).Day));
        AddPrototypeGetter(prototype, realm, "monthCode", tv => new JsValue(GetPlainDate(tv).MonthCode));
        AddPrototypeGetter(prototype, realm, "dayOfWeek", tv => new JsValue(GetPlainDate(tv).DayOfWeek));
        AddPrototypeGetter(prototype, realm, "dayOfYear", tv => new JsValue(GetPlainDate(tv).DayOfYear));
        AddPrototypeGetter(prototype, realm, "weekOfYear", tv => new JsValue(GetPlainDate(tv).WeekOfYear));
        AddPrototypeGetter(prototype, realm, "daysInMonth", tv => new JsValue(GetPlainDate(tv).DaysInMonth));
        AddPrototypeGetter(prototype, realm, "daysInYear", tv => new JsValue(GetPlainDate(tv).DaysInYear));
        AddPrototypeGetter(prototype, realm, "monthsInYear", tv => new JsValue(GetPlainDate(tv).MonthsInYear));
        AddPrototypeGetter(prototype, realm, "inLeapYear", tv => new JsValue(GetPlainDate(tv).InLeapYear));
        AddPrototypeGetter(prototype, realm, "calendarId", tv => new JsValue(GetPlainDate(tv).Calendar));

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, _) =>
            new JsValue(GetPlainDate(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
            new JsValue(GetPlainDate(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "valueOf", 0, (_, _) =>
            throw StandardLibrary.ThrowTypeError("Temporal.PlainDate.prototype.valueOf does not support implicit conversion", realm: realm));

        AddPrototypeMethod(prototype, realm, "equals", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var other = ToTemporalPlainDate(args.GetArgument(0), realm);
            return new JsValue(date.Equals(other));
        });

        AddPrototypeMethod(prototype, realm, "add", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapPlainDate(date.Add(duration), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapPlainDate(date.Subtract(duration), realm, prototype);
        });

        // Constructor
        var ctor = new HostFunction((thisValue, args) =>
        {
            var year = (int)JsOps.ToNumber(args.GetArgument(0));
            var month = (int)JsOps.ToNumber(args.GetArgument(1));
            var day = (int)JsOps.ToNumber(args.GetArgument(2));
            var calendar = args.Count > 3 && !args[3].IsUndefined ? JsOps.ToJsString(args[3]) : "iso8601";

            var date = new JsTemporalPlainDate(year, month, day, calendar);
            return WrapPlainDate(date, realm, prototype);
        }, realm) { IsConstructor = true };

        ctor.DefineProperty("prototype",
            new PropertyDescriptor { Value = prototype, Writable = false, Enumerable = false, Configurable = false });
        prototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = ctor, Writable = true, Enumerable = false, Configurable = true });

        // Static methods
        var from = CreateFunction(realm, "from", 1, (_, args) =>
        {
            var date = ToTemporalPlainDate(args.GetArgument(0), realm);
            return WrapPlainDate(date, realm, prototype);
        });
        ctor.DefineProperty("from",
            new PropertyDescriptor { Value = from, Writable = true, Enumerable = false, Configurable = true });

        var compare = CreateFunction(realm, "compare", 2, (_, args) =>
        {
            var d1 = ToTemporalPlainDate(args.GetArgument(0), realm);
            var d2 = ToTemporalPlainDate(args.GetArgument(1), realm);
            return new JsValue(d1.CompareTo(d2));
        });
        ctor.DefineProperty("compare",
            new PropertyDescriptor { Value = compare, Writable = true, Enumerable = false, Configurable = true });

        return ctor;
    }

    private static HostFunction CreatePlainTimeConstructor(RealmState realm)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototype.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal.PlainTime", Writable = false, Enumerable = false, Configurable = true });

        // Prototype getters
        AddPrototypeGetter(prototype, realm, "hour", tv => new JsValue(GetPlainTime(tv).Hour));
        AddPrototypeGetter(prototype, realm, "minute", tv => new JsValue(GetPlainTime(tv).Minute));
        AddPrototypeGetter(prototype, realm, "second", tv => new JsValue(GetPlainTime(tv).Second));
        AddPrototypeGetter(prototype, realm, "millisecond", tv => new JsValue(GetPlainTime(tv).Millisecond));
        AddPrototypeGetter(prototype, realm, "microsecond", tv => new JsValue(GetPlainTime(tv).Microsecond));
        AddPrototypeGetter(prototype, realm, "nanosecond", tv => new JsValue(GetPlainTime(tv).Nanosecond));

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, _) =>
            new JsValue(GetPlainTime(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
            new JsValue(GetPlainTime(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "valueOf", 0, (_, _) =>
            throw StandardLibrary.ThrowTypeError("Temporal.PlainTime.prototype.valueOf does not support implicit conversion", realm: realm));

        AddPrototypeMethod(prototype, realm, "equals", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var other = ToTemporalPlainTime(args.GetArgument(0), realm);
            return new JsValue(time.Equals(other));
        });

        AddPrototypeMethod(prototype, realm, "add", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapPlainTime(time.Add(duration), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapPlainTime(time.Subtract(duration), realm, prototype);
        });

        // Constructor
        var ctor = new HostFunction((thisValue, args) =>
        {
            var hour = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
            var minute = args.Count > 1 ? (int)JsOps.ToNumber(args[1]) : 0;
            var second = args.Count > 2 ? (int)JsOps.ToNumber(args[2]) : 0;
            var millisecond = args.Count > 3 ? (int)JsOps.ToNumber(args[3]) : 0;
            var microsecond = args.Count > 4 ? (int)JsOps.ToNumber(args[4]) : 0;
            var nanosecond = args.Count > 5 ? (int)JsOps.ToNumber(args[5]) : 0;

            var time = new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond);
            return WrapPlainTime(time, realm, prototype);
        }, realm) { IsConstructor = true };

        ctor.DefineProperty("prototype",
            new PropertyDescriptor { Value = prototype, Writable = false, Enumerable = false, Configurable = false });
        prototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = ctor, Writable = true, Enumerable = false, Configurable = true });

        // Static methods
        var from = CreateFunction(realm, "from", 1, (_, args) =>
        {
            var time = ToTemporalPlainTime(args.GetArgument(0), realm);
            return WrapPlainTime(time, realm, prototype);
        });
        ctor.DefineProperty("from",
            new PropertyDescriptor { Value = from, Writable = true, Enumerable = false, Configurable = true });

        var compare = CreateFunction(realm, "compare", 2, (_, args) =>
        {
            var t1 = ToTemporalPlainTime(args.GetArgument(0), realm);
            var t2 = ToTemporalPlainTime(args.GetArgument(1), realm);
            return new JsValue(t1.CompareTo(t2));
        });
        ctor.DefineProperty("compare",
            new PropertyDescriptor { Value = compare, Writable = true, Enumerable = false, Configurable = true });

        return ctor;
    }

    private static HostFunction CreatePlainDateTimeConstructor(RealmState realm)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototype.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal.PlainDateTime", Writable = false, Enumerable = false, Configurable = true });

        // Prototype getters
        AddPrototypeGetter(prototype, realm, "year", tv => new JsValue(GetPlainDateTime(tv).Year));
        AddPrototypeGetter(prototype, realm, "month", tv => new JsValue(GetPlainDateTime(tv).Month));
        AddPrototypeGetter(prototype, realm, "day", tv => new JsValue(GetPlainDateTime(tv).Day));
        AddPrototypeGetter(prototype, realm, "hour", tv => new JsValue(GetPlainDateTime(tv).Hour));
        AddPrototypeGetter(prototype, realm, "minute", tv => new JsValue(GetPlainDateTime(tv).Minute));
        AddPrototypeGetter(prototype, realm, "second", tv => new JsValue(GetPlainDateTime(tv).Second));
        AddPrototypeGetter(prototype, realm, "millisecond", tv => new JsValue(GetPlainDateTime(tv).Millisecond));
        AddPrototypeGetter(prototype, realm, "microsecond", tv => new JsValue(GetPlainDateTime(tv).Microsecond));
        AddPrototypeGetter(prototype, realm, "nanosecond", tv => new JsValue(GetPlainDateTime(tv).Nanosecond));
        AddPrototypeGetter(prototype, realm, "monthCode", tv => new JsValue(GetPlainDateTime(tv).MonthCode));
        AddPrototypeGetter(prototype, realm, "dayOfWeek", tv => new JsValue(GetPlainDateTime(tv).DayOfWeek));
        AddPrototypeGetter(prototype, realm, "dayOfYear", tv => new JsValue(GetPlainDateTime(tv).DayOfYear));
        AddPrototypeGetter(prototype, realm, "weekOfYear", tv => new JsValue(GetPlainDateTime(tv).WeekOfYear));
        AddPrototypeGetter(prototype, realm, "daysInMonth", tv => new JsValue(GetPlainDateTime(tv).DaysInMonth));
        AddPrototypeGetter(prototype, realm, "daysInYear", tv => new JsValue(GetPlainDateTime(tv).DaysInYear));
        AddPrototypeGetter(prototype, realm, "monthsInYear", tv => new JsValue(GetPlainDateTime(tv).MonthsInYear));
        AddPrototypeGetter(prototype, realm, "inLeapYear", tv => new JsValue(GetPlainDateTime(tv).InLeapYear));
        AddPrototypeGetter(prototype, realm, "calendarId", tv => new JsValue(GetPlainDateTime(tv).Calendar));

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, _) =>
            new JsValue(GetPlainDateTime(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
            new JsValue(GetPlainDateTime(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "valueOf", 0, (_, _) =>
            throw StandardLibrary.ThrowTypeError("Temporal.PlainDateTime.prototype.valueOf does not support implicit conversion", realm: realm));

        AddPrototypeMethod(prototype, realm, "equals", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var other = ToTemporalPlainDateTime(args.GetArgument(0), realm);
            return new JsValue(dt.Equals(other));
        });

        AddPrototypeMethod(prototype, realm, "toPlainDate", 0, (thisValue, _) =>
        {
            var dt = GetPlainDateTime(thisValue);
            return WrapPlainDate(dt.ToPlainDate(), realm);
        });

        AddPrototypeMethod(prototype, realm, "toPlainTime", 0, (thisValue, _) =>
        {
            var dt = GetPlainDateTime(thisValue);
            return WrapPlainTime(dt.ToPlainTime(), realm);
        });

        // Constructor
        var ctor = new HostFunction((thisValue, args) =>
        {
            var year = (int)JsOps.ToNumber(args.GetArgument(0));
            var month = (int)JsOps.ToNumber(args.GetArgument(1));
            var day = (int)JsOps.ToNumber(args.GetArgument(2));
            var hour = args.Count > 3 ? (int)JsOps.ToNumber(args[3]) : 0;
            var minute = args.Count > 4 ? (int)JsOps.ToNumber(args[4]) : 0;
            var second = args.Count > 5 ? (int)JsOps.ToNumber(args[5]) : 0;
            var millisecond = args.Count > 6 ? (int)JsOps.ToNumber(args[6]) : 0;
            var microsecond = args.Count > 7 ? (int)JsOps.ToNumber(args[7]) : 0;
            var nanosecond = args.Count > 8 ? (int)JsOps.ToNumber(args[8]) : 0;
            var calendar = args.Count > 9 && !args[9].IsUndefined ? JsOps.ToJsString(args[9]) : "iso8601";

            var dt = new JsTemporalPlainDateTime(year, month, day, hour, minute, second,
                millisecond, microsecond, nanosecond, calendar);
            return WrapPlainDateTime(dt, realm, prototype);
        }, realm) { IsConstructor = true };

        ctor.DefineProperty("prototype",
            new PropertyDescriptor { Value = prototype, Writable = false, Enumerable = false, Configurable = false });
        prototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = ctor, Writable = true, Enumerable = false, Configurable = true });

        // Static methods
        var from = CreateFunction(realm, "from", 1, (_, args) =>
        {
            var dt = ToTemporalPlainDateTime(args.GetArgument(0), realm);
            return WrapPlainDateTime(dt, realm, prototype);
        });
        ctor.DefineProperty("from",
            new PropertyDescriptor { Value = from, Writable = true, Enumerable = false, Configurable = true });

        var compare = CreateFunction(realm, "compare", 2, (_, args) =>
        {
            var dt1 = ToTemporalPlainDateTime(args.GetArgument(0), realm);
            var dt2 = ToTemporalPlainDateTime(args.GetArgument(1), realm);
            return new JsValue(dt1.CompareTo(dt2));
        });
        ctor.DefineProperty("compare",
            new PropertyDescriptor { Value = compare, Writable = true, Enumerable = false, Configurable = true });

        return ctor;
    }

    #region Helper methods

    private static HostFunction CreateFunction(RealmState realm, string name, int length,
        Func<JsValue, IReadOnlyList<JsValue>, JsValue> fn)
    {
        var hostFn = new HostFunction((thisValue, args) => fn(thisValue, args), realm, false);
        hostFn.DefineProperty("length",
            new PropertyDescriptor { Value = (double)length, Writable = false, Enumerable = false, Configurable = true });
        hostFn.DefineProperty("name",
            new PropertyDescriptor { Value = name, Writable = false, Enumerable = false, Configurable = true });
        hostFn.Delete("prototype");
        return hostFn;
    }

    private static void AddPrototypeMethod(JsObject prototype, RealmState realm, string name, int length,
        Func<JsValue, IReadOnlyList<JsValue>, JsValue> fn)
    {
        var method = CreateFunction(realm, name, length, fn);
        prototype.DefineProperty(name,
            new PropertyDescriptor { Value = method, Writable = true, Enumerable = false, Configurable = true });
    }

    private static void AddPrototypeGetter(JsObject prototype, RealmState realm, string name,
        Func<JsValue, JsValue> getter)
    {
        var getterFn = new HostFunction((thisValue, _) => getter(thisValue), realm, false);
        getterFn.DefineProperty("length",
            new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
        getterFn.DefineProperty("name",
            new PropertyDescriptor { Value = $"get {name}", Writable = false, Enumerable = false, Configurable = true });
        getterFn.Delete("prototype");

        prototype.DefineProperty(name,
            new PropertyDescriptor { Get = getterFn, Enumerable = false, Configurable = true });
    }

    private static double GetNumberArg(IReadOnlyList<JsValue> args, int index)
    {
        if (index >= args.Count || args[index].IsUndefined)
            return 0;
        return JsOps.ToNumber(args[index]);
    }

    #endregion

    #region Wrapper methods

    private static JsValue WrapInstant(JsTemporalInstant instant, RealmState realm, JsObject? prototype = null)
    {
        var obj = new JsObject(prototype ?? realm.ObjectPrototype);
        obj.SetProperty(TemporalInstantSlot, JsValue.FromObjectUnsafe(instant));
        return JsValue.FromObjectUnsafe(obj);
    }

    private static JsValue WrapDuration(JsTemporalDuration duration, RealmState realm, JsObject? prototype = null)
    {
        var obj = new JsObject(prototype ?? realm.ObjectPrototype);
        obj.SetProperty(TemporalDurationSlot, JsValue.FromObjectUnsafe(duration));
        return JsValue.FromObjectUnsafe(obj);
    }

    private static JsValue WrapPlainDate(JsTemporalPlainDate date, RealmState realm, JsObject? prototype = null)
    {
        var obj = new JsObject(prototype ?? realm.ObjectPrototype);
        obj.SetProperty(TemporalPlainDateSlot, JsValue.FromObjectUnsafe(date));
        return JsValue.FromObjectUnsafe(obj);
    }

    private static JsValue WrapPlainTime(JsTemporalPlainTime time, RealmState realm, JsObject? prototype = null)
    {
        var obj = new JsObject(prototype ?? realm.ObjectPrototype);
        obj.SetProperty(TemporalPlainTimeSlot, JsValue.FromObjectUnsafe(time));
        return JsValue.FromObjectUnsafe(obj);
    }

    private static JsValue WrapPlainDateTime(JsTemporalPlainDateTime dateTime, RealmState realm, JsObject? prototype = null)
    {
        var obj = new JsObject(prototype ?? realm.ObjectPrototype);
        obj.SetProperty(TemporalPlainDateTimeSlot, JsValue.FromObjectUnsafe(dateTime));
        return JsValue.FromObjectUnsafe(obj);
    }

    #endregion

    #region Unwrapper methods

    private static JsTemporalInstant GetInstant(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalInstantSlot, out var slot) &&
            slot.TryGetObject<JsTemporalInstant>(out var instant))
        {
            return instant;
        }
        throw new InvalidOperationException("Value is not a Temporal.Instant");
    }

    private static JsTemporalDuration GetDuration(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalDurationSlot, out var slot) &&
            slot.TryGetObject<JsTemporalDuration>(out var duration))
        {
            return duration;
        }
        throw new InvalidOperationException("Value is not a Temporal.Duration");
    }

    private static JsTemporalPlainDate GetPlainDate(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainDateSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainDate>(out var date))
        {
            return date;
        }
        throw new InvalidOperationException("Value is not a Temporal.PlainDate");
    }

    private static JsTemporalPlainTime GetPlainTime(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainTimeSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainTime>(out var time))
        {
            return time;
        }
        throw new InvalidOperationException("Value is not a Temporal.PlainTime");
    }

    private static JsTemporalPlainDateTime GetPlainDateTime(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainDateTimeSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainDateTime>(out var dateTime))
        {
            return dateTime;
        }
        throw new InvalidOperationException("Value is not a Temporal.PlainDateTime");
    }

    #endregion

    #region Conversion methods

    private static JsTemporalInstant ToTemporalInstant(JsValue value, RealmState realm)
    {
        // If it's already a Temporal.Instant
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalInstantSlot, out var slot) &&
            slot.TryGetObject<JsTemporalInstant>(out var instant))
        {
            return instant;
        }

        // Try to parse as string
        if (value.IsString)
        {
            var str = value.AsString() ?? "";
            // Simple ISO 8601 parsing
            if (DateTimeOffset.TryParse(str, out var dto))
            {
                return new JsTemporalInstant(dto);
            }
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.Instant", realm: realm);
    }

    private static JsTemporalDuration ToTemporalDuration(JsValue value, RealmState realm)
    {
        // If it's already a Temporal.Duration
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalDurationSlot, out var slot) &&
            slot.TryGetObject<JsTemporalDuration>(out var duration))
        {
            return duration;
        }

        // If it's an object with duration properties
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return new JsTemporalDuration(
                GetPropertyAsNumber(accessor, "years"),
                GetPropertyAsNumber(accessor, "months"),
                GetPropertyAsNumber(accessor, "weeks"),
                GetPropertyAsNumber(accessor, "days"),
                GetPropertyAsNumber(accessor, "hours"),
                GetPropertyAsNumber(accessor, "minutes"),
                GetPropertyAsNumber(accessor, "seconds"),
                GetPropertyAsNumber(accessor, "milliseconds"),
                GetPropertyAsNumber(accessor, "microseconds"),
                GetPropertyAsNumber(accessor, "nanoseconds"));
        }

        // Try to parse as ISO 8601 duration string
        if (value.IsString)
        {
            var str = value.AsString() ?? "";
            return ParseIsoDuration(str, realm);
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.Duration", realm: realm);
    }

    private static JsTemporalPlainDate ToTemporalPlainDate(JsValue value, RealmState realm)
    {
        // If it's already a Temporal.PlainDate
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainDateSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainDate>(out var date))
        {
            return date;
        }

        // Try to parse as string
        if (value.IsString)
        {
            return JsTemporalPlainDate.From(value.AsString() ?? "");
        }

        // If it's an object with date properties
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            var year = (int)GetPropertyAsNumber(accessor, "year");
            var month = (int)GetPropertyAsNumber(accessor, "month");
            var day = (int)GetPropertyAsNumber(accessor, "day");
            return new JsTemporalPlainDate(year, month, day);
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDate", realm: realm);
    }

    private static JsTemporalPlainTime ToTemporalPlainTime(JsValue value, RealmState realm)
    {
        // If it's already a Temporal.PlainTime
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainTimeSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainTime>(out var time))
        {
            return time;
        }

        // Try to parse as string
        if (value.IsString)
        {
            return JsTemporalPlainTime.From(value.AsString() ?? "");
        }

        // If it's an object with time properties
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            var hour = (int)GetPropertyAsNumber(accessor, "hour");
            var minute = (int)GetPropertyAsNumber(accessor, "minute");
            var second = (int)GetPropertyAsNumber(accessor, "second");
            var millisecond = (int)GetPropertyAsNumber(accessor, "millisecond");
            var microsecond = (int)GetPropertyAsNumber(accessor, "microsecond");
            var nanosecond = (int)GetPropertyAsNumber(accessor, "nanosecond");
            return new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond);
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainTime", realm: realm);
    }

    private static JsTemporalPlainDateTime ToTemporalPlainDateTime(JsValue value, RealmState realm)
    {
        // If it's already a Temporal.PlainDateTime
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainDateTimeSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainDateTime>(out var dateTime))
        {
            return dateTime;
        }

        // Try to parse as string
        if (value.IsString)
        {
            return JsTemporalPlainDateTime.From(value.AsString() ?? "");
        }

        // If it's an object with datetime properties
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            var year = (int)GetPropertyAsNumber(accessor, "year");
            var month = (int)GetPropertyAsNumber(accessor, "month");
            var day = (int)GetPropertyAsNumber(accessor, "day");
            var hour = (int)GetPropertyAsNumber(accessor, "hour");
            var minute = (int)GetPropertyAsNumber(accessor, "minute");
            var second = (int)GetPropertyAsNumber(accessor, "second");
            var millisecond = (int)GetPropertyAsNumber(accessor, "millisecond");
            var microsecond = (int)GetPropertyAsNumber(accessor, "microsecond");
            var nanosecond = (int)GetPropertyAsNumber(accessor, "nanosecond");
            return new JsTemporalPlainDateTime(year, month, day, hour, minute, second,
                millisecond, microsecond, nanosecond);
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDateTime", realm: realm);
    }

    private static double GetPropertyAsNumber(IJsPropertyAccessor accessor, string name)
    {
        if (accessor.TryGetProperty(name, out var value) && !value.IsUndefined)
        {
            return JsOps.ToNumber(value);
        }
        return 0;
    }

    private static JsTemporalDuration ParseIsoDuration(string str, RealmState realm)
    {
        // Very basic ISO 8601 duration parsing (P1Y2M3DT4H5M6S format)
        // Full implementation would be more comprehensive
        if (!str.StartsWith('P'))
        {
            throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);
        }

        double years = 0, months = 0, weeks = 0, days = 0;
        double hours = 0, minutes = 0, seconds = 0;

        var isTimePart = false;
        var currentNumber = "";

        for (var i = 1; i < str.Length; i++)
        {
            var c = str[i];
            if (char.IsDigit(c) || c == '.')
            {
                currentNumber += c;
            }
            else if (c == 'T')
            {
                isTimePart = true;
            }
            else if (currentNumber.Length > 0)
            {
                var value = double.Parse(currentNumber, System.Globalization.CultureInfo.InvariantCulture);
                currentNumber = "";

                if (isTimePart)
                {
                    switch (c)
                    {
                        case 'H': hours = value; break;
                        case 'M': minutes = value; break;
                        case 'S': seconds = value; break;
                    }
                }
                else
                {
                    switch (c)
                    {
                        case 'Y': years = value; break;
                        case 'M': months = value; break;
                        case 'W': weeks = value; break;
                        case 'D': days = value; break;
                    }
                }
            }
        }

        return new JsTemporalDuration(years, months, weeks, days, hours, minutes, seconds);
    }

    #endregion
}
