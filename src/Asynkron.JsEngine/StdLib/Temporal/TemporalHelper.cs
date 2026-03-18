#region

using System.Linq;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Ast;
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
    private const long NanosecondsPerMicrosecond = 1_000L;
    private const long NanosecondsPerMillisecond = 1_000_000L;
    private const long NanosecondsPerSecond = 1_000_000_000L;
    private const long NanosecondsPerMinute = 60L * NanosecondsPerSecond;
    private const long NanosecondsPerHour = 60L * NanosecondsPerMinute;
    private const long NanosecondsPerDay = 24L * NanosecondsPerHour;
    private static readonly BigInteger InstantMaxEpochNanoseconds =
        new BigInteger(864) * BigInteger.Pow(10, 19);
    private static readonly BigInteger InstantMinEpochNanoseconds = -InstantMaxEpochNanoseconds;
    private static readonly BigInteger PlainDateTimeMinEpochNanoseconds = InstantMinEpochNanoseconds + 1;
    private static readonly BigInteger PlainDateTimeMaxEpochNanoseconds = InstantMaxEpochNanoseconds - 1;
    private static readonly Dictionary<string, long> PlainDateTimeRoundingIncrements = new(StringComparer.Ordinal)
    {
        ["day"] = 1,
        ["hour"] = 24,
        ["minute"] = 60,
        ["second"] = 60,
        ["millisecond"] = 1000,
        ["microsecond"] = 1000,
        ["nanosecond"] = 1000
    };
    private static readonly Dictionary<string, long> PlainTimeRoundingIncrements = new(StringComparer.Ordinal)
    {
        ["hour"] = 24,
        ["minute"] = 60,
        ["second"] = 60,
        ["millisecond"] = 1000,
        ["microsecond"] = 1000,
        ["nanosecond"] = 1000
    };
    private static readonly Dictionary<string, long> InstantRoundingIncrements = new(StringComparer.Ordinal)
    {
        ["hour"] = 24,
        ["minute"] = 1440,
        ["second"] = 86400,
        ["millisecond"] = 86_400_000,
        ["microsecond"] = 86_400_000_000,
        ["nanosecond"] = 86_400_000_000_000
    };
    private static readonly HashSet<string> ValidRoundingModes = new(StringComparer.Ordinal)
    {
        "ceil",
        "floor",
        "trunc",
        "halfCeil",
        "halfFloor",
        "halfExpand",
        "halfTrunc",
        "halfEven",
        "expand"
    };

    private static readonly HashSet<string> ValidOffsetOptions = new(StringComparer.Ordinal)
    {
        "auto",
        "never"
    };

    private static readonly HashSet<string> ValidTimeZoneNameOptions = new(StringComparer.Ordinal)
    {
        "auto",
        "never",
        "critical"
    };

    /// <summary>
    /// Known calendar identifiers per ECMA-402 / Temporal spec.
    /// </summary>
    private static readonly HashSet<string> ValidCalendarIds = new(StringComparer.Ordinal)
    {
        "iso8601",
        "buddhist",
        "chinese",
        "coptic",
        "dangi",
        "ethioaa",
        "ethiopic",
        "gregory",
        "hebrew",
        "indian",
        "islamic",
        "islamic-civil",
        "islamic-rgsa",
        "islamic-tbla",
        "islamic-umalqura",
        "japanese",
        "persian",
        "roc"
    };

    /// <summary>
    /// Deprecated calendar aliases → canonical ID.
    /// </summary>
    private static readonly Dictionary<string, string> CalendarAliases = new(StringComparer.Ordinal)
    {
        ["islamicc"] = "islamic-civil"
    };

    /// <summary>
    /// Converts a Temporal calendar argument to a canonical calendar identifier.
    /// Performs ASCII lowercasing and validation per the Temporal spec.
    /// </summary>
    /// <param name="calendarArg">The JS value for the calendar argument.</param>
    /// <returns>Canonical calendar ID string.</returns>
    private static string ToTemporalCalendarIdentifier(JsValue calendarArg)
    {
        // If undefined, default to iso8601
        if (calendarArg.IsUndefined)
            return "iso8601";

        // Must be a string — TypeError for other types (except undefined)
        if (!calendarArg.IsString)
        {
            throw StandardLibrary.ThrowTypeError(
                $"{JsOps.TypeOf(calendarArg).AsString()} is not a valid calendar");
        }

        var id = calendarArg.AsString();

        // Validate: must not be empty, must not contain '[' (no ISO string annotations)
        if (string.IsNullOrEmpty(id) || id.Contains('['))
        {
            throw StandardLibrary.ThrowRangeError($"invalid calendar identifier: '{id}'");
        }

        // ASCII-lowercase only (NOT Unicode case folding - \u0130 must NOT become 'i')
        id = AsciiLowercase(id);

        // Map deprecated aliases
        if (CalendarAliases.TryGetValue(id, out var canonical))
            id = canonical;

        // Validate against known calendar list
        if (!ValidCalendarIds.Contains(id))
        {
            throw StandardLibrary.ThrowRangeError($"invalid calendar identifier: '{id}'");
        }

        return id;
    }

    /// <summary>
    /// ASCII-only lowercase. Does not perform Unicode case folding.
    /// Per Temporal spec, only A-Z are lowercased.
    /// </summary>
    private static string AsciiLowercase(string s)
    {
        var hasUpper = false;
        foreach (var c in s)
        {
            if (c is >= 'A' and <= 'Z')
            {
                hasUpper = true;
                break;
            }
        }

        if (!hasUpper)
            return s;

        return string.Create(s.Length, s, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                span[i] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
            }
        });
    }

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
        _ = CreateTemporalObject(realm);
        if (_prototypeCache.TryGetValue(realm, out prototypes))
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

        // Temporal.Now.plainDateISO(timeZone)
        var plainDateISOFn = CreateFunction(realm, "plainDateISO", 0, (_, args) =>
        {
            var tzId = ResolveNowTimeZone(args, realm);
            var date = JsTemporalPlainDate.Today(FindTimeZone(tzId));
            return WrapPlainDate(date, realm, prototypes.PlainDatePrototype);
        });
        now.DefineProperty("plainDateISO",
            new PropertyDescriptor { Value = plainDateISOFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.plainTimeISO(timeZone)
        var plainTimeISOFn = CreateFunction(realm, "plainTimeISO", 0, (_, args) =>
        {
            var tzId = ResolveNowTimeZone(args, realm);
            var tz = FindTimeZone(tzId);
            var now2 = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var time = new JsTemporalPlainTime(now2.Hour, now2.Minute, now2.Second, now2.Millisecond, now2.Microsecond, 0);
            return WrapPlainTime(time, realm, prototypes.PlainTimePrototype);
        });
        now.DefineProperty("plainTimeISO",
            new PropertyDescriptor { Value = plainTimeISOFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.plainDateTimeISO(timeZone)
        var plainDateTimeISOFn = CreateFunction(realm, "plainDateTimeISO", 0, (_, args) =>
        {
            var tzId = ResolveNowTimeZone(args, realm);
            var tz = FindTimeZone(tzId);
            var now2 = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var dt = new JsTemporalPlainDateTime(now2.Year, now2.Month, now2.Day,
                now2.Hour, now2.Minute, now2.Second, now2.Millisecond, now2.Microsecond, 0);
            return WrapPlainDateTime(dt, realm, prototypes.PlainDateTimePrototype);
        });
        now.DefineProperty("plainDateTimeISO",
            new PropertyDescriptor { Value = plainDateTimeISOFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.zonedDateTimeISO(timeZone)
        var zonedDateTimeISOFn = CreateFunction(realm, "zonedDateTimeISO", 0, (_, args) =>
        {
            var tzId = ResolveNowTimeZone(args, realm);
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
            var options = GetTemporalRoundingOptions(
                args.GetArgument(0),
                realm,
                "Temporal.Instant.prototype.round",
                InstantRoundingIncrements,
                allowMaxIncrement: true);

            var unitNanoseconds = GetUnitNanoseconds(options.SmallestUnit);
            var incrementNanoseconds = new BigInteger(unitNanoseconds) * options.Increment;
            var rounded = RoundToIncrement(instant.EpochNanoseconds, incrementNanoseconds, options.RoundingMode, treatNegativeAsPositive: true);
            return WrapInstant(new JsTemporalInstant(rounded), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "toZonedDateTimeISO", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var timeZoneId = ToTemporalTimeZoneSlot(args.GetArgument(0), realm);
            var zdt = new JsTemporalZonedDateTime(instant, timeZoneId);
            return WrapZonedDateTime(zdt, realm, prototypes.ZonedDateTimePrototype);
        });

        // Constructor - per spec, Temporal.Instant(epochNanoseconds) requires a BigInt
        var ctor = new HostFunction((_, _) => JsValue.Undefined, realm)
        { IsConstructor = true };
        ctor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Temporal.Instant cannot be called without 'new'", realm: realm);
            }

            var epochNanoseconds = args.GetArgument(0);

            // Per spec: ToBigInt(epochNanoseconds) - converts BigInt, string, boolean
            JsBigInt bigInt;
            try
            {
                bigInt = StandardLibrary.ToBigInt(epochNanoseconds, realmState: realm);
            }
            catch (ThrowSignal)
            {
                throw;
            }
            catch
            {
                throw StandardLibrary.ThrowTypeError("Temporal.Instant requires a BigInt argument", realm: realm);
            }

            if (bigInt.Value < InstantMinEpochNanoseconds || bigInt.Value > InstantMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("Temporal.Instant: epoch nanoseconds out of range", realm: realm);
            }

            var instant = new JsTemporalInstant(bigInt.Value);
            return ApplyNewTargetPrototype(WrapInstant(instant, realm, prototype), newTarget, ctor, prototype);
        });
        ctor.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        ctor.DefineProperty("name",
            new PropertyDescriptor { Value = "Instant", Writable = false, Enumerable = false, Configurable = true });

        ctor.DefineProperty("prototype",
            new PropertyDescriptor { Value = prototype, Writable = false, Enumerable = false, Configurable = false });
        prototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = ctor, Writable = true, Enumerable = false, Configurable = true });

        // Static methods
        var fromEpochMilliseconds = CreateFunction(realm, "fromEpochMilliseconds", 1, (_, args) =>
        {
            var ms = JsOps.ToNumber(args.GetArgument(0));
            // Per spec: If epochMilliseconds is not an integral Number, throw a RangeError
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms != Math.Truncate(ms))
            {
                throw StandardLibrary.ThrowRangeError("Temporal.Instant.fromEpochMilliseconds requires an integral number", realm: realm);
            }
            // Validate range: ±8.64e15 milliseconds (same as Date limits)
            if (ms < -8.64e15 || ms > 8.64e15)
            {
                throw StandardLibrary.ThrowRangeError("Temporal.Instant.fromEpochMilliseconds: value out of range", realm: realm);
            }
            var instant = JsTemporalInstant.FromEpochMilliseconds((long)ms);
            return WrapInstant(instant, realm, prototype);
        });
        ctor.DefineProperty("fromEpochMilliseconds",
            new PropertyDescriptor { Value = fromEpochMilliseconds, Writable = true, Enumerable = false, Configurable = true });

        var fromEpochNanoseconds = CreateFunction(realm, "fromEpochNanoseconds", 1, (_, args) =>
        {
            var arg = args.GetArgument(0);
            // Per spec: the argument must be a BigInt
            if (!arg.TryGetBigInt(out var bigInt))
            {
                throw StandardLibrary.ThrowTypeError("Temporal.Instant.fromEpochNanoseconds requires a BigInt argument", realm: realm);
            }
            var nanos = bigInt.Value;
            // Validate range: ±8.64e21 nanoseconds
            if (nanos < InstantMinEpochNanoseconds || nanos > InstantMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("Temporal.Instant.fromEpochNanoseconds: value out of range", realm: realm);
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
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, args) =>
        {
            var duration = GetDuration(thisValue);
            var options = args.GetArgument(0);
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.Duration.prototype.toString");
            var (precision, roundingMode) = GetToStringPrecisionOptions(optionsObj, realm);

            // Duration doesn't support "minute" smallestUnit
            if (precision.FractionalDigits == -2)
            {
                throw StandardLibrary.ThrowRangeError(
                    "\"minute\" is not a valid value for smallestUnit in toString", realm: realm);
            }

            return new JsValue(FormatDurationToString(duration, precision, roundingMode));
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

            // Per spec: argument must be an object
            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                throw StandardLibrary.ThrowTypeError("with() argument must be an object", realm: realm);
            }

            // Per spec: ToTemporalPartialDurationRecord — read properties in alphabetical order
            // and apply ToIntegerIfIntegral to each; at least one must be present
            var any = false;

            var days = ReadDurationField(accessor, "days", duration.Days, ref any, realm);
            var hours = ReadDurationField(accessor, "hours", duration.Hours, ref any, realm);
            var microseconds = ReadDurationField(accessor, "microseconds", duration.Microseconds, ref any, realm);
            var milliseconds = ReadDurationField(accessor, "milliseconds", duration.Milliseconds, ref any, realm);
            var minutes = ReadDurationField(accessor, "minutes", duration.Minutes, ref any, realm);
            var months = ReadDurationField(accessor, "months", duration.Months, ref any, realm);
            var nanoseconds = ReadDurationField(accessor, "nanoseconds", duration.Nanoseconds, ref any, realm);
            var seconds = ReadDurationField(accessor, "seconds", duration.Seconds, ref any, realm);
            var weeks = ReadDurationField(accessor, "weeks", duration.Weeks, ref any, realm);
            var years = ReadDurationField(accessor, "years", duration.Years, ref any, realm);

            if (!any)
            {
                throw StandardLibrary.ThrowTypeError("with() argument must have at least one duration property", realm: realm);
            }

            // CreateTemporalDuration validates sign consistency and range
            RejectDurationSign(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds, realm);
            if (!IsValidDuration(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds))
            {
                throw StandardLibrary.ThrowRangeError("Duration value is out of range", realm: realm);
            }

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
            var divisor = smallestUnit switch
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
        var ctor = new HostFunction((_, _) => JsValue.Undefined, realm)
        { IsConstructor = true };
        ctor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Temporal.Duration cannot be called without 'new'", realm: realm);
            }

            var years = GetDurationArg(args, 0);
            var months = GetDurationArg(args, 1);
            var weeks = GetDurationArg(args, 2);
            var days = GetDurationArg(args, 3);
            var hours = GetDurationArg(args, 4);
            var minutes = GetDurationArg(args, 5);
            var seconds = GetDurationArg(args, 6);
            var milliseconds = GetDurationArg(args, 7);
            var microseconds = GetDurationArg(args, 8);
            var nanoseconds = GetDurationArg(args, 9);

            // Per spec: reject mixed signs
            RejectDurationSign(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds, realm);

            // Per spec: IsValidDuration check - reject if balanced values exceed limits
            if (!IsValidDuration(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds))
            {
                throw StandardLibrary.ThrowRangeError("Duration value is out of range", realm: realm);
            }

            var duration = new JsTemporalDuration(years, months, weeks, days, hours, minutes, seconds,
                milliseconds, microseconds, nanoseconds);

            return ApplyNewTargetPrototype(WrapDuration(duration, realm, prototype), newTarget, ctor, prototype);
        });
        ctor.DefineProperty("length",
            new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
        ctor.DefineProperty("name",
            new PropertyDescriptor { Value = "Duration", Writable = false, Enumerable = false, Configurable = true });

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
        AddPrototypeGetter(prototype, realm, "yearOfWeek", tv => new JsValue(GetPlainDate(tv).YearOfWeek));
        AddPrototypeGetter(prototype, realm, "daysInMonth", tv => new JsValue(GetPlainDate(tv).DaysInMonth));
        AddPrototypeGetter(prototype, realm, "daysInYear", tv => new JsValue(GetPlainDate(tv).DaysInYear));
        AddPrototypeGetter(prototype, realm, "monthsInYear", tv => new JsValue(GetPlainDate(tv).MonthsInYear));
        AddPrototypeGetter(prototype, realm, "inLeapYear", tv => new JsValue(GetPlainDate(tv).InLeapYear));
        AddPrototypeGetter(prototype, realm, "calendarId", tv => new JsValue(GetPlainDate(tv).Calendar));
        AddPrototypeGetter(prototype, realm, "daysInWeek", tv => { GetPlainDate(tv); return new JsValue(7); }); // ISO 8601 always has 7 days per week
        AddPrototypeGetter(prototype, realm, "era", tv => { GetPlainDate(tv); return JsValue.Undefined; }); // ISO 8601 calendar has no era
        AddPrototypeGetter(prototype, realm, "eraYear", tv => { GetPlainDate(tv); return JsValue.Undefined; }); // ISO 8601 calendar has no era

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var options = args.GetArgument(0);
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDate.prototype.toString");
            var showCalendar = GetTemporalShowCalendarNameOption(optionsObj, realm);

            if (string.Equals(showCalendar, "always", StringComparison.Ordinal))
            {
                return new JsValue(date.ToStringWithCalendar());
            }
            if (string.Equals(showCalendar, "critical", StringComparison.Ordinal))
            {
                return new JsValue(date.ToStringWithCalendar(critical: true));
            }
            if (string.Equals(showCalendar, "never", StringComparison.Ordinal))
            {
                return new JsValue(date.ToStringBasic());
            }
            return new JsValue(date.ToString());
        });

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
                throw StandardLibrary.ThrowTypeError("Temporal.PlainDate.prototype.with requires an object argument", realm: realm);
            }

            // Validate options (second argument)
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDate.prototype.with");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            var year = accessor.TryGetProperty("year", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : date.Year;
            var month = accessor.TryGetProperty("month", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : date.Month;
            var day = accessor.TryGetProperty("day", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : date.Day;

            if (string.Equals(overflow, "constrain", StringComparison.Ordinal))
            {
                (year, month, day) = ConstrainISODate(year, month, day);
            }
            else
            {
                RejectISODate(year, month, day, realm);
            }

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
            var time = new JsTemporalPlainTime(0, 0, 0, 0, 0, 0);

            if (arg.TryGetObject<IJsPropertyAccessor>(out var accessor))
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
        var ctor = new HostFunction((_, _) => JsValue.Undefined, realm)
        { IsConstructor = true };
        ctor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Temporal.PlainDate cannot be called without 'new'", realm: realm);
            }

            var year = ToIntegerWithRangeCheck(args.GetArgument(0), "year", realm);
            var month = ToIntegerWithRangeCheck(args.GetArgument(1), "month", realm);
            var day = ToIntegerWithRangeCheck(args.GetArgument(2), "day", realm);
            var calendarArg = args.Count > 3 ? args[3] : JsValue.Undefined;
            var calendar = ToTemporalCalendarIdentifier(calendarArg);

            RejectISODate(year, month, day, realm);
            var date = new JsTemporalPlainDate(year, month, day, calendar);
            return ApplyNewTargetPrototype(WrapPlainDate(date, realm, prototype), newTarget, ctor, prototype);
        });
        ctor.DefineProperty("length",
            new PropertyDescriptor { Value = 3d, Writable = false, Enumerable = false, Configurable = true });
        ctor.DefineProperty("name",
            new PropertyDescriptor { Value = "PlainDate", Writable = false, Enumerable = false, Configurable = true });

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
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var options = args.GetArgument(0);
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainTime.prototype.toString");
            var (precision, roundingMode) = GetToStringPrecisionOptions(optionsObj, realm);
            return new JsValue(RoundAndFormatPlainTime(time, precision, roundingMode));
        });

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
                throw StandardLibrary.ThrowTypeError("Temporal.PlainTime.prototype.with requires an object argument", realm: realm);
            }

            // Validate options (second argument)
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainTime.prototype.with");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            var hour = accessor.TryGetProperty("hour", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Hour;
            var minute = accessor.TryGetProperty("minute", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Minute;
            var second = accessor.TryGetProperty("second", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Second;
            var millisecond = accessor.TryGetProperty("millisecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Millisecond;
            var microsecond = accessor.TryGetProperty("microsecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Microsecond;
            var nanosecond = accessor.TryGetProperty("nanosecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : time.Nanosecond;

            if (string.Equals(overflow, "constrain", StringComparison.Ordinal))
            {
                hour = ConstrainTimeComponent(hour, 0, 23);
                minute = ConstrainTimeComponent(minute, 0, 59);
                second = ConstrainTimeComponent(second, 0, 59);
                millisecond = ConstrainTimeComponent(millisecond, 0, 999);
                microsecond = ConstrainTimeComponent(microsecond, 0, 999);
                nanosecond = ConstrainTimeComponent(nanosecond, 0, 999);
            }
            else
            {
                // reject
                RejectISOTime(hour, minute, second, millisecond, microsecond, nanosecond, realm);
            }

            return WrapPlainTime(new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "round", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var options = GetTemporalRoundingOptions(
                args.GetArgument(0),
                realm,
                "Temporal.PlainTime.prototype.round",
                PlainTimeRoundingIncrements,
                allowMaxIncrement: false);

            var rounded = RoundPlainTime(time, options);
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
        var ctor = new HostFunction((_, _) => JsValue.Undefined, realm)
        { IsConstructor = true };
        ctor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Temporal.PlainTime cannot be called without 'new'", realm: realm);
            }

            var hour = ToIntegerOrDefault(args, 0, "hour", realm);
            var minute = ToIntegerOrDefault(args, 1, "minute", realm);
            var second = ToIntegerOrDefault(args, 2, "second", realm);
            var millisecond = ToIntegerOrDefault(args, 3, "millisecond", realm);
            var microsecond = ToIntegerOrDefault(args, 4, "microsecond", realm);
            var nanosecond = ToIntegerOrDefault(args, 5, "nanosecond", realm);

            RejectTemporalTimeRange(hour, minute, second, millisecond, microsecond, nanosecond, realm);
            var time = new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond);
            return ApplyNewTargetPrototype(WrapPlainTime(time, realm, prototype), newTarget, ctor, prototype);
        });
        ctor.DefineProperty("length",
            new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
        ctor.DefineProperty("name",
            new PropertyDescriptor { Value = "PlainTime", Writable = false, Enumerable = false, Configurable = true });

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
        AddPrototypeGetter(prototype, realm, "yearOfWeek", tv => new JsValue(GetPlainDateTime(tv).YearOfWeek));
        AddPrototypeGetter(prototype, realm, "daysInMonth", tv => new JsValue(GetPlainDateTime(tv).DaysInMonth));
        AddPrototypeGetter(prototype, realm, "daysInYear", tv => new JsValue(GetPlainDateTime(tv).DaysInYear));
        AddPrototypeGetter(prototype, realm, "monthsInYear", tv => new JsValue(GetPlainDateTime(tv).MonthsInYear));
        AddPrototypeGetter(prototype, realm, "inLeapYear", tv => new JsValue(GetPlainDateTime(tv).InLeapYear));
        AddPrototypeGetter(prototype, realm, "calendarId", tv => new JsValue(GetPlainDateTime(tv).Calendar));
        AddPrototypeGetter(prototype, realm, "daysInWeek", tv => { GetPlainDateTime(tv); return new JsValue(7); });
        AddPrototypeGetter(prototype, realm, "era", tv => { GetPlainDateTime(tv); return JsValue.Undefined; });
        AddPrototypeGetter(prototype, realm, "eraYear", tv => { GetPlainDateTime(tv); return JsValue.Undefined; });

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var options = args.GetArgument(0);

            // Per spec: GetOptionsObject throws TypeError if options is not undefined and not an object
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDateTime.prototype.toString");

            // Per spec, options must be accessed in alphabetical order:
            // calendarName, fractionalSecondDigits, roundingMode, smallestUnit
            var showCalendar = GetTemporalShowCalendarNameOption(optionsObj, realm);
            var (precision, roundingMode) = GetToStringPrecisionOptions(optionsObj, realm);

            // Round the datetime
            var (roundedDt, fracDigits) = RoundPlainDateTimeForToString(dt, precision, roundingMode, realm);

            // Format the time part
            var timePart = FormatTimeToString(
                roundedDt.Hour, roundedDt.Minute, roundedDt.Second,
                roundedDt.Millisecond, roundedDt.Microsecond, roundedDt.Nanosecond,
                fracDigits);

            // Format the date part using proper year formatting (6-digit for extended years)
            var datePart = roundedDt.Date.ToStringBasic();
            var result = $"{datePart}T{timePart}";

            // Append calendar if needed
            if (string.Equals(showCalendar, "always", StringComparison.Ordinal))
            {
                result += $"[u-ca={roundedDt.Calendar}]";
            }
            else if (string.Equals(showCalendar, "critical", StringComparison.Ordinal))
            {
                result += $"[!u-ca={roundedDt.Calendar}]";
            }
            else if (string.Equals(showCalendar, "auto", StringComparison.Ordinal) &&
                     !string.Equals(roundedDt.Calendar, "iso8601", StringComparison.Ordinal))
            {
                result += $"[u-ca={roundedDt.Calendar}]";
            }

            return new JsValue(result);
        });

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

            if (tzArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
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
                throw StandardLibrary.ThrowTypeError("Temporal.PlainDateTime.prototype.with requires an object argument", realm: realm);
            }

            // Validate options (second argument)
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDateTime.prototype.with");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            var year = accessor.TryGetProperty("year", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Year;
            var month = accessor.TryGetProperty("month", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Month;
            var day = accessor.TryGetProperty("day", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Day;
            var hour = accessor.TryGetProperty("hour", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Hour;
            var minute = accessor.TryGetProperty("minute", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Minute;
            var second = accessor.TryGetProperty("second", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Second;
            var millisecond = accessor.TryGetProperty("millisecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Millisecond;
            var microsecond = accessor.TryGetProperty("microsecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Microsecond;
            var nanosecond = accessor.TryGetProperty("nanosecond", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : dt.Nanosecond;

            if (string.Equals(overflow, "constrain", StringComparison.Ordinal))
            {
                (year, month, day) = ConstrainISODate(year, month, day);
                hour = ConstrainTimeComponent(hour, 0, 23);
                minute = ConstrainTimeComponent(minute, 0, 59);
                second = ConstrainTimeComponent(second, 0, 59);
                millisecond = ConstrainTimeComponent(millisecond, 0, 999);
                microsecond = ConstrainTimeComponent(microsecond, 0, 999);
                nanosecond = ConstrainTimeComponent(nanosecond, 0, 999);
            }
            else
            {
                RejectISODate(year, month, day, realm);
                RejectISOTime(hour, minute, second, millisecond, microsecond, nanosecond, realm);
            }

            return WrapPlainDateTime(new JsTemporalPlainDateTime(year, month, day, hour, minute, second, millisecond, microsecond, nanosecond, dt.Calendar), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "round", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var options = GetTemporalRoundingOptions(
                args.GetArgument(0),
                realm,
                "Temporal.PlainDateTime.prototype.round",
                PlainDateTimeRoundingIncrements,
                allowMaxIncrement: false);

            var rounded = RoundPlainDateTime(dt, options, realm);
            return WrapPlainDateTime(rounded, realm, prototype);
        });

        // Constructor
        var ctor = new HostFunction((_, _) => JsValue.Undefined, realm)
        { IsConstructor = true };
        ctor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Temporal.PlainDateTime cannot be called without 'new'", realm: realm);
            }

            var year = ToIntegerWithRangeCheck(args.GetArgument(0), "year", realm);
            var month = ToIntegerWithRangeCheck(args.GetArgument(1), "month", realm);
            var day = ToIntegerWithRangeCheck(args.GetArgument(2), "day", realm);
            var hour = ToIntegerOrDefault(args, 3, "hour", realm);
            var minute = ToIntegerOrDefault(args, 4, "minute", realm);
            var second = ToIntegerOrDefault(args, 5, "second", realm);
            var millisecond = ToIntegerOrDefault(args, 6, "millisecond", realm);
            var microsecond = ToIntegerOrDefault(args, 7, "microsecond", realm);
            var nanosecond = ToIntegerOrDefault(args, 8, "nanosecond", realm);
            var calendarArg = args.Count > 9 ? args[9] : JsValue.Undefined;
            var calendar = ToTemporalCalendarIdentifier(calendarArg);

            RejectISODate(year, month, day, realm);
            RejectTemporalTimeRange(hour, minute, second, millisecond, microsecond, nanosecond, realm);
            var dt = new JsTemporalPlainDateTime(year, month, day, hour, minute, second,
                millisecond, microsecond, nanosecond, calendar);
            return ApplyNewTargetPrototype(WrapPlainDateTime(dt, realm, prototype), newTarget, ctor, prototype);
        });
        ctor.DefineProperty("length",
            new PropertyDescriptor { Value = 3d, Writable = false, Enumerable = false, Configurable = true });
        ctor.DefineProperty("name",
            new PropertyDescriptor { Value = "PlainDateTime", Writable = false, Enumerable = false, Configurable = true });

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
        AddPrototypeGetter(prototype, realm, "epochNanoseconds", tv =>
        {
            var zdt = GetZonedDateTime(tv);
            return JsValue.FromObjectUnsafe(new JsBigInt(zdt.Instant.EpochNanoseconds));
        });
        AddPrototypeGetter(prototype, realm, "epochSeconds", tv => new JsValue((double)GetZonedDateTime(tv).EpochSeconds));
        AddPrototypeGetter(prototype, realm, "monthCode", tv => new JsValue(GetZonedDateTime(tv).MonthCode));
        AddPrototypeGetter(prototype, realm, "dayOfWeek", tv => new JsValue(GetZonedDateTime(tv).DayOfWeek));
        AddPrototypeGetter(prototype, realm, "dayOfYear", tv => new JsValue(GetZonedDateTime(tv).DayOfYear));
        AddPrototypeGetter(prototype, realm, "weekOfYear", tv => new JsValue(GetZonedDateTime(tv).WeekOfYear));
        AddPrototypeGetter(prototype, realm, "yearOfWeek", tv => new JsValue(GetZonedDateTime(tv).YearOfWeek));
        AddPrototypeGetter(prototype, realm, "daysInMonth", tv => new JsValue(GetZonedDateTime(tv).DaysInMonth));
        AddPrototypeGetter(prototype, realm, "daysInYear", tv => new JsValue(GetZonedDateTime(tv).DaysInYear));
        AddPrototypeGetter(prototype, realm, "inLeapYear", tv => new JsValue(GetZonedDateTime(tv).InLeapYear));
        AddPrototypeGetter(prototype, realm, "timeZoneId", tv => new JsValue(GetZonedDateTime(tv).TimeZoneId));
        AddPrototypeGetter(prototype, realm, "offset", tv => new JsValue(GetZonedDateTime(tv).Offset));
        AddPrototypeGetter(prototype, realm, "calendarId", tv => new JsValue(GetZonedDateTime(tv).Calendar));
        AddPrototypeGetter(prototype, realm, "daysInWeek", tv => { GetZonedDateTime(tv); return new JsValue(7); });
        AddPrototypeGetter(prototype, realm, "monthsInYear", tv => { GetZonedDateTime(tv); return new JsValue(12); });
        AddPrototypeGetter(prototype, realm, "era", tv => { GetZonedDateTime(tv); return JsValue.Undefined; });
        AddPrototypeGetter(prototype, realm, "eraYear", tv => { GetZonedDateTime(tv); return JsValue.Undefined; });
        AddPrototypeGetter(prototype, realm, "offsetNanoseconds", tv =>
        {
            var zdt = GetZonedDateTime(tv);
            // Parse offset string like "+01:00" to nanoseconds
            var offset = zdt.Offset;
            var totalSeconds = ParseOffsetToSeconds(offset);
            return new JsValue((double)totalSeconds * 1_000_000_000L);
        });

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var options = args.GetArgument(0);
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.ZonedDateTime.prototype.toString");

            // Read options in strict alphabetical order per spec:
            // calendarName, fractionalSecondDigits, offset, roundingMode, smallestUnit, timeZoneName
            var showCalendar = GetTemporalShowCalendarNameOption(optionsObj, realm);
            var (precision, roundingMode) = GetToStringPrecisionOptionsWithInterleave(
                optionsObj, realm, "offset", ValidOffsetOptions, "auto",
                "timeZoneName", ValidTimeZoneNameOptions, "auto",
                out var showOffset, out var showTimeZone);

            // Round the instant according to precision
            var epochNanos = zdt.Instant.EpochNanoseconds;
            var incrementNanoseconds = new BigInteger(GetUnitNanoseconds(precision.SmallestUnit)) * precision.Increment;
            var rounded = RoundToIncrement(epochNanos, incrementNanoseconds, roundingMode);
            var roundedInstant = new JsTemporalInstant(rounded);
            var roundedZdt = new JsTemporalZonedDateTime(roundedInstant, zdt.TimeZoneId, zdt.Calendar);

            // Build output
            var datePart = $"{roundedZdt.Year:D4}-{roundedZdt.Month:D2}-{roundedZdt.Day:D2}";
            var timePart = FormatTimeToString(
                roundedZdt.Hour, roundedZdt.Minute, roundedZdt.Second,
                roundedZdt.Millisecond, roundedZdt.Microsecond, roundedZdt.Nanosecond,
                precision.FractionalDigits);

            var result = $"{datePart}T{timePart}";

            if (!string.Equals(showOffset, "never", StringComparison.Ordinal))
            {
                result += roundedZdt.Offset;
            }

            if (string.Equals(showTimeZone, "auto", StringComparison.Ordinal))
            {
                result += $"[{roundedZdt.TimeZoneId}]";
            }
            else if (string.Equals(showTimeZone, "critical", StringComparison.Ordinal))
            {
                result += $"[!{roundedZdt.TimeZoneId}]";
            }
            // "never" — omit timezone

            if (string.Equals(showCalendar, "always", StringComparison.Ordinal))
            {
                result += $"[u-ca={roundedZdt.Calendar}]";
            }
            else if (string.Equals(showCalendar, "critical", StringComparison.Ordinal))
            {
                result += $"[!u-ca={roundedZdt.Calendar}]";
            }
            else if (string.Equals(showCalendar, "auto", StringComparison.Ordinal) &&
                     !string.Equals(roundedZdt.Calendar, "iso8601", StringComparison.Ordinal))
            {
                result += $"[u-ca={roundedZdt.Calendar}]";
            }

            return new JsValue(result);
        });

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
            var options = GetTemporalRoundingOptions(
                args.GetArgument(0),
                realm,
                "Temporal.ZonedDateTime.prototype.round",
                PlainDateTimeRoundingIncrements,
                allowMaxIncrement: false);

            if (options.SmallestUnit == "day")
            {
                var roundedInstant = RoundZonedDateTimeToDay(zdt, options, realm);
                var roundedZdtDay = new JsTemporalZonedDateTime(roundedInstant, zdt.TimeZoneId, zdt.Calendar);
                return WrapZonedDateTime(roundedZdtDay, realm, prototype);
            }

            var local = GetLocalPlainDateTime(zdt, realm);

            var roundedLocal = RoundPlainDateTime(local, options, realm);
            var offset = zdt.FixedOffset ?? ResolveTimeZoneOffset(CreateTimeZoneLocalDateTime(roundedLocal), zdt.TimeZone, zdt.FixedOffset);
            var offsetNanoseconds = new BigInteger(offset.Ticks) * 100;
            var localEpochNanoseconds = ToEpochNanoseconds(roundedLocal);
            var roundedInstantNanoseconds = localEpochNanoseconds - offsetNanoseconds;
            if (roundedInstantNanoseconds < InstantMinEpochNanoseconds ||
                roundedInstantNanoseconds > InstantMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
            }

            var roundedZdt = new JsTemporalZonedDateTime(
                JsTemporalInstant.FromEpochNanoseconds(roundedInstantNanoseconds),
                zdt.TimeZoneId,
                zdt.Calendar);
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
            var timeZoneId = ToTemporalTimeZoneSlot(args.GetArgument(0), realm);
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

        AddPrototypeGetter(prototype, realm, "hoursInDay", tv =>
        {
            var zdt = GetZonedDateTime(tv);

            // Compute start-of-day for this day
            var todayStart = new JsTemporalZonedDateTime(
                zdt.Year, zdt.Month, zdt.Day, 0, 0, 0, 0, 0, 0,
                zdt.TimeZoneId, zdt.Calendar);

            // Compute start-of-day for next day
            var nextYear = zdt.Year;
            var nextMonth = zdt.Month;
            var nextDay = zdt.Day + 1;
            var maxDay = IsoCalendarHelpers.DaysInMonth(nextYear, nextMonth);
            if (nextDay > maxDay)
            {
                nextDay = 1;
                nextMonth++;
                if (nextMonth > 12)
                {
                    nextMonth = 1;
                    nextYear++;
                }
            }
            var nextDayStart = new JsTemporalZonedDateTime(
                nextYear, nextMonth, nextDay, 0, 0, 0, 0, 0, 0,
                zdt.TimeZoneId, zdt.Calendar);

            var diffNanos = nextDayStart.Instant.EpochNanoseconds - todayStart.Instant.EpochNanoseconds;
            var hours = (double)diffNanos / 3_600_000_000_000.0;
            return new JsValue(hours);
        });

        // Constructor
        var ctor = new HostFunction((_, _) => JsValue.Undefined, realm)
        { IsConstructor = true };
        ctor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Temporal.ZonedDateTime cannot be called without 'new'", realm: realm);
            }

            var epochNanoseconds = args.GetArgument(0);
            var timeZoneArg = args.GetArgument(1);
            var calendarArg = args.Count > 2 ? args[2] : JsValue.Undefined;

            var timeZoneId = ToTemporalTimeZoneSlot(timeZoneArg, realm);
            var calendar = ToTemporalCalendarIdentifier(calendarArg);

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
            return ApplyNewTargetPrototype(WrapZonedDateTime(zdt, realm, prototype), newTarget, ctor, prototype);
        });
        ctor.DefineProperty("length",
            new PropertyDescriptor { Value = 2d, Writable = false, Enumerable = false, Configurable = true });
        ctor.DefineProperty("name",
            new PropertyDescriptor { Value = "ZonedDateTime", Writable = false, Enumerable = false, Configurable = true });

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
        AddPrototypeGetter(prototype, realm, "era", tv => { GetPlainYearMonth(tv); return JsValue.Undefined; });
        AddPrototypeGetter(prototype, realm, "eraYear", tv => { GetPlainYearMonth(tv); return JsValue.Undefined; });

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var options = args.GetArgument(0);
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainYearMonth.prototype.toString");
            var showCalendar = GetTemporalShowCalendarNameOption(optionsObj, realm);

            if (string.Equals(showCalendar, "always", StringComparison.Ordinal))
            {
                return new JsValue(ym.ToStringWithCalendar());
            }
            if (string.Equals(showCalendar, "critical", StringComparison.Ordinal))
            {
                return new JsValue(ym.ToStringWithCalendar(critical: true));
            }
            if (string.Equals(showCalendar, "never", StringComparison.Ordinal))
            {
                return new JsValue(ym.ToStringBasic());
            }
            return new JsValue(ym.ToString());
        });

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
                throw StandardLibrary.ThrowTypeError("Temporal.PlainYearMonth.prototype.with requires an object argument", realm: realm);
            }

            // Validate options (second argument)
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainYearMonth.prototype.with");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            var year = accessor.TryGetProperty("year", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : ym.Year;
            var month = accessor.TryGetProperty("month", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : ym.Month;

            if (string.Equals(overflow, "constrain", StringComparison.Ordinal))
            {
                month = Math.Clamp(month, 1, 12);
            }
            else
            {
                if (month is < 1 or > 12)
                {
                    throw StandardLibrary.ThrowRangeError("Month value is out of range (1-12)", realm: realm);
                }
            }

            return WrapPlainYearMonth(new JsTemporalPlainYearMonth(year, month, ym.Calendar), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainDate", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var dayArg = args.GetArgument(0);

            // Per spec: argument must be an object
            if (!dayArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                throw StandardLibrary.ThrowTypeError("toPlainDate requires an object argument with a 'day' property", realm: realm);
            }

            // Must have 'day' property
            if (!accessor.TryGetProperty("day", out var dayValue) || dayValue.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("toPlainDate requires a 'day' property", realm: realm);
            }

            var dayNum = JsOps.ToNumber(dayValue);
            if (double.IsInfinity(dayNum) || double.IsNaN(dayNum))
            {
                throw StandardLibrary.ThrowRangeError("day value must be finite", realm: realm);
            }

            // Constrain day to valid range for the month
            var day = (int)dayNum;
            var maxDay = IsoCalendarHelpers.DaysInMonth(ym.Year, ym.Month);
            day = Math.Min(Math.Max(day, 1), maxDay);

            var date = new JsTemporalPlainDate(ym.Year, ym.Month, day, ym.Calendar);
            ValidatePlainDateRange(date, realm);
            return WrapPlainDate(date, realm, prototypes.PlainDatePrototype);
        });

        // Constructor
        var ctor = new HostFunction((_, _) => JsValue.Undefined, realm)
        { IsConstructor = true };
        ctor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Temporal.PlainYearMonth cannot be called without 'new'", realm: realm);
            }

            var year = (int)JsOps.ToNumber(args.GetArgument(0));
            var month = (int)JsOps.ToNumber(args.GetArgument(1));
            var calendarArg = args.Count > 2 ? args[2] : JsValue.Undefined;
            var calendar = ToTemporalCalendarIdentifier(calendarArg);

            var ym = new JsTemporalPlainYearMonth(year, month, calendar);
            return ApplyNewTargetPrototype(WrapPlainYearMonth(ym, realm, prototype), newTarget, ctor, prototype);
        });
        ctor.DefineProperty("length",
            new PropertyDescriptor { Value = 2d, Writable = false, Enumerable = false, Configurable = true });
        ctor.DefineProperty("name",
            new PropertyDescriptor { Value = "PlainYearMonth", Writable = false, Enumerable = false, Configurable = true });

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
        // Note: PlainMonthDay does NOT have month or year getters per spec — use monthCode instead
        AddPrototypeGetter(prototype, realm, "day", tv => new JsValue(GetPlainMonthDay(tv).Day));
        AddPrototypeGetter(prototype, realm, "monthCode", tv => new JsValue(GetPlainMonthDay(tv).MonthCode));
        AddPrototypeGetter(prototype, realm, "calendarId", tv => new JsValue(GetPlainMonthDay(tv).Calendar));

        // Prototype methods
        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, args) =>
        {
            var md = GetPlainMonthDay(thisValue);
            var options = args.GetArgument(0);
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainMonthDay.prototype.toString");
            var showCalendar = GetTemporalShowCalendarNameOption(optionsObj, realm);

            if (string.Equals(showCalendar, "always", StringComparison.Ordinal))
            {
                return new JsValue(md.ToStringWithCalendar());
            }
            if (string.Equals(showCalendar, "critical", StringComparison.Ordinal))
            {
                return new JsValue(md.ToStringWithCalendar(critical: true));
            }
            if (string.Equals(showCalendar, "never", StringComparison.Ordinal))
            {
                return new JsValue(md.ToStringBasic());
            }
            return new JsValue(md.ToString());
        });

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
                throw StandardLibrary.ThrowTypeError("Temporal.PlainMonthDay.prototype.with requires an object argument", realm: realm);
            }

            // Validate options (second argument)
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainMonthDay.prototype.with");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            var month = accessor.TryGetProperty("month", out var v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : md.Month;
            var day = accessor.TryGetProperty("day", out v) && !v.IsUndefined ? (int)JsOps.ToNumber(v) : md.Day;

            if (string.Equals(overflow, "constrain", StringComparison.Ordinal))
            {
                month = Math.Clamp(month, 1, 12);
                var maxDay = DateTime.DaysInMonth(md.ReferenceYear, month);
                day = Math.Clamp(day, 1, maxDay);
            }
            else
            {
                if (month is < 1 or > 12)
                {
                    throw StandardLibrary.ThrowRangeError("Month value is out of range (1-12)", realm: realm);
                }

                var maxDay = DateTime.DaysInMonth(md.ReferenceYear, month);
                if (day < 1 || day > maxDay)
                {
                    throw StandardLibrary.ThrowRangeError("Day value is out of range", realm: realm);
                }
            }

            return WrapPlainMonthDay(new JsTemporalPlainMonthDay(month, day, md.Calendar), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainDate", 1, (thisValue, args) =>
        {
            var md = GetPlainMonthDay(thisValue);
            var yearArg = args.GetArgument(0);

            // Per spec: argument must be an object
            if (!yearArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                throw StandardLibrary.ThrowTypeError("toPlainDate requires an object argument with a 'year' property", realm: realm);
            }

            // Must have 'year' property
            if (!accessor.TryGetProperty("year", out var yearValue) || yearValue.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("toPlainDate requires a 'year' property", realm: realm);
            }

            var yearNum = JsOps.ToNumber(yearValue);
            if (double.IsInfinity(yearNum) || double.IsNaN(yearNum))
            {
                throw StandardLibrary.ThrowRangeError("year value must be finite", realm: realm);
            }

            var year = (int)yearNum;

            // Constrain day to valid range for the target year/month
            var maxDay = IsoCalendarHelpers.DaysInMonth(year, md.Month);
            var day = Math.Min(md.Day, maxDay);

            var date = new JsTemporalPlainDate(year, md.Month, day, md.Calendar);
            ValidatePlainDateRange(date, realm);
            return WrapPlainDate(date, realm, prototypes.PlainDatePrototype);
        });

        // Constructor
        var ctor = new HostFunction((_, _) => JsValue.Undefined, realm)
        { IsConstructor = true };
        ctor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Temporal.PlainMonthDay cannot be called without 'new'", realm: realm);
            }

            var month = (int)JsOps.ToNumber(args.GetArgument(0));
            var day = (int)JsOps.ToNumber(args.GetArgument(1));
            var calendarArg = args.Count > 2 ? args[2] : JsValue.Undefined;
            var calendar = ToTemporalCalendarIdentifier(calendarArg);

            var md = new JsTemporalPlainMonthDay(month, day, calendar);
            return ApplyNewTargetPrototype(WrapPlainMonthDay(md, realm, prototype), newTarget, ctor, prototype);
        });
        ctor.DefineProperty("length",
            new PropertyDescriptor { Value = 2d, Writable = false, Enumerable = false, Configurable = true });
        ctor.DefineProperty("name",
            new PropertyDescriptor { Value = "PlainMonthDay", Writable = false, Enumerable = false, Configurable = true });

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

    /// <summary>
    ///     Resolves the time zone argument for Temporal.Now methods.
    ///     Returns the local time zone ID if no argument provided.
    /// </summary>
    private static string ResolveNowTimeZone(IReadOnlyList<JsValue> args, RealmState realm)
    {
        if (args.Count == 0 || args[0].IsUndefined)
        {
            var tzId = TimeZoneInfo.Local.Id;
            if (OperatingSystem.IsWindows() && TimeZoneInfo.TryConvertWindowsIdToIanaId(tzId, out var ianaId))
            {
                tzId = ianaId;
            }
            return tzId;
        }

        var tzStr = ToTemporalTimeZoneSlot(args[0], realm);

        // Convert Windows timezone ID to IANA if needed
        if (OperatingSystem.IsWindows() && TimeZoneInfo.TryConvertWindowsIdToIanaId(tzStr, out var iana))
        {
            return iana;
        }
        return tzStr;
    }

    /// <summary>
    ///     Validates a JsValue as a timezone argument, type-checks it, parses ISO datetime strings,
    ///     and returns a canonicalized timezone identifier.
    /// </summary>
    private static string ToTemporalTimeZoneSlot(JsValue value, RealmState realm)
    {
        // Per spec: argument must be a string — non-string types throw TypeError
        if (!value.IsString)
        {
            throw StandardLibrary.ThrowTypeError("Expected a string for time zone argument", realm: realm);
        }

        var input = value.AsString();
        var tzStr = ToTemporalTimeZoneIdentifier(input, realm);

        // Canonicalize: use the timezone info's ID (handles case normalization like 'UtC' → 'UTC')
        var tzInfo = FindTimeZone(tzStr);
        return tzInfo.Id;
    }

    /// <summary>
    ///     Parses a string as a Temporal time zone identifier.
    ///     Handles ISO datetime strings by extracting the timezone portion,
    ///     and validates the result as a valid IANA name or UTC offset.
    /// </summary>
    private static string ToTemporalTimeZoneIdentifier(string input, RealmState realm)
    {
        if (string.IsNullOrEmpty(input))
        {
            throw StandardLibrary.ThrowRangeError("Invalid time zone identifier", realm: realm);
        }

        // Check if this looks like an ISO datetime string (digits before 'T')
        var tIdx = input.IndexOf('T');
        if (tIdx >= 4 && char.IsDigit(input[0]))
        {
            return ExtractTimeZoneFromISO(input, tIdx, realm);
        }

        // Not an ISO datetime string - validate directly as timezone identifier
        ValidateTimeZoneIdentifier(input, realm);
        return input;
    }

    /// <summary>
    ///     Extracts the timezone identifier from an ISO datetime string.
    ///     Priority: annotation [name] > inline offset (Z, +HH:MM) > bare datetime (error).
    /// </summary>
    private static string ExtractTimeZoneFromISO(string input, int tIdx, RealmState realm)
    {
        // Check for annotation [timezone]
        var bracketIdx = input.IndexOf('[');
        if (bracketIdx >= 0)
        {
            var closeBracket = input.IndexOf(']', bracketIdx);
            if (closeBracket > bracketIdx)
            {
                var annotation = input.Substring(bracketIdx + 1, closeBracket - bracketIdx - 1);

                // Skip calendar annotations like u-ca=iso8601
                if (!annotation.StartsWith("u-ca=", StringComparison.Ordinal))
                {
                    // This is a timezone annotation — validate it
                    if (annotation.Length > 0 && (annotation[0] == '+' || annotation[0] == '-'))
                    {
                        // It's an offset annotation — reject sub-minute precision
                        RejectSubMinuteOffset(annotation, realm);
                    }
                    ValidateTimeZoneIdentifier(annotation, realm);
                    return annotation;
                }
            }
        }

        // No annotation — extract offset from the time portion
        var afterT = input.Substring(tIdx + 1);

        // Remove any bracket annotations from the time portion
        var aBracket = afterT.IndexOf('[');
        if (aBracket >= 0)
        {
            afterT = afterT.Substring(0, aBracket);
        }

        // Check for Z at end
        if (afterT.EndsWith('Z') || afterT.EndsWith('z'))
        {
            return "UTC";
        }

        // Look for offset: scan from right for + or - followed by digits
        for (var i = afterT.Length - 1; i >= 2; i--)
        {
            if ((afterT[i] == '+' || afterT[i] == '-') && i + 1 < afterT.Length && char.IsDigit(afterT[i + 1]))
            {
                var offset = afterT.Substring(i);
                // Reject sub-minute precision
                RejectSubMinuteOffset(offset, realm);
                // Normalize to +HH:MM format
                offset = NormalizeUtcOffset(offset);
                return offset;
            }
        }

        // No offset found — bare datetime string
        throw StandardLibrary.ThrowRangeError("bare date-time string is not a time zone", realm: realm);
    }

    /// <summary>
    ///     Rejects UTC offset strings with sub-minute precision (seconds or fractional seconds).
    ///     Valid: +HH:MM, +HHMM. Invalid: +HH:MM:SS, +HH:MM:SS.sss.
    /// </summary>
    private static void RejectSubMinuteOffset(string offset, RealmState realm)
    {
        // Strip the sign
        var body = offset.TrimStart('+', '-');

        // Check colon-separated format: HH:MM[:SS[.fff]]
        if (body.Contains(':'))
        {
            var parts = body.Split(':');
            if (parts.Length >= 3)
            {
                // Has seconds component — sub-minute precision
                throw StandardLibrary.ThrowRangeError(
                    $"UTC offset with sub-minute precision is not a valid time zone: {offset}", realm: realm);
            }
            return;
        }

        // Check compact format: HHMM[SS[fff]]
        // Valid: 4 digits (HHMM). Invalid: 6+ digits (HHMMSS)
        if (body.Length > 4)
        {
            throw StandardLibrary.ThrowRangeError(
                $"UTC offset with sub-minute precision is not a valid time zone: {offset}", realm: realm);
        }
    }

    /// <summary>
    ///     Normalizes a UTC offset string to ±HH:MM format.
    ///     Handles both +HH:MM and +HHMM input formats.
    /// </summary>
    private static string NormalizeUtcOffset(string offset)
    {
        var sign = offset[0]; // + or -
        var body = offset.Substring(1);

        // Already in HH:MM format
        if (body.Contains(':'))
        {
            return offset;
        }

        // Compact HHMM format — insert colon
        if (body.Length == 4)
        {
            return $"{sign}{body[..2]}:{body[2..]}";
        }

        // Just HH — append :00
        if (body.Length == 2)
        {
            return $"{sign}{body}:00";
        }

        return offset;
    }

    /// <summary>
    ///     Validates that a string is a valid IANA time zone identifier or UTC offset.
    ///     This method validates already-extracted timezone identifiers (not raw ISO datetime strings).
    /// </summary>
    private static void ValidateTimeZoneIdentifier(string id, RealmState realm)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw StandardLibrary.ThrowRangeError("Invalid time zone identifier", realm: realm);
        }

        // Try to find the timezone — will throw if invalid
        try
        {
            FindTimeZone(id);
        }
        catch (TimeZoneNotFoundException)
        {
            throw StandardLibrary.ThrowRangeError($"Invalid time zone identifier: {id}", realm: realm);
        }
    }

    /// <summary>
    ///     Finds a TimeZoneInfo by IANA name, system ID, or UTC offset string.
    /// </summary>
    private static TimeZoneInfo FindTimeZone(string id)
    {
        if (string.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        // Handle UTC offset strings like "+01:00", "-07:00"
        if (id.Length >= 3 && (id[0] == '+' || id[0] == '-') && char.IsDigit(id[1]))
        {
            var normalized = NormalizeUtcOffset(id);
            var body = normalized.Substring(1);
            var parts = body.Split(':');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var hours) &&
                int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var minutes))
            {
                var sign = normalized[0] == '-' ? -1 : 1;
                var offset = new TimeSpan(sign * hours, sign * minutes, 0);
                return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
            }
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            // Try converting from IANA to Windows
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }
            throw;
        }
    }

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

    /// <summary>
    /// Validates that options is an object (not a primitive). Per Temporal spec GetOptionsObject.
    /// </summary>
    private static IJsPropertyAccessor? ValidateOptionsObject(JsValue options, RealmState realm, string method)
    {
        // If undefined, return null (options not provided)
        if (options.IsUndefined)
        {
            return null;
        }

        // If null or any other primitive, throw TypeError
        if (options.IsNull || !options.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            throw StandardLibrary.ThrowTypeError($"{method} requires options to be an object", realm: realm);
        }

        return accessor;
    }

    /// <summary>
    /// Gets the calendarName option from a validated options object.
    /// Returns "auto", "always", "never", or "critical".
    /// Throws RangeError for invalid values.
    /// </summary>
    private static string GetTemporalShowCalendarNameOption(IJsPropertyAccessor? optionsObj, RealmState realm)
    {
        if (optionsObj is null)
        {
            return "auto";
        }

        if (!optionsObj.TryGetProperty("calendarName", out var calendarNameVal) || calendarNameVal.IsUndefined)
        {
            return "auto";
        }

        var calendarName = JsOps.ToJsString(calendarNameVal);
        if (!string.Equals(calendarName, "auto", StringComparison.Ordinal) &&
            !string.Equals(calendarName, "always", StringComparison.Ordinal) &&
            !string.Equals(calendarName, "never", StringComparison.Ordinal) &&
            !string.Equals(calendarName, "critical", StringComparison.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError($"{calendarName} is an invalid value for calendarName option", realm: realm);
        }

        return calendarName;
    }

    /// <summary>
    /// Gets the overflow option from a validated options object.
    /// Returns "constrain" (default) or "reject".
    /// Throws RangeError for invalid values.
    /// </summary>
    private static string GetTemporalOverflowOption(IJsPropertyAccessor? optionsObj, RealmState realm)
    {
        if (optionsObj is null)
        {
            return "constrain";
        }

        if (!optionsObj.TryGetProperty("overflow", out var overflowVal) || overflowVal.IsUndefined)
        {
            return "constrain";
        }

        var overflow = JsOps.ToJsString(overflowVal);
        if (!string.Equals(overflow, "constrain", StringComparison.Ordinal) &&
            !string.Equals(overflow, "reject", StringComparison.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError($"{overflow} is an invalid value for overflow option", realm: realm);
        }

        return overflow;
    }

    /// <summary>
    /// Generic helper to read a string option from an options object.
    /// Returns defaultValue if the option is undefined or options is null.
    /// Throws RangeError if the value is not in the validValues set.
    /// </summary>
    private static string GetTemporalStringOption(
        IJsPropertyAccessor? optionsObj, string optionName,
        HashSet<string> validValues, string defaultValue, RealmState realm)
    {
        if (optionsObj is null)
        {
            return defaultValue;
        }

        if (!optionsObj.TryGetProperty(optionName, out var val) || val.IsUndefined)
        {
            return defaultValue;
        }

        var str = JsOps.ToJsString(val);
        if (!validValues.Contains(str))
        {
            throw StandardLibrary.ThrowRangeError(
                $"\"{str}\" is not a valid value for {optionName} option", realm: realm);
        }

        return str;
    }

    /// <summary>
    /// Constrains a time component value to its valid range.
    /// </summary>
    private static int ConstrainTimeComponent(int value, int min, int max)
    {
        return Math.Clamp(value, min, max);
    }

    /// <summary>
    /// Constrains a date component value to its valid range.
    /// For day, it clamps to 1..DaysInMonth(year, month).
    /// </summary>
    private static (int year, int month, int day) ConstrainISODate(int year, int month, int day)
    {
        month = Math.Clamp(month, 1, 12);
        var maxDay = DateTime.DaysInMonth(year, month);
        day = Math.Clamp(day, 1, maxDay);
        return (year, month, day);
    }

    private static double GetNumberArg(IReadOnlyList<JsValue> args, int index)
    {
        if (index >= args.Count || args[index].IsUndefined)
        {
            return 0;
        }

        return JsOps.ToNumber(args[index]);
    }

    /// <summary>
    /// Gets a Duration component argument and validates per the Temporal spec:
    /// - Must be a finite integer (no fractional, no Infinity, no NaN)
    /// </summary>
    private static double GetDurationArg(IReadOnlyList<JsValue> args, int index)
    {
        if (index >= args.Count || args[index].IsUndefined)
        {
            return 0;
        }

        var value = JsOps.ToNumber(args[index]);

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw StandardLibrary.ThrowRangeError("Duration field value is not finite");
        }

        if (value != Math.Truncate(value))
        {
            throw StandardLibrary.ThrowRangeError("Duration field value is not an integer");
        }

        // Per spec: ToIntegerIfIntegral converts -0 to +0
        return value == 0 ? 0 : value;
    }

    /// <summary>
    ///     Applies the correct prototype from newTarget for subclassing support.
    ///     Per spec: OrdinaryCreateFromConstructor uses newTarget.prototype when newTarget differs from the constructor.
    /// </summary>
    private static JsValue ApplyNewTargetPrototype(JsValue result, JsValue newTarget, HostFunction ctor, JsObject defaultPrototype)
    {
        // If newTarget is the constructor itself, prototype is already correct
        if (newTarget.TryGetObject<IJsCallable>(out var newTargetCallable) && !ReferenceEquals(newTargetCallable, ctor))
        {
            // Get newTarget.prototype
            if (newTargetCallable is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("prototype", out var protoVal) &&
                protoVal.TryGetObject<JsObject>(out var subclassPrototype))
            {
                // Set the prototype on the wrapped object to the subclass prototype
                if (result.TryGetObject<JsObject>(out var obj))
                {
                    obj.SetPrototype(subclassPrototype);
                }
            }
        }
        return result;
    }

    /// <summary>
    ///     Validates that all duration fields have the same sign (all positive, all negative, or all zero).
    ///     Per Temporal spec: mixed signs cause a RangeError.
    /// </summary>
    private static void RejectDurationSign(double years, double months, double weeks, double days,
        double hours, double minutes, double seconds, double milliseconds, double microseconds,
        double nanoseconds, RealmState? realm = null)
    {
        var hasPositive = false;
        var hasNegative = false;
        ReadOnlySpan<double> fields = [years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds];
        foreach (var field in fields)
        {
            if (field > 0) hasPositive = true;
            else if (field < 0) hasNegative = true;
        }
        if (hasPositive && hasNegative)
        {
            throw StandardLibrary.ThrowRangeError("Duration fields must not have mixed signs", realm: realm);
        }
    }

    /// <summary>
    ///     Per Temporal spec: IsValidDuration checks that the duration fields are within limits.
    ///     Normalizes to total nanoseconds, then checks totalSeconds against 2^53.
    /// </summary>
    private static bool IsValidDuration(double years, double months, double weeks, double days,
        double hours, double minutes, double seconds, double milliseconds, double microseconds, double nanoseconds)
    {
        const double maxYMW = 4294967296; // 2^32

        // 1. Years, months, weeks must be in range (-2^32, 2^32)
        if (Math.Abs(years) >= maxYMW || Math.Abs(months) >= maxYMW || Math.Abs(weeks) >= maxYMW)
        {
            return false;
        }

        // 2. NormalizeTimeDuration: compute total nanoseconds from all time fields
        // Use BigInteger to handle arbitrarily large values
        var totalNanoseconds =
            new BigInteger(days) * 86_400_000_000_000 +
            new BigInteger(hours) * 3_600_000_000_000 +
            new BigInteger(minutes) * 60_000_000_000 +
            new BigInteger(seconds) * 1_000_000_000 +
            new BigInteger(milliseconds) * 1_000_000 +
            new BigInteger(microseconds) * 1_000 +
            new BigInteger(nanoseconds);

        // Per spec: abs(normalizedSeconds) >= 2^53 → invalid
        // In nanoseconds: abs(totalNanoseconds) >= 2^53 * 10^9
        var maxTimeDuration = new BigInteger(9007199254740992) * 1_000_000_000;
        if (BigInteger.Abs(totalNanoseconds) >= maxTimeDuration)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Reads a duration field from a property bag for Duration.prototype.with().
    ///     Per spec: applies ToIntegerIfIntegral if property is present and not undefined.
    ///     Returns the existing duration value if not present or undefined.
    /// </summary>
    private static double ReadDurationField(IJsPropertyAccessor accessor, string name, double existingValue, ref bool any, RealmState? realm = null)
    {
        if (!accessor.TryGetProperty(name, out var v) || v.IsUndefined)
        {
            return existingValue;
        }

        any = true;
        var value = JsOps.ToNumber(v);

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw StandardLibrary.ThrowRangeError($"Duration field '{name}' is not finite", realm: realm);
        }

        if (value != Math.Truncate(value))
        {
            throw StandardLibrary.ThrowRangeError($"Duration field '{name}' is not an integer", realm: realm);
        }

        // Per spec: ToIntegerIfIntegral converts -0 to +0
        return value == 0 ? 0 : value;
    }

    /// <summary>
    ///     Gets an argument from the args list, returning defaultValue (0) if the argument is
    ///     not present or is undefined. Per Temporal spec, undefined arguments default to 0.
    /// </summary>
    private static int ToIntegerOrDefault(IReadOnlyList<JsValue> args, int index, string fieldName, RealmState? realm = null, int defaultValue = 0)
    {
        if (index >= args.Count || args[index].IsUndefined)
        {
            return defaultValue;
        }

        return ToIntegerWithRangeCheck(args[index], fieldName, realm);
    }

    /// <summary>
    ///     Converts a JS value to an integer, throwing RangeError for Infinity/NaN and non-integer values.
    ///     Per Temporal spec: ToIntegerWithTruncation then range check.
    /// </summary>
    private static int ToIntegerWithRangeCheck(JsValue value, string fieldName, RealmState? realm = null)
    {
        var num = JsOps.ToNumber(value);
        if (double.IsNaN(num) || double.IsInfinity(num))
        {
            throw StandardLibrary.ThrowRangeError($"Invalid {fieldName}: Infinity or NaN", realm: realm);
        }

        return (int)Math.Truncate(num);
    }

    /// <summary>
    ///     Validates that time component values are within valid ranges.
    ///     Throws RangeError if any component is out of range.
    /// </summary>
    private static void RejectTemporalTimeRange(int hour, int minute, int second,
        int millisecond, int microsecond, int nanosecond, RealmState? realm = null)
    {
        if (hour is < 0 or > 23 ||
            minute is < 0 or > 59 ||
            second is < 0 or > 59 ||
            millisecond is < 0 or > 999 ||
            microsecond is < 0 or > 999 ||
            nanosecond is < 0 or > 999)
        {
            throw StandardLibrary.ThrowRangeError("Time value is out of range", realm: realm);
        }
    }

    /// <summary>
    ///     Validates that date component values form a valid ISO date.
    ///     Throws RangeError if any component is out of range.
    /// </summary>
    private static void RejectISODate(int year, int month, int day, RealmState? realm = null)
    {
        if (month is < 1 or > 12)
        {
            throw StandardLibrary.ThrowRangeError("Month value is out of range (1-12)", realm: realm);
        }

        var daysInMonth = DateTime.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
        if (day < 1 || day > daysInMonth)
        {
            throw StandardLibrary.ThrowRangeError("Day value is out of range", realm: realm);
        }

        // Check ISO date range limits per Temporal spec
        // Min: -271821-04-19, Max: +275760-09-13
        if (year < IsoDateMin.year || year > IsoDateMax.year)
        {
            throw StandardLibrary.ThrowRangeError("Date value is out of representable range", realm: realm);
        }

        if (year == IsoDateMin.year && (month < IsoDateMin.month || (month == IsoDateMin.month && day < IsoDateMin.day)))
        {
            throw StandardLibrary.ThrowRangeError("Date value is out of representable range", realm: realm);
        }

        if (year == IsoDateMax.year && (month > IsoDateMax.month || (month == IsoDateMax.month && day > IsoDateMax.day)))
        {
            throw StandardLibrary.ThrowRangeError("Date value is out of representable range", realm: realm);
        }
    }

    private static void RejectISOTime(int hour, int minute, int second, int millisecond, int microsecond, int nanosecond, RealmState? realm = null)
    {
        if (hour is < 0 or > 23)
        {
            throw StandardLibrary.ThrowRangeError("Hour value is out of range (0-23)", realm: realm);
        }

        if (minute is < 0 or > 59)
        {
            throw StandardLibrary.ThrowRangeError("Minute value is out of range (0-59)", realm: realm);
        }

        if (second is < 0 or > 59)
        {
            throw StandardLibrary.ThrowRangeError("Second value is out of range (0-59)", realm: realm);
        }

        if (millisecond is < 0 or > 999)
        {
            throw StandardLibrary.ThrowRangeError("Millisecond value is out of range (0-999)", realm: realm);
        }

        if (microsecond is < 0 or > 999)
        {
            throw StandardLibrary.ThrowRangeError("Microsecond value is out of range (0-999)", realm: realm);
        }

        if (nanosecond is < 0 or > 999)
        {
            throw StandardLibrary.ThrowRangeError("Nanosecond value is out of range (0-999)", realm: realm);
        }
    }

    /// <summary>
    ///     Validates that a PlainDate is within the representable ISO date range.
    /// </summary>
    private static void ValidatePlainDateRange(JsTemporalPlainDate date, RealmState realm)
    {
        RejectISODate(date.Year, date.Month, date.Day, realm);
    }

    // Temporal spec ISO date range limits
    private static readonly (int year, int month, int day) IsoDateMin = (-271821, 4, 19);
    private static readonly (int year, int month, int day) IsoDateMax = (275760, 9, 13);

    private static long ParseOffsetToSeconds(string offset)
    {
        // Parse offset string like "+01:00", "-05:30", or "Z"
        if (string.IsNullOrEmpty(offset) || string.Equals(offset, "Z", StringComparison.Ordinal))
        {
            return 0;
        }

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

    internal static JsValue CreateInstantFromEpochMilliseconds(RealmState realm, double epochMilliseconds)
    {
        // Date-based conversions are millisecond-precision, so truncate before wrapping.
        var instant = JsTemporalInstant.FromEpochMilliseconds((long)Math.Truncate(epochMilliseconds));
        var prototypes = GetPrototypes(realm);
        return WrapInstant(instant, realm, prototypes.InstantPrototype);
    }

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

    internal static JsTemporalInstant GetInstant(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalInstantSlot, out var slot) &&
            slot.TryGetObject<JsTemporalInstant>(out var instant))
        {
            return instant;
        }
        throw StandardLibrary.ThrowTypeError("Value is not a Temporal.Instant");
    }

    private static JsTemporalDuration GetDuration(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalDurationSlot, out var slot) &&
            slot.TryGetObject<JsTemporalDuration>(out var duration))
        {
            return duration;
        }
        throw StandardLibrary.ThrowTypeError("Value is not a Temporal.Duration");
    }

    private static JsTemporalPlainDate GetPlainDate(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainDateSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainDate>(out var date))
        {
            return date;
        }
        throw StandardLibrary.ThrowTypeError("Value is not a Temporal.PlainDate");
    }

    private static JsTemporalPlainTime GetPlainTime(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainTimeSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainTime>(out var time))
        {
            return time;
        }
        throw StandardLibrary.ThrowTypeError("Value is not a Temporal.PlainTime");
    }

    private static JsTemporalPlainDateTime GetPlainDateTime(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainDateTimeSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainDateTime>(out var dateTime))
        {
            return dateTime;
        }
        throw StandardLibrary.ThrowTypeError("Value is not a Temporal.PlainDateTime");
    }

    private static JsTemporalZonedDateTime GetZonedDateTime(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalZonedDateTimeSlot, out var slot) &&
            slot.TryGetObject<JsTemporalZonedDateTime>(out var zonedDateTime))
        {
            return zonedDateTime;
        }
        throw StandardLibrary.ThrowTypeError("Value is not a Temporal.ZonedDateTime");
    }

    private static JsTemporalPlainYearMonth GetPlainYearMonth(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainYearMonthSlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainYearMonth>(out var yearMonth))
        {
            return yearMonth;
        }
        throw StandardLibrary.ThrowTypeError("Value is not a Temporal.PlainYearMonth");
    }

    private static JsTemporalPlainMonthDay GetPlainMonthDay(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(TemporalPlainMonthDaySlot, out var slot) &&
            slot.TryGetObject<JsTemporalPlainMonthDay>(out var monthDay))
        {
            return monthDay;
        }
        throw StandardLibrary.ThrowTypeError("Value is not a Temporal.PlainMonthDay");
    }

    private readonly record struct TemporalRoundingOptions(string SmallestUnit, long Increment, string RoundingMode);

    private static TemporalRoundingOptions GetTemporalRoundingOptions(
        JsValue optionsArg,
        RealmState realm,
        string methodName,
        IReadOnlyDictionary<string, long> unitMaxIncrements,
        bool allowMaxIncrement)
    {
        if (optionsArg.IsUndefined)
        {
            throw StandardLibrary.ThrowTypeError($"{methodName} requires an options argument", realm: realm);
        }

        var roundingMode = "halfExpand";
        double roundingIncrementNumber = 1;
        string smallestUnit;

        if (optionsArg.IsString)
        {
            smallestUnit = optionsArg.AsString() ?? string.Empty;
        }
        else if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            if (accessor.TryGetProperty("roundingIncrement", out var roundingIncrementValue) && !roundingIncrementValue.IsUndefined)
            {
                roundingIncrementNumber = JsOps.ToNumber(roundingIncrementValue);
            }

            if (accessor.TryGetProperty("roundingMode", out var roundingModeValue) && !roundingModeValue.IsUndefined)
            {
                roundingMode = JsOps.ToJsString(roundingModeValue);
            }

            if (accessor.TryGetProperty("smallestUnit", out var smallestUnitValue) && !smallestUnitValue.IsUndefined)
            {
                smallestUnit = JsOps.ToJsString(smallestUnitValue);
            }
            else
            {
                throw StandardLibrary.ThrowRangeError($"{methodName} requires a smallestUnit option", realm: realm);
            }
        }
        else
        {
            throw StandardLibrary.ThrowTypeError($"{methodName} requires options to be a string or object", realm: realm);
        }

        smallestUnit = NormalizeSmallestUnit(smallestUnit);
        if (!unitMaxIncrements.TryGetValue(smallestUnit, out var maxIncrement))
        {
            throw StandardLibrary.ThrowRangeError($"Invalid smallestUnit: {smallestUnit}", realm: realm);
        }

        if (!ValidRoundingModes.Contains(roundingMode))
        {
            throw StandardLibrary.ThrowRangeError($"Invalid roundingMode: {roundingMode}", realm: realm);
        }

        if (double.IsNaN(roundingIncrementNumber) || double.IsInfinity(roundingIncrementNumber))
        {
            throw StandardLibrary.ThrowRangeError("Invalid roundingIncrement", realm: realm);
        }

        var increment = (long)Math.Truncate(roundingIncrementNumber);
        if (increment < 1 || increment > 1_000_000_000L)
        {
            throw StandardLibrary.ThrowRangeError("Invalid roundingIncrement", realm: realm);
        }

        if (maxIncrement == 1)
        {
            if (increment != 1)
            {
                throw StandardLibrary.ThrowRangeError("Invalid roundingIncrement", realm: realm);
            }
        }
        else
        {
            if (increment > maxIncrement || maxIncrement % increment != 0)
            {
                throw StandardLibrary.ThrowRangeError("Invalid roundingIncrement", realm: realm);
            }

            if (!allowMaxIncrement && increment == maxIncrement)
            {
                throw StandardLibrary.ThrowRangeError("Invalid roundingIncrement", realm: realm);
            }
        }

        return new TemporalRoundingOptions(smallestUnit, increment, roundingMode);
    }

    private static string NormalizeSmallestUnit(string unit)
    {
        return unit switch
        {
            "days" => "day",
            "hours" => "hour",
            "minutes" => "minute",
            "seconds" => "second",
            "milliseconds" => "millisecond",
            "microseconds" => "microsecond",
            "nanoseconds" => "nanosecond",
            _ => unit
        };
    }

    private static long GetUnitNanoseconds(string smallestUnit)
    {
        return smallestUnit switch
        {
            "day" => NanosecondsPerDay,
            "hour" => NanosecondsPerHour,
            "minute" => NanosecondsPerMinute,
            "second" => NanosecondsPerSecond,
            "millisecond" => NanosecondsPerMillisecond,
            "microsecond" => NanosecondsPerMicrosecond,
            "nanosecond" => 1L,
            _ => 1L
        };
    }

    private static BigInteger RoundToIncrement(
        BigInteger value,
        BigInteger increment,
        string roundingMode,
        bool treatNegativeAsPositive = false)
    {
        if (increment == BigInteger.One)
        {
            return value;
        }

        var quotient = DivRemFloor(value, increment, out var remainder);
        if (remainder.IsZero)
        {
            return value;
        }

        var lower = quotient * increment;
        var upper = lower + increment;

        var sign = treatNegativeAsPositive ? 1 : value.Sign;
        switch (roundingMode)
        {
            case "floor":
                return lower;
            case "ceil":
                return upper;
            case "trunc":
                return sign >= 0 ? lower : upper;
            case "expand":
                return sign >= 0 ? upper : lower;
        }

        var twiceRemainder = remainder * 2;
        var compare = twiceRemainder.CompareTo(increment);
        if (compare < 0)
        {
            return lower;
        }
        if (compare > 0)
        {
            return upper;
        }

        return roundingMode switch
        {
            "halfCeil" => upper,
            "halfFloor" => lower,
            "halfTrunc" => sign >= 0 ? lower : upper,
            "halfExpand" => sign >= 0 ? upper : lower,
            "halfEven" => quotient.IsEven ? lower : upper,
            _ => sign >= 0 ? upper : lower
        };
    }

    private static JsTemporalPlainTime RoundPlainTime(JsTemporalPlainTime time, TemporalRoundingOptions options)
    {
        var totalNanoseconds = new BigInteger(time.TotalNanoseconds);
        var incrementNanoseconds = new BigInteger(GetUnitNanoseconds(options.SmallestUnit)) * options.Increment;
        var rounded = RoundToIncrement(totalNanoseconds, incrementNanoseconds, options.RoundingMode);
        var normalized = PositiveMod(rounded, NanosecondsPerDay);
        return CreatePlainTimeFromNanoseconds((long)normalized);
    }

    private static JsTemporalPlainDateTime RoundPlainDateTime(
        JsTemporalPlainDateTime dateTime,
        TemporalRoundingOptions options,
        RealmState realm)
    {
        var totalNanoseconds = ToEpochNanoseconds(dateTime);
        var incrementNanoseconds = new BigInteger(GetUnitNanoseconds(options.SmallestUnit)) * options.Increment;
        var rounded = RoundToIncrement(totalNanoseconds, incrementNanoseconds, options.RoundingMode, treatNegativeAsPositive: true);

        if (rounded < PlainDateTimeMinEpochNanoseconds || rounded > PlainDateTimeMaxEpochNanoseconds)
        {
            throw StandardLibrary.ThrowRangeError("Temporal.PlainDateTime is out of range", realm: realm);
        }

        return FromEpochNanoseconds(rounded);
    }

    private static JsTemporalInstant RoundZonedDateTimeToDay(
        JsTemporalZonedDateTime zonedDateTime,
        TemporalRoundingOptions options,
        RealmState realm)
    {
        var epochNanoseconds = zonedDateTime.Instant.EpochNanoseconds;
        if (epochNanoseconds < InstantMinEpochNanoseconds ||
            epochNanoseconds > InstantMaxEpochNanoseconds)
        {
            throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
        }

        var localDateTime = GetLocalPlainDateTime(zonedDateTime, realm);
        var year = localDateTime.Year;
        var month = localDateTime.Month;
        var day = localDateTime.Day;
        var startOfDay = GetStartOfDayInstant(year, month, day, zonedDateTime.TimeZone, zonedDateTime.FixedOffset, realm);
        var dayNumber = IsoToDayNumber(year, month, day);
        var (nextYear, nextMonth, nextDay) = DayNumberToIsoDate(dayNumber + 1);
        var startOfNextDay = GetStartOfDayInstant(nextYear, nextMonth, nextDay, zonedDateTime.TimeZone, zonedDateTime.FixedOffset, realm);

        var dayLength = startOfNextDay - startOfDay;
        if (dayLength <= 0)
        {
            throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
        }

        var offsetNanoseconds = zonedDateTime.Instant.EpochNanoseconds - startOfDay;
        var incrementNanoseconds = dayLength * options.Increment;
        var roundedOffset = RoundToIncrement(offsetNanoseconds, incrementNanoseconds, options.RoundingMode);
        var roundedInstant = startOfDay + roundedOffset;

        if (roundedInstant < InstantMinEpochNanoseconds || roundedInstant > InstantMaxEpochNanoseconds)
        {
            throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
        }

        return JsTemporalInstant.FromEpochNanoseconds(roundedInstant);
    }

    private static JsTemporalPlainDateTime GetLocalPlainDateTime(
        JsTemporalZonedDateTime zonedDateTime,
        RealmState realm)
    {
        if (zonedDateTime.FixedOffset.HasValue)
        {
            var offsetNanoseconds = new BigInteger(zonedDateTime.FixedOffset.Value.Ticks) * 100;
            var localEpochNanoseconds = zonedDateTime.Instant.EpochNanoseconds + offsetNanoseconds;
            if (localEpochNanoseconds < PlainDateTimeMinEpochNanoseconds ||
                localEpochNanoseconds > PlainDateTimeMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
            }

            var localDateTime = FromEpochNanoseconds(localEpochNanoseconds);
            return new JsTemporalPlainDateTime(
                localDateTime.Year,
                localDateTime.Month,
                localDateTime.Day,
                localDateTime.Hour,
                localDateTime.Minute,
                localDateTime.Second,
                localDateTime.Millisecond,
                localDateTime.Microsecond,
                localDateTime.Nanosecond,
                zonedDateTime.Calendar);
        }

        DateTimeOffset localDateTimeOffset;
        try
        {
            var utc = zonedDateTime.Instant.ToDateTimeOffset();
            localDateTimeOffset = TimeZoneInfo.ConvertTime(utc, zonedDateTime.TimeZone);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
        }
        catch (OverflowException)
        {
            throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
        }

        return new JsTemporalPlainDateTime(
            localDateTimeOffset.Year,
            localDateTimeOffset.Month,
            localDateTimeOffset.Day,
            localDateTimeOffset.Hour,
            localDateTimeOffset.Minute,
            localDateTimeOffset.Second,
            localDateTimeOffset.Millisecond,
            localDateTimeOffset.Microsecond,
            zonedDateTime.Nanosecond,
            zonedDateTime.Calendar);
    }

    private static BigInteger GetStartOfDayInstant(
        int year,
        int month,
        int day,
        TimeZoneInfo timeZone,
        TimeSpan? fixedOffset,
        RealmState realm)
    {
        if (fixedOffset.HasValue)
        {
            var offsetNanoseconds = new BigInteger(fixedOffset.Value.Ticks) * 100;
            var localEpochNanoseconds = ToEpochNanoseconds(year, month, day, 0, 0, 0, 0, 0, 0);
            if (localEpochNanoseconds < PlainDateTimeMinEpochNanoseconds ||
                localEpochNanoseconds > PlainDateTimeMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
            }
            var instantNanoseconds = localEpochNanoseconds - offsetNanoseconds;
            if (instantNanoseconds < InstantMinEpochNanoseconds ||
                instantNanoseconds > InstantMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
            }

            return instantNanoseconds;
        }

        DateTime localDateTime;
        try
        {
            localDateTime = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
        }

        var candidate = localDateTime;
        for (var i = 0; i < 86_400; i++)
        {
            if (!timeZone.IsInvalidTime(candidate))
            {
                var offset = ResolveTimeZoneOffset(candidate, timeZone, fixedOffset);
                return ToEpochNanoseconds(candidate, offset);
            }

            candidate = candidate.AddSeconds(1);
        }

        throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
    }

    private static TimeSpan ResolveTimeZoneOffset(DateTime localDateTime, TimeZoneInfo timeZone, TimeSpan? fixedOffset)
    {
        if (fixedOffset.HasValue)
        {
            return fixedOffset.Value;
        }

        if (timeZone.IsAmbiguousTime(localDateTime))
        {
            var offsets = timeZone.GetAmbiguousTimeOffsets(localDateTime);
            return offsets.Max();
        }

        return timeZone.GetUtcOffset(localDateTime);
    }

    private static BigInteger ToEpochNanoseconds(DateTime localDateTime, TimeSpan offset)
    {
        var utcDateTime = DateTime.SpecifyKind(localDateTime - offset, DateTimeKind.Utc);
        var dto = new DateTimeOffset(utcDateTime);
        return new JsTemporalInstant(dto).EpochNanoseconds;
    }

    private static BigInteger ToEpochNanoseconds(JsTemporalPlainDateTime dateTime)
    {
        return ToEpochNanoseconds(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second,
            dateTime.Millisecond, dateTime.Microsecond, dateTime.Nanosecond);
    }

    private static DateTime CreateTimeZoneLocalDateTime(JsTemporalPlainDateTime dateTime)
    {
        var localDateTime = new DateTime(
            dateTime.Year,
            dateTime.Month,
            dateTime.Day,
            dateTime.Hour,
            dateTime.Minute,
            dateTime.Second,
            dateTime.Millisecond,
            dateTime.Microsecond);
        return DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
    }

    private static BigInteger ToEpochNanoseconds(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        int millisecond,
        int microsecond,
        int nanosecond)
    {
        var dayNumber = IsoToDayNumber(year, month, day);
        var timeNanoseconds =
            (long)hour * NanosecondsPerHour +
            (long)minute * NanosecondsPerMinute +
            (long)second * NanosecondsPerSecond +
            (long)millisecond * NanosecondsPerMillisecond +
            (long)microsecond * NanosecondsPerMicrosecond +
            nanosecond;
        return (BigInteger)dayNumber * NanosecondsPerDay + timeNanoseconds;
    }

    private static JsTemporalPlainDateTime FromEpochNanoseconds(BigInteger epochNanoseconds)
    {
        var dayNumber = DivRemFloor(epochNanoseconds, new BigInteger(NanosecondsPerDay), out var remainder);
        var (year, month, day) = DayNumberToIsoDate((long)dayNumber);
        var time = CreatePlainTimeFromNanoseconds((long)remainder);
        return new JsTemporalPlainDateTime(
            year, month, day,
            time.Hour, time.Minute, time.Second,
            time.Millisecond, time.Microsecond, time.Nanosecond);
    }

    private static long IsoToDayNumber(int year, int month, int day)
    {
        long y = year;
        long m = month;
        long d = day;

        y -= m <= 2 ? 1 : 0;
        var era = (y >= 0 ? y : y - 399) / 400;
        var yoe = y - era * 400;
        var mp = m + (m > 2 ? -3 : 9);
        var doy = (153 * mp + 2) / 5 + d - 1;
        var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        return era * 146097 + doe - 719468;
    }

    private static (int Year, int Month, int Day) DayNumberToIsoDate(long dayNumber)
    {
        var z = dayNumber + 719468;
        var era = (z >= 0 ? z : z - 146096) / 146097;
        var doe = z - era * 146097;
        var yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        var y = yoe + era * 400;
        var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        var mp = (5 * doy + 2) / 153;
        var d = doy - (153 * mp + 2) / 5 + 1;
        var m = mp + (mp < 10 ? 3 : -9);
        y += m <= 2 ? 1 : 0;
        return ((int)y, (int)m, (int)d);
    }

    private static JsTemporalPlainTime CreatePlainTimeFromNanoseconds(long totalNanoseconds)
    {
        var remaining = totalNanoseconds;
        var hour = (int)(remaining / NanosecondsPerHour);
        remaining %= NanosecondsPerHour;
        var minute = (int)(remaining / NanosecondsPerMinute);
        remaining %= NanosecondsPerMinute;
        var second = (int)(remaining / NanosecondsPerSecond);
        remaining %= NanosecondsPerSecond;
        var millisecond = (int)(remaining / NanosecondsPerMillisecond);
        remaining %= NanosecondsPerMillisecond;
        var microsecond = (int)(remaining / NanosecondsPerMicrosecond);
        var nanosecond = (int)(remaining % NanosecondsPerMicrosecond);
        return new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond);
    }

    private static BigInteger DivRemFloor(BigInteger value, BigInteger divisor, out BigInteger remainder)
    {
        var quotient = BigInteger.DivRem(value, divisor, out remainder);
        if (remainder.Sign < 0)
        {
            remainder += divisor;
            quotient -= 1;
        }

        return quotient;
    }

    private static BigInteger PositiveMod(BigInteger value, long modulus)
    {
        var mod = value % modulus;
        if (mod.Sign < 0)
        {
            mod += modulus;
        }

        return mod;
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

            // Strip bracket annotations (timezone/calendar) if present
            var bracketIdx = str.IndexOf('[');
            if (bracketIdx >= 0)
            {
                str = str[..bracketIdx];
            }

            // Try custom parsing first (preserves nanosecond precision, handles all years)
            var parsed = ParseExtendedYearInstant(str);
            if (parsed is not null)
            {
                return parsed;
            }

            // Fall back to standard ISO 8601 parsing
            if (DateTimeOffset.TryParse(str, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dto))
            {
                return new JsTemporalInstant(dto);
            }
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.Instant", realm: realm);
    }

    /// <summary>
    ///     Parses an ISO 8601 instant string with extended year, year 0, or nanosecond precision.
    ///     Handles formats: YYYY-MM-DDTHH:mm:ss[.fffffffff]Z, +YYYYYY-..., -YYYYYY-...
    ///     Returns null if the string cannot be parsed.
    /// </summary>
    private static JsTemporalInstant? ParseExtendedYearInstant(string str)
    {
        int year;
        string timePart;

        if (str.Length > 0 && (str[0] == '+' || str[0] == '-' || str[0] == '\u2212'))
        {
            // Extended year format: +YYYYYY-MM-DDTHH:mm:ssZ or -YYYYYY-MM-DDTHH:mm:ssZ
            var sign = str[0] == '-' || str[0] == '\u2212' ? -1 : 1;
            var rest = str[1..];

            var tIdx = rest.IndexOf('T');
            if (tIdx < 0) return null;

            var datePart = rest[..tIdx];
            timePart = rest[(tIdx + 1)..];

            var lastDash = datePart.LastIndexOf('-');
            if (lastDash <= 0) return null;
            var secondLastDash = datePart.LastIndexOf('-', lastDash - 1);
            if (secondLastDash <= 0) return null;

            if (!int.TryParse(datePart[..secondLastDash], System.Globalization.CultureInfo.InvariantCulture, out var yearAbs) ||
                !int.TryParse(datePart[(secondLastDash + 1)..lastDash], System.Globalization.CultureInfo.InvariantCulture, out var month) ||
                !int.TryParse(datePart[(lastDash + 1)..], System.Globalization.CultureInfo.InvariantCulture, out var day))
                return null;

            year = sign * yearAbs;
            return ComputeInstantFromParts(year, month, day, timePart);
        }

        // Standard year format: YYYY-MM-DDTHH:mm:ss...
        {
            var tIdx = str.IndexOf('T');
            if (tIdx < 0) return null;

            var datePart = str[..tIdx];
            timePart = str[(tIdx + 1)..];

            var dashParts = datePart.Split('-');
            if (dashParts.Length < 3) return null;

            if (!int.TryParse(dashParts[0], System.Globalization.CultureInfo.InvariantCulture, out year) ||
                !int.TryParse(dashParts[1], System.Globalization.CultureInfo.InvariantCulture, out var month) ||
                !int.TryParse(dashParts[2], System.Globalization.CultureInfo.InvariantCulture, out var day))
                return null;

            return ComputeInstantFromParts(year, month, day, timePart);
        }
    }

    /// <summary>
    ///     Computes a JsTemporalInstant from date components and a time+offset string.
    /// </summary>
    private static JsTemporalInstant ComputeInstantFromParts(int year, int month, int day, string timePart)
    {
        // Remove timezone offset to get pure time
        var offsetNanos = 0L;
        if (timePart.EndsWith('Z') || timePart.EndsWith('z'))
        {
            timePart = timePart[..^1];
        }
        else
        {
            // Look for offset: last + or - followed by a digit
            for (var i = timePart.Length - 1; i >= 2; i--)
            {
                if ((timePart[i] == '+' || timePart[i] == '-') && i + 1 < timePart.Length && char.IsDigit(timePart[i + 1]))
                {
                    offsetNanos = ParseOffsetToNanos(timePart[i..]);
                    timePart = timePart[..i];
                    break;
                }
            }
        }

        // Parse HH:mm:ss[.fffffffff]
        var timeParts = timePart.Split(':');
        var hour = timeParts.Length > 0 ? int.Parse(timeParts[0], System.Globalization.CultureInfo.InvariantCulture) : 0;
        var minute = timeParts.Length > 1 ? int.Parse(timeParts[1], System.Globalization.CultureInfo.InvariantCulture) : 0;

        var second = 0;
        long subSecondNanos = 0;
        if (timeParts.Length > 2)
        {
            var secStr = timeParts[2];
            var dotIdx = secStr.IndexOf('.');
            if (dotIdx >= 0)
            {
                second = int.Parse(secStr[..dotIdx], System.Globalization.CultureInfo.InvariantCulture);
                var frac = secStr[(dotIdx + 1)..];
                // Pad to 9 digits for nanosecond precision
                frac = frac.PadRight(9, '0');
                if (frac.Length > 9) frac = frac[..9];
                subSecondNanos = long.Parse(frac, System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                second = int.Parse(secStr, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        // Compute epoch days using shared helper (proleptic Gregorian)
        var epochDays = IsoCalendarHelpers.DateToEpochDays(year, month, day);

        // Compute epoch nanoseconds
        var epochNanos = new System.Numerics.BigInteger(epochDays) * 86400L * 1_000_000_000L;
        epochNanos += (long)hour * 3_600_000_000_000L;
        epochNanos += (long)minute * 60_000_000_000L;
        epochNanos += (long)second * 1_000_000_000L;
        epochNanos += subSecondNanos;
        epochNanos -= offsetNanos;

        return new JsTemporalInstant(epochNanos);
    }

    /// <summary>
    ///     Parses an offset string like "+01:00", "-05:30" to nanoseconds.
    /// </summary>
    private static long ParseOffsetToNanos(string offsetStr)
    {
        var sign = offsetStr[0] == '-' ? -1L : 1L;
        var body = offsetStr[1..];
        var parts = body.Split(':');
        int hours, minutes = 0;

        if (parts.Length == 1)
        {
            if (body.Length == 4)
            {
                hours = int.Parse(body[..2], System.Globalization.CultureInfo.InvariantCulture);
                minutes = int.Parse(body[2..], System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                hours = int.Parse(body, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        else
        {
            hours = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            minutes = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        }

        return sign * ((long)hours * 3_600_000_000_000L + (long)minutes * 60_000_000_000L);
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
        // ISO 8601 duration parsing (P1Y2M3DT4H5M6S format)
        // Supports optional leading '-' or '+' sign for negative/positive durations
        var s = str;
        var sign = 1;
        if (s.Length > 0 && (s[0] == '-' || s[0] == '\u2212'))
        {
            sign = -1;
            s = s[1..];
        }
        else if (s.Length > 0 && s[0] == '+')
        {
            s = s[1..];
        }

        if (!s.StartsWith('P'))
        {
            throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);
        }

        double years = 0, months = 0, weeks = 0, days = 0;
        double hours = 0, minutes = 0, seconds = 0;
        double milliseconds = 0, microseconds = 0, nanoseconds = 0;

        var isTimePart = false;
        var currentNumber = "";

        for (var i = 1; i < s.Length; i++)
        {
            var c = s[i];
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
                        case 'S':
                            // Handle fractional seconds by splitting into s/ms/us/ns
                            seconds = Math.Truncate(value);
                            var frac = value - seconds;
                            if (frac > 0)
                            {
                                var fracNanos = frac * 1_000_000_000;
                                milliseconds = Math.Truncate(fracNanos / 1_000_000);
                                fracNanos -= milliseconds * 1_000_000;
                                microseconds = Math.Truncate(fracNanos / 1_000);
                                fracNanos -= microseconds * 1_000;
                                nanoseconds = Math.Round(fracNanos);
                            }
                            break;
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

        return new JsTemporalDuration(
            sign * years, sign * months, sign * weeks, sign * days,
            sign * hours, sign * minutes, sign * seconds,
            sign * milliseconds, sign * microseconds, sign * nanoseconds);
    }

    /// <summary>
    /// Result of ToSecondsStringPrecision spec operation.
    /// FractionalDigits: -1 = "auto", -2 = "minute" (omit seconds), 0-9 = exact digit count.
    /// </summary>
    private readonly record struct SecondsStringPrecision(int FractionalDigits, string SmallestUnit, long Increment);

    /// <summary>
    /// Implements the Temporal spec's ToSecondsStringPrecision + roundingMode parsing for toString methods.
    /// Reads options in alphabetical order: fractionalSecondDigits, roundingMode, smallestUnit.
    /// Returns precision info and roundingMode (default "trunc" for toString).
    /// </summary>
    private static (SecondsStringPrecision Precision, string RoundingMode) GetToStringPrecisionOptions(
        IJsPropertyAccessor? optionsObj, RealmState realm)
    {
        if (optionsObj is null)
        {
            return (new SecondsStringPrecision(-1, "nanosecond", 1), "trunc");
        }

        // Step 1: Read fractionalSecondDigits (alphabetical order)
        int fractionalDigits = -1; // -1 = auto
        bool hasFracDigits = false;
        if (optionsObj.TryGetProperty("fractionalSecondDigits", out var fracDigitsVal) && !fracDigitsVal.IsUndefined)
        {
            hasFracDigits = true;
            if (fracDigitsVal.IsNumber)
            {
                var num = fracDigitsVal.AsDouble();
                if (double.IsNaN(num))
                {
                    throw StandardLibrary.ThrowRangeError("fractionalSecondDigits must not be NaN", realm: realm);
                }

                var floored = (int)Math.Floor(num);
                if (floored < 0 || floored > 9 || double.IsInfinity(num))
                {
                    throw StandardLibrary.ThrowRangeError(
                        $"{num.ToString(System.Globalization.CultureInfo.InvariantCulture)} is out of range for fractionalSecondDigits (0-9)",
                        realm: realm);
                }

                fractionalDigits = floored;
            }
            else
            {
                // Not a number type: convert to string
                var str = JsOps.ToJsString(fracDigitsVal);
                if (!string.Equals(str, "auto", StringComparison.Ordinal))
                {
                    throw StandardLibrary.ThrowRangeError(
                        $"\"{str}\" is not a valid value for fractionalSecondDigits", realm: realm);
                }
                // fractionalDigits stays -1 (auto)
            }
        }

        // Step 2: Read roundingMode (alphabetical order)
        var roundingMode = "trunc";
        if (optionsObj.TryGetProperty("roundingMode", out var roundingModeVal) && !roundingModeVal.IsUndefined)
        {
            roundingMode = JsOps.ToJsString(roundingModeVal);
            if (!ValidRoundingModes.Contains(roundingMode))
            {
                throw StandardLibrary.ThrowRangeError($"Invalid roundingMode: {roundingMode}", realm: realm);
            }
        }

        // Step 3: Read smallestUnit (alphabetical order) — overrides fractionalSecondDigits
        if (optionsObj.TryGetProperty("smallestUnit", out var smallestUnitVal) && !smallestUnitVal.IsUndefined)
        {
            var smallestUnit = JsOps.ToJsString(smallestUnitVal);
            smallestUnit = NormalizeSmallestUnit(smallestUnit);

            var precision = smallestUnit switch
            {
                "minute" => new SecondsStringPrecision(-2, "minute", 1),
                "second" => new SecondsStringPrecision(0, "second", 1),
                "millisecond" => new SecondsStringPrecision(3, "millisecond", 1),
                "microsecond" => new SecondsStringPrecision(6, "microsecond", 1),
                "nanosecond" => new SecondsStringPrecision(9, "nanosecond", 1),
                _ => throw StandardLibrary.ThrowRangeError(
                    $"\"{smallestUnit}\" is not a valid value for smallestUnit in toString", realm: realm)
            };
            return (precision, roundingMode);
        }

        // No smallestUnit — use fractionalSecondDigits
        if (hasFracDigits && fractionalDigits >= 0)
        {
            var (unit, increment) = fractionalDigits switch
            {
                0 => ("second", 1L),
                1 => ("nanosecond", 100_000_000L),
                2 => ("nanosecond", 10_000_000L),
                3 => ("nanosecond", 1_000_000L),
                4 => ("nanosecond", 100_000L),
                5 => ("nanosecond", 10_000L),
                6 => ("nanosecond", 1_000L),
                7 => ("nanosecond", 100L),
                8 => ("nanosecond", 10L),
                _ => ("nanosecond", 1L) // 9
            };
            return (new SecondsStringPrecision(fractionalDigits, unit, increment), roundingMode);
        }

        // Default: auto
        return (new SecondsStringPrecision(-1, "nanosecond", 1), roundingMode);
    }

    /// <summary>
    /// Like GetToStringPrecisionOptions, but reads additional string options interleaved at the correct
    /// alphabetical positions. For ZonedDateTime: calendarName, fractionalSecondDigits, offset, roundingMode, smallestUnit, timeZoneName.
    /// </summary>
    private static (SecondsStringPrecision Precision, string RoundingMode) GetToStringPrecisionOptionsWithInterleave(
        IJsPropertyAccessor? optionsObj, RealmState realm,
        string afterFracOptionName, HashSet<string> afterFracValidValues, string afterFracDefault,
        string afterSmallestOptionName, HashSet<string> afterSmallestValidValues, string afterSmallestDefault,
        out string afterFracResult, out string afterSmallestResult)
    {
        if (optionsObj is null)
        {
            afterFracResult = afterFracDefault;
            afterSmallestResult = afterSmallestDefault;
            return (new SecondsStringPrecision(-1, "nanosecond", 1), "trunc");
        }

        // Step 1: Read fractionalSecondDigits
        int fractionalDigits = -1;
        bool hasFracDigits = false;
        if (optionsObj.TryGetProperty("fractionalSecondDigits", out var fracDigitsVal) && !fracDigitsVal.IsUndefined)
        {
            hasFracDigits = true;
            if (fracDigitsVal.IsNumber)
            {
                var num = fracDigitsVal.AsDouble();
                if (double.IsNaN(num))
                {
                    throw StandardLibrary.ThrowRangeError("fractionalSecondDigits must not be NaN", realm: realm);
                }

                var floored = (int)Math.Floor(num);
                if (floored < 0 || floored > 9 || double.IsInfinity(num))
                {
                    throw StandardLibrary.ThrowRangeError(
                        $"{num.ToString(System.Globalization.CultureInfo.InvariantCulture)} is out of range for fractionalSecondDigits (0-9)",
                        realm: realm);
                }

                fractionalDigits = floored;
            }
            else
            {
                var str = JsOps.ToJsString(fracDigitsVal);
                if (!string.Equals(str, "auto", StringComparison.Ordinal))
                {
                    throw StandardLibrary.ThrowRangeError(
                        $"\"{str}\" is not a valid value for fractionalSecondDigits", realm: realm);
                }
            }
        }

        // Step 2: Read the interleaved option (e.g. "offset") — between fractionalSecondDigits and roundingMode
        afterFracResult = GetTemporalStringOption(optionsObj, afterFracOptionName, afterFracValidValues, afterFracDefault, realm);

        // Step 3: Read roundingMode
        var roundingMode = "trunc";
        if (optionsObj.TryGetProperty("roundingMode", out var roundingModeVal) && !roundingModeVal.IsUndefined)
        {
            roundingMode = JsOps.ToJsString(roundingModeVal);
            if (!ValidRoundingModes.Contains(roundingMode))
            {
                throw StandardLibrary.ThrowRangeError($"Invalid roundingMode: {roundingMode}", realm: realm);
            }
        }

        // Step 4: Read smallestUnit
        if (optionsObj.TryGetProperty("smallestUnit", out var smallestUnitVal) && !smallestUnitVal.IsUndefined)
        {
            var smallestUnit = JsOps.ToJsString(smallestUnitVal);
            smallestUnit = NormalizeSmallestUnit(smallestUnit);

            // Step 5: Read the trailing option (e.g. "timeZoneName") — after smallestUnit
            afterSmallestResult = GetTemporalStringOption(optionsObj, afterSmallestOptionName, afterSmallestValidValues, afterSmallestDefault, realm);

            var precision = smallestUnit switch
            {
                "minute" => new SecondsStringPrecision(-2, "minute", 1),
                "second" => new SecondsStringPrecision(0, "second", 1),
                "millisecond" => new SecondsStringPrecision(3, "millisecond", 1),
                "microsecond" => new SecondsStringPrecision(6, "microsecond", 1),
                "nanosecond" => new SecondsStringPrecision(9, "nanosecond", 1),
                _ => throw StandardLibrary.ThrowRangeError(
                    $"\"{smallestUnit}\" is not a valid value for smallestUnit in toString", realm: realm)
            };
            return (precision, roundingMode);
        }

        // Step 5: Read the trailing option when no smallestUnit
        afterSmallestResult = GetTemporalStringOption(optionsObj, afterSmallestOptionName, afterSmallestValidValues, afterSmallestDefault, realm);

        if (hasFracDigits && fractionalDigits >= 0)
        {
            var (unit, increment) = fractionalDigits switch
            {
                0 => ("second", 1L),
                1 => ("nanosecond", 100_000_000L),
                2 => ("nanosecond", 10_000_000L),
                3 => ("nanosecond", 1_000_000L),
                4 => ("nanosecond", 100_000L),
                5 => ("nanosecond", 10_000L),
                6 => ("nanosecond", 1_000L),
                7 => ("nanosecond", 100L),
                8 => ("nanosecond", 10L),
                _ => ("nanosecond", 1L)
            };
            return (new SecondsStringPrecision(fractionalDigits, unit, increment), roundingMode);
        }

        return (new SecondsStringPrecision(-1, "nanosecond", 1), roundingMode);
    }

    /// <summary>
    /// Formats a time as ISO 8601 string with the specified precision.
    /// FractionalDigits: -2 = minute only, -1 = auto, 0-9 = exact digits.
    /// </summary>
    private static string FormatTimeToString(
        int hour, int minute, int second,
        int millisecond, int microsecond, int nanosecond,
        int fractionalDigits)
    {
        // "minute" precision — no seconds at all
        if (fractionalDigits == -2)
        {
            return $"{hour:D2}:{minute:D2}";
        }

        var baseTime = $"{hour:D2}:{minute:D2}:{second:D2}";

        // 0 fractional digits — seconds only
        if (fractionalDigits == 0)
        {
            return baseTime;
        }

        var totalSubSecondNanos = (long)millisecond * 1_000_000L + (long)microsecond * 1_000L + nanosecond;

        // Auto mode: strip trailing zeros, but always show seconds
        if (fractionalDigits == -1)
        {
            if (totalSubSecondNanos == 0)
            {
                return baseTime;
            }

            var fractionStr = totalSubSecondNanos.ToString("D9", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
            return $"{baseTime}.{fractionStr}";
        }

        // Exact number of digits (1-9)
        var fullFraction = totalSubSecondNanos.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        return $"{baseTime}.{fullFraction[..fractionalDigits]}";
    }

    /// <summary>
    /// Formats a Duration with the specified precision options.
    /// The fractionalSecondDigits controls the sub-second portion of the seconds component.
    /// </summary>
    private static string FormatDurationToString(
        JsTemporalDuration duration, SecondsStringPrecision precision, string roundingMode)
    {
        var sign = duration.Sign;
        var absYears = (long)Math.Abs(duration.Years);
        var absMonths = (long)Math.Abs(duration.Months);
        var absWeeks = (long)Math.Abs(duration.Weeks);
        var absDays = (long)Math.Abs(duration.Days);
        var absHours = (long)Math.Abs(duration.Hours);
        var absMinutes = (long)Math.Abs(duration.Minutes);
        var absSeconds = (long)Math.Abs(duration.Seconds);
        var absMilliseconds = (long)Math.Abs(duration.Milliseconds);
        var absMicroseconds = (long)Math.Abs(duration.Microseconds);
        var absNanoseconds = (long)Math.Abs(duration.Nanoseconds);

        // Round the sub-second part based on precision
        var totalSubSecondNanos = absMilliseconds * 1_000_000L +
                                  absMicroseconds * 1_000L +
                                  absNanoseconds;

        if (precision.Increment > 1 || !string.Equals(precision.SmallestUnit, "nanosecond", StringComparison.Ordinal))
        {
            var totalNanos = new BigInteger(absSeconds) * NanosecondsPerSecond + new BigInteger(totalSubSecondNanos);
            var incrementNanos = new BigInteger(GetUnitNanoseconds(precision.SmallestUnit)) * precision.Increment;
            totalNanos = RoundToIncrement(totalNanos, incrementNanos, roundingMode);
            absSeconds = (long)(totalNanos / NanosecondsPerSecond);
            totalSubSecondNanos = (long)(totalNanos % NanosecondsPerSecond);
        }

        var sb = new StringBuilder();
        if (sign < 0)
        {
            sb.Append('-');
        }

        sb.Append('P');

        if (absYears != 0)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{absYears}Y");
        }

        if (absMonths != 0)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{absMonths}M");
        }

        if (absWeeks != 0)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{absWeeks}W");
        }

        if (absDays != 0)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{absDays}D");
        }

        var hasTimePart = absHours != 0 || absMinutes != 0 || absSeconds != 0 || totalSubSecondNanos != 0;
        // If fractionalDigits is explicitly set and >= 0, always show time part with seconds
        var forcedPrecision = precision.FractionalDigits >= 0;
        if (hasTimePart || forcedPrecision)
        {
            sb.Append('T');
            if (absHours != 0)
            {
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{absHours}H");
            }

            if (absMinutes != 0)
            {
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{absMinutes}M");
            }

            if (absSeconds != 0 || totalSubSecondNanos != 0 || forcedPrecision)
            {
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{absSeconds}");

                var fractionalDigits = precision.FractionalDigits;
                if (fractionalDigits == -1)
                {
                    // Auto: include fraction only if non-zero, trim trailing zeros
                    if (totalSubSecondNanos != 0)
                    {
                        var fractionStr = totalSubSecondNanos.ToString("D9", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
                        sb.Append('.');
                        sb.Append(fractionStr);
                    }
                }
                else if (fractionalDigits > 0)
                {
                    var fullFraction = totalSubSecondNanos.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
                    sb.Append('.');
                    sb.Append(fullFraction.AsSpan(0, fractionalDigits));
                }
                // fractionalDigits == 0: no fraction

                sb.Append('S');
            }
        }

        // Handle zero duration
        if (sb.Length == 1 || (sb.Length == 2 && sb[0] == '-'))
        {
            return "PT0S";
        }

        return sb.ToString();
    }

    /// <summary>
    /// Rounds a PlainTime for toString and returns the formatted string.
    /// Handles rounding, midnight wrapping, and precision formatting.
    /// </summary>
    private static string RoundAndFormatPlainTime(
        JsTemporalPlainTime time, SecondsStringPrecision precision, string roundingMode)
    {
        var totalNanoseconds = new BigInteger(time.TotalNanoseconds);
        var incrementNanoseconds = new BigInteger(GetUnitNanoseconds(precision.SmallestUnit)) * precision.Increment;
        var rounded = RoundToIncrement(totalNanoseconds, incrementNanoseconds, roundingMode);
        var normalized = PositiveMod(rounded, NanosecondsPerDay);
        var roundedTime = CreatePlainTimeFromNanoseconds((long)normalized);

        return FormatTimeToString(
            roundedTime.Hour, roundedTime.Minute, roundedTime.Second,
            roundedTime.Millisecond, roundedTime.Microsecond, roundedTime.Nanosecond,
            precision.FractionalDigits);
    }

    /// <summary>
    /// Rounds a PlainDateTime for toString and returns the rounded date/time.
    /// Handles date rollover from time rounding.
    /// </summary>
    private static (JsTemporalPlainDateTime RoundedDateTime, int FractionalDigits) RoundPlainDateTimeForToString(
        JsTemporalPlainDateTime dt, SecondsStringPrecision precision, string roundingMode, RealmState realm)
    {
        var totalNanoseconds = ToEpochNanoseconds(dt);
        var incrementNanoseconds = new BigInteger(GetUnitNanoseconds(precision.SmallestUnit)) * precision.Increment;
        var rounded = RoundToIncrement(totalNanoseconds, incrementNanoseconds, roundingMode, treatNegativeAsPositive: true);

        if (rounded < PlainDateTimeMinEpochNanoseconds || rounded > PlainDateTimeMaxEpochNanoseconds)
        {
            throw StandardLibrary.ThrowRangeError("Temporal.PlainDateTime is out of range", realm: realm);
        }

        return (FromEpochNanoseconds(rounded), precision.FractionalDigits);
    }

    #endregion
}
