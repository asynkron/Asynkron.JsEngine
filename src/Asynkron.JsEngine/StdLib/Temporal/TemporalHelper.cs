#region

using System.Numerics;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
///     Holds cached prototypes for Temporal types to enable cross-type method calls.
/// </summary>
internal sealed class TemporalPrototypes
{
    public JsObject InstantPrototype { get; set; } = null!;
    public JsObject DurationPrototype { get; set; } = null!;
    public JsObject PlainDatePrototype { get; set; } = null!;
    public JsObject PlainTimePrototype { get; set; } = null!;
    public JsObject PlainDateTimePrototype { get; set; } = null!;
    public JsObject ZonedDateTimePrototype { get; set; } = null!;
    public JsObject PlainYearMonthPrototype { get; set; } = null!;
    public JsObject PlainMonthDayPrototype { get; set; } = null!;
}

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
    private const string TemporalZonedDateTimeSlot = "[[TemporalZonedDateTime]]";
    private const string TemporalPlainYearMonthSlot = "[[TemporalPlainYearMonth]]";
    private const string TemporalPlainMonthDaySlot = "[[TemporalPlainMonthDay]]";

    // Cached prototypes per realm - stored via WeakReference to avoid memory leaks
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RealmState, TemporalPrototypes>
        _prototypeCache = new();

    public static JsObject CreateTemporalObject(RealmState realm)
    {
        var temporal = new JsObject(realm.ObjectPrototype);

        // Create and cache prototypes for this realm
        var prototypes = new TemporalPrototypes();
        _prototypeCache.Add(realm, prototypes);

        // Set @@toStringTag
        temporal.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal", Writable = false, Enumerable = false, Configurable = true });

        // Temporal.Now namespace
        var now = CreateTemporalNow(realm, prototypes);
        temporal.DefineProperty("Now",
            new PropertyDescriptor { Value = now, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Instant constructor
        var instantCtor = CreateInstantConstructor(realm, prototypes);
        temporal.DefineProperty("Instant",
            new PropertyDescriptor { Value = instantCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Duration constructor
        var durationCtor = CreateDurationConstructor(realm, prototypes);
        temporal.DefineProperty("Duration",
            new PropertyDescriptor { Value = durationCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.PlainDate constructor
        var plainDateCtor = CreatePlainDateConstructor(realm, prototypes);
        temporal.DefineProperty("PlainDate",
            new PropertyDescriptor { Value = plainDateCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.PlainTime constructor
        var plainTimeCtor = CreatePlainTimeConstructor(realm, prototypes);
        temporal.DefineProperty("PlainTime",
            new PropertyDescriptor { Value = plainTimeCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.PlainDateTime constructor
        var plainDateTimeCtor = CreatePlainDateTimeConstructor(realm, prototypes);
        temporal.DefineProperty("PlainDateTime",
            new PropertyDescriptor { Value = plainDateTimeCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.ZonedDateTime constructor
        var zonedDateTimeCtor = CreateZonedDateTimeConstructor(realm, prototypes);
        temporal.DefineProperty("ZonedDateTime",
            new PropertyDescriptor { Value = zonedDateTimeCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.PlainYearMonth constructor
        var plainYearMonthCtor = CreatePlainYearMonthConstructor(realm, prototypes);
        temporal.DefineProperty("PlainYearMonth",
            new PropertyDescriptor { Value = plainYearMonthCtor, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.PlainMonthDay constructor
        var plainMonthDayCtor = CreatePlainMonthDayConstructor(realm, prototypes);
        temporal.DefineProperty("PlainMonthDay",
            new PropertyDescriptor { Value = plainMonthDayCtor, Writable = true, Enumerable = false, Configurable = true });

        return temporal;
    }

    private static TemporalPrototypes GetPrototypes(RealmState realm)
    {
        if (_prototypeCache.TryGetValue(realm, out var prototypes))
        {
            return prototypes;
        }
        throw new InvalidOperationException("Temporal prototypes not initialized for this realm");
    }

    private static JsObject CreateTemporalNow(RealmState realm, TemporalPrototypes prototypes)
    {
        var now = new JsObject(realm.ObjectPrototype);

        now.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal.Now", Writable = false, Enumerable = false, Configurable = true });

        // Temporal.Now.instant()
        var instantFn = CreateFunction(realm, "instant", 0, (_, _) =>
        {
            var instant = JsTemporalInstant.Now();
            return WrapInstant(instant, realm, prototypes.InstantPrototype);
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
            return WrapPlainDate(date, realm, prototypes.PlainDatePrototype);
        });
        now.DefineProperty("plainDateISO",
            new PropertyDescriptor { Value = plainDateISOFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.plainTimeISO()
        var plainTimeISOFn = CreateFunction(realm, "plainTimeISO", 0, (_, _) =>
        {
            var time = JsTemporalPlainTime.Now();
            return WrapPlainTime(time, realm, prototypes.PlainTimePrototype);
        });
        now.DefineProperty("plainTimeISO",
            new PropertyDescriptor { Value = plainTimeISOFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.plainDateTimeISO()
        var plainDateTimeISOFn = CreateFunction(realm, "plainDateTimeISO", 0, (_, _) =>
        {
            var dt = JsTemporalPlainDateTime.Now();
            return WrapPlainDateTime(dt, realm, prototypes.PlainDateTimePrototype);
        });
        now.DefineProperty("plainDateTimeISO",
            new PropertyDescriptor { Value = plainDateTimeISOFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.zonedDateTimeISO()
        var zonedDateTimeISOFn = CreateFunction(realm, "zonedDateTimeISO", 0, (_, args) =>
        {
            var tzId = TimeZoneInfo.Local.Id;
            // If timezone argument provided, use it
            if (args.Count > 0 && !args[0].IsUndefined)
            {
                tzId = JsOps.ToJsString(args[0]);
            }
            // Convert Windows timezone ID to IANA if needed
            if (OperatingSystem.IsWindows() && TimeZoneInfo.TryConvertWindowsIdToIanaId(tzId, out var ianaId))
            {
                tzId = ianaId;
            }
            var zdt = JsTemporalZonedDateTime.Now(tzId);
            return WrapZonedDateTime(zdt, realm, prototypes.ZonedDateTimePrototype);
        });
        now.DefineProperty("zonedDateTimeISO",
            new PropertyDescriptor { Value = zonedDateTimeISOFn, Writable = true, Enumerable = false, Configurable = true });

        return now;
    }

    private static HostFunction CreateInstantConstructor(RealmState realm, TemporalPrototypes prototypes)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototypes.InstantPrototype = prototype;
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

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, _) =>
        {
            // Basic implementation - returns ISO 8601 instant string
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

        AddPrototypeMethod(prototype, realm, "add", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            // Instant only supports time units (hours, minutes, seconds, ms, us, ns)
            var totalNanos = (long)(duration.Hours * 3600_000_000_000L +
                                    duration.Minutes * 60_000_000_000L +
                                    duration.Seconds * 1_000_000_000L +
                                    duration.Milliseconds * 1_000_000L +
                                    duration.Microseconds * 1_000L +
                                    duration.Nanoseconds);
            var newInstant = new JsTemporalInstant(instant.EpochNanoseconds + totalNanos);
            return WrapInstant(newInstant, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            var totalNanos = (long)(duration.Hours * 3600_000_000_000L +
                                    duration.Minutes * 60_000_000_000L +
                                    duration.Seconds * 1_000_000_000L +
                                    duration.Milliseconds * 1_000_000L +
                                    duration.Microseconds * 1_000L +
                                    duration.Nanoseconds);
            var newInstant = new JsTemporalInstant(instant.EpochNanoseconds - totalNanos);
            return WrapInstant(newInstant, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var other = ToTemporalInstant(args.GetArgument(0), realm);
            var diffNanos = other.EpochNanoseconds - instant.EpochNanoseconds;
            var duration = JsTemporalDuration.FromNanoseconds((double)diffNanos);
            return WrapDuration(duration, realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var other = ToTemporalInstant(args.GetArgument(0), realm);
            var diffNanos = instant.EpochNanoseconds - other.EpochNanoseconds;
            var duration = JsTemporalDuration.FromNanoseconds((double)diffNanos);
            return WrapDuration(duration, realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "round", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var optionsArg = args.GetArgument(0);
            string smallestUnit;
            if (optionsArg.IsString)
            {
                smallestUnit = optionsArg.AsString() ?? "nanosecond";
            }
            else if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                     accessor.TryGetProperty("smallestUnit", out var unitValue))
            {
                smallestUnit = JsOps.ToJsString(unitValue);
            }
            else
            {
                smallestUnit = "nanosecond";
            }

            var nanos = instant.EpochNanoseconds;
            long divisor = smallestUnit switch
            {
                "hour" => 3600_000_000_000L,
                "minute" => 60_000_000_000L,
                "second" => 1_000_000_000L,
                "millisecond" => 1_000_000L,
                "microsecond" => 1_000L,
                _ => 1L
            };
            var rounded = (nanos / divisor) * divisor;
            return WrapInstant(new JsTemporalInstant(rounded), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "toZonedDateTimeISO", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var tzArg = args.GetArgument(0);
            var timeZoneId = JsOps.ToJsString(tzArg);
            var zdt = new JsTemporalZonedDateTime(instant, timeZoneId);
            return WrapZonedDateTime(zdt, realm, prototypes.ZonedDateTimePrototype);
        });

        // Constructor
        var ctor = new HostFunction((thisValue, args) =>
        {
            var epochNanoseconds = args.GetArgument(0);

            JsTemporalInstant instant;
            if (epochNanoseconds.TryGetBigInt(out var bigInt))
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
            if (arg.TryGetBigInt(out var bigInt))
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

        var from = CreateFunction(realm, "from", 1, (_, args) =>
        {
            var instant = ToTemporalInstant(args.GetArgument(0), realm);
            return WrapInstant(instant, realm, prototype);
        });
        ctor.DefineProperty("from",
            new PropertyDescriptor { Value = from, Writable = true, Enumerable = false, Configurable = true });

        var compare = CreateFunction(realm, "compare", 2, (_, args) =>
        {
            var i1 = ToTemporalInstant(args.GetArgument(0), realm);
            var i2 = ToTemporalInstant(args.GetArgument(1), realm);
            return new JsValue(i1.CompareTo(i2));
        });
        ctor.DefineProperty("compare",
            new PropertyDescriptor { Value = compare, Writable = true, Enumerable = false, Configurable = true });

        return ctor;
    }

    private static HostFunction CreateDurationConstructor(RealmState realm, TemporalPrototypes prototypes)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototypes.DurationPrototype = prototype;
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
        AddPrototypeGetter(prototype, realm, "blank", tv => new JsValue(GetDuration(tv).Blank));

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

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, _) =>
        {
            // Basic implementation - returns ISO 8601 duration string
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
            string unit;
            if (unitArg.IsString)
            {
                unit = unitArg.AsString() ?? "nanoseconds";
            }
            else if (unitArg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                     accessor.TryGetProperty("unit", out var unitValue))
            {
                unit = JsOps.ToJsString(unitValue);
            }
            else
            {
                unit = JsOps.ToJsString(unitArg);
            }
            return new JsValue(duration.Total(unit));
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var duration = GetDuration(thisValue);
            var overrides = args.GetArgument(0);
            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                return WrapDuration(duration, realm, prototype);
            }

            var years = accessor.TryGetProperty("years", out var v) && !v.IsUndefined ? JsOps.ToNumber(v) : duration.Years;
            var months = accessor.TryGetProperty("months", out v) && !v.IsUndefined ? JsOps.ToNumber(v) : duration.Months;
            var weeks = accessor.TryGetProperty("weeks", out v) && !v.IsUndefined ? JsOps.ToNumber(v) : duration.Weeks;
            var days = accessor.TryGetProperty("days", out v) && !v.IsUndefined ? JsOps.ToNumber(v) : duration.Days;
            var hours = accessor.TryGetProperty("hours", out v) && !v.IsUndefined ? JsOps.ToNumber(v) : duration.Hours;
            var minutes = accessor.TryGetProperty("minutes", out v) && !v.IsUndefined ? JsOps.ToNumber(v) : duration.Minutes;
            var seconds = accessor.TryGetProperty("seconds", out v) && !v.IsUndefined ? JsOps.ToNumber(v) : duration.Seconds;
            var milliseconds = accessor.TryGetProperty("milliseconds", out v) && !v.IsUndefined ? JsOps.ToNumber(v) : duration.Milliseconds;
            var microseconds = accessor.TryGetProperty("microseconds", out v) && !v.IsUndefined ? JsOps.ToNumber(v) : duration.Microseconds;
            var nanoseconds = accessor.TryGetProperty("nanoseconds", out v) && !v.IsUndefined ? JsOps.ToNumber(v) : duration.Nanoseconds;

            return WrapDuration(new JsTemporalDuration(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "round", 1, (thisValue, args) =>
        {
            var duration = GetDuration(thisValue);
            var optionsArg = args.GetArgument(0);
            string smallestUnit;
            if (optionsArg.IsString)
            {
                smallestUnit = optionsArg.AsString() ?? "nanosecond";
            }
            else if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                     accessor.TryGetProperty("smallestUnit", out var unitValue))
            {
                smallestUnit = JsOps.ToJsString(unitValue);
            }
            else
            {
                smallestUnit = "nanosecond";
            }

            // Convert to total nanoseconds, round, then convert back
            var totalNanos = duration.Total("nanoseconds");
            double divisor = smallestUnit switch
            {
                "year" or "years" => 31_556_952_000_000_000.0,
                "month" or "months" => 2_629_746_000_000_000.0,
                "week" or "weeks" => 604_800_000_000_000.0,
                "day" or "days" => 86_400_000_000_000.0,
                "hour" or "hours" => 3_600_000_000_000.0,
                "minute" or "minutes" => 60_000_000_000.0,
                "second" or "seconds" => 1_000_000_000.0,
                "millisecond" or "milliseconds" => 1_000_000.0,
                "microsecond" or "microseconds" => 1_000.0,
                _ => 1.0
            };
            var rounded = Math.Round(totalNanos / divisor) * divisor;
            return WrapDuration(JsTemporalDuration.FromNanoseconds(rounded), realm, prototype);
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

    private static HostFunction CreatePlainDateConstructor(RealmState realm, TemporalPrototypes prototypes)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototypes.PlainDatePrototype = prototype;
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
        AddPrototypeGetter(prototype, realm, "daysInWeek", _ => new JsValue(7)); // ISO 8601 always has 7 days per week
        AddPrototypeGetter(prototype, realm, "era", _ => JsValue.Undefined); // ISO 8601 calendar has no era
        AddPrototypeGetter(prototype, realm, "eraYear", _ => JsValue.Undefined); // ISO 8601 calendar has no era

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, _) =>
            new JsValue(GetPlainDate(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
            new JsValue(GetPlainDate(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, _) =>
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

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var other = ToTemporalPlainDate(args.GetArgument(0), realm);
            var duration = date.Until(other);
            return WrapDuration(duration, realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var other = ToTemporalPlainDate(args.GetArgument(0), realm);
            var duration = date.Since(other);
            return WrapDuration(duration, realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var overrides = args.GetArgument(0);
            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                return WrapPlainDate(date, realm, prototype);
            }

            var year = accessor.TryGetProperty("year", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : date.Year;
            var month = accessor.TryGetProperty("month", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : date.Month;
            var day = accessor.TryGetProperty("day", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : date.Day;

            return WrapPlainDate(new JsTemporalPlainDate(year, month, day, date.Calendar), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainDateTime", 0, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            JsTemporalPlainTime time;
            if (args.Count > 0 && !args[0].IsUndefined)
            {
                time = ToTemporalPlainTime(args[0], realm);
            }
            else
            {
                time = new JsTemporalPlainTime(0, 0, 0, 0, 0, 0);
            }
            var dt = date.ToPlainDateTime(time);
            return WrapPlainDateTime(dt, realm, prototypes.PlainDateTimePrototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainYearMonth", 0, (thisValue, _) =>
        {
            var date = GetPlainDate(thisValue);
            var ym = date.ToPlainYearMonth();
            return WrapPlainYearMonth(ym, realm, prototypes.PlainYearMonthPrototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainMonthDay", 0, (thisValue, _) =>
        {
            var date = GetPlainDate(thisValue);
            var md = date.ToPlainMonthDay();
            return WrapPlainMonthDay(md, realm, prototypes.PlainMonthDayPrototype);
        });

        AddPrototypeMethod(prototype, realm, "toZonedDateTime", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var arg = args.GetArgument(0);
            string timeZone;
            JsTemporalPlainTime time = new JsTemporalPlainTime(0, 0, 0, 0, 0, 0);

            if (arg.TryGetObject<IJsPropertyAccessor>(out var accessor) && accessor is not null)
            {
                // Object with timeZone and optional plainTime
                if (accessor.TryGetProperty("timeZone", out var tzValue))
                {
                    timeZone = JsOps.ToJsString(tzValue);
                }
                else
                {
                    throw StandardLibrary.ThrowTypeError("toZonedDateTime requires a timeZone property", null, realm);
                }

                if (accessor.TryGetProperty("plainTime", out var timeValue) && !timeValue.IsUndefined)
                {
                    time = ToTemporalPlainTime(timeValue, realm);
                }
            }
            else
            {
                // String timezone
                timeZone = JsOps.ToJsString(arg);
            }

            var dt = date.ToPlainDateTime(time);
            var zdt = new JsTemporalZonedDateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second,
                dt.Millisecond, dt.Microsecond, dt.Nanosecond, timeZone, date.Calendar);
            return WrapZonedDateTime(zdt, realm, prototypes.ZonedDateTimePrototype);
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

    private static HostFunction CreatePlainTimeConstructor(RealmState realm, TemporalPrototypes prototypes)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototypes.PlainTimePrototype = prototype;
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

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, _) =>
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

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var other = ToTemporalPlainTime(args.GetArgument(0), realm);
            var duration = time.Until(other);
            return WrapDuration(duration, realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var other = ToTemporalPlainTime(args.GetArgument(0), realm);
            var duration = time.Since(other);
            return WrapDuration(duration, realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var overrides = args.GetArgument(0);
            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                return WrapPlainTime(time, realm, prototype);
            }

            var hour = accessor.TryGetProperty("hour", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Hour;
            var minute = accessor.TryGetProperty("minute", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Minute;
            var second = accessor.TryGetProperty("second", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Second;
            var millisecond = accessor.TryGetProperty("millisecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Millisecond;
            var microsecond = accessor.TryGetProperty("microsecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Microsecond;
            var nanosecond = accessor.TryGetProperty("nanosecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Nanosecond;

            return WrapPlainTime(new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "round", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var optionsArg = args.GetArgument(0);
            string smallestUnit;
            if (optionsArg.IsString)
            {
                smallestUnit = optionsArg.AsString() ?? "nanosecond";
            }
            else if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                     accessor.TryGetProperty("smallestUnit", out var unitValue))
            {
                smallestUnit = JsOps.ToJsString(unitValue);
            }
            else
            {
                smallestUnit = "nanosecond";
            }

            var rounded = time.Round(smallestUnit);
            return WrapPlainTime(rounded, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainDateTime", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var date = ToTemporalPlainDate(args.GetArgument(0), realm);
            var dt = new JsTemporalPlainDateTime(date, time);
            return WrapPlainDateTime(dt, realm);
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

    private static HostFunction CreatePlainDateTimeConstructor(RealmState realm, TemporalPrototypes prototypes)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototypes.PlainDateTimePrototype = prototype;
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
        AddPrototypeGetter(prototype, realm, "daysInWeek", _ => new JsValue(7)); // ISO 8601 always has 7 days per week
        AddPrototypeGetter(prototype, realm, "era", _ => JsValue.Undefined); // ISO 8601 calendar has no era
        AddPrototypeGetter(prototype, realm, "eraYear", _ => JsValue.Undefined); // ISO 8601 calendar has no era

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, _) =>
            new JsValue(GetPlainDateTime(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
            new JsValue(GetPlainDateTime(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, _) =>
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
            return WrapPlainDate(dt.ToPlainDate(), realm, prototypes.PlainDatePrototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainTime", 0, (thisValue, _) =>
        {
            var dt = GetPlainDateTime(thisValue);
            return WrapPlainTime(dt.ToPlainTime(), realm, prototypes.PlainTimePrototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainYearMonth", 0, (thisValue, _) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var ym = new JsTemporalPlainYearMonth(dt.Year, dt.Month, dt.Calendar);
            return WrapPlainYearMonth(ym, realm, prototypes.PlainYearMonthPrototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainMonthDay", 0, (thisValue, _) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var md = new JsTemporalPlainMonthDay(dt.Month, dt.Day, dt.Calendar);
            return WrapPlainMonthDay(md, realm, prototypes.PlainMonthDayPrototype);
        });

        AddPrototypeMethod(prototype, realm, "toZonedDateTime", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var tzArg = args.GetArgument(0);
            string timeZone;

            if (tzArg.TryGetObject<IJsPropertyAccessor>(out var accessor) && accessor is not null)
            {
                if (accessor.TryGetProperty("timeZone", out var tzValue))
                {
                    timeZone = JsOps.ToJsString(tzValue);
                }
                else
                {
                    throw StandardLibrary.ThrowTypeError("toZonedDateTime requires a timeZone property", null, realm);
                }
            }
            else
            {
                timeZone = JsOps.ToJsString(tzArg);
            }

            var zdt = new JsTemporalZonedDateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second,
                dt.Millisecond, dt.Microsecond, dt.Nanosecond, timeZone, dt.Calendar);
            return WrapZonedDateTime(zdt, realm, prototypes.ZonedDateTimePrototype);
        });

        AddPrototypeMethod(prototype, realm, "add", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapPlainDateTime(dt.Add(duration), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapPlainDateTime(dt.Subtract(duration), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var other = ToTemporalPlainDateTime(args.GetArgument(0), realm);
            var duration = dt.Until(other);
            return WrapDuration(duration, realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var other = ToTemporalPlainDateTime(args.GetArgument(0), realm);
            var duration = dt.Since(other);
            return WrapDuration(duration, realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var overrides = args.GetArgument(0);
            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                return WrapPlainDateTime(dt, realm, prototype);
            }

            var year = accessor.TryGetProperty("year", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Year;
            var month = accessor.TryGetProperty("month", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Month;
            var day = accessor.TryGetProperty("day", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Day;
            var hour = accessor.TryGetProperty("hour", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Hour;
            var minute = accessor.TryGetProperty("minute", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Minute;
            var second = accessor.TryGetProperty("second", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Second;
            var millisecond = accessor.TryGetProperty("millisecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Millisecond;
            var microsecond = accessor.TryGetProperty("microsecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Microsecond;
            var nanosecond = accessor.TryGetProperty("nanosecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Nanosecond;

            return WrapPlainDateTime(new JsTemporalPlainDateTime(year, month, day, hour, minute, second, millisecond, microsecond, nanosecond, dt.Calendar), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "round", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var optionsArg = args.GetArgument(0);
            string smallestUnit;
            if (optionsArg.IsString)
            {
                smallestUnit = optionsArg.AsString() ?? "nanosecond";
            }
            else if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                     accessor.TryGetProperty("smallestUnit", out var unitValue))
            {
                smallestUnit = JsOps.ToJsString(unitValue);
            }
            else
            {
                smallestUnit = "nanosecond";
            }

            var rounded = dt.Round(smallestUnit);
            return WrapPlainDateTime(rounded, realm, prototype);
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

    private static HostFunction CreateZonedDateTimeConstructor(RealmState realm, TemporalPrototypes prototypes)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototypes.ZonedDateTimePrototype = prototype;
        prototype.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal.ZonedDateTime", Writable = false, Enumerable = false, Configurable = true });

        // Prototype getters
        AddPrototypeGetter(prototype, realm, "year", tv => new JsValue(GetZonedDateTime(tv).Year));
        AddPrototypeGetter(prototype, realm, "month", tv => new JsValue(GetZonedDateTime(tv).Month));
        AddPrototypeGetter(prototype, realm, "day", tv => new JsValue(GetZonedDateTime(tv).Day));
        AddPrototypeGetter(prototype, realm, "hour", tv => new JsValue(GetZonedDateTime(tv).Hour));
        AddPrototypeGetter(prototype, realm, "minute", tv => new JsValue(GetZonedDateTime(tv).Minute));
        AddPrototypeGetter(prototype, realm, "second", tv => new JsValue(GetZonedDateTime(tv).Second));
        AddPrototypeGetter(prototype, realm, "millisecond", tv => new JsValue(GetZonedDateTime(tv).Millisecond));
        AddPrototypeGetter(prototype, realm, "microsecond", tv => new JsValue(GetZonedDateTime(tv).Microsecond));
        AddPrototypeGetter(prototype, realm, "nanosecond", tv => new JsValue(GetZonedDateTime(tv).Nanosecond));
        AddPrototypeGetter(prototype, realm, "epochMilliseconds", tv => new JsValue((double)GetZonedDateTime(tv).EpochMilliseconds));
        AddPrototypeGetter(prototype, realm, "epochSeconds", tv => new JsValue((double)GetZonedDateTime(tv).EpochSeconds));
        AddPrototypeGetter(prototype, realm, "monthCode", tv => new JsValue(GetZonedDateTime(tv).MonthCode));
        AddPrototypeGetter(prototype, realm, "dayOfWeek", tv => new JsValue(GetZonedDateTime(tv).DayOfWeek));
        AddPrototypeGetter(prototype, realm, "dayOfYear", tv => new JsValue(GetZonedDateTime(tv).DayOfYear));
        AddPrototypeGetter(prototype, realm, "weekOfYear", tv => new JsValue(GetZonedDateTime(tv).WeekOfYear));
        AddPrototypeGetter(prototype, realm, "daysInMonth", tv => new JsValue(GetZonedDateTime(tv).DaysInMonth));
        AddPrototypeGetter(prototype, realm, "daysInYear", tv => new JsValue(GetZonedDateTime(tv).DaysInYear));
        AddPrototypeGetter(prototype, realm, "inLeapYear", tv => new JsValue(GetZonedDateTime(tv).InLeapYear));
        AddPrototypeGetter(prototype, realm, "timeZoneId", tv => new JsValue(GetZonedDateTime(tv).TimeZoneId));
        AddPrototypeGetter(prototype, realm, "offset", tv => new JsValue(GetZonedDateTime(tv).Offset));
        AddPrototypeGetter(prototype, realm, "calendarId", tv => new JsValue(GetZonedDateTime(tv).Calendar));
        AddPrototypeGetter(prototype, realm, "daysInWeek", _ => new JsValue(7)); // ISO 8601 always has 7 days per week
        AddPrototypeGetter(prototype, realm, "monthsInYear", _ => new JsValue(12)); // ISO 8601 always has 12 months per year
        AddPrototypeGetter(prototype, realm, "era", _ => JsValue.Undefined); // ISO 8601 calendar has no era
        AddPrototypeGetter(prototype, realm, "eraYear", _ => JsValue.Undefined); // ISO 8601 calendar has no era
        AddPrototypeGetter(prototype, realm, "offsetNanoseconds", tv =>
        {
            var zdt = GetZonedDateTime(tv);
            // Parse offset string like "+01:00" to nanoseconds
            var offset = zdt.Offset;
            var totalSeconds = ParseOffsetToSeconds(offset);
            return new JsValue((double)totalSeconds * 1_000_000_000L);
        });

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, _) =>
            new JsValue(GetZonedDateTime(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
            new JsValue(GetZonedDateTime(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, _) =>
            new JsValue(GetZonedDateTime(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "valueOf", 0, (_, _) =>
            throw StandardLibrary.ThrowTypeError("Temporal.ZonedDateTime.prototype.valueOf does not support implicit conversion", realm: realm));

        AddPrototypeMethod(prototype, realm, "toInstant", 0, (thisValue, _) =>
            WrapInstant(GetZonedDateTime(thisValue).ToInstant(), realm, prototypes.InstantPrototype));

        AddPrototypeMethod(prototype, realm, "toPlainDateTime", 0, (thisValue, _) =>
            WrapPlainDateTime(GetZonedDateTime(thisValue).ToPlainDateTime(), realm, prototypes.PlainDateTimePrototype));

        AddPrototypeMethod(prototype, realm, "toPlainDate", 0, (thisValue, _) =>
            WrapPlainDate(GetZonedDateTime(thisValue).ToPlainDate(), realm, prototypes.PlainDatePrototype));

        AddPrototypeMethod(prototype, realm, "toPlainTime", 0, (thisValue, _) =>
            WrapPlainTime(GetZonedDateTime(thisValue).ToPlainTime(), realm, prototypes.PlainTimePrototype));

        AddPrototypeMethod(prototype, realm, "add", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapZonedDateTime(zdt.Add(duration), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapZonedDateTime(zdt.Subtract(duration), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var other = ToTemporalZonedDateTime(args.GetArgument(0), realm);
            var diffNanos = other.Instant.EpochNanoseconds - zdt.Instant.EpochNanoseconds;
            var duration = JsTemporalDuration.FromNanoseconds((double)diffNanos);
            return WrapDuration(duration, realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var other = ToTemporalZonedDateTime(args.GetArgument(0), realm);
            var diffNanos = zdt.Instant.EpochNanoseconds - other.Instant.EpochNanoseconds;
            var duration = JsTemporalDuration.FromNanoseconds((double)diffNanos);
            return WrapDuration(duration, realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "round", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var optionsArg = args.GetArgument(0);
            string smallestUnit;
            if (optionsArg.IsString)
            {
                smallestUnit = optionsArg.AsString() ?? "nanosecond";
            }
            else if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                     accessor.TryGetProperty("smallestUnit", out var unitValue))
            {
                smallestUnit = JsOps.ToJsString(unitValue);
            }
            else
            {
                smallestUnit = "nanosecond";
            }

            // Round by rounding the epoch nanoseconds
            var nanos = zdt.Instant.EpochNanoseconds;
            long divisor = smallestUnit switch
            {
                "hour" or "hours" => 3600_000_000_000L,
                "minute" or "minutes" => 60_000_000_000L,
                "second" or "seconds" => 1_000_000_000L,
                "millisecond" or "milliseconds" => 1_000_000L,
                "microsecond" or "microseconds" => 1_000L,
                _ => 1L
            };
            var rounded = (nanos / divisor) * divisor;
            var newInstant = new JsTemporalInstant(rounded);
            var roundedZdt = new JsTemporalZonedDateTime(newInstant, zdt.TimeZoneId, zdt.Calendar);
            return WrapZonedDateTime(roundedZdt, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var overrides = args.GetArgument(0);
            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                return WrapZonedDateTime(zdt, realm, prototype);
            }

            var year = accessor.TryGetProperty("year", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : zdt.Year;
            var month = accessor.TryGetProperty("month", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : zdt.Month;
            var day = accessor.TryGetProperty("day", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : zdt.Day;
            var hour = accessor.TryGetProperty("hour", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : zdt.Hour;
            var minute = accessor.TryGetProperty("minute", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : zdt.Minute;
            var second = accessor.TryGetProperty("second", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : zdt.Second;
            var millisecond = accessor.TryGetProperty("millisecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : zdt.Millisecond;
            var microsecond = accessor.TryGetProperty("microsecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : zdt.Microsecond;
            var nanosecond = accessor.TryGetProperty("nanosecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : zdt.Nanosecond;

            var newZdt = new JsTemporalZonedDateTime(year, month, day, hour, minute, second, millisecond, microsecond, nanosecond, zdt.TimeZoneId, zdt.Calendar);
            return WrapZonedDateTime(newZdt, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "equals", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var other = ToTemporalZonedDateTime(args.GetArgument(0), realm);
            return new JsValue(zdt.Equals(other));
        });

        AddPrototypeMethod(prototype, realm, "startOfDay", 0, (thisValue, _) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var startOfDayZdt = new JsTemporalZonedDateTime(
                zdt.Year, zdt.Month, zdt.Day, 0, 0, 0, 0, 0, 0,
                zdt.TimeZoneId, zdt.Calendar);
            return WrapZonedDateTime(startOfDayZdt, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "withTimeZone", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var timeZoneId = JsOps.ToJsString(args.GetArgument(0));
            var newZdt = zdt.WithTimeZone(timeZoneId);
            return WrapZonedDateTime(newZdt, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "withPlainTime", 0, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            JsTemporalPlainTime time;
            if (args.Count > 0 && !args[0].IsUndefined)
            {
                time = ToTemporalPlainTime(args[0], realm);
            }
            else
            {
                time = new JsTemporalPlainTime(0, 0, 0, 0, 0, 0);
            }
            var newZdt = new JsTemporalZonedDateTime(
                zdt.Year, zdt.Month, zdt.Day,
                time.Hour, time.Minute, time.Second,
                time.Millisecond, time.Microsecond, time.Nanosecond,
                zdt.TimeZoneId, zdt.Calendar);
            return WrapZonedDateTime(newZdt, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "hoursInDay", 0, (thisValue, _) =>
        {
            // For most days this is 24, but DST transitions can make it 23 or 25
            // For simplicity, we return 24 here (proper implementation would check DST)
            return new JsValue(24);
        });

        // Constructor
        var ctor = new HostFunction((thisValue, args) =>
        {
            var epochNanoseconds = args.GetArgument(0);
            var timeZoneArg = args.GetArgument(1);
            var calendarArg = args.Count > 2 ? args[2] : JsValue.Undefined;

            var timeZoneId = JsOps.ToJsString(timeZoneArg);
            var calendar = calendarArg.IsUndefined ? "iso8601" : JsOps.ToJsString(calendarArg);

            JsTemporalInstant instant;
            if (epochNanoseconds.TryGetBigInt(out var bigInt))
            {
                instant = new JsTemporalInstant(bigInt.Value);
            }
            else
            {
                var ns = JsOps.ToNumber(epochNanoseconds);
                instant = JsTemporalInstant.FromEpochNanoseconds(new System.Numerics.BigInteger(ns));
            }

            var zdt = new JsTemporalZonedDateTime(instant, timeZoneId, calendar);
            return WrapZonedDateTime(zdt, realm, prototype);
        }, realm) { IsConstructor = true };

        ctor.DefineProperty("prototype",
            new PropertyDescriptor { Value = prototype, Writable = false, Enumerable = false, Configurable = false });
        prototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = ctor, Writable = true, Enumerable = false, Configurable = true });

        // Static methods
        var from = CreateFunction(realm, "from", 1, (_, args) =>
        {
            var zdt = ToTemporalZonedDateTime(args.GetArgument(0), realm);
            return WrapZonedDateTime(zdt, realm, prototype);
        });
        ctor.DefineProperty("from",
            new PropertyDescriptor { Value = from, Writable = true, Enumerable = false, Configurable = true });

        var compare = CreateFunction(realm, "compare", 2, (_, args) =>
        {
            var z1 = ToTemporalZonedDateTime(args.GetArgument(0), realm);
            var z2 = ToTemporalZonedDateTime(args.GetArgument(1), realm);
            return new JsValue(z1.CompareTo(z2));
        });
        ctor.DefineProperty("compare",
            new PropertyDescriptor { Value = compare, Writable = true, Enumerable = false, Configurable = true });

        return ctor;
    }

    private static HostFunction CreatePlainYearMonthConstructor(RealmState realm, TemporalPrototypes prototypes)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototypes.PlainYearMonthPrototype = prototype;
        prototype.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal.PlainYearMonth", Writable = false, Enumerable = false, Configurable = true });

        // Prototype getters
        AddPrototypeGetter(prototype, realm, "year", tv => new JsValue(GetPlainYearMonth(tv).Year));
        AddPrototypeGetter(prototype, realm, "month", tv => new JsValue(GetPlainYearMonth(tv).Month));
        AddPrototypeGetter(prototype, realm, "monthCode", tv => new JsValue(GetPlainYearMonth(tv).MonthCode));
        AddPrototypeGetter(prototype, realm, "daysInMonth", tv => new JsValue(GetPlainYearMonth(tv).DaysInMonth));
        AddPrototypeGetter(prototype, realm, "daysInYear", tv => new JsValue(GetPlainYearMonth(tv).DaysInYear));
        AddPrototypeGetter(prototype, realm, "monthsInYear", tv => new JsValue(GetPlainYearMonth(tv).MonthsInYear));
        AddPrototypeGetter(prototype, realm, "inLeapYear", tv => new JsValue(GetPlainYearMonth(tv).InLeapYear));
        AddPrototypeGetter(prototype, realm, "calendarId", tv => new JsValue(GetPlainYearMonth(tv).Calendar));
        AddPrototypeGetter(prototype, realm, "era", _ => JsValue.Undefined); // ISO 8601 calendar has no era
        AddPrototypeGetter(prototype, realm, "eraYear", _ => JsValue.Undefined); // ISO 8601 calendar has no era

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, _) =>
            new JsValue(GetPlainYearMonth(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
            new JsValue(GetPlainYearMonth(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, _) =>
            new JsValue(GetPlainYearMonth(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "valueOf", 0, (_, _) =>
            throw StandardLibrary.ThrowTypeError("Temporal.PlainYearMonth.prototype.valueOf does not support implicit conversion", realm: realm));

        AddPrototypeMethod(prototype, realm, "equals", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var other = ToTemporalPlainYearMonth(args.GetArgument(0), realm);
            return new JsValue(ym.Equals(other));
        });

        AddPrototypeMethod(prototype, realm, "add", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapPlainYearMonth(ym.Add(duration), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapPlainYearMonth(ym.Subtract(duration), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var other = ToTemporalPlainYearMonth(args.GetArgument(0), realm);
            // Calculate difference in months
            var monthsDiff = (other.Year - ym.Year) * 12 + (other.Month - ym.Month);
            var years = monthsDiff / 12;
            var months = monthsDiff % 12;
            return WrapDuration(new JsTemporalDuration(years, months, 0, 0, 0, 0, 0, 0, 0, 0), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var other = ToTemporalPlainYearMonth(args.GetArgument(0), realm);
            // Calculate difference in months
            var monthsDiff = (ym.Year - other.Year) * 12 + (ym.Month - other.Month);
            var years = monthsDiff / 12;
            var months = monthsDiff % 12;
            return WrapDuration(new JsTemporalDuration(years, months, 0, 0, 0, 0, 0, 0, 0, 0), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var overrides = args.GetArgument(0);
            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                return WrapPlainYearMonth(ym, realm, prototype);
            }

            var year = accessor.TryGetProperty("year", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : ym.Year;
            var month = accessor.TryGetProperty("month", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : ym.Month;

            return WrapPlainYearMonth(new JsTemporalPlainYearMonth(year, month, ym.Calendar), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainDate", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var dayArg = args.GetArgument(0);
            int day;
            if (dayArg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                accessor.TryGetProperty("day", out var dayValue))
            {
                day = (int)JsOps.ToNumber(dayValue);
            }
            else
            {
                day = (int)JsOps.ToNumber(dayArg);
            }
            return WrapPlainDate(ym.ToPlainDate(day), realm, prototypes.PlainDatePrototype);
        });

        // Constructor
        var ctor = new HostFunction((thisValue, args) =>
        {
            var year = (int)JsOps.ToNumber(args.GetArgument(0));
            var month = (int)JsOps.ToNumber(args.GetArgument(1));
            var calendar = args.Count > 2 && !args[2].IsUndefined ? JsOps.ToJsString(args[2]) : "iso8601";

            var ym = new JsTemporalPlainYearMonth(year, month, calendar);
            return WrapPlainYearMonth(ym, realm, prototype);
        }, realm) { IsConstructor = true };

        ctor.DefineProperty("prototype",
            new PropertyDescriptor { Value = prototype, Writable = false, Enumerable = false, Configurable = false });
        prototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = ctor, Writable = true, Enumerable = false, Configurable = true });

        // Static methods
        var from = CreateFunction(realm, "from", 1, (_, args) =>
        {
            var ym = ToTemporalPlainYearMonth(args.GetArgument(0), realm);
            return WrapPlainYearMonth(ym, realm, prototype);
        });
        ctor.DefineProperty("from",
            new PropertyDescriptor { Value = from, Writable = true, Enumerable = false, Configurable = true });

        var compare = CreateFunction(realm, "compare", 2, (_, args) =>
        {
            var ym1 = ToTemporalPlainYearMonth(args.GetArgument(0), realm);
            var ym2 = ToTemporalPlainYearMonth(args.GetArgument(1), realm);
            return new JsValue(ym1.CompareTo(ym2));
        });
        ctor.DefineProperty("compare",
            new PropertyDescriptor { Value = compare, Writable = true, Enumerable = false, Configurable = true });

        return ctor;
    }

    private static HostFunction CreatePlainMonthDayConstructor(RealmState realm, TemporalPrototypes prototypes)
    {
        var prototype = new JsObject(realm.ObjectPrototype);
        prototypes.PlainMonthDayPrototype = prototype;
        prototype.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor { Value = "Temporal.PlainMonthDay", Writable = false, Enumerable = false, Configurable = true });

        // Prototype getters
        AddPrototypeGetter(prototype, realm, "month", tv => new JsValue(GetPlainMonthDay(tv).Month));
        AddPrototypeGetter(prototype, realm, "day", tv => new JsValue(GetPlainMonthDay(tv).Day));
        AddPrototypeGetter(prototype, realm, "monthCode", tv => new JsValue(GetPlainMonthDay(tv).MonthCode));
        AddPrototypeGetter(prototype, realm, "calendarId", tv => new JsValue(GetPlainMonthDay(tv).Calendar));

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, _) =>
            new JsValue(GetPlainMonthDay(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
            new JsValue(GetPlainMonthDay(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, _) =>
            new JsValue(GetPlainMonthDay(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "valueOf", 0, (_, _) =>
            throw StandardLibrary.ThrowTypeError("Temporal.PlainMonthDay.prototype.valueOf does not support implicit conversion", realm: realm));

        AddPrototypeMethod(prototype, realm, "equals", 1, (thisValue, args) =>
        {
            var md = GetPlainMonthDay(thisValue);
            var other = ToTemporalPlainMonthDay(args.GetArgument(0), realm);
            return new JsValue(md.Equals(other));
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var md = GetPlainMonthDay(thisValue);
            var overrides = args.GetArgument(0);
            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                return WrapPlainMonthDay(md, realm, prototype);
            }

            var month = accessor.TryGetProperty("month", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : md.Month;
            var day = accessor.TryGetProperty("day", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : md.Day;

            return WrapPlainMonthDay(new JsTemporalPlainMonthDay(month, day, md.Calendar), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainDate", 1, (thisValue, args) =>
        {
            var md = GetPlainMonthDay(thisValue);
            var yearArg = args.GetArgument(0);
            int year;
            if (yearArg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                accessor.TryGetProperty("year", out var yearValue))
            {
                year = (int)JsOps.ToNumber(yearValue);
            }
            else
            {
                year = (int)JsOps.ToNumber(yearArg);
            }
            return WrapPlainDate(md.ToPlainDate(year), realm, prototypes.PlainDatePrototype);
        });

        // Constructor
        var ctor = new HostFunction((thisValue, args) =>
        {
            var month = (int)JsOps.ToNumber(args.GetArgument(0));
            var day = (int)JsOps.ToNumber(args.GetArgument(1));
            var calendar = args.Count > 2 && !args[2].IsUndefined ? JsOps.ToJsString(args[2]) : "iso8601";

            var md = new JsTemporalPlainMonthDay(month, day, calendar);
            return WrapPlainMonthDay(md, realm, prototype);
        }, realm) { IsConstructor = true };

        ctor.DefineProperty("prototype",
            new PropertyDescriptor { Value = prototype, Writable = false, Enumerable = false, Configurable = false });
        prototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = ctor, Writable = true, Enumerable = false, Configurable = true });

        // Static methods
        var from = CreateFunction(realm, "from", 1, (_, args) =>
        {
            var md = ToTemporalPlainMonthDay(args.GetArgument(0), realm);
            return WrapPlainMonthDay(md, realm, prototype);
        });
        ctor.DefineProperty("from",
            new PropertyDescriptor { Value = from, Writable = true, Enumerable = false, Configurable = true });

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

    private static long ParseOffsetToSeconds(string offset)
    {
        // Parse offset string like "+01:00", "-05:30", or "Z"
        if (string.IsNullOrEmpty(offset) || offset == "Z")
            return 0;

        var sign = 1;
        var start = 0;

        if (offset[0] == '+')
        {
            start = 1;
        }
        else if (offset[0] == '-')
        {
            sign = -1;
            start = 1;
        }

        var parts = offset.Substring(start).Split(':');
        var hours = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        var minutes = parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
        var seconds = parts.Length > 2 ? int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 0;

        return sign * (hours * 3600L + minutes * 60L + seconds);
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

    private static JsValue WrapZonedDateTime(JsTemporalZonedDateTime zonedDateTime, RealmState realm, JsObject? prototype = null)
    {
        var obj = new JsObject(prototype ?? realm.ObjectPrototype);
        obj.SetProperty(TemporalZonedDateTimeSlot, JsValue.FromObjectUnsafe(zonedDateTime));
        return JsValue.FromObjectUnsafe(obj);
    }

    private static JsValue WrapPlainYearMonth(JsTemporalPlainYearMonth yearMonth, RealmState realm, JsObject? prototype = null)
    {
        var obj = new JsObject(prototype ?? realm.ObjectPrototype);
        obj.SetProperty(TemporalPlainYearMonthSlot, JsValue.FromObjectUnsafe(yearMonth));
        return JsValue.FromObjectUnsafe(obj);
    }

    private static JsValue WrapPlainMonthDay(JsTemporalPlainMonthDay monthDay, RealmState realm, JsObject? prototype = null)
    {
        var obj = new JsObject(prototype ?? realm.ObjectPrototype);
        obj.SetProperty(TemporalPlainMonthDaySlot, JsValue.FromObjectUnsafe(monthDay));
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

    private static JsTemporalZonedDateTime GetZonedDateTime(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalZonedDateTimeSlot, out var slot) &&
            slot.TryGetObject<JsTemporalZonedDateTime>(out var zonedDateTime))
        {
            return zonedDateTime;
        }
        throw new InvalidOperationException("Value is not a Temporal.ZonedDateTime");
    }

    private static JsTemporalPlainYearMonth GetPlainYearMonth(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainYearMonthSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainYearMonth>(out var yearMonth))
        {
            return yearMonth;
        }
        throw new InvalidOperationException("Value is not a Temporal.PlainYearMonth");
    }

    private static JsTemporalPlainMonthDay GetPlainMonthDay(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainMonthDaySlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainMonthDay>(out var monthDay))
        {
            return monthDay;
        }
        throw new InvalidOperationException("Value is not a Temporal.PlainMonthDay");
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

    private static JsTemporalZonedDateTime ToTemporalZonedDateTime(JsValue value, RealmState realm)
    {
        // If it's already a Temporal.ZonedDateTime
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalZonedDateTimeSlot, out var slot) &&
            slot.TryGetObject<JsTemporalZonedDateTime>(out var zonedDateTime))
        {
            return zonedDateTime;
        }

        // Try to parse as string
        if (value.IsString)
        {
            return JsTemporalZonedDateTime.From(value.AsString() ?? "");
        }

        // If it's an object with zoned datetime properties
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
            var timeZoneId = GetPropertyAsString(accessor, "timeZone") ?? TimeZoneInfo.Local.Id;
            var calendar = GetPropertyAsString(accessor, "calendar") ?? "iso8601";

            return new JsTemporalZonedDateTime(year, month, day, hour, minute, second,
                millisecond, microsecond, nanosecond, timeZoneId, calendar);
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.ZonedDateTime", realm: realm);
    }

    private static JsTemporalPlainYearMonth ToTemporalPlainYearMonth(JsValue value, RealmState realm)
    {
        // If it's already a Temporal.PlainYearMonth
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainYearMonthSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainYearMonth>(out var yearMonth))
        {
            return yearMonth;
        }

        // Try to parse as string
        if (value.IsString)
        {
            return JsTemporalPlainYearMonth.From(value.AsString() ?? "");
        }

        // If it's an object with year/month properties
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            var year = (int)GetPropertyAsNumber(accessor, "year");
            var month = (int)GetPropertyAsNumber(accessor, "month");
            var calendar = GetPropertyAsString(accessor, "calendar") ?? "iso8601";
            return new JsTemporalPlainYearMonth(year, month, calendar);
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainYearMonth", realm: realm);
    }

    private static JsTemporalPlainMonthDay ToTemporalPlainMonthDay(JsValue value, RealmState realm)
    {
        // If it's already a Temporal.PlainMonthDay
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainMonthDaySlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainMonthDay>(out var monthDay))
        {
            return monthDay;
        }

        // Try to parse as string
        if (value.IsString)
        {
            return JsTemporalPlainMonthDay.From(value.AsString() ?? "");
        }

        // If it's an object with month/day properties
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            var month = (int)GetPropertyAsNumber(accessor, "month");
            var day = (int)GetPropertyAsNumber(accessor, "day");
            var calendar = GetPropertyAsString(accessor, "calendar") ?? "iso8601";
            return new JsTemporalPlainMonthDay(month, day, calendar);
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainMonthDay", realm: realm);
    }

    private static double GetPropertyAsNumber(IJsPropertyAccessor accessor, string name)
    {
        if (accessor.TryGetProperty(name, out var value) && !value.IsUndefined)
        {
            return JsOps.ToNumber(value);
        }
        return 0;
    }

    private static string? GetPropertyAsString(IJsPropertyAccessor accessor, string name)
    {
        if (accessor.TryGetProperty(name, out var value) && !value.IsUndefined)
        {
            return JsOps.ToJsString(value);
        }
        return null;
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
