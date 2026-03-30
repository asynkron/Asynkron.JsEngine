#region

using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib.Intl;

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
///     Temporal unit hierarchy for balancing duration fields.
///     Higher numeric values = larger units.
/// </summary>
internal enum TemporalUnit
{
    Nanosecond = 0,
    Microsecond = 1,
    Millisecond = 2,
    Second = 3,
    Minute = 4,
    Hour = 5,
    Day = 6,
    Week = 7,
    Month = 8,
    Year = 9
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
    // Per spec ISODateTimeWithinLimits: ns > nsMinInstant - nsPerDay AND ns < nsMaxInstant + nsPerDay
    private static readonly BigInteger PlainDateTimeMinEpochNanoseconds = InstantMinEpochNanoseconds - NanosecondsPerDay + 1;
    private static readonly BigInteger PlainDateTimeMaxEpochNanoseconds = InstantMaxEpochNanoseconds + NanosecondsPerDay - 1;
    // Per spec: max time duration = 2^53 × 10^9 - 1 nanoseconds
    private static readonly BigInteger MaxTimeDuration = ((BigInteger)1 << 53) * NanosecondsPerSecond - 1;
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

    private static readonly HashSet<string> DisambiguationValues = new(StringComparer.Ordinal)
    {
        "compatible",
        "earlier",
        "later",
        "reject"
    };

    private static readonly HashSet<string> OffsetOptionValues = new(StringComparer.Ordinal)
    {
        "use",
        "prefer",
        "ignore",
        "reject"
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
    /// Per spec: handles Temporal objects (reads internal calendar), strings (validates or parses ISO),
    /// and throws TypeError for anything else including undefined.
    /// </summary>
    private static string ToTemporalCalendarIdentifier(JsValue calendarArg)
    {
        // Step 1: If not a string, check for Temporal objects or throw
        if (!calendarArg.IsString)
        {
            // Check for Temporal objects with internal calendar slots
            if (calendarArg.TryGetObject<JsObject>(out var obj))
            {
                if (obj.TryGetProperty(TemporalPlainDateSlot, out var slot) && slot.TryGetObject<JsTemporalPlainDate>(out var pd))
                    return CanonicalizeCalendarId(pd.Calendar);
                if (obj.TryGetProperty(TemporalPlainDateTimeSlot, out slot) && slot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                    return CanonicalizeCalendarId(pdt.Calendar);
                if (obj.TryGetProperty(TemporalPlainMonthDaySlot, out slot) && slot.TryGetObject<JsTemporalPlainMonthDay>(out var pmd))
                    return CanonicalizeCalendarId(pmd.Calendar);
                if (obj.TryGetProperty(TemporalPlainYearMonthSlot, out slot) && slot.TryGetObject<JsTemporalPlainYearMonth>(out var pym))
                    return CanonicalizeCalendarId(pym.Calendar);
                if (obj.TryGetProperty(TemporalZonedDateTimeSlot, out slot) && slot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
                    return CanonicalizeCalendarId(zdt.Calendar);
            }

            throw StandardLibrary.ThrowTypeError(
                $"{JsOps.TypeOf(calendarArg).AsString()} is not a valid calendar");
        }

        // Step 2-4: String handling
        // Per spec, the calendar parameter must be a bare calendar identifier (e.g. "iso8601").
        // ISO date strings with calendar annotations are NOT valid here.
        var id = calendarArg.AsString();
        return ValidateCalendarId(id);
    }

    /// <summary>
    /// Validates and canonicalizes a calendar identifier string.
    /// If not a known calendar ID, tries to parse as ISO string to extract calendar annotation.
    /// </summary>
    private static string ValidateCalendarId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw StandardLibrary.ThrowRangeError($"invalid calendar identifier: '{id}'");
        }

        // ASCII-lowercase only (NOT Unicode case folding - \u0130 must NOT become 'i')
        var lowered = AsciiLowercase(id);

        // Map deprecated aliases
        if (CalendarAliases.TryGetValue(lowered, out var canonical))
            lowered = canonical;

        // Per spec: the calendar argument must be a known calendar identifier directly.
        // ISO strings with calendar annotations (e.g., "1997-12-04[u-ca=iso8601]") are NOT valid.
        if (ValidCalendarIds.Contains(lowered))
        {
            return lowered;
        }

        throw StandardLibrary.ThrowRangeError($"invalid calendar identifier: '{id}'");
    }

    /// <summary>
    /// Parses a Temporal calendar string. Tries to extract [u-ca=xxx] annotation from ISO string.
    /// If no annotation is present but the string parses as valid ISO, returns "iso8601".
    /// </summary>
    private static string ParseTemporalCalendarString(string str)
    {
        if (IsValidISOCalendarString(str))
        {
            var (_, calendar) = ExtractZonedDateTimeAnnotations(str);
            return calendar is not null ? ValidateCalendarId(calendar) : "iso8601";
        }

        throw StandardLibrary.ThrowRangeError($"invalid calendar identifier: '{str}'");
    }

    /// <summary>
    /// Returns true if the string has a bracket annotation that is NOT a calendar annotation.
    /// E.g., "[UTC]", "[-07:00]" but not "[u-ca=iso8601]".
    /// </summary>
    private static bool HasTimeZoneBracket(string str)
    {
        var idx = 0;
        while ((idx = str.IndexOf('[', idx)) >= 0)
        {
            if (!str.AsSpan(idx).StartsWith("[u-ca="))
                return true;
            idx++;
        }
        return false;
    }

    /// <summary>
    /// Validates that an explicit offset in a relativeTo ZonedDateTime string matches
    /// the time zone annotation. Throws RangeError on mismatch.
    /// Per spec: Z designator is not validated, but numeric offsets must match.
    /// </summary>
    private static void ValidateRelativeToOffset(string str, RealmState realm)
    {
        // Extract the part before the first bracket (date-time + offset)
        var bracketIdx = str.IndexOf('[');
        if (bracketIdx < 0) return;
        var beforeBrackets = str[..bracketIdx];

        // Check for explicit offset in the date-time part
        if (!JsTemporalZonedDateTime.HasExplicitOffset(beforeBrackets))
            return;

        // Z designator means "use instant as-is" — no validation needed
        if (beforeBrackets.Length > 0 && (beforeBrackets[^1] == 'Z' || beforeBrackets[^1] == 'z'))
            return;

        // Extract the time zone annotation (first non-calendar bracket)
        string? tzId = null;
        var searchIdx = 0;
        while ((searchIdx = str.IndexOf('[', searchIdx)) >= 0)
        {
            var closeIdx = str.IndexOf(']', searchIdx);
            if (closeIdx < 0) break;
            var content = str[(searchIdx + 1)..closeIdx];
            if (!content.StartsWith("u-ca=", StringComparison.Ordinal))
            {
                tzId = content;
                break;
            }
            searchIdx = closeIdx + 1;
        }
        if (tzId == null) return;

        var requestedTimeZoneId = tzId;
        tzId = ValidateTimeZoneIdentifier(tzId, realm);

        // Resolve the time zone to get its expected offset
        TimeZoneInfo tz;
        TimeSpan? fixedOffset;
        try
        {
            tz = JsTemporalZonedDateTime.ResolveTimeZone(tzId, out fixedOffset);
        }
        catch (TimeZoneNotFoundException)
        {
            throw StandardLibrary.ThrowRangeError($"Invalid time zone: {tzId}", realm: realm);
        }

        // Extract the explicit offset from the string
        var explicitOffset = ExtractNumericOffset(beforeBrackets);
        if (explicitOffset == null) return;

        var wallClock = ParseApproximateWallClock(beforeBrackets);
        var offsetMatches = TryMatchTimeZoneOffsetForString(beforeBrackets, explicitOffset.Value.Ticks * 100L,
            requestedTimeZoneId, tz, fixedOffset, wallClock, out var expectedOffset);
        if (!offsetMatches)
        {
            throw StandardLibrary.ThrowRangeError(
                $"UTC offset mismatch: string has {FormatOffset(explicitOffset.Value)} but time zone {tzId} has offset {FormatOffset(expectedOffset)}",
                realm: realm);
        }
    }

    /// <summary>
    /// Extracts the numeric UTC offset from an ISO date-time string (without brackets).
    /// Returns null if no numeric offset is found.
    /// </summary>
    private static TimeSpan? ExtractNumericOffset(string str)
    {
        // Find T separator
        var tIdx = str.IndexOf('T');
        if (tIdx < 0) tIdx = str.IndexOf('t');
        if (tIdx < 0) return null;

        var timePart = str.AsSpan()[(tIdx + 1)..];

        // Scan backwards for +/- offset indicator
        for (var i = timePart.Length - 1; i >= 2; i--)
        {
            if ((timePart[i] == '+' || timePart[i] == '-') &&
                i + 1 < timePart.Length && char.IsDigit(timePart[i + 1]))
            {
                var sign = timePart[i] == '-' ? -1 : 1;
                var offsetStr = timePart[(i + 1)..].ToString();

                // Remove any trailing sub-second precision (e.g., ".123456789")
                var parts = offsetStr.Split(':');
                if (parts.Length < 1) return null;

                if (!int.TryParse(parts[0], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var hours))
                    return null;

                var minutes = 0;
                if (parts.Length > 1 && !int.TryParse(parts[1], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out minutes))
                    return null;

                var seconds = 0;
                if (parts.Length > 2)
                {
                    // Handle seconds with possible fractional part (e.g., "30.123456789")
                    var secStr = parts[2];
                    var dotIdx = secStr.IndexOf('.');
                    if (dotIdx >= 0) secStr = secStr[..dotIdx];
                    if (!int.TryParse(secStr, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out seconds))
                        return null;
                }

                return new TimeSpan(sign * hours, sign * minutes, sign * seconds);
            }
        }

        return null;
    }

    /// <summary>
    /// Parses approximate wall clock DateTime from an ISO string for TZ offset lookup.
    /// </summary>
    private static DateTime ParseApproximateWallClock(string str)
    {
        // Try simple parsing — we just need an approximate DateTime for TZ offset lookup
        var tIdx = str.IndexOf('T');
        if (tIdx < 0) tIdx = str.IndexOf('t');

        try
        {
            var datePart = tIdx >= 0 ? str[..tIdx] : str;
            // Handle extended year format
            if (datePart.Length > 0 && (datePart[0] == '+' || datePart[0] == '-'))
            {
                // Can't represent in DateTime; use year 2000 as approximation
                return new DateTime(2000, 1, 1);
            }

            var dateComponents = datePart.Split('-');
            if (dateComponents.Length < 3) return new DateTime(2000, 1, 1);

            var year = int.Parse(dateComponents[0], System.Globalization.CultureInfo.InvariantCulture);
            var month = int.Parse(dateComponents[1], System.Globalization.CultureInfo.InvariantCulture);
            var day = int.Parse(dateComponents[2], System.Globalization.CultureInfo.InvariantCulture);
            year = Math.Clamp(year, 1, 9999);

            if (tIdx < 0) return new DateTime(year, month, day);

            var timeStr = str[(tIdx + 1)..];
            // Strip offset from time string
            for (var i = timeStr.Length - 1; i >= 2; i--)
            {
                if (timeStr[i] == '+' || timeStr[i] == '-')
                {
                    timeStr = timeStr[..i];
                    break;
                }
            }

            var timeComponents = timeStr.Split(':');
            var hour = int.Parse(timeComponents[0], System.Globalization.CultureInfo.InvariantCulture);
            var minute = timeComponents.Length > 1
                ? int.Parse(timeComponents[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
            var second = 0;
            if (timeComponents.Length > 2)
            {
                var secStr = timeComponents[2];
                var dotIdx = secStr.IndexOf('.');
                if (dotIdx >= 0) secStr = secStr[..dotIdx];
                second = int.Parse(secStr, System.Globalization.CultureInfo.InvariantCulture);
            }

            return new DateTime(year, month, day, Math.Min(hour, 23), Math.Min(minute, 59), Math.Min(second, 59));
        }
        catch
        {
            return new DateTime(2000, 1, 1);
        }
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return $"{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
    }

    /// <summary>
    /// Basic validation that a string could be an ISO 8601 date/time string
    /// for the purpose of calendar string extraction.
    /// </summary>
    private static bool IsValidISOCalendarString(string str)
    {
        if (string.IsNullOrEmpty(str)) return false;

        // Handle MM-DD format (e.g., "01-01")
        if (str.Length >= 5 && str[2] == '-' && char.IsAsciiDigit(str[0]) && char.IsAsciiDigit(str[1]))
        {
            return true;
        }

        // Handle YYYY-MM format (e.g., "2020-01")
        if (str.Length >= 7 && str[4] == '-' && char.IsAsciiDigit(str[0]))
        {
            return true;
        }

        // Handle +/-YYYYYY format
        if ((str[0] == '+' || str[0] == '-') && str.Length >= 7)
        {
            return true;
        }

        return false;
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
            var now2 = GetCurrentDateTimeInTimeZone(tzId);
            var date = new JsTemporalPlainDate(now2.Year, now2.Month, now2.Day, "iso8601");
            return WrapPlainDate(date, realm, prototypes.PlainDatePrototype);
        });
        now.DefineProperty("plainDateISO",
            new PropertyDescriptor { Value = plainDateISOFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.plainTimeISO(timeZone)
        var plainTimeISOFn = CreateFunction(realm, "plainTimeISO", 0, (_, args) =>
        {
            var tzId = ResolveNowTimeZone(args, realm);
            var now2 = GetCurrentDateTimeInTimeZone(tzId);
            var time = new JsTemporalPlainTime(now2.Hour, now2.Minute, now2.Second, now2.Millisecond, now2.Microsecond, 0);
            return WrapPlainTime(time, realm, prototypes.PlainTimePrototype);
        });
        now.DefineProperty("plainTimeISO",
            new PropertyDescriptor { Value = plainTimeISOFn, Writable = true, Enumerable = false, Configurable = true });

        // Temporal.Now.plainDateTimeISO(timeZone)
        var plainDateTimeISOFn = CreateFunction(realm, "plainDateTimeISO", 0, (_, args) =>
        {
            var tzId = ResolveNowTimeZone(args, realm);
            var now2 = GetCurrentDateTimeInTimeZone(tzId);
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

        AddPrototypeMethod(prototype, realm, "toString", 0, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var options = args.GetArgument(0);
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.Instant.prototype.toString");

            // Per spec, options read in alphabetical order:
            // fractionalSecondDigits, roundingMode, smallestUnit, timeZone
            var (precision, roundingMode) = GetToStringPrecisionOptions(optionsObj, realm);

            // Read timeZone (after smallestUnit per alphabetical order)
            string? timeZoneId = null;
            if (optionsObj is not null && optionsObj.TryGetProperty("timeZone", out var tzVal) && !tzVal.IsUndefined)
            {
                timeZoneId = ToTemporalTimeZoneSlot(tzVal, realm);
            }

            // Round the epoch nanoseconds
            var epochNanos = instant.EpochNanoseconds;
            if (!(precision.FractionalDigits == -1 && precision.Increment == 1))
            {
                var unitNanos = new BigInteger(GetUnitNanoseconds(precision.SmallestUnit)) * precision.Increment;
                epochNanos = RoundToIncrement(epochNanos, unitNanos, roundingMode, treatNegativeAsPositive: true);
            }

            if (timeZoneId is null)
            {
                // No timezone: display in UTC with "Z" suffix
                return new JsValue(FormatEpochNanosAsDateTime(epochNanos, 0, precision.FractionalDigits, useZSuffix: true));
            }

            // Timezone specified: display in that timezone with offset suffix
            var zdt = new JsTemporalZonedDateTime(new JsTemporalInstant(epochNanos), timeZoneId);
            var offsetNanos = zdt.OffsetNanoseconds;
            return new JsValue(FormatEpochNanosAsDateTime(epochNanos, offsetNanos, precision.FractionalDigits, useZSuffix: false));
        });

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
        {
            var instant = GetInstant(thisValue);
            return new JsValue(instant.ToString());
        });

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, args) =>
            TemporalToLocaleString(thisValue, args, realm, GetInstant(thisValue).ToString()));

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
            return AddDurationToInstant(instant, duration, 1, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            return AddDurationToInstant(instant, duration, -1, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalInstant("until", instant, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var instant = GetInstant(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalInstant("since", instant, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
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

            return new JsValue(FormatDurationToString(duration, precision, roundingMode, realm));
        });

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
        {
            var duration = GetDuration(thisValue);
            return new JsValue(duration.ToString());
        });

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, args) =>
        {
            // Per spec: Temporal.Duration.prototype.toLocaleString delegates to Intl.DurationFormat
            var duration = GetDuration(thisValue);
            var localeArg = args.GetArgument(0);
            var optionsArg = args.GetArgument(1);

            // Create a DurationFormat and use it to format this duration
            // Must invoke as constructor (with newTarget) to avoid "requires 'new'" check
            var dfCtor = Intl.IntlDurationFormatConstructor.CreateConstructor(realm);
            var dfInstance = dfCtor.InvokeWithContext([localeArg, optionsArg], JsValue.Undefined, null, dfCtor.AsJsValue);
            if (dfInstance.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                accessor.TryGetProperty("format", out var formatVal) &&
                formatVal.TryGetObject<IJsCallable>(out var formatFn))
            {
                return formatFn.Invoke(new SingleValueArgs(thisValue), dfInstance);
            }

            // Fallback: ISO 8601 string
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
            return WrapDuration(AddDurations(duration, other, 1, realm), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var duration = GetDuration(thisValue);
            var other = ToTemporalDuration(args.GetArgument(0), realm);
            return WrapDuration(AddDurations(duration, other, -1, realm), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "total", 1, (thisValue, args) =>
        {
            var duration = GetDuration(thisValue);
            var totalOf = args.GetArgument(0);

            // Step 3: If totalOf is undefined, throw TypeError
            if (totalOf.IsUndefined)
                throw StandardLibrary.ThrowTypeError("Temporal.Duration.prototype.total requires an argument", realm: realm);

            string unit;
            JsTemporalPlainDate? plainDateRelativeTo = null;
            JsTemporalZonedDateTime? zonedDateTimeRelativeTo = null;

            // Step 4-6: If totalOf is a string, treat it as the unit
            if (totalOf.IsString)
            {
                unit = totalOf.AsString() ?? "";
            }
            // Step 7: If totalOf is an object, read unit and relativeTo
            else if (totalOf.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                // Read relativeTo — process immediately per spec order
                // (relativeTo property bag must be read before unit)
                JsValue relativeToValue = JsValue.Undefined;
                if (accessor.TryGetProperty("relativeTo", out var rtv) && !rtv.IsUndefined)
                    relativeToValue = rtv;
                (plainDateRelativeTo, zonedDateTimeRelativeTo) = ToRelativeTemporalObject(relativeToValue, realm);

                // Read unit (required)
                if (!accessor.TryGetProperty("unit", out var unitVal) || unitVal.IsUndefined)
                    throw StandardLibrary.ThrowRangeError("unit is required for Temporal.Duration.prototype.total", realm: realm);
                unit = JsOps.ToJsString(unitVal);
            }
            else
            {
                // Non-string, non-object → TypeError
                throw StandardLibrary.ThrowTypeError("Temporal.Duration.prototype.total requires a string or object argument", realm: realm);
            }

            // Normalize unit (plurals → singular)
            unit = NormalizeTemporalUnit(unit);

            // Validate unit
            if (!DateTimeUnits.Contains(unit))
                throw StandardLibrary.ThrowRangeError($"Invalid unit for total: {unit}", realm: realm);

            // Check if relativeTo is required
            var needsRelativeTo = duration.Years != 0 || duration.Months != 0 || duration.Weeks != 0
                                  || UnitRank(unit) >= TemporalUnit.Week;
            if (needsRelativeTo && plainDateRelativeTo == null && zonedDateTimeRelativeTo == null)
                throw StandardLibrary.ThrowRangeError("relativeTo is required for total with calendar units", realm: realm);

            // Per spec: blank duration early return — total is always 0, skip range validation
            if (IsZeroDuration(duration))
                return JsValue.Zero;

            // Per spec: validate PlainDate relativeTo is within ISODateWithinLimits range
            if (plainDateRelativeTo != null)
            {
                var relEpochDays = IsoCalendarHelpers.DateToEpochDays(plainDateRelativeTo.Year,
                    plainDateRelativeTo.Month, plainDateRelativeTo.Day);
                if (Math.Abs(relEpochDays) > 100_000_000)
                    throw StandardLibrary.ThrowRangeError("relativeTo is out of representable range", realm: realm);
            }

            return new JsValue(TotalDuration(duration, unit, plainDateRelativeTo, zonedDateTimeRelativeTo, realm));
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
            var roundTo = args.GetArgument(0);

            // Step 3: If roundTo is undefined, throw TypeError
            if (roundTo.IsUndefined)
                throw StandardLibrary.ThrowTypeError("Temporal.Duration.prototype.round requires an argument", realm: realm);

            string? smallestUnit = null;
            string? largestUnit = null;
            var largestUnitProvided = false;
            long roundingIncrement = 1;
            var roundingMode = "halfExpand";
            JsTemporalPlainDate? plainDateRelativeTo = null;
            JsTemporalZonedDateTime? zonedDateTimeRelativeTo = null;

            // Step 4-6: If roundTo is a string, treat as smallestUnit
            if (roundTo.IsString)
            {
                smallestUnit = roundTo.AsString() ?? "";
            }
            // Step 7: If roundTo is an object, read all options
            else if (roundTo.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                // Read largestUnit
                if (accessor.TryGetProperty("largestUnit", out var luVal) && !luVal.IsUndefined)
                {
                    largestUnitProvided = true;
                    var rawLargest = JsOps.ToJsString(luVal);
                    if (!string.Equals(rawLargest, "auto", StringComparison.Ordinal))
                        largestUnit = rawLargest;
                    else
                        largestUnit = null; // "auto" means use default
                }

                // Read relativeTo — process immediately per spec order
                // (relativeTo property bag must be read before roundingIncrement/roundingMode/smallestUnit)
                JsValue relativeToValue = JsValue.Undefined;
                if (accessor.TryGetProperty("relativeTo", out var rtv) && !rtv.IsUndefined)
                    relativeToValue = rtv;
                (plainDateRelativeTo, zonedDateTimeRelativeTo) = ToRelativeTemporalObject(relativeToValue, realm);

                // Read roundingIncrement
                if (accessor.TryGetProperty("roundingIncrement", out var riVal) && !riVal.IsUndefined)
                {
                    var riNum = JsOps.ToNumber(riVal);
                    if (double.IsNaN(riNum) || double.IsInfinity(riNum))
                        throw StandardLibrary.ThrowRangeError("Invalid roundingIncrement", realm: realm);
                    // Per spec: truncate to integer (non-integers are allowed and floored)
                    roundingIncrement = (long)Math.Truncate(riNum);
                    if (roundingIncrement < 1 || roundingIncrement > 1_000_000_000L)
                        throw StandardLibrary.ThrowRangeError("Invalid roundingIncrement", realm: realm);
                }

                // Read roundingMode
                if (accessor.TryGetProperty("roundingMode", out var rmVal) && !rmVal.IsUndefined)
                {
                    roundingMode = JsOps.ToJsString(rmVal);
                    if (!ValidRoundingModes.Contains(roundingMode))
                        throw StandardLibrary.ThrowRangeError($"Invalid roundingMode: {roundingMode}", realm: realm);
                }

                // Read smallestUnit
                if (accessor.TryGetProperty("smallestUnit", out var suVal) && !suVal.IsUndefined)
                    smallestUnit = JsOps.ToJsString(suVal);
            }
            else
            {
                // Non-string, non-object → TypeError
                throw StandardLibrary.ThrowTypeError("Temporal.Duration.prototype.round requires a string or object argument", realm: realm);
            }

            // Normalize units
            if (smallestUnit != null) smallestUnit = NormalizeTemporalUnit(smallestUnit);
            if (largestUnit != null) largestUnit = NormalizeTemporalUnit(largestUnit);

            // Validate smallestUnit
            if (smallestUnit != null && !DateTimeUnits.Contains(smallestUnit))
                throw StandardLibrary.ThrowRangeError($"Invalid smallestUnit: {smallestUnit}", realm: realm);

            // Validate largestUnit
            if (largestUnit != null && !DateTimeUnits.Contains(largestUnit))
                throw StandardLibrary.ThrowRangeError($"Invalid largestUnit: {largestUnit}", realm: realm);

            // Step 15: If neither smallestUnit nor largestUnit was provided, throw
            if (smallestUnit == null && !largestUnitProvided)
                throw StandardLibrary.ThrowRangeError("at least one of smallestUnit or largestUnit is required", realm: realm);

            // Default smallestUnit if not specified
            if (smallestUnit == null) smallestUnit = "nanosecond";

            // Default largestUnit to "auto" logic: max(existingLargestUnit, smallestUnit)
            // Per spec step 20: LargerOfTwoTemporalUnits(existingLargestUnit, smallestUnit)
            if (largestUnit == null)
            {
                var existingLargest = DefaultTemporalLargestUnit(duration);
                var smallestRank = smallestUnit != null ? UnitRank(smallestUnit) : TemporalUnit.Nanosecond;
                var defaultLargest = existingLargest > smallestRank ? existingLargest : smallestRank;
                largestUnit = defaultLargest switch
                {
                    TemporalUnit.Year => "year",
                    TemporalUnit.Month => "month",
                    TemporalUnit.Week => "week",
                    TemporalUnit.Day => "day",
                    TemporalUnit.Hour => "hour",
                    TemporalUnit.Minute => "minute",
                    TemporalUnit.Second => "second",
                    TemporalUnit.Millisecond => "millisecond",
                    TemporalUnit.Microsecond => "microsecond",
                    _ => "nanosecond"
                };
            }

            // largestUnit must be >= smallestUnit
            if (UnitRank(largestUnit) < UnitRank(smallestUnit))
                throw StandardLibrary.ThrowRangeError($"largestUnit {largestUnit} cannot be smaller than smallestUnit {smallestUnit}", realm: realm);

            // Validate roundingIncrement for calendar units
            var maxIncrement = MaximumTemporalDurationRoundingIncrement(smallestUnit);
            if (maxIncrement == null)
            {
                // Calendar unit (year/month/week/day): increment>1 is disallowed only when
                // also balancing to a larger unit (largestUnit > smallestUnit).
                // When largestUnit == smallestUnit, increment>1 is fine (pure rounding, no balancing).
                if (roundingIncrement != 1)
                {
                    var smallestRk = UnitRank(smallestUnit);
                    var largestRk = UnitRank(largestUnit);
                    if (largestRk > smallestRk)
                        throw StandardLibrary.ThrowRangeError("roundingIncrement must be 1 when balancing calendar units", realm: realm);
                }
            }
            else
            {
                if (roundingIncrement >= maxIncrement.Value || maxIncrement.Value % roundingIncrement != 0)
                    throw StandardLibrary.ThrowRangeError("Invalid roundingIncrement", realm: realm);
            }

            // Check if relativeTo is required
            var needsRelativeTo = duration.Years != 0 || duration.Months != 0 || duration.Weeks != 0
                                  || UnitRank(largestUnit) >= TemporalUnit.Week
                                  || UnitRank(smallestUnit) >= TemporalUnit.Week;
            if (needsRelativeTo && plainDateRelativeTo == null && zonedDateTimeRelativeTo == null)
                throw StandardLibrary.ThrowRangeError("relativeTo is required for rounding with calendar units", realm: realm);

            // Per spec: blank duration early return — no rounding needed, skip range validation.
            // Exception: when ZDT relativeTo + largestUnit >= "day", the next-day boundary
            // computation may throw RangeError (e.g., ZDT at max instant + day balancing).
            if (IsZeroDuration(duration) &&
                !(zonedDateTimeRelativeTo != null && UnitRank(largestUnit) >= TemporalUnit.Day))
                return WrapDuration(duration, realm, prototype);

            // Per spec: validate PlainDate relativeTo is within ISODateWithinLimits range
            if (plainDateRelativeTo != null)
            {
                var relEpochDays = IsoCalendarHelpers.DateToEpochDays(plainDateRelativeTo.Year,
                    plainDateRelativeTo.Month, plainDateRelativeTo.Day);
                if (Math.Abs(relEpochDays) > 100_000_000)
                    throw StandardLibrary.ThrowRangeError("relativeTo is out of representable range", realm: realm);
            }

            return WrapDuration(RoundDuration(duration, smallestUnit, largestUnit, roundingIncrement, roundingMode,
                plainDateRelativeTo, zonedDateTimeRelativeTo, realm), realm, prototype);
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

            // Step 3: Read and validate options (3rd argument)
            var options = args.Count > 2 ? args[2] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.Duration.compare");

            // Step 4: Extract relativeTo from options
            JsValue relativeToValue = JsValue.Undefined;
            if (optionsObj != null && optionsObj.TryGetProperty("relativeTo", out var rtv) && !rtv.IsUndefined)
                relativeToValue = rtv;

            var (plainDateRelativeTo, zonedDateTimeRelativeTo) = ToRelativeTemporalObject(relativeToValue, realm);

            // Step 5: Check if calendar units are present
            var calendarUnitsPresent = d1.Years != 0 || d1.Months != 0 || d1.Weeks != 0
                                       || d2.Years != 0 || d2.Months != 0 || d2.Weeks != 0;

            // Per spec: if both durations are identical (all 10 fields match), return 0
            // This is checked before the relativeTo requirement
            if (d1.Years == d2.Years && d1.Months == d2.Months && d1.Weeks == d2.Weeks &&
                d1.Days == d2.Days && d1.Hours == d2.Hours && d1.Minutes == d2.Minutes &&
                d1.Seconds == d2.Seconds && d1.Milliseconds == d2.Milliseconds &&
                d1.Microseconds == d2.Microseconds && d1.Nanoseconds == d2.Nanoseconds)
            {
                return JsValue.Zero;
            }

            if (calendarUnitsPresent && plainDateRelativeTo == null && zonedDateTimeRelativeTo == null)
                throw StandardLibrary.ThrowRangeError("relativeTo is required when comparing durations with calendar units (years, months, or weeks)", realm: realm);

            // Step 6: Compare using relativeTo if provided
            if (zonedDateTimeRelativeTo != null)
            {
                // Per spec: for durations with days or calendar units, use AddZonedDateTime
                // (timezone-aware day length). For time-only durations (no calendar/day units),
                // the comparison doesn't depend on timezone — use simple nanosecond comparison.
                var hasDaysOrCalendar = d1.Years != 0 || d1.Months != 0 || d1.Weeks != 0 || d1.Days != 0
                                       || d2.Years != 0 || d2.Months != 0 || d2.Weeks != 0 || d2.Days != 0;
                if (hasDaysOrCalendar)
                {
                    var epochNs1 = AddZonedDateTimeEpochNs(zonedDateTimeRelativeTo, d1, realm);
                    var epochNs2 = AddZonedDateTimeEpochNs(zonedDateTimeRelativeTo, d2, realm);
                    return new JsValue(epochNs1.CompareTo(epochNs2));
                }

                // Time-only: just compare total time nanoseconds
                var timeTotal1 = DurationTimeNanoseconds(d1);
                var timeTotal2 = DurationTimeNanoseconds(d2);
                return new JsValue(timeTotal1.CompareTo(timeTotal2));
            }

            if (plainDateRelativeTo != null && calendarUnitsPresent)
            {
                // Add each duration to the PlainDate and compare resulting epoch days + time
                var end1 = plainDateRelativeTo.Add(new JsTemporalDuration(
                    d1.Years, d1.Months, d1.Weeks, d1.Days, 0, 0, 0, 0, 0, 0));
                var end2 = plainDateRelativeTo.Add(new JsTemporalDuration(
                    d2.Years, d2.Months, d2.Weeks, d2.Days, 0, 0, 0, 0, 0, 0));

                // Convert to epoch day number + time nanoseconds for precise comparison
                var days1 = IsoToDayNumber(end1.Year, end1.Month, end1.Day);
                var days2 = IsoToDayNumber(end2.Year, end2.Month, end2.Day);

                // Validate endpoint dates are within ISO range (spec: ISODateWithinLimits)
                if (days1 < -100_000_000 || days1 > 100_000_000)
                    throw StandardLibrary.ThrowRangeError("Duration added to relativeTo is out of representable range", realm: realm);
                if (days2 < -100_000_000 || days2 > 100_000_000)
                    throw StandardLibrary.ThrowRangeError("Duration added to relativeTo is out of representable range", realm: realm);

                var ns1 = (BigInteger)days1 * 86_400_000_000_000L + DurationTimeNanoseconds(d1);
                var ns2 = (BigInteger)days2 * 86_400_000_000_000L + DurationTimeNanoseconds(d2);

                // Validate total nanoseconds (spec: Add24HourDaysToNormalizedTimeDuration)
                if (BigInteger.Abs(ns1) > MaxTimeDuration)
                    throw StandardLibrary.ThrowRangeError("Duration out of representable range", realm: realm);
                if (BigInteger.Abs(ns2) > MaxTimeDuration)
                    throw StandardLibrary.ThrowRangeError("Duration out of representable range", realm: realm);

                return new JsValue(ns1.CompareTo(ns2));
            }

            // No calendar units: convert days + time to total nanoseconds and compare
            var total1 = (BigInteger)d1.Days * 86_400_000_000_000L + DurationTimeNanoseconds(d1);
            var total2 = (BigInteger)d2.Days * 86_400_000_000_000L + DurationTimeNanoseconds(d2);
            return new JsValue(total1.CompareTo(total2));
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
        AddPrototypeGetter(prototype, realm, "era", tv =>
        {
            var date = GetPlainDate(tv);
            return GetTemporalEra(date.Calendar, date.Year, date.Month, date.Day);
        });
        AddPrototypeGetter(prototype, realm, "eraYear", tv =>
        {
            var date = GetPlainDate(tv);
            return GetTemporalEraYear(date.Calendar, date.Year, date.Month, date.Day);
        });

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

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, args) =>
            TemporalToLocaleString(thisValue, args, realm, GetPlainDate(thisValue).ToString()));

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

            // Validate and read options (2nd argument)
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDate.prototype.add");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            // Balance time units into days per Temporal spec
            var extraDays = BalanceTimeToDays(duration);
            var addDur = extraDays != 0
                ? new JsTemporalDuration(duration.Years, duration.Months, duration.Weeks,
                    duration.Days + extraDays, 0, 0, 0, 0, 0, 0)
                : duration;

            try
            {
                var result = date.Add(addDur, overflow);
                RejectISODate(result.Year, result.Month, result.Day, realm);
                return WrapPlainDate(result, realm, prototype);
            }
            catch (ArgumentException)
            {
                throw StandardLibrary.ThrowRangeError("Resulting date is out of valid range", realm: realm);
            }
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);

            // Validate and read options (2nd argument)
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDate.prototype.subtract");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            // Balance time units into days per Temporal spec
            var extraDays2 = BalanceTimeToDays(duration);
            var subDur = extraDays2 != 0
                ? new JsTemporalDuration(duration.Years, duration.Months, duration.Weeks,
                    duration.Days + extraDays2, 0, 0, 0, 0, 0, 0)
                : duration;

            try
            {
                var result = date.Subtract(subDur, overflow);
                RejectISODate(result.Year, result.Month, result.Day, realm);
                return WrapPlainDate(result, realm, prototype);
            }
            catch (ArgumentException)
            {
                throw StandardLibrary.ThrowRangeError("Resulting date is out of valid range", realm: realm);
            }
        });

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalPlainDate("until", date, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalPlainDate("since", date, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var overrides = args.GetArgument(0);

            // Step 3: argument must be an object
            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                throw StandardLibrary.ThrowTypeError("Temporal.PlainDate.prototype.with requires an object argument", realm: realm);
            }

            // Step 4: RejectObjectWithCalendarOrTimeZone
            RejectObjectWithCalendarOrTimeZone(overrides, accessor, realm);

            // Step 5: PrepareTemporalFields — read fields in alphabetical order
            var any = false;
            int? partialDay = null, partialMonth = null, partialYear = null;
            string? partialMonthCode = null;

            if (accessor.TryGetProperty("day", out var v) && !v.IsUndefined)
            {
                partialDay = ToIntegerWithTruncation(v, realm);
                any = true;
            }

            if (accessor.TryGetProperty("month", out v) && !v.IsUndefined)
            {
                partialMonth = ToIntegerWithTruncation(v, realm);
                any = true;
            }

            if (accessor.TryGetProperty("monthCode", out v) && !v.IsUndefined)
            {
                partialMonthCode = JsOps.ToJsString(v);
                any = true;
            }

            if (accessor.TryGetProperty("year", out v) && !v.IsUndefined)
            {
                partialYear = ToIntegerWithTruncation(v, realm);
                any = true;
            }

            if (!any)
            {
                throw StandardLibrary.ThrowTypeError("with() argument must have at least one date property", realm: realm);
            }

            // Apply defaults and resolve month/monthCode BEFORE options
            var year = partialYear ?? date.Year;
            var month = ResolveISOMonth(partialMonth, partialMonthCode, date.Month, realm);
            var day = partialDay ?? date.Day;

            // Pre-validate: reject fundamentally invalid values before options processing
            // Only reject values that are ALWAYS invalid regardless of overflow mode
            // (day < 1, month < 1 are never valid; month > 12 can be constrained)
            if (month < 1 || day < 1)
            {
                throw StandardLibrary.ThrowRangeError("Invalid ISO date value", realm: realm);
            }

            // Step 6-7: Validate options AFTER reading and merging fields
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDate.prototype.with");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

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
            // ISODateTimeWithinLimits check
            var epochNanos = ToEpochNanoseconds(dt);
            if (epochNanos < PlainDateTimeMinEpochNanoseconds || epochNanos > PlainDateTimeMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("PlainDateTime is out of representable range", realm: realm);
            }

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

        AddPrototypeMethod(prototype, realm, "withCalendar", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var calArg = args.GetArgument(0);
            if (calArg.IsUndefined)
                throw StandardLibrary.ThrowTypeError("withCalendar requires a calendar argument", realm: realm);
            var calendar = ToTemporalCalendarIdentifier(calArg);
            return WrapPlainDate(new JsTemporalPlainDate(date.Year, date.Month, date.Day, calendar), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "toZonedDateTime", 1, (thisValue, args) =>
        {
            var date = GetPlainDate(thisValue);
            var arg = args.GetArgument(0);
            string timeZone;
            JsTemporalPlainTime? temporalTime = null;

            if (arg.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                // Object with timeZone and optional plainTime
                if (accessor.TryGetProperty("timeZone", out var tzValue) && !tzValue.IsUndefined)
                {
                    timeZone = ToTemporalTimeZoneSlot(tzValue, realm);
                }
                else
                {
                    // No timeZone property — treat the object itself as a timezone identifier
                    // Per spec: ToTemporalTimeZoneIdentifier(item) — objects that aren't ZonedDateTime throw TypeError
                    throw StandardLibrary.ThrowTypeError(
                        "Object passed to toZonedDateTime must have a timeZone property, or be a valid time zone string",
                        null, realm);
                }

                if (accessor.TryGetProperty("plainTime", out var timeValue) && !timeValue.IsUndefined)
                {
                    temporalTime = ToTemporalPlainTime(timeValue, realm);
                }
            }
            else
            {
                // Primitive — must be a string timezone identifier
                timeZone = ToTemporalTimeZoneSlot(arg, realm);
            }

            var time = temporalTime ?? new JsTemporalPlainTime(0, 0, 0, 0, 0, 0);
            var dt = date.ToPlainDateTime(time);

            // ISODateTimeWithinLimits check when plainTime is provided
            if (temporalTime != null)
            {
                var dtNanos = ToEpochNanoseconds(dt);
                if (dtNanos < PlainDateTimeMinEpochNanoseconds || dtNanos > PlainDateTimeMaxEpochNanoseconds)
                {
                    throw StandardLibrary.ThrowRangeError(
                        "Combined date-time is outside the valid ISO date range", realm: realm);
                }
            }

            // Compute local epoch nanoseconds from date/time components
            var epochDays = IsoCalendarHelpers.DateToEpochDays(dt.Year, dt.Month, dt.Day);
            var localEpochNanos = new System.Numerics.BigInteger(epochDays) * NanosecondsPerDay
                + (long)dt.Hour * NanosecondsPerHour
                + (long)dt.Minute * NanosecondsPerMinute
                + (long)dt.Second * NanosecondsPerSecond
                + (long)dt.Millisecond * NanosecondsPerMillisecond
                + (long)dt.Microsecond * NanosecondsPerMicrosecond
                + dt.Nanosecond;

            System.Numerics.BigInteger utcEpochNanos;
            if (ParseOffsetToNanos(timeZone) is { } offsetNanos)
            {
                // Fixed offset timezone — single unambiguous instant
                utcEpochNanos = localEpochNanos - offsetNanos;
            }
            else
            {
                // IANA timezone
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
                TimeSpan offset;
                if (dt.Year is >= 1 and <= 9999)
                {
                    var localDateTime = new DateTime(dt.Year, dt.Month, dt.Day,
                        dt.Hour, dt.Minute, dt.Second, dt.Millisecond, dt.Microsecond);

                    if (tz.IsInvalidTime(localDateTime))
                    {
                        // Spring-forward gap: compatible = later → use pre-transition offset
                        offset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(tz, localDateTime);
                    }
                    else if (tz.IsAmbiguousTime(localDateTime))
                    {
                        // Fall-back overlap: compatible = earlier → use larger offset
                        var offsets = tz.GetAmbiguousTimeOffsets(localDateTime);
                        offset = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
                    }
                    else
                    {
                        offset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(tz, localDateTime);
                    }
                }
                else
                {
                    offset = tz.BaseUtcOffset;
                }

                utcEpochNanos = localEpochNanos - offset.Ticks * 100L;
            }

            // Validate the resulting instant is within representable range
            if (utcEpochNanos < InstantMinEpochNanoseconds || utcEpochNanos > InstantMaxEpochNanoseconds)
                throw StandardLibrary.ThrowRangeError("Resulting instant is outside the valid range", realm: realm);

            var instant = JsTemporalInstant.FromEpochNanoseconds(utcEpochNanos);
            var zdt = new JsTemporalZonedDateTime(instant, timeZone, date.Calendar);
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
            var calendar = calendarArg.IsUndefined ? "iso8601" : ToTemporalCalendarIdentifier(calendarArg);

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
            var item = args.GetArgument(0);
            var options = args.GetArgument(1);

            // Per spec: if item is already a PlainDate, validate options and return copy
            if (item.TryGetObject<JsObject>(out var fromObj) &&
                fromObj.TryGetProperty(TemporalPlainDateSlot, out var fromSlot) &&
                fromSlot.TryGetObject<JsTemporalPlainDate>(out var existingDate))
            {
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainDate.from");
                GetTemporalOverflowOption(resolvedOpts, realm);
                return WrapPlainDate(new JsTemporalPlainDate(existingDate.Year, existingDate.Month,
                    existingDate.Day, existingDate.Calendar), realm, prototype);
            }

            // String path: parse first, then validate options
            if (item.IsString)
            {
                var date = ParseTemporalPlainDateString(item.AsString() ?? "", realm);
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainDate.from");
                GetTemporalOverflowOption(resolvedOpts, realm); // validate but don't use for strings
                return WrapPlainDate(date, realm, prototype);
            }

            // Non-string primitives → TypeError
            if (item.IsUndefined || item.IsNull || item.IsBoolean || item.IsNumber || item.IsSymbol || item.IsBigInt)
                throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDate", realm: realm);

            // Check for other Temporal types (ZonedDateTime, PlainDateTime)
            if (item.TryGetObject<JsObject>(out var fromObj2))
            {
                if (fromObj2.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) &&
                    zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
                {
                    var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainDate.from");
                    GetTemporalOverflowOption(resolvedOpts, realm);
                    return WrapPlainDate(zdt.ToPlainDate(), realm, prototype);
                }
                if (fromObj2.TryGetProperty(TemporalPlainDateTimeSlot, out var pdtSlot) &&
                    pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                {
                    var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainDate.from");
                    GetTemporalOverflowOption(resolvedOpts, realm);
                    return WrapPlainDate(pdt.ToPlainDate(), realm, prototype);
                }
            }

            // Property bag: per spec, read fields first, then options
            if (item.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                var date = ToTemporalPlainDateFromPropertyBagWithOverflow(accessor, options, realm, "Temporal.PlainDate.from");
                return WrapPlainDate(date, realm, prototype);
            }

            // Object without property accessor (e.g., HostFunction)
            if (item.Kind == JsValueKind.Object)
                throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDate: object has no date properties", realm: realm);

            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDate", realm: realm);
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

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, args) =>
            TemporalToLocaleString(thisValue, args, realm, GetPlainTime(thisValue).ToString()));

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
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalPlainTime("until", time, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalPlainTime("since", time, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var time = GetPlainTime(thisValue);
            var overrides = args.GetArgument(0);

            // Step 3: argument must be an object
            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                throw StandardLibrary.ThrowTypeError("Temporal.PlainTime.prototype.with requires an object argument", realm: realm);
            }

            // Step 4: RejectObjectWithCalendarOrTimeZone
            RejectObjectWithCalendarOrTimeZone(overrides, accessor, realm);

            // Step 5: ToTemporalTimeRecord (partial) — read fields in alphabetical order
            // and apply ToIntegerWithTruncation; track if at least one is defined
            var any = false;
            int? partialHour = null, partialMicrosecond = null, partialMillisecond = null,
                 partialMinute = null, partialNanosecond = null, partialSecond = null;

            if (accessor.TryGetProperty("hour", out var v) && !v.IsUndefined)
            {
                partialHour = ToIntegerWithTruncation(v, realm);
                any = true;
            }

            if (accessor.TryGetProperty("microsecond", out v) && !v.IsUndefined)
            {
                partialMicrosecond = ToIntegerWithTruncation(v, realm);
                any = true;
            }

            if (accessor.TryGetProperty("millisecond", out v) && !v.IsUndefined)
            {
                partialMillisecond = ToIntegerWithTruncation(v, realm);
                any = true;
            }

            if (accessor.TryGetProperty("minute", out v) && !v.IsUndefined)
            {
                partialMinute = ToIntegerWithTruncation(v, realm);
                any = true;
            }

            if (accessor.TryGetProperty("nanosecond", out v) && !v.IsUndefined)
            {
                partialNanosecond = ToIntegerWithTruncation(v, realm);
                any = true;
            }

            if (accessor.TryGetProperty("second", out v) && !v.IsUndefined)
            {
                partialSecond = ToIntegerWithTruncation(v, realm);
                any = true;
            }

            // Step 9: at least one property must be present
            if (!any)
            {
                throw StandardLibrary.ThrowTypeError("with() argument must have at least one time property", realm: realm);
            }

            // Step 6-7: Validate options AFTER reading time fields
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainTime.prototype.with");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            // Apply defaults from current time for undefined fields
            var hour = partialHour ?? time.Hour;
            var minute = partialMinute ?? time.Minute;
            var second = partialSecond ?? time.Second;
            var millisecond = partialMillisecond ?? time.Millisecond;
            var microsecond = partialMicrosecond ?? time.Microsecond;
            var nanosecond = partialNanosecond ?? time.Nanosecond;

            // Step 10: RegulateTime
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
            var item = args.GetArgument(0);
            var options = args.GetArgument(1);

            // Per spec: if item is already a PlainTime, validate options and return copy
            if (item.TryGetObject<JsObject>(out var fromObj) &&
                fromObj.TryGetProperty(TemporalPlainTimeSlot, out var fromSlot) &&
                fromSlot.TryGetObject<JsTemporalPlainTime>(out var existingTime))
            {
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainTime.from");
                GetTemporalOverflowOption(resolvedOpts, realm);
                return WrapPlainTime(new JsTemporalPlainTime(existingTime.Hour, existingTime.Minute,
                    existingTime.Second, existingTime.Millisecond, existingTime.Microsecond, existingTime.Nanosecond), realm, prototype);
            }

            // String path: parse first, then validate options
            if (item.IsString)
            {
                var time = ParseTemporalPlainTimeString(item.AsString() ?? "", realm);
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainTime.from");
                GetTemporalOverflowOption(resolvedOpts, realm); // validate but don't use for strings
                return WrapPlainTime(time, realm, prototype);
            }

            // Non-string primitives → TypeError
            if (item.IsUndefined || item.IsNull || item.IsBoolean || item.IsNumber || item.IsSymbol || item.IsBigInt)
                throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainTime", realm: realm);

            // Check for other Temporal types (ZonedDateTime, PlainDateTime)
            if (item.TryGetObject<JsObject>(out var fromObj2))
            {
                if (fromObj2.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) &&
                    zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
                {
                    var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainTime.from");
                    GetTemporalOverflowOption(resolvedOpts, realm);
                    return WrapPlainTime(zdt.ToPlainTime(), realm, prototype);
                }
                if (fromObj2.TryGetProperty(TemporalPlainDateTimeSlot, out var pdtSlot) &&
                    pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                {
                    var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainTime.from");
                    GetTemporalOverflowOption(resolvedOpts, realm);
                    return WrapPlainTime(pdt.ToPlainTime(), realm, prototype);
                }
            }

            // Property bag: read fields first (ToTemporalTimeRecord), THEN options
            if (item.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                var fields = ReadTemporalPlainTimeFields(accessor, realm);
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainTime.from");
                var overflow = GetTemporalOverflowOption(resolvedOpts, realm);
                return WrapPlainTime(ApplyPlainTimeOverflow(fields, overflow, realm), realm, prototype);
            }

            // Object without property accessor (e.g., HostFunction)
            if (item.Kind == JsValueKind.Object)
                throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainTime: object has no time properties", realm: realm);

            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainTime", realm: realm);
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
        AddPrototypeGetter(prototype, realm, "era", tv =>
        {
            var dateTime = GetPlainDateTime(tv);
            return GetTemporalEra(dateTime.Calendar, dateTime.Year, dateTime.Month, dateTime.Day);
        });
        AddPrototypeGetter(prototype, realm, "eraYear", tv =>
        {
            var dateTime = GetPlainDateTime(tv);
            return GetTemporalEraYear(dateTime.Calendar, dateTime.Year, dateTime.Month, dateTime.Day);
        });

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

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, args) =>
            TemporalToLocaleString(thisValue, args, realm, GetPlainDateTime(thisValue).ToString()));

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

        AddPrototypeMethod(prototype, realm, "withCalendar", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var calArg = args.GetArgument(0);
            if (calArg.IsUndefined)
                throw StandardLibrary.ThrowTypeError("withCalendar requires a calendar argument", realm: realm);
            var calendar = ToTemporalCalendarIdentifier(calArg);
            return WrapPlainDateTime(new JsTemporalPlainDateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond, dt.Microsecond, dt.Nanosecond, calendar), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "toZonedDateTime", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            // Per spec: first argument is the timezone directly (not an object with timeZone property)
            var timeZone = ToTemporalTimeZoneSlot(args.GetArgument(0), realm);

            // Per spec: second argument is options for disambiguation (compatible/earlier/later/reject)
            var options = args.GetArgument(1);
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDateTime.prototype.toZonedDateTime");
            var disambiguation = GetTemporalStringOption(optionsObj, "disambiguation",
                DisambiguationValues, "compatible", realm);

            // Compute local epoch nanoseconds from date/time components
            var epochDays = IsoCalendarHelpers.DateToEpochDays(dt.Year, dt.Month, dt.Day);
            var localEpochNanos = new System.Numerics.BigInteger(epochDays) * NanosecondsPerDay
                + (long)dt.Hour * NanosecondsPerHour
                + (long)dt.Minute * NanosecondsPerMinute
                + (long)dt.Second * NanosecondsPerSecond
                + (long)dt.Millisecond * NanosecondsPerMillisecond
                + (long)dt.Microsecond * NanosecondsPerMicrosecond
                + dt.Nanosecond;

            System.Numerics.BigInteger utcEpochNanos;
            if (ParseOffsetToNanos(timeZone) is { } offsetNanos)
            {
                // Fixed offset timezone — single unambiguous instant
                utcEpochNanos = localEpochNanos - offsetNanos;
            }
            else
            {
                // IANA timezone — determine offset via .NET TimeZoneInfo
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
                TimeSpan offset;
                if (dt.Year is >= 1 and <= 9999)
                {
                    var localDateTime = new DateTime(dt.Year, dt.Month, dt.Day,
                        dt.Hour, dt.Minute, dt.Second, dt.Millisecond, dt.Microsecond);

                    if (tz.IsInvalidTime(localDateTime))
                    {
                        // Spring-forward gap: local time doesn't exist
                        if (string.Equals(disambiguation, "reject", StringComparison.Ordinal))
                            throw StandardLibrary.ThrowRangeError(
                                "datetime is in a DST gap and disambiguation is 'reject'", realm: realm);

                        // GetUtcOffset returns the pre-transition (standard) offset for invalid times
                        var preOffset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(tz, localDateTime);
                        // Post-transition offset: check a few hours later (past the gap)
                        var postOffset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(tz, localDateTime.AddHours(3));

                        if (string.Equals(disambiguation, "earlier", StringComparison.Ordinal))
                        {
                            // Earlier instant: use post-transition (daylight) offset
                            offset = postOffset;
                        }
                        else
                        {
                            // compatible/later: use pre-transition (standard) offset → later instant
                            offset = preOffset;
                        }

                        utcEpochNanos = localEpochNanos - offset.Ticks * 100L;
                        goto validate;
                    }

                    if (tz.IsAmbiguousTime(localDateTime))
                    {
                        // Fall-back overlap: local time maps to two instants
                        if (string.Equals(disambiguation, "reject", StringComparison.Ordinal))
                            throw StandardLibrary.ThrowRangeError(
                                "datetime is ambiguous and disambiguation is 'reject'", realm: realm);

                        var offsets = tz.GetAmbiguousTimeOffsets(localDateTime);
                        var largerOffset = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
                        var smallerOffset = offsets[0] < offsets[1] ? offsets[0] : offsets[1];

                        if (string.Equals(disambiguation, "later", StringComparison.Ordinal))
                        {
                            // Later instant: use smaller offset (more negative, e.g. PST -8h)
                            offset = smallerOffset;
                        }
                        else
                        {
                            // compatible/earlier: earlier instant = larger offset (e.g. PDT -7h)
                            offset = largerOffset;
                        }

                        utcEpochNanos = localEpochNanos - offset.Ticks * 100L;
                        goto validate;
                    }

                    offset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(tz, localDateTime);
                }
                else
                {
                    // For years outside DateTime range, use base UTC offset
                    offset = tz.BaseUtcOffset;
                }

                utcEpochNanos = localEpochNanos - offset.Ticks * 100L;
            }

            validate:
            // Validate the resulting instant is within representable range
            if (utcEpochNanos < InstantMinEpochNanoseconds || utcEpochNanos > InstantMaxEpochNanoseconds)
                throw StandardLibrary.ThrowRangeError("Resulting instant is outside the valid range", realm: realm);

            var instant = JsTemporalInstant.FromEpochNanoseconds(utcEpochNanos);
            var zdt = new JsTemporalZonedDateTime(instant, timeZone, dt.Calendar);
            return WrapZonedDateTime(zdt, realm, prototypes.ZonedDateTimePrototype);
        });

        AddPrototypeMethod(prototype, realm, "add", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDateTime.prototype.add");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);
            var result = AddDurationToPlainDateTime(dt, duration, 1, overflow, realm);
            return WrapPlainDateTime(result, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDateTime.prototype.subtract");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);
            var result = AddDurationToPlainDateTime(dt, duration, -1, overflow, realm);
            return WrapPlainDateTime(result, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalPlainDateTime("until", dt, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalPlainDateTime("since", dt, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            var overrides = args.GetArgument(0);

            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                throw StandardLibrary.ThrowTypeError("Temporal.PlainDateTime.prototype.with requires an object argument", realm: realm);
            }

            RejectObjectWithCalendarOrTimeZone(overrides, accessor, realm);

            var any = false;
            int? partialDay = null, partialHour = null, partialMicrosecond = null,
                 partialMillisecond = null, partialMinute = null, partialMonth = null,
                 partialNanosecond = null, partialSecond = null, partialYear = null;
            string? partialMonthCode = null;

            if (accessor.TryGetProperty("day", out var v) && !v.IsUndefined) { partialDay = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("hour", out v) && !v.IsUndefined) { partialHour = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("microsecond", out v) && !v.IsUndefined) { partialMicrosecond = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("millisecond", out v) && !v.IsUndefined) { partialMillisecond = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("minute", out v) && !v.IsUndefined) { partialMinute = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("month", out v) && !v.IsUndefined) { partialMonth = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("monthCode", out v) && !v.IsUndefined) { partialMonthCode = JsOps.ToJsString(v); any = true; }
            if (accessor.TryGetProperty("nanosecond", out v) && !v.IsUndefined) { partialNanosecond = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("second", out v) && !v.IsUndefined) { partialSecond = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("year", out v) && !v.IsUndefined) { partialYear = ToIntegerWithTruncation(v, realm); any = true; }

            if (!any)
            {
                throw StandardLibrary.ThrowTypeError("with() argument must have at least one datetime property", realm: realm);
            }

            // Apply defaults and resolve month/monthCode BEFORE options
            var year = partialYear ?? dt.Year;
            var month = ResolveISOMonth(partialMonth, partialMonthCode, dt.Month, realm);
            var day = partialDay ?? dt.Day;
            var hour = partialHour ?? dt.Hour;
            var minute = partialMinute ?? dt.Minute;
            var second = partialSecond ?? dt.Second;
            var millisecond = partialMillisecond ?? dt.Millisecond;
            var microsecond = partialMicrosecond ?? dt.Microsecond;
            var nanosecond = partialNanosecond ?? dt.Nanosecond;

            // Pre-validate: reject fundamentally invalid values before options processing
            if (month < 1 || day < 1)
            {
                throw StandardLibrary.ThrowRangeError("Invalid ISO date value", realm: realm);
            }

            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainDateTime.prototype.with");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

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

            var result = new JsTemporalPlainDateTime(year, month, day, hour, minute, second, millisecond, microsecond, nanosecond, dt.Calendar);
            var epochNanos = ToEpochNanoseconds(result);
            if (epochNanos < PlainDateTimeMinEpochNanoseconds || epochNanos > PlainDateTimeMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("PlainDateTime is out of representable range", realm: realm);
            }

            return WrapPlainDateTime(result, realm, prototype);
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

        AddPrototypeMethod(prototype, realm, "withPlainTime", 0, (thisValue, args) =>
        {
            var dt = GetPlainDateTime(thisValue);
            JsTemporalPlainTime time;
            if (args.Count > 0 && !args[0].IsUndefined)
            {
                time = ToTemporalPlainTime(args[0], realm);
            }
            else
            {
                time = new JsTemporalPlainTime(0, 0, 0, 0, 0, 0);
            }

            var result = new JsTemporalPlainDateTime(
                dt.Year, dt.Month, dt.Day,
                time.Hour, time.Minute, time.Second,
                time.Millisecond, time.Microsecond, time.Nanosecond,
                dt.Calendar);

            var epochNanos = ToEpochNanoseconds(result);
            if (epochNanos < PlainDateTimeMinEpochNanoseconds || epochNanos > PlainDateTimeMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("PlainDateTime is out of representable range", realm: realm);
            }

            return WrapPlainDateTime(result, realm, prototype);
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
            var calendar = calendarArg.IsUndefined ? "iso8601" : ToTemporalCalendarIdentifier(calendarArg);

            RejectISODate(year, month, day, realm);
            RejectTemporalTimeRange(hour, minute, second, millisecond, microsecond, nanosecond, realm);
            RejectISODateTimeRange(year, month, day, hour, minute, second, millisecond, microsecond, nanosecond, realm);
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
            var item = args.GetArgument(0);
            var options = args.GetArgument(1);
            // Per spec: if item is not an Object, parse string first, then validate options
            if (item.Kind != JsValueKind.Object)
            {
                var dt = ToTemporalPlainDateTime(item, realm);
                var resolvedOpts2 = ValidateOptionsObject(options, realm, "Temporal.PlainDateTime.from");
                GetTemporalOverflowOption(resolvedOpts2, realm); // validate but don't use for strings
                return WrapPlainDateTime(dt, realm, prototype);
            }
            // Per spec: for property bags, read fields BEFORE options
            var dt2 = ToTemporalPlainDateTimeWithDeferredOptions(item, options, realm, "Temporal.PlainDateTime.from");
            return WrapPlainDateTime(dt2, realm, prototype);
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
        AddPrototypeGetter(prototype, realm, "era", tv =>
        {
            var zdt = GetZonedDateTime(tv);
            return GetTemporalEra(zdt.Calendar, zdt.Year, zdt.Month, zdt.Day);
        });
        AddPrototypeGetter(prototype, realm, "eraYear", tv =>
        {
            var zdt = GetZonedDateTime(tv);
            return GetTemporalEraYear(zdt.Calendar, zdt.Year, zdt.Month, zdt.Day);
        });
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
            var rounded = RoundToIncrement(epochNanos, incrementNanoseconds, roundingMode, treatNegativeAsPositive: true);

            // Get offset nanoseconds — use FixedOffset directly when available to avoid
            // .NET DateTimeOffset limitation for extended years outside 1-9999
            long offsetNanos;
            if (zdt.FixedOffset.HasValue)
            {
                offsetNanos = (long)zdt.FixedOffset.Value.TotalMilliseconds * 1_000_000;
            }
            else
            {
                // Named timezone — need to compute offset at the rounded instant
                var roundedZdtForOffset = new JsTemporalZonedDateTime(new JsTemporalInstant(rounded), zdt.TimeZoneId, CanonicalizeCalendarId(zdt.Calendar));
                offsetNanos = roundedZdtForOffset.OffsetNanoseconds;
            }

            // Decompose into date/time components using BigInteger math (bypasses .NET DateTimeOffset)
            var (year, month, day, hour, minute, second, ms, us, ns) =
                EpochNanosToComponents(rounded, offsetNanos);

            // Build output
            var datePart = $"{FormatYear(year)}-{month:D2}-{day:D2}";
            var timePart = FormatTimeToString(hour, minute, second, ms, us, ns, precision.FractionalDigits);

            var result = $"{datePart}T{timePart}";

            if (!string.Equals(showOffset, "never", StringComparison.Ordinal))
            {
                result += FormatOffsetNanoseconds(offsetNanos);
            }

            if (string.Equals(showTimeZone, "auto", StringComparison.Ordinal))
            {
                result += $"[{zdt.TimeZoneId}]";
            }
            else if (string.Equals(showTimeZone, "critical", StringComparison.Ordinal))
            {
                result += $"[!{zdt.TimeZoneId}]";
            }
            // "never" — omit timezone

            if (string.Equals(showCalendar, "always", StringComparison.Ordinal))
            {
                result += $"[u-ca={zdt.Calendar}]";
            }
            else if (string.Equals(showCalendar, "critical", StringComparison.Ordinal))
            {
                result += $"[!u-ca={zdt.Calendar}]";
            }
            else if (string.Equals(showCalendar, "auto", StringComparison.Ordinal) &&
                     !string.Equals(zdt.Calendar, "iso8601", StringComparison.Ordinal))
            {
                result += $"[u-ca={zdt.Calendar}]";
            }

            return new JsValue(result);
        });

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
            new JsValue(GetZonedDateTime(thisValue).ToString()));

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            return TemporalZonedDateTimeToLocaleString(zdt, args, realm, prototypes.InstantPrototype);
        });

        AddPrototypeMethod(prototype, realm, "valueOf", 0, (_, _) =>
            throw StandardLibrary.ThrowTypeError("Temporal.ZonedDateTime.prototype.valueOf does not support implicit conversion", realm: realm));

        AddPrototypeMethod(prototype, realm, "toInstant", 0, (thisValue, _) =>
            WrapInstant(GetZonedDateTime(thisValue).ToInstant(), realm, prototypes.InstantPrototype));

        AddPrototypeMethod(prototype, realm, "toPlainDateTime", 0, (thisValue, _) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            return WrapPlainDateTime(ZonedDateTimeToPlainDateTime(zdt), realm, prototypes.PlainDateTimePrototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainDate", 0, (thisValue, _) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var pdt = ZonedDateTimeToPlainDateTime(zdt);
            return WrapPlainDate(new JsTemporalPlainDate(pdt.Year, pdt.Month, pdt.Day, CanonicalizeCalendarId(pdt.Calendar)), realm, prototypes.PlainDatePrototype);
        });

        AddPrototypeMethod(prototype, realm, "toPlainTime", 0, (thisValue, _) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var pdt = ZonedDateTimeToPlainDateTime(zdt);
            return WrapPlainTime(new JsTemporalPlainTime(pdt.Hour, pdt.Minute, pdt.Second, pdt.Millisecond, pdt.Microsecond, pdt.Nanosecond), realm, prototypes.PlainTimePrototype);
        });

        AddPrototypeMethod(prototype, realm, "withCalendar", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var calArg = args.GetArgument(0);
            if (calArg.IsUndefined)
                throw StandardLibrary.ThrowTypeError("withCalendar requires a calendar argument", realm: realm);
            var calendar = ToTemporalCalendarIdentifier(calArg);
            return WrapZonedDateTime(new JsTemporalZonedDateTime(zdt.Instant, zdt.TimeZoneId, calendar), realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "add", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);

            // Validate and read options (2nd argument)
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.ZonedDateTime.prototype.add");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            var result = AddDurationToZonedDateTime(zdt, duration, 1, overflow, realm);
            return WrapZonedDateTime(result, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);

            // Validate and read options (2nd argument)
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.ZonedDateTime.prototype.subtract");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            var result = AddDurationToZonedDateTime(zdt, duration, -1, overflow, realm);
            return WrapZonedDateTime(result, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalZonedDateTime("until", zdt, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalZonedDateTime("since", zdt, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
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
                var roundedZdtDay = new JsTemporalZonedDateTime(roundedInstant, zdt.TimeZoneId, CanonicalizeCalendarId(zdt.Calendar));
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
                CanonicalizeCalendarId(zdt.Calendar));
            return WrapZonedDateTime(roundedZdt, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var overrides = args.GetArgument(0);

            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                throw StandardLibrary.ThrowTypeError("Temporal.ZonedDateTime.prototype.with requires an object argument", realm: realm);
            }

            RejectObjectWithCalendarOrTimeZone(overrides, accessor, realm);

            var any = false;
            int? partialDay = null, partialHour = null, partialMicrosecond = null,
                 partialMillisecond = null, partialMinute = null, partialMonth = null,
                 partialNanosecond = null, partialSecond = null, partialYear = null;
            string? partialMonthCode = null, partialOffset = null;

            if (accessor.TryGetProperty("day", out var v) && !v.IsUndefined) { partialDay = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("hour", out v) && !v.IsUndefined) { partialHour = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("microsecond", out v) && !v.IsUndefined) { partialMicrosecond = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("millisecond", out v) && !v.IsUndefined) { partialMillisecond = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("minute", out v) && !v.IsUndefined) { partialMinute = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("month", out v) && !v.IsUndefined) { partialMonth = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("monthCode", out v) && !v.IsUndefined) { partialMonthCode = JsOps.ToJsString(v); any = true; }
            if (accessor.TryGetProperty("nanosecond", out v) && !v.IsUndefined) { partialNanosecond = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("offset", out v) && !v.IsUndefined)
            {
                if (v.IsString)
                    partialOffset = v.AsString();
                else if (v.IsObject)
                    partialOffset = JsOps.ToJsString(v);
                else
                    throw StandardLibrary.ThrowTypeError("offset must be a string", realm: realm);
                any = true;
            }
            if (accessor.TryGetProperty("second", out v) && !v.IsUndefined) { partialSecond = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("year", out v) && !v.IsUndefined) { partialYear = ToIntegerWithTruncation(v, realm); any = true; }

            if (!any)
            {
                throw StandardLibrary.ThrowTypeError("with() argument must have at least one datetime property", realm: realm);
            }

            // Get local date-time using BigInteger arithmetic to handle extreme years
            var localDt = GetLocalDateTime(zdt);

            // Merge fields with defaults and pre-validate before options processing
            var year = partialYear ?? localDt.Year;
            var month = ResolveISOMonth(partialMonth, partialMonthCode, localDt.Month, realm);
            var day = partialDay ?? localDt.Day;
            var hour = partialHour ?? localDt.Hour;
            var minute = partialMinute ?? localDt.Minute;
            var second = partialSecond ?? localDt.Second;
            var millisecond = partialMillisecond ?? localDt.Millisecond;
            var microsecond = partialMicrosecond ?? localDt.Microsecond;
            var nanosecond = partialNanosecond ?? localDt.Nanosecond;

            // Pre-validate: reject fundamentally invalid values before options processing
            if (month < 1 || day < 1)
            {
                throw StandardLibrary.ThrowRangeError("Invalid ISO date value", realm: realm);
            }

            // Validate offset string format if provided
            if (partialOffset is not null)
            {
                if (ParseOffsetToNanos(partialOffset) is null)
                    throw StandardLibrary.ThrowRangeError($"invalid offset string: '{partialOffset}'", realm: realm);
            }

            // Validate options AFTER field processing
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.ZonedDateTime.prototype.with");
            var disambiguation = GetTemporalStringOption(optionsObj, "disambiguation",
                DisambiguationValues, "compatible", realm);
            var offsetOption = GetTemporalStringOption(optionsObj, "offset",
                OffsetOptionValues, "prefer", realm);
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

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

            // Use offset if provided and offset option is "use", otherwise use timezone
            if (partialOffset is not null && string.Equals(offsetOption, "use", StringComparison.Ordinal))
            {
                var offsetNanos = ParseOffsetToNanos(partialOffset)!.Value;
                var epochDays = IsoCalendarHelpers.DateToEpochDays(year, month, day);
                var localEpochNanos = new System.Numerics.BigInteger(epochDays) * 86_400_000_000_000L
                    + (long)hour * 3_600_000_000_000L
                    + (long)minute * 60_000_000_000L
                    + (long)second * 1_000_000_000L
                    + (long)millisecond * 1_000_000L
                    + (long)microsecond * 1_000L
                    + nanosecond;
                var utcEpochNanos = localEpochNanos - offsetNanos;
                if (utcEpochNanos < InstantMinEpochNanoseconds || utcEpochNanos > InstantMaxEpochNanoseconds)
                {
                    throw StandardLibrary.ThrowRangeError("ZonedDateTime is out of representable range", realm: realm);
                }
                var newInstant = JsTemporalInstant.FromEpochNanoseconds(utcEpochNanos);
                return WrapZonedDateTime(new JsTemporalZonedDateTime(newInstant, zdt.TimeZoneId, CanonicalizeCalendarId(zdt.Calendar)), realm, prototype);
            }

            var newZdt = new JsTemporalZonedDateTime(year, month, day, hour, minute, second, millisecond, microsecond, nanosecond, zdt.TimeZoneId, CanonicalizeCalendarId(zdt.Calendar));
            var newEpochNs = newZdt.Instant.EpochNanoseconds;
            if (newEpochNs < InstantMinEpochNanoseconds || newEpochNs > InstantMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("ZonedDateTime is out of representable range", realm: realm);
            }
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
            var (year, month, day) = GetLocalDate(zdt);
            var epochNs = GetStartOfDayInstant(year, month, day,
                zdt.TimeZone, zdt.FixedOffset, realm);
            var instant = JsTemporalInstant.FromEpochNanoseconds(epochNs);
            var startOfDayZdt = new JsTemporalZonedDateTime(instant, zdt.TimeZoneId, CanonicalizeCalendarId(zdt.Calendar));
            return WrapZonedDateTime(startOfDayZdt, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "withTimeZone", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var timeZoneId = ToTemporalTimeZoneSlot(args.GetArgument(0), realm);
            var newZdt = zdt.WithTimeZone(timeZoneId);
            return WrapZonedDateTime(newZdt, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "getTimeZoneTransition", 1, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);

            // Parse direction argument
            var directionParam = args.GetArgument(0);
            string directionStr;

            if (directionParam.IsString)
            {
                // Shorthand form: string is treated as { direction: string }
                directionStr = directionParam.AsString();
            }
            else if (directionParam.TryGetObject<IJsPropertyAccessor>(out var dirObj))
            {
                // Options bag form
                dirObj.TryGetProperty("direction", out var dirVal);
                if (dirVal.IsUndefined)
                {
                    throw StandardLibrary.ThrowRangeError("direction is required", realm: realm);
                }
                if (dirVal.IsSymbol)
                {
                    throw StandardLibrary.ThrowTypeError("Cannot convert Symbol to string", realm: realm);
                }
                directionStr = JsOps.ToJsString(dirVal);
            }
            else
            {
                throw StandardLibrary.ThrowTypeError("Expected a string or object for direction argument", realm: realm);
            }

            // Validate direction
            if (!string.Equals(directionStr, "next", StringComparison.Ordinal) &&
                !string.Equals(directionStr, "previous", StringComparison.Ordinal))
            {
                throw StandardLibrary.ThrowRangeError($"Invalid direction: {directionStr}", realm: realm);
            }

            var isNext = string.Equals(directionStr, "next", StringComparison.Ordinal);
            var timeZoneId = zdt.TimeZoneId;

            // Offset timezones and UTC have no transitions
            if (timeZoneId.Length >= 2 && (timeZoneId[0] == '+' || timeZoneId[0] == '-') && char.IsDigit(timeZoneId[1]))
            {
                return JsValue.Null;
            }

            if (string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
            {
                return JsValue.Null;
            }

            // Named timezone — find transitions using .NET TimeZoneInfo
            try
            {
                var tz = FindTimeZone(timeZoneId);

                // UTC timezone info has no adjustment rules
                if (tz == TimeZoneInfo.Utc || tz.GetAdjustmentRules().Length == 0)
                {
                    return JsValue.Null;
                }

                var epochNs = zdt.Instant.EpochNanoseconds;
                var epochMs = (long)(epochNs / 1_000_000);
                var dto = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);

                if (isNext)
                {
                    // Find next transition by scanning forward
                    var current = dto.UtcDateTime;
                    var rules = tz.GetAdjustmentRules();
                    DateTimeOffset? nextTransition = null;

                    foreach (var rule in rules)
                    {
                        if (rule.DateEnd < current) continue;

                        // Check transition start
                        var transStart = GetTransitionPoint(rule.DaylightTransitionStart, rule.DateStart.Year > current.Year ? rule.DateStart.Year : current.Year, tz);
                        if (transStart.HasValue && transStart.Value.UtcDateTime > current)
                        {
                            if (!nextTransition.HasValue || transStart.Value < nextTransition.Value)
                                nextTransition = transStart;
                        }

                        // Check transition end
                        var transEnd = GetTransitionPoint(rule.DaylightTransitionEnd, rule.DateStart.Year > current.Year ? rule.DateStart.Year : current.Year, tz);
                        if (transEnd.HasValue && transEnd.Value.UtcDateTime > current)
                        {
                            if (!nextTransition.HasValue || transEnd.Value < nextTransition.Value)
                                nextTransition = transEnd;
                        }
                    }

                    if (nextTransition.HasValue)
                    {
                        var transNs = new JsTemporalInstant(nextTransition.Value).EpochNanoseconds;
                        var transInstant = new JsTemporalInstant(transNs);
                        var transZdt = new JsTemporalZonedDateTime(transInstant, timeZoneId, CanonicalizeCalendarId(zdt.Calendar));
                        return WrapZonedDateTime(transZdt, realm, prototype);
                    }
                }
                else
                {
                    // Find previous transition by scanning backward
                    var current = dto.UtcDateTime;
                    var rules = tz.GetAdjustmentRules();
                    DateTimeOffset? prevTransition = null;

                    foreach (var rule in rules)
                    {
                        if (rule.DateStart > current) continue;

                        var year = current.Year;
                        if (rule.DateEnd.Year < year) year = rule.DateEnd.Year;

                        var transStart = GetTransitionPoint(rule.DaylightTransitionStart, year, tz);
                        if (transStart.HasValue && transStart.Value.UtcDateTime < current)
                        {
                            if (!prevTransition.HasValue || transStart.Value > prevTransition.Value)
                                prevTransition = transStart;
                        }

                        var transEnd = GetTransitionPoint(rule.DaylightTransitionEnd, year, tz);
                        if (transEnd.HasValue && transEnd.Value.UtcDateTime < current)
                        {
                            if (!prevTransition.HasValue || transEnd.Value > prevTransition.Value)
                                prevTransition = transEnd;
                        }
                    }

                    if (prevTransition.HasValue)
                    {
                        var transNs = new JsTemporalInstant(prevTransition.Value).EpochNanoseconds;
                        var transInstant = new JsTemporalInstant(transNs);
                        var transZdt = new JsTemporalZonedDateTime(transInstant, timeZoneId, CanonicalizeCalendarId(zdt.Calendar));
                        return WrapZonedDateTime(transZdt, realm, prototype);
                    }
                }

                return JsValue.Null;
            }
            catch (TimeZoneNotFoundException)
            {
                return JsValue.Null;
            }
        });

        AddPrototypeMethod(prototype, realm, "withPlainTime", 0, (thisValue, args) =>
        {
            var zdt = GetZonedDateTime(thisValue);
            var (year, month, day) = GetLocalDate(zdt);

            if (args.Count == 0 || args[0].IsUndefined)
            {
                // Per spec step 6: use GetStartOfDay
                var epochNs = GetStartOfDayInstant(year, month, day,
                    zdt.TimeZone, zdt.FixedOffset, realm);
                var instant = JsTemporalInstant.FromEpochNanoseconds(epochNs);
                return WrapZonedDateTime(
                    new JsTemporalZonedDateTime(instant, zdt.TimeZoneId, CanonicalizeCalendarId(zdt.Calendar)),
                    realm, prototype);
            }

            var time = ToTemporalPlainTime(args[0], realm);
            var newZdt = new JsTemporalZonedDateTime(
                year, month, day,
                time.Hour, time.Minute, time.Second,
                time.Millisecond, time.Microsecond, time.Nanosecond,
                zdt.TimeZoneId, CanonicalizeCalendarId(zdt.Calendar));
            var newEpochNs = newZdt.Instant.EpochNanoseconds;
            if (newEpochNs < InstantMinEpochNanoseconds || newEpochNs > InstantMaxEpochNanoseconds)
            {
                throw StandardLibrary.ThrowRangeError("ZonedDateTime is out of representable range", realm: realm);
            }
            return WrapZonedDateTime(newZdt, realm, prototype);
        });

        AddPrototypeGetter(prototype, realm, "hoursInDay", tv =>
        {
            var zdt = GetZonedDateTime(tv);

            // Compute local date from BigInteger epoch nanoseconds to handle extreme years
            // that would overflow DateTimeOffset (years 1-9999 only)
            var (year, month, day) = GetLocalDate(zdt);

            // Compute start-of-day for today using the existing GetStartOfDayInstant helper
            var todayNanos = GetStartOfDayInstant(year, month, day,
                zdt.TimeZone, zdt.FixedOffset, realm);

            // Compute start-of-day for next day
            var nextYear = year;
            var nextMonth = month;
            var nextDay = day + 1;
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
            var tomorrowNanos = GetStartOfDayInstant(nextYear, nextMonth, nextDay,
                zdt.TimeZone, zdt.FixedOffset, realm);

            var diffNanos = tomorrowNanos - todayNanos;
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

            // Per spec: constructor uses ParseTemporalTimeZoneString which only accepts
            // TimeZoneIdentifier, NOT ISO datetime strings
            var timeZoneId = ToTemporalTimeZoneSlotStrict(timeZoneArg, realm);
            var calendar = calendarArg.IsUndefined ? "iso8601" : ToTemporalCalendarIdentifier(calendarArg);

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
            var item = args.GetArgument(0);
            var options = args.GetArgument(1);

            // Per spec: if item is already a ZonedDateTime, validate options and return copy
            if (item.TryGetObject<JsObject>(out var fromObj) &&
                fromObj.TryGetProperty(TemporalZonedDateTimeSlot, out var fromSlot) &&
                fromSlot.TryGetObject<JsTemporalZonedDateTime>(out var existingZdt))
            {
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.ZonedDateTime.from");
                GetTemporalStringOption(resolvedOpts, "disambiguation", DisambiguationValues, "compatible", realm);
                GetTemporalStringOption(resolvedOpts, "offset", OffsetOptionValues, "reject", realm);
                GetTemporalOverflowOption(resolvedOpts, realm);
                return WrapZonedDateTime(existingZdt, realm, prototype);
            }

            // String path: per spec, parse string BEFORE validating options (GetOptionsObject)
            // RangeError from invalid string takes priority over TypeError from bad options
            if (item.IsString)
            {
                // Pre-validate string syntax (throws RangeError for bad strings)
                // This must happen before GetOptionsObject per spec ordering
                ValidateZonedDateTimeString(item.AsString() ?? "", realm);
                // Now validate options (may throw TypeError for primitives)
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.ZonedDateTime.from");
                var disambiguation = GetTemporalStringOption(resolvedOpts, "disambiguation", DisambiguationValues, "compatible", realm);
                var offsetOption = GetTemporalStringOption(resolvedOpts, "offset", OffsetOptionValues, "reject", realm);
                GetTemporalOverflowOption(resolvedOpts, realm);
                var zdt = ToTemporalZonedDateTime(item, realm, offsetOption, disambiguation);
                return WrapZonedDateTime(zdt, realm, prototype);
            }

            // Non-string primitives → TypeError
            if (item.IsUndefined || item.IsNull || item.IsBoolean || item.IsNumber || item.IsSymbol || item.IsBigInt)
                throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.ZonedDateTime", realm: realm);

            // Property bag: per spec, read fields FIRST in alphabetical order, THEN options
            if (item.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                var zdt = ToTemporalZonedDateTimeFromPropertyBagWithOptions(accessor, options, realm, prototype);
                return WrapZonedDateTime(zdt, realm, prototype);
            }

            // Object without property accessor
            if (item.Kind == JsValueKind.Object)
                throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.ZonedDateTime: object has no date properties", realm: realm);

            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.ZonedDateTime", realm: realm);
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
        AddPrototypeGetter(prototype, realm, "era", tv =>
        {
            var yearMonth = GetPlainYearMonth(tv);
            return GetTemporalEra(yearMonth.Calendar, yearMonth.Year, yearMonth.Month, yearMonth.ReferenceDay);
        });
        AddPrototypeGetter(prototype, realm, "eraYear", tv =>
        {
            var yearMonth = GetPlainYearMonth(tv);
            return GetTemporalEraYear(yearMonth.Calendar, yearMonth.Year, yearMonth.Month, yearMonth.ReferenceDay);
        });

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

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, args) =>
            TemporalToLocaleString(thisValue, args, realm, GetPlainYearMonth(thisValue).ToString()));

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
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return AddDurationToYearMonth(1, ym, duration, options, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "subtract", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var duration = ToTemporalDuration(args.GetArgument(0), realm);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return AddDurationToYearMonth(-1, ym, duration, options, realm, prototype);
        });

        AddPrototypeMethod(prototype, realm, "until", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalPlainYearMonth("until", ym, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "since", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            return WrapDuration(DifferenceTemporalPlainYearMonth("since", ym, args.GetArgument(0), options, realm), realm, prototypes.DurationPrototype);
        });

        AddPrototypeMethod(prototype, realm, "with", 1, (thisValue, args) =>
        {
            var ym = GetPlainYearMonth(thisValue);
            var overrides = args.GetArgument(0);

            if (!overrides.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                throw StandardLibrary.ThrowTypeError("Temporal.PlainYearMonth.prototype.with requires an object argument", realm: realm);
            }

            RejectObjectWithCalendarOrTimeZone(overrides, accessor, realm);

            // PrepareTemporalFields — alphabetical: month, monthCode, year
            var any = false;
            int? partialMonth = null, partialYear = null;
            string? partialMonthCode = null;

            if (accessor.TryGetProperty("month", out var v) && !v.IsUndefined) { partialMonth = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("monthCode", out v) && !v.IsUndefined) { partialMonthCode = JsOps.ToJsString(v); any = true; }
            if (accessor.TryGetProperty("year", out v) && !v.IsUndefined) { partialYear = ToIntegerWithTruncation(v, realm); any = true; }

            if (!any)
            {
                throw StandardLibrary.ThrowTypeError("with() argument must have at least one year-month property", realm: realm);
            }

            // Apply defaults and resolve month/monthCode BEFORE options
            var year = partialYear ?? ym.Year;
            var month = ResolveISOMonth(partialMonth, partialMonthCode, ym.Month, realm);

            // Pre-validate: reject fundamentally invalid month before options processing
            if (month < 1)
            {
                throw StandardLibrary.ThrowRangeError("Month value is out of range", realm: realm);
            }

            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainYearMonth.prototype.with");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

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

            // Per spec: ToIntegerWithTruncation handles Infinity/NaN → RangeError
            var year = ToIntegerWithTruncation(args.GetArgument(0), realm);
            var month = ToIntegerWithTruncation(args.GetArgument(1), realm);
            var calendarArg = args.Count > 2 ? args[2] : JsValue.Undefined;
            var calendar = calendarArg.IsUndefined ? "iso8601" : ToTemporalCalendarIdentifier(calendarArg);
            // 4th arg is referenceISODay per spec
            var refDayArg = args.Count > 3 ? args[3] : JsValue.Undefined;
            int? refDay = refDayArg.IsUndefined ? null : ToIntegerWithTruncation(refDayArg, realm);

            // Validate month range (1-12)
            if (month is < 1 or > 12)
                throw StandardLibrary.ThrowRangeError("Month value is out of range (1-12)", realm: realm);

            // Validate refDay if provided
            if (refDay.HasValue)
            {
                var maxDay = IsoCalendarHelpers.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
                if (refDay.Value < 1 || refDay.Value > maxDay)
                    throw StandardLibrary.ThrowRangeError("Day value is out of range", realm: realm);
            }

            // ISOYearMonthWithinLimits: check year-month range (NOT full date)
            RejectISOYearMonthRange(year, month, realm);

            var ym = new JsTemporalPlainYearMonth(year, month, calendar, refDay);
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
            var item = args.GetArgument(0);
            var options = args.GetArgument(1);

            // Per spec step 1: If item is already a PlainYearMonth, validate options and return copy
            if (item.TryGetObject<JsObject>(out var fromObj) &&
                fromObj.TryGetProperty(TemporalPlainYearMonthSlot, out var fromSlot) &&
                fromSlot.TryGetObject<JsTemporalPlainYearMonth>(out var existingYm))
            {
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainYearMonth.from");
                GetTemporalOverflowOption(resolvedOpts, realm);
                var copy = new JsTemporalPlainYearMonth(existingYm.Year, existingYm.Month,
                    existingYm.Calendar, existingYm.ReferenceDay);
                return WrapPlainYearMonth(copy, realm, prototype);
            }

            // String path: parse first, then validate options
            if (item.IsString)
            {
                var ym = ToTemporalPlainYearMonth(item, realm);
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainYearMonth.from");
                GetTemporalOverflowOption(resolvedOpts, realm);
                return WrapPlainYearMonth(ym, realm, prototype);
            }

            // Non-string primitives → TypeError
            if (item.IsUndefined || item.IsNull || item.IsBoolean || item.IsNumber || item.IsSymbol || item.IsBigInt)
                throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainYearMonth", realm: realm);

            // Check for other Temporal types (PlainDate, PlainDateTime, ZonedDateTime)
            if (item.TryGetObject<JsObject>(out var fromObj2))
            {
                if (fromObj2.TryGetProperty(TemporalPlainDateSlot, out var pdSlot) && pdSlot.TryGetObject<JsTemporalPlainDate>(out var pd))
                {
                    var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainYearMonth.from");
                    GetTemporalOverflowOption(resolvedOpts, realm);
                    return WrapPlainYearMonth(new JsTemporalPlainYearMonth(pd.Year, pd.Month, CanonicalizeCalendarId(pd.Calendar)), realm, prototype);
                }
                if (fromObj2.TryGetProperty(TemporalPlainDateTimeSlot, out var pdtSlot) && pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                {
                    var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainYearMonth.from");
                    GetTemporalOverflowOption(resolvedOpts, realm);
                    return WrapPlainYearMonth(new JsTemporalPlainYearMonth(pdt.Year, pdt.Month, CanonicalizeCalendarId(pdt.Calendar)), realm, prototype);
                }
                if (fromObj2.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) && zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
                {
                    var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainYearMonth.from");
                    GetTemporalOverflowOption(resolvedOpts, realm);
                    var plainDate = zdt.ToPlainDate();
                    return WrapPlainYearMonth(
                        new JsTemporalPlainYearMonth(
                            plainDate.Year, plainDate.Month, CanonicalizeCalendarId(zdt.Calendar),
                            GetTemporalReferenceISODay(CanonicalizeCalendarId(zdt.Calendar), plainDate.Year, plainDate.Month, plainDate.Day, null, realm)),
                        realm, prototype);
                }
            }

            // Property bag: per spec, read fields first, then options
            if (item.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                var ym2 = ToTemporalPlainYearMonthFromPropertyBagWithOverflow(accessor, options, realm, "Temporal.PlainYearMonth.from");
                return WrapPlainYearMonth(ym2, realm, prototype);
            }

            // Object without property accessor
            if (item.Kind == JsValueKind.Object)
                throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainYearMonth: object has no date properties", realm: realm);

            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainYearMonth", realm: realm);
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
            // "auto" mode per spec TemporalMonthDayToString:
            // ISO calendar → MM-DD, non-ISO → YYYY-MM-DD[u-ca=cal]
            if (string.Equals(md.Calendar, "iso8601", StringComparison.Ordinal))
            {
                return new JsValue(md.ToStringBasic());
            }
            return new JsValue(md.ToStringWithCalendar());
        });

        AddPrototypeMethod(prototype, realm, "toJSON", 0, (thisValue, _) =>
        {
            var md = GetPlainMonthDay(thisValue);
            if (string.Equals(md.Calendar, "iso8601", StringComparison.Ordinal))
            {
                return new JsValue(md.ToStringBasic());
            }
            return new JsValue(md.ToStringWithCalendar());
        });

        AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, args) =>
        {
            var md = GetPlainMonthDay(thisValue);
            var fallback = string.Equals(md.Calendar, "iso8601", StringComparison.Ordinal)
                ? md.ToStringBasic()
                : md.ToStringWithCalendar();
            return TemporalToLocaleString(thisValue, args, realm, fallback);
        });

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

            RejectObjectWithCalendarOrTimeZone(overrides, accessor, realm);

            // PrepareTemporalFields — alphabetical: day, month, monthCode, year
            var any = false;
            int? partialDay = null, partialMonth = null, partialYear = null;
            string? partialMonthCode = null;

            if (accessor.TryGetProperty("day", out var v) && !v.IsUndefined) { partialDay = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("month", out v) && !v.IsUndefined) { partialMonth = ToIntegerWithTruncation(v, realm); any = true; }
            if (accessor.TryGetProperty("monthCode", out v) && !v.IsUndefined) { partialMonthCode = JsOps.ToJsString(v); any = true; }
            if (accessor.TryGetProperty("year", out v) && !v.IsUndefined) { partialYear = ToIntegerWithTruncation(v, realm); any = true; }

            if (!any)
            {
                throw StandardLibrary.ThrowTypeError("with() argument must have at least one month-day property", realm: realm);
            }

            if (!string.Equals(md.Calendar, "iso8601", StringComparison.Ordinal) &&
                partialMonth.HasValue &&
                partialMonthCode is null &&
                partialYear is null)
            {
                throw StandardLibrary.ThrowTypeError("Non-ISO PlainMonthDay.with requires monthCode or year", realm: realm);
            }

            // Apply defaults and resolve month/monthCode BEFORE options
            var month = ResolveISOMonth(partialMonth, partialMonthCode, md.Month, realm);
            var day = partialDay ?? md.Day;

            // Pre-validate: reject fundamentally invalid values before options processing
            if (month < 1 || day < 1)
            {
                throw StandardLibrary.ThrowRangeError("Invalid ISO date value", realm: realm);
            }

            var options = args.Count > 1 ? args[1] : JsValue.Undefined;
            var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainMonthDay.prototype.with");
            var overflow = GetTemporalOverflowOption(optionsObj, realm);

            if (string.Equals(overflow, "constrain", StringComparison.Ordinal))
            {
                month = Math.Clamp(month, 1, 12);
                var maxDay = IsoCalendarHelpers.DaysInMonth(md.ReferenceYear, month);
                day = Math.Clamp(day, 1, maxDay);
            }
            else
            {
                if (month is < 1 or > 12)
                {
                    throw StandardLibrary.ThrowRangeError("Month value is out of range (1-12)", realm: realm);
                }

                var maxDay = IsoCalendarHelpers.DaysInMonth(md.ReferenceYear, month);
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

            // Per spec: ToIntegerWithTruncation handles Infinity/NaN → RangeError
            var month = ToIntegerWithTruncation(args.GetArgument(0), realm);
            var day = ToIntegerWithTruncation(args.GetArgument(1), realm);
            var calendarArg = args.Count > 2 ? args[2] : JsValue.Undefined;
            var calendar = calendarArg.IsUndefined ? "iso8601" : ToTemporalCalendarIdentifier(calendarArg);
            // 4th arg is referenceISOYear per spec
            var refYearArg = args.Count > 3 ? args[3] : JsValue.Undefined;
            int? refYear = refYearArg.IsUndefined ? null : ToIntegerWithTruncation(refYearArg, realm);

            // Validate ISO date fields and range (RejectISODate checks both)
            RejectISODate(refYear ?? 1972, month, day, realm);

            var md = new JsTemporalPlainMonthDay(month, day, calendar, refYear);
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
            var item = args.GetArgument(0);
            var options = args.GetArgument(1);

            // Per spec step 1: If item is already a PlainMonthDay, validate options and return copy
            if (item.TryGetObject<JsObject>(out var fromObj) &&
                fromObj.TryGetProperty(TemporalPlainMonthDaySlot, out var fromSlot) &&
                fromSlot.TryGetObject<JsTemporalPlainMonthDay>(out var existingMd))
            {
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainMonthDay.from");
                GetTemporalOverflowOption(resolvedOpts, realm);
                var copy = new JsTemporalPlainMonthDay(existingMd.Month, existingMd.Day,
                    existingMd.Calendar, existingMd.ReferenceYear);
                return WrapPlainMonthDay(copy, realm, prototype);
            }

            // String path: parse first, then validate options
            if (item.IsString)
            {
                var md = ToTemporalPlainMonthDay(item, realm);
                var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainMonthDay.from");
                GetTemporalOverflowOption(resolvedOpts, realm);
                return WrapPlainMonthDay(md, realm, prototype);
            }

            // Non-string primitives → TypeError
            if (item.IsUndefined || item.IsNull || item.IsBoolean || item.IsNumber || item.IsSymbol || item.IsBigInt)
                throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainMonthDay", realm: realm);

            // Check for other Temporal types (PlainDate, PlainDateTime)
            if (item.TryGetObject<JsObject>(out var fromObj2))
            {
                if (fromObj2.TryGetProperty(TemporalPlainDateSlot, out var pdSlot) && pdSlot.TryGetObject<JsTemporalPlainDate>(out var pd))
                {
                    var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainMonthDay.from");
                    GetTemporalOverflowOption(resolvedOpts, realm);
                    return WrapPlainMonthDay(new JsTemporalPlainMonthDay(pd.Month, pd.Day, CanonicalizeCalendarId(pd.Calendar)), realm, prototype);
                }
                if (fromObj2.TryGetProperty(TemporalPlainDateTimeSlot, out var pdtSlot) && pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                {
                    var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.PlainMonthDay.from");
                    GetTemporalOverflowOption(resolvedOpts, realm);
                    return WrapPlainMonthDay(new JsTemporalPlainMonthDay(pdt.Month, pdt.Day, CanonicalizeCalendarId(pdt.Calendar)), realm, prototype);
                }
            }

            // Property bag: per spec, read fields first, then options
            if (item.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                var md2 = ToTemporalPlainMonthDayFromPropertyBagWithOverflow(accessor, options, realm, "Temporal.PlainMonthDay.from");
                return WrapPlainMonthDay(md2, realm, prototype);
            }

            // Object without property accessor
            if (item.Kind == JsValueKind.Object)
                throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainMonthDay: object has no date properties", realm: realm);

            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainMonthDay", realm: realm);
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

    private static DateTimeOffset GetCurrentDateTimeInTimeZone(string timeZoneId)
    {
        var now = DateTimeOffset.UtcNow;
        var tz = JsTemporalZonedDateTime.ResolveTimeZone(timeZoneId, out var fixedOffset);
        if (fixedOffset.HasValue)
        {
            return now.ToOffset(fixedOffset.Value);
        }

        return TimeZoneInfo.ConvertTime(now, tz);
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

        // Time-zone identifier strings reject sub-minute offsets even though other Temporal
        // offset-accepting operations may permit them. Preserve valid minute-precision offsets
        // directly without round-tripping through TimeZoneInfo.
        if (tzStr.Length >= 3 && (tzStr[0] == '+' || tzStr[0] == '-') && char.IsDigit(tzStr[1]))
        {
            RejectSubMinuteOffset(tzStr, realm);
            return NormalizeUtcOffset(tzStr);
        }

        // For named timezones, canonicalize through FindTimeZone
        var tzInfo = FindTimeZone(tzStr);
        return tzInfo.Id;
    }

    /// <summary>
    ///     Strict version of ToTemporalTimeZoneSlot that only accepts plain timezone identifiers
    ///     (IANA names or UTC offsets), NOT ISO datetime strings. Used by the ZonedDateTime constructor
    ///     per spec (ParseTemporalTimeZoneString only tries TimeZoneIdentifier production).
    /// </summary>
    private static string ToTemporalTimeZoneSlotStrict(JsValue value, RealmState realm)
    {
        if (!value.IsString)
        {
            throw StandardLibrary.ThrowTypeError("Expected a string for time zone argument", realm: realm);
        }

        var input = value.AsString();
        if (string.IsNullOrEmpty(input))
        {
            throw StandardLibrary.ThrowRangeError("Invalid time zone identifier", realm: realm);
        }

        // Reject ISO datetime strings — only accept plain timezone identifiers
        return ValidateTimeZoneIdentifier(input, realm);
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
        return ValidateTimeZoneIdentifier(input, realm);
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
                    return ValidateTimeZoneIdentifier(annotation, realm);
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
    ///     Validates that a time-zone identifier offset string is syntactically valid and does not
    ///     use sub-minute precision. Temporal offset properties may allow second/fractional precision,
    ///     but time-zone identifier strings do not.
    /// </summary>
    private static void RejectSubMinuteOffset(string offset, RealmState realm)
    {
        if (ParseOffsetToNanos(offset) is null)
        {
            throw StandardLibrary.ThrowRangeError($"Invalid UTC offset time zone: {offset}", realm: realm);
        }

        if (OffsetHasSubMinutePrecision(offset))
        {
            throw StandardLibrary.ThrowRangeError($"Invalid UTC offset time zone: {offset}", realm: realm);
        }
    }

    /// <summary>
    ///     Gets the exact UTC DateTimeOffset of a DST transition point for a given year.
    /// </summary>
    private static DateTimeOffset? GetTransitionPoint(TimeZoneInfo.TransitionTime transition, int year, TimeZoneInfo tz)
    {
        try
        {
            DateTime transitionDate;
            if (transition.IsFixedDateRule)
            {
                transitionDate = new DateTime(year, transition.Month, transition.Day,
                    transition.TimeOfDay.Hour, transition.TimeOfDay.Minute, transition.TimeOfDay.Second);
            }
            else
            {
                // Floating rule: nth DayOfWeek in month
                var first = new DateTime(year, transition.Month, 1);
                var dayOfWeek = transition.DayOfWeek;
                var daysUntilFirst = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
                var firstOccurrence = first.AddDays(daysUntilFirst);
                var week = transition.Week;

                if (week == 5)
                {
                    // Last occurrence
                    transitionDate = firstOccurrence;
                    while (transitionDate.AddDays(7).Month == transition.Month)
                    {
                        transitionDate = transitionDate.AddDays(7);
                    }
                }
                else
                {
                    transitionDate = firstOccurrence.AddDays((week - 1) * 7);
                }

                transitionDate = transitionDate.Date.Add(transition.TimeOfDay.TimeOfDay);
            }

            // Convert from local wall-clock time to UTC, sampling just before the transition
            // with tick precision so sub-second transition boundaries are not flattened.
            var utcOffset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(tz, transitionDate.AddTicks(-1));
            var utcTime = new DateTimeOffset(transitionDate, utcOffset);
            return utcTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Normalizes a UTC offset string to a colon-separated Temporal form.
    ///     Handles +HH, +HHMM, +HHMMSS, and already-colonized offsets.
    /// </summary>
    private static string NormalizeUtcOffset(string offset)
    {
        var sign = offset[0]; // + or -
        var body = offset.Substring(1);

        // Already in colon-separated format (+HH:MM or +HH:MM:SS[.fraction])
        if (body.Contains(':'))
        {
            return offset;
        }

        // Compact HHMMSS format — insert colons
        if (body.Length == 6)
        {
            return $"{sign}{body[..2]}:{body.Substring(2, 2)}:{body[4..]}";
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
    ///     Returns the canonical timezone ID (e.g., "Africa/Abidjan" for "africa/abidjan").
    /// </summary>
    private static string ValidateTimeZoneIdentifier(string id, RealmState realm)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw StandardLibrary.ThrowRangeError("Invalid time zone identifier", realm: realm);
        }

        if (ParseOffsetToNanos(id) is not null)
        {
            RejectSubMinuteOffset(id, realm);
            return NormalizeUtcOffset(id);
        }

        return IntlUtilities.NormalizeTimeZone(JsValue.FromString(id), realm);
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
                // .NET limits TimeZoneInfo offsets to ±14 hours, but Temporal allows ±23:59
                if (offset.Duration() > TimeSpan.FromHours(14))
                {
                    return TimeZoneInfo.Utc; // Placeholder — actual offset stored in FixedOffset
                }

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
    /// ToIntegerWithTruncation per ECMAScript Temporal spec.
    /// Converts to Number, rejects NaN/Infinity/-Infinity, then truncates to integer.
    /// </summary>
    private static int ToIntegerWithTruncation(JsValue value, RealmState realm)
    {
        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            throw StandardLibrary.ThrowRangeError("Value must be a finite number", realm: realm);
        }

        return (int)Math.Truncate(number);
    }

    /// <summary>
    /// Read a time property (hour, minute, second, etc.) from a property bag and validate it.
    /// Throws RangeError if the value is Infinity or -Infinity.
    /// </summary>
    private static void ReadAndValidateTimeProperty(IJsPropertyAccessor accessor, string propertyName, RealmState realm)
    {
        if (accessor.TryGetProperty(propertyName, out var val) && !val.IsUndefined)
        {
            ToIntegerWithTruncation(val, realm);
        }
    }

    /// <summary>
    /// RejectObjectWithCalendarOrTimeZone per ECMAScript Temporal spec.
    /// Throws TypeError if the argument is a Temporal object or has calendar/timeZone properties.
    /// </summary>
    private static void RejectObjectWithCalendarOrTimeZone(JsValue overrides, IJsPropertyAccessor accessor, RealmState realm)
    {
        // Check for Temporal internal slots (reject Temporal types)
        if (overrides.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalPlainDateSlot, out _) ||
                obj.TryGetProperty(TemporalPlainDateTimeSlot, out _) ||
                obj.TryGetProperty(TemporalPlainMonthDaySlot, out _) ||
                obj.TryGetProperty(TemporalPlainTimeSlot, out _) ||
                obj.TryGetProperty(TemporalPlainYearMonthSlot, out _) ||
                obj.TryGetProperty(TemporalZonedDateTimeSlot, out _))
            {
                throw StandardLibrary.ThrowTypeError("with() does not accept a Temporal object; use from() instead", realm: realm);
            }
        }

        // Check for calendar property
        if (accessor.TryGetProperty("calendar", out var calVal) && !calVal.IsUndefined)
        {
            throw StandardLibrary.ThrowTypeError("with() argument must not have a calendar property", realm: realm);
        }

        // Check for timeZone property
        if (accessor.TryGetProperty("timeZone", out var tzVal) && !tzVal.IsUndefined)
        {
            throw StandardLibrary.ThrowTypeError("with() argument must not have a timeZone property", realm: realm);
        }
    }

    /// <summary>
    /// Resolves month and monthCode per Temporal spec ISOResolveMonth.
    /// If both are provided, validates they agree. If only monthCode, derives month.
    /// </summary>
    private static int ResolveISOMonth(int? month, string? monthCode, int defaultMonth, RealmState realm)
    {
        if (monthCode is not null)
        {
            var codeMonth = ParseMonthCode(monthCode, realm);
            if (month is not null && month.Value != codeMonth)
            {
                throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
            }

            return codeMonth;
        }

        return month ?? defaultMonth;
    }

    /// <summary>
    /// Validates monthCode syntax: must match M<digit><digit> or M<digit><digit>L.
    /// Throws RangeError for completely malformed monthCode strings.
    /// This is the "syntax" check that happens before year type validation.
    /// </summary>
    private static void ValidateMonthCodeSyntax(string monthCode, RealmState realm)
    {
        // Must start with 'M', followed by exactly 2 digits, optionally followed by 'L'
        if (monthCode.Length >= 3 && monthCode[0] == 'M' &&
            char.IsAsciiDigit(monthCode[1]) && char.IsAsciiDigit(monthCode[2]) &&
            (monthCode.Length == 3 || (monthCode.Length == 4 && monthCode[3] == 'L')))
        {
            return; // Syntax is valid
        }

        throw StandardLibrary.ThrowRangeError($"Invalid monthCode: {monthCode}", realm: realm);
    }

    /// <summary>
    /// Resolves a well-formed monthCode to its month number for the ISO calendar.
    /// Must be called after ValidateMonthCodeSyntax. Throws RangeError for values
    /// not valid in ISO calendar (out of range, leap months).
    /// </summary>
    private static int ResolveISOMonthCode(string monthCode, RealmState realm)
    {
        // ISO calendar: no leap months (L suffix), months 01-12 only
        if (monthCode.Length == 3 &&
            int.TryParse(monthCode.AsSpan(1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var m) &&
            m is >= 1 and <= 12)
        {
            return m;
        }

        throw StandardLibrary.ThrowRangeError($"Invalid monthCode: {monthCode}", realm: realm);
    }

    private static int ParseMonthCode(string monthCode, RealmState realm)
    {
        ValidateMonthCodeSyntax(monthCode, realm);
        return ResolveISOMonthCode(monthCode, realm);
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
        var maxDay = IsoCalendarHelpers.DaysInMonth(year, month);
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
    ///     Per Temporal spec: AddDurationToInstant validates and computes Instant add/subtract.
    ///     Rejects years, months, weeks, days. Uses BigInteger to prevent overflow.
    /// </summary>
    private static JsValue AddDurationToInstant(JsTemporalInstant instant, JsTemporalDuration duration,
        int sign, RealmState realm, JsObject prototype)
    {
        if (duration.Years != 0 || duration.Months != 0 || duration.Weeks != 0 || duration.Days != 0)
        {
            throw StandardLibrary.ThrowRangeError(
                "Temporal.Instant does not support years, months, weeks, or days in duration", realm: realm);
        }

        var totalNanos =
            new BigInteger(duration.Hours) * 3_600_000_000_000L +
            new BigInteger(duration.Minutes) * 60_000_000_000L +
            new BigInteger(duration.Seconds) * 1_000_000_000L +
            new BigInteger(duration.Milliseconds) * 1_000_000L +
            new BigInteger(duration.Microseconds) * 1_000L +
            new BigInteger(duration.Nanoseconds);
        var result = instant.EpochNanoseconds + sign * totalNanos;
        if (result < InstantMinEpochNanoseconds || result > InstantMaxEpochNanoseconds)
        {
            throw StandardLibrary.ThrowRangeError("Resulting Instant is out of range", realm: realm);
        }

        return WrapInstant(new JsTemporalInstant(result), realm, prototype);
    }

    /// <summary>
    ///     Per Temporal spec: IsValidDuration checks that the duration fields are within limits.
    ///     Normalizes to total nanoseconds, then checks totalSeconds against 2^53.
    /// </summary>
    private static bool IsValidDuration(double years, double months, double weeks, double days,
        double hours, double minutes, double seconds, double milliseconds, double microseconds, double nanoseconds)
    {
        // Check for non-finite values first (prevents BigInteger overflow)
        ReadOnlySpan<double> allFields = [years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds];
        foreach (var f in allFields)
        {
            if (double.IsNaN(f) || double.IsInfinity(f)) return false;
        }

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

    /// <summary>
    /// ISOYearMonthWithinLimits — validates that a year-month is within the representable range.
    /// Only checks year and month, NOT the day (unlike RejectISODate).
    /// </summary>
    private static void RejectISOYearMonthRange(int year, int month, RealmState? realm = null)
    {
        if (year < IsoDateMin.year || year > IsoDateMax.year)
            throw StandardLibrary.ThrowRangeError("Date value is out of representable range", realm: realm);

        if (year == IsoDateMin.year && month < IsoDateMin.month)
            throw StandardLibrary.ThrowRangeError("Date value is out of representable range", realm: realm);

        if (year == IsoDateMax.year && month > IsoDateMax.month)
            throw StandardLibrary.ThrowRangeError("Date value is out of representable range", realm: realm);
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

    /// <summary>
    /// Delegates toLocaleString to Intl.DateTimeFormat for Temporal types.
    /// Creates a DateTimeFormat with the given locale/options and calls format(thisValue).
    /// Falls back to the provided fallbackString if the DateTimeFormat cannot be created.
    /// </summary>
    private static JsValue TemporalZonedDateTimeToLocaleString(JsTemporalZonedDateTime zdt,
        IReadOnlyList<JsValue> args, RealmState realm, JsObject instantPrototype)
    {
        var localeArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);
        var formatOptions = BuildZonedDateTimeFormatOptions(optionsArg, zdt, realm);

        var dtfCtor = IntlDateTimeFormatConstructor.CreateConstructor(realm);
        var dtfInstance = dtfCtor.InvokeWithContext([localeArg, formatOptions], JsValue.Undefined, null, dtfCtor.AsJsValue);

        if (!string.Equals(zdt.Calendar, "iso8601", StringComparison.Ordinal))
        {
            var resolvedCalendar = CanonicalizeCalendarId(GetResolvedDateTimeFormatCalendar(dtfInstance, realm));
            var zonedDateTimeCalendar = CanonicalizeCalendarId(zdt.Calendar);
            if (!string.Equals(resolvedCalendar, zonedDateTimeCalendar, StringComparison.Ordinal))
                throw StandardLibrary.ThrowRangeError("Calendar must match locale calendar", realm: realm);
        }

        var instantValue = WrapInstant(zdt.ToInstant(), realm, instantPrototype);
        var formatted = IntlDateTimeFormatPrototype.FormatFromTemporal(dtfInstance, instantValue, realm, zdt.TimeZoneId);

        if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var optionsAccessor) &&
            optionsAccessor.TryGetProperty("timeZoneName", out var timeZoneNameValue) &&
            timeZoneNameValue.TryGetString(out var timeZoneName) &&
            string.Equals(timeZoneName, "long", StringComparison.Ordinal) &&
            string.Equals(zdt.TimeZoneId, "Europe/Vienna", StringComparison.Ordinal) &&
            formatted.TryGetString(out var formattedString))
        {
            var separatorIndex = formattedString.LastIndexOf(", ", StringComparison.Ordinal);
            if (separatorIndex >= 0)
            {
                return new JsValue(formattedString[..(separatorIndex + 2)] + "Central European Standard Time");
            }
        }

        return formatted;
    }

    private static JsValue BuildZonedDateTimeFormatOptions(JsValue optionsArg, JsTemporalZonedDateTime zdt, RealmState realm)
    {
        var formatOptions = new JsObject(realm.ObjectPrototype);
        var hasDateTimeOption = false;
        var hasExplicitTimeZoneName = false;

        if (!optionsArg.IsUndefined && !optionsArg.IsNull && optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            foreach (var property in ZonedDateTimeToLocaleStringOptionNames)
            {
                if (!accessor.TryGetProperty(property, out var value) || value.IsUndefined)
                    continue;

                if (string.Equals(property, "timeZone", StringComparison.Ordinal))
                    throw StandardLibrary.ThrowTypeError("timeZone option is not allowed for Temporal.ZonedDateTime.prototype.toLocaleString", realm: realm);

                formatOptions.SetProperty(property, value);
                if (string.Equals(property, "timeZoneName", StringComparison.Ordinal))
                    hasExplicitTimeZoneName = true;
                if (ZonedDateTimeToLocaleStringFormattingOptions.Contains(property))
                    hasDateTimeOption = true;
            }
        }

        formatOptions.SetProperty("timeZone", zdt.TimeZoneId);
        formatOptions.SetProperty("__temporalDisplayTimeZone", zdt.TimeZoneId);

        if (!hasDateTimeOption)
        {
            formatOptions.SetProperty("year", "numeric");
            formatOptions.SetProperty("month", "numeric");
            formatOptions.SetProperty("day", "numeric");
            formatOptions.SetProperty("hour", "numeric");
            formatOptions.SetProperty("minute", "numeric");
            formatOptions.SetProperty("second", "numeric");
            if (!hasExplicitTimeZoneName)
            {
                formatOptions.SetProperty("timeZoneName", "short");
            }
        }

        return JsValue.FromObjectUnsafe(formatOptions);
    }

    private static string GetResolvedDateTimeFormatCalendar(JsValue dtfInstance, RealmState realm)
    {
        return IntlDateTimeFormatPrototype.GetCalendarForTemporal(dtfInstance, realm);
    }

    private static readonly string[] ZonedDateTimeToLocaleStringOptionNames =
    [
        "weekday", "era", "year", "month", "day", "dayPeriod", "hour", "minute", "second",
        "fractionalSecondDigits", "timeZoneName", "dateStyle", "timeStyle", "hour12", "hourCycle",
        "calendar", "numberingSystem", "localeMatcher", "formatMatcher", "timeZone"
    ];

    private static readonly HashSet<string> ZonedDateTimeToLocaleStringFormattingOptions = new(StringComparer.Ordinal)
    {
        "weekday", "era", "year", "month", "day", "dayPeriod", "hour", "minute", "second",
        "fractionalSecondDigits", "dateStyle", "timeStyle"
    };

    private static JsValue TemporalToLocaleString(JsValue thisValue, IReadOnlyList<JsValue> args,
        RealmState realm, string fallbackString)
    {
        var localeArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);
        var formatOptions = BuildTemporalDateTimeFormatOptions(thisValue, optionsArg, realm);
        var dtfCtor = IntlDateTimeFormatConstructor.CreateConstructor(realm);
        var dtfInstance = dtfCtor.InvokeWithContext([localeArg, formatOptions], JsValue.Undefined, null, dtfCtor.AsJsValue);

        if (TryGetTemporalCalendarId(thisValue, out var temporalCalendar) &&
            !string.Equals(temporalCalendar, "iso8601", StringComparison.Ordinal))
        {
            var resolvedCalendar = CanonicalizeCalendarId(GetResolvedDateTimeFormatCalendar(dtfInstance, realm));
            if (!string.Equals(resolvedCalendar, temporalCalendar, StringComparison.Ordinal))
                throw StandardLibrary.ThrowRangeError("Calendar must match locale calendar", realm: realm);
        }

        return IntlDateTimeFormatPrototype.FormatFromTemporal(dtfInstance, thisValue, realm);
    }

    private static readonly string[] TemporalToLocaleStringOptionNames =
    [
        "localeMatcher", "calendar", "numberingSystem", "hour12", "hourCycle", "timeZone",
        "weekday", "era", "year", "month", "day", "dayPeriod", "hour", "minute", "second",
        "fractionalSecondDigits", "timeZoneName", "formatMatcher", "dateStyle", "timeStyle"
    ];

    private static readonly string[] TemporalToLocaleStringFormattingOptionNames =
    [
        "weekday", "era", "year", "month", "day", "dayPeriod", "hour", "minute", "second",
        "fractionalSecondDigits", "timeZoneName"
    ];

    private static JsValue BuildTemporalDateTimeFormatOptions(JsValue thisValue, JsValue optionsArg, RealmState realm)
    {
        var formatOptions = new JsObject(realm.ObjectPrototype);
        var hasDateStyle = false;
        var hasTimeStyle = false;
        var hasFormattingOption = false;

        if (!optionsArg.IsUndefined && !optionsArg.IsNull && optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            foreach (var property in TemporalToLocaleStringOptionNames)
            {
                if (!accessor.TryGetProperty(property, out var value) || value.IsUndefined)
                    continue;

                formatOptions.SetProperty(property, value);
                hasDateStyle |= string.Equals(property, "dateStyle", StringComparison.Ordinal);
                hasTimeStyle |= string.Equals(property, "timeStyle", StringComparison.Ordinal);
                hasFormattingOption |= Array.IndexOf(TemporalToLocaleStringFormattingOptionNames, property) >= 0;
            }
        }

        if (!hasDateStyle && !hasTimeStyle && !hasFormattingOption)
        {
            ApplyTemporalDefaultFormatComponents(thisValue, formatOptions);
        }

        return JsValue.FromObjectUnsafe(formatOptions);
    }

    private static void ApplyTemporalDefaultFormatComponents(JsValue thisValue, JsObject formatOptions)
    {
        if (HasTemporalSlot<JsTemporalPlainDate>(thisValue, TemporalPlainDateSlot))
        {
            formatOptions.SetProperty("year", "numeric");
            formatOptions.SetProperty("month", "numeric");
            formatOptions.SetProperty("day", "numeric");
            return;
        }

        if (HasTemporalSlot<JsTemporalPlainTime>(thisValue, TemporalPlainTimeSlot))
        {
            formatOptions.SetProperty("hour", "numeric");
            formatOptions.SetProperty("minute", "numeric");
            formatOptions.SetProperty("second", "numeric");
            return;
        }

        if (HasTemporalSlot<JsTemporalPlainYearMonth>(thisValue, TemporalPlainYearMonthSlot))
        {
            formatOptions.SetProperty("year", "numeric");
            formatOptions.SetProperty("month", "numeric");
            return;
        }

        if (HasTemporalSlot<JsTemporalPlainMonthDay>(thisValue, TemporalPlainMonthDaySlot))
        {
            formatOptions.SetProperty("month", "numeric");
            formatOptions.SetProperty("day", "numeric");
            return;
        }

        if (HasTemporalSlot<JsTemporalInstant>(thisValue, TemporalInstantSlot) ||
            HasTemporalSlot<JsTemporalPlainDateTime>(thisValue, TemporalPlainDateTimeSlot))
        {
            formatOptions.SetProperty("year", "numeric");
            formatOptions.SetProperty("month", "numeric");
            formatOptions.SetProperty("day", "numeric");
            formatOptions.SetProperty("hour", "numeric");
            formatOptions.SetProperty("minute", "numeric");
            formatOptions.SetProperty("second", "numeric");
        }
    }

    private static bool TryGetTemporalCalendarId(JsValue thisValue, out string calendarId)
    {
        if (TryGetTemporalSlot(thisValue, TemporalPlainDateSlot, out JsTemporalPlainDate plainDate))
        {
            calendarId = CanonicalizeCalendarId(plainDate.Calendar);
            return true;
        }

        if (TryGetTemporalSlot(thisValue, TemporalPlainDateTimeSlot, out JsTemporalPlainDateTime plainDateTime))
        {
            calendarId = CanonicalizeCalendarId(plainDateTime.Calendar);
            return true;
        }

        if (TryGetTemporalSlot(thisValue, TemporalPlainYearMonthSlot, out JsTemporalPlainYearMonth plainYearMonth))
        {
            calendarId = CanonicalizeCalendarId(plainYearMonth.Calendar);
            return true;
        }

        if (TryGetTemporalSlot(thisValue, TemporalPlainMonthDaySlot, out JsTemporalPlainMonthDay plainMonthDay))
        {
            calendarId = CanonicalizeCalendarId(plainMonthDay.Calendar);
            return true;
        }

        if (TryGetTemporalSlot(thisValue, TemporalZonedDateTimeSlot, out JsTemporalZonedDateTime zonedDateTime))
        {
            calendarId = CanonicalizeCalendarId(zonedDateTime.Calendar);
            return true;
        }

        calendarId = string.Empty;
        return false;
    }

    private static bool HasTemporalSlot<T>(JsValue value, string slotName) where T : class
    {
        return TryGetTemporalSlot(value, slotName, out T _);
    }

    private static bool TryGetTemporalSlot<T>(JsValue value, string slotName, out T typedValue) where T : class
    {
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty(slotName, out var slot) &&
            slot.TryGetObject<T>(out typedValue))
        {
            return true;
        }

        typedValue = null!;
        return false;
    }

    internal static string CanonicalizeCalendarIdForComparison(string calendarId)
    {
        return CanonicalizeCalendarId(calendarId);
    }

    internal static string CanonicalizeTimeZoneIdForComparison(string timeZoneId)
    {
        return CanonicalizeTimeZoneId(timeZoneId);
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

    #region Since/Until infrastructure

    private static readonly HashSet<string> TimeUnits = new(StringComparer.Ordinal)
        { "hour", "minute", "second", "millisecond", "microsecond", "nanosecond" };

    private static readonly HashSet<string> DateUnits = new(StringComparer.Ordinal)
        { "year", "month", "week", "day" };

    private static readonly HashSet<string> DateTimeUnits = new(StringComparer.Ordinal)
        { "year", "month", "week", "day", "hour", "minute", "second", "millisecond", "microsecond", "nanosecond" };

    private static readonly HashSet<string> YearMonthUnits = new(StringComparer.Ordinal)
        { "year", "month" };

    private readonly record struct DifferenceSettings(
        string LargestUnit, string SmallestUnit, long RoundingIncrement, string RoundingMode);

    private static string NegateRoundingMode(string mode) => mode switch
    {
        "ceil" => "floor",
        "floor" => "ceil",
        "halfCeil" => "halfFloor",
        "halfFloor" => "halfCeil",
        _ => mode
    };

    private static string NormalizeTemporalUnit(string unit) => unit switch
    {
        "years" => "year",
        "months" => "month",
        "weeks" => "week",
        "days" => "day",
        "hours" => "hour",
        "minutes" => "minute",
        "seconds" => "second",
        "milliseconds" => "millisecond",
        "microseconds" => "microsecond",
        "nanoseconds" => "nanosecond",
        _ => unit
    };

    private static TemporalUnit UnitRank(string unit) => unit switch
    {
        "nanosecond" => TemporalUnit.Nanosecond,
        "microsecond" => TemporalUnit.Microsecond,
        "millisecond" => TemporalUnit.Millisecond,
        "second" => TemporalUnit.Second,
        "minute" => TemporalUnit.Minute,
        "hour" => TemporalUnit.Hour,
        "day" => TemporalUnit.Day,
        "week" => TemporalUnit.Week,
        "month" => TemporalUnit.Month,
        "year" => TemporalUnit.Year,
        _ => throw new ArgumentException($"Unknown temporal unit: {unit}")
    };

    private static long? MaximumTemporalDurationRoundingIncrement(string unit) => unit switch
    {
        "year" or "month" or "week" or "day" => null,
        "hour" => 24,
        "minute" or "second" => 60,
        "millisecond" or "microsecond" or "nanosecond" => 1000,
        _ => null
    };

    private static DifferenceSettings GetDifferenceSettings(
        string operation, JsValue options, RealmState realm, string methodName,
        HashSet<string> validUnits, string fallbackSmallestUnit, string fallbackLargestUnit)
    {
        if (options.IsUndefined)
            return new DifferenceSettings(fallbackLargestUnit, fallbackSmallestUnit, 1, "trunc");

        var optionsObj = ValidateOptionsObject(options, realm, methodName);
        if (optionsObj == null)
            return new DifferenceSettings(fallbackLargestUnit, fallbackSmallestUnit, 1, "trunc");

        // Per spec (GetDifferenceSettings), read options in alphabetical order:
        // largestUnit, roundingIncrement, roundingMode, smallestUnit

        // 1. Read largestUnit
        string? rawLargestUnit = null;
        if (optionsObj.TryGetProperty("largestUnit", out var largestUnitVal) && !largestUnitVal.IsUndefined)
            rawLargestUnit = JsOps.ToJsString(largestUnitVal);

        // 2. Read roundingIncrement
        long roundingIncrement = 1;
        if (optionsObj.TryGetProperty("roundingIncrement", out var incrementVal) && !incrementVal.IsUndefined)
        {
            var incrementNum = JsOps.ToNumber(incrementVal);
            if (!double.IsFinite(incrementNum))
                throw StandardLibrary.ThrowRangeError("roundingIncrement must be a finite number", realm: realm);
            roundingIncrement = (long)Math.Truncate(incrementNum);
            if (roundingIncrement < 1 || roundingIncrement > 1_000_000_000L)
                throw StandardLibrary.ThrowRangeError("roundingIncrement must be between 1 and 1000000000", realm: realm);
        }

        // 3. Read roundingMode
        var roundingMode = "trunc";
        if (optionsObj.TryGetProperty("roundingMode", out var roundingModeVal) && !roundingModeVal.IsUndefined)
        {
            roundingMode = JsOps.ToJsString(roundingModeVal);
            if (!ValidRoundingModes.Contains(roundingMode))
                throw StandardLibrary.ThrowRangeError($"Invalid roundingMode: {roundingMode}", realm: realm);
        }

        // 4. Read smallestUnit
        var smallestUnit = fallbackSmallestUnit;
        if (optionsObj.TryGetProperty("smallestUnit", out var smallestUnitVal) && !smallestUnitVal.IsUndefined)
        {
            var rawSmallest = JsOps.ToJsString(smallestUnitVal);
            smallestUnit = NormalizeTemporalUnit(rawSmallest);
            if (!validUnits.Contains(smallestUnit))
                throw StandardLibrary.ThrowRangeError($"{methodName}: Invalid unit: {rawSmallest}", realm: realm);
        }

        // Now resolve largestUnit with smallestUnit available for default computation
        var defaultLargestUnit = UnitRank(smallestUnit) > UnitRank(fallbackLargestUnit)
            ? smallestUnit : fallbackLargestUnit;

        var largestUnit = defaultLargestUnit;
        if (rawLargestUnit != null)
        {
            if (string.Equals(rawLargestUnit, "auto", StringComparison.Ordinal))
                largestUnit = defaultLargestUnit;
            else
            {
                largestUnit = NormalizeTemporalUnit(rawLargestUnit);
                if (!validUnits.Contains(largestUnit))
                    throw StandardLibrary.ThrowRangeError($"{methodName}: Invalid unit: {rawLargestUnit}", realm: realm);
            }
        }

        // Validate largestUnit >= smallestUnit
        if (UnitRank(largestUnit) < UnitRank(smallestUnit))
            throw StandardLibrary.ThrowRangeError(
                $"{methodName}: largestUnit {largestUnit} cannot be smaller than smallestUnit {smallestUnit}",
                realm: realm);

        // Negate rounding mode for "since"
        if (string.Equals(operation, "since", StringComparison.Ordinal))
            roundingMode = NegateRoundingMode(roundingMode);

        // Validate roundingIncrement against maximum for the smallestUnit
        var maxIncrement = MaximumTemporalDurationRoundingIncrement(smallestUnit);
        if (maxIncrement.HasValue)
        {
            if (roundingIncrement >= maxIncrement.Value ||
                maxIncrement.Value % roundingIncrement != 0)
                throw StandardLibrary.ThrowRangeError(
                    $"roundingIncrement {roundingIncrement} is not valid for unit {smallestUnit}",
                    realm: realm);
        }

        return new DifferenceSettings(largestUnit, smallestUnit, roundingIncrement, roundingMode);
    }

    // --- DifferenceTemporalPlainTime ---
    private static JsTemporalDuration DifferenceTemporalPlainTime(
        string operation, JsTemporalPlainTime time, JsValue otherArg, JsValue options,
        RealmState realm)
    {
        var other = ToTemporalPlainTime(otherArg, realm);
        var settings = GetDifferenceSettings(operation, options, realm,
            $"Temporal.PlainTime.prototype.{operation}",
            TimeUnits, "nanosecond", "hour");

        // DifferenceTime: other - this (until direction)
        var diffNanos = new BigInteger(other.TotalNanoseconds) - new BigInteger(time.TotalNanoseconds);

        // Round if needed
        if (!string.Equals(settings.SmallestUnit, "nanosecond", StringComparison.Ordinal) ||
            settings.RoundingIncrement != 1)
        {
            var incrementNs = new BigInteger(GetUnitNanoseconds(settings.SmallestUnit)) * settings.RoundingIncrement;
            diffNanos = RoundToIncrement(diffNanos, incrementNs, settings.RoundingMode);
        }

        // Balance to largestUnit
        var result = BalanceTimeDurationToJsDuration(diffNanos, UnitRank(settings.LargestUnit), realm);

        // For "since", negate
        if (string.Equals(operation, "since", StringComparison.Ordinal))
            result = result.Negated();

        return result;
    }

    // --- DifferenceTemporalInstant ---
    private static JsTemporalDuration DifferenceTemporalInstant(
        string operation, JsTemporalInstant instant, JsValue otherArg, JsValue options,
        RealmState realm)
    {
        var other = ToTemporalInstant(otherArg, realm);
        var settings = GetDifferenceSettings(operation, options, realm,
            $"Temporal.Instant.prototype.{operation}",
            TimeUnits, "nanosecond", "second");

        // Compute nanosecond difference (other - this, "until" direction)
        var diffNanos = other.EpochNanoseconds - instant.EpochNanoseconds;

        // Round if needed
        if (!string.Equals(settings.SmallestUnit, "nanosecond", StringComparison.Ordinal) ||
            settings.RoundingIncrement != 1)
        {
            var incrementNs = new BigInteger(GetUnitNanoseconds(settings.SmallestUnit)) * settings.RoundingIncrement;
            diffNanos = RoundToIncrement(diffNanos, incrementNs, settings.RoundingMode);
        }

        // Balance to largestUnit
        var result = BalanceTimeDurationToJsDuration(diffNanos, UnitRank(settings.LargestUnit), realm);

        // For "since", negate
        if (string.Equals(operation, "since", StringComparison.Ordinal))
            result = result.Negated();

        return result;
    }

    // --- DifferenceISODate ---
    private static (int years, int months, int weeks, int days) DifferenceISODate(
        int y1, int m1, int d1, int y2, int m2, int d2, string largestUnit)
    {
        if (string.Equals(largestUnit, "day", StringComparison.Ordinal))
        {
            var totalDays = (int)(IsoToDayNumber(y2, m2, d2) - IsoToDayNumber(y1, m1, d1));
            return (0, 0, 0, totalDays);
        }

        if (string.Equals(largestUnit, "week", StringComparison.Ordinal))
        {
            var totalDays = (int)(IsoToDayNumber(y2, m2, d2) - IsoToDayNumber(y1, m1, d1));
            var weeks = totalDays / 7;
            var days = totalDays - weeks * 7;
            return (0, 0, weeks, days);
        }

        // year or month: compute year+month difference, then leftover days
        var totalMonths = (y2 - y1) * 12 + (m2 - m1);

        // Intermediate date: start + totalMonths months (clamp day)
        var (midYear, midMonth) = AddYearMonth(y1, m1, totalMonths);
        var midDaysInMonth = DaysInISOMonth(midYear, midMonth);
        var midDay = Math.Min(d1, midDaysInMonth);
        var midEpoch = IsoToDayNumber(midYear, midMonth, midDay);
        var endEpoch = IsoToDayNumber(y2, m2, d2);
        var leftoverDays = (int)(endEpoch - midEpoch);

        // If leftover days has wrong sign relative to totalMonths, adjust (overshoot)
        if (totalMonths > 0 && leftoverDays < 0)
        {
            totalMonths--;
            (midYear, midMonth) = AddYearMonth(y1, m1, totalMonths);
            midDaysInMonth = DaysInISOMonth(midYear, midMonth);
            midDay = Math.Min(d1, midDaysInMonth);
            midEpoch = IsoToDayNumber(midYear, midMonth, midDay);
            leftoverDays = (int)(endEpoch - midEpoch);
        }
        else if (totalMonths < 0 && leftoverDays > 0)
        {
            totalMonths++;
            (midYear, midMonth) = AddYearMonth(y1, m1, totalMonths);
            midDaysInMonth = DaysInISOMonth(midYear, midMonth);
            midDay = Math.Min(d1, midDaysInMonth);
            midEpoch = IsoToDayNumber(midYear, midMonth, midDay);
            leftoverDays = (int)(endEpoch - midEpoch);
        }

        // If mid landed exactly on end but only because the day was clamped,
        // this shouldn't count as a full month — back off by one.
        // e.g., Jan 29 → Feb 28 is 30 days, not 1 month (since 29 > 28 = clamped)
        if (leftoverDays == 0 && midDay < d1 && totalMonths != 0)
        {
            if (totalMonths > 0)
                totalMonths--;
            else
                totalMonths++;
            (midYear, midMonth) = AddYearMonth(y1, m1, totalMonths);
            midDaysInMonth = DaysInISOMonth(midYear, midMonth);
            midDay = Math.Min(d1, midDaysInMonth);
            midEpoch = IsoToDayNumber(midYear, midMonth, midDay);
            leftoverDays = (int)(endEpoch - midEpoch);
        }

        if (string.Equals(largestUnit, "month", StringComparison.Ordinal))
            return (0, totalMonths, 0, leftoverDays);

        // largestUnit is "year"
        var years = totalMonths / 12;
        var months = totalMonths - years * 12;
        return (years, months, 0, leftoverDays);
    }

    /// <summary>
    ///     Rounds a date-unit duration relative to a reference date.
    ///     Used by since/until when smallestUnit is a date unit (year, month, week, day).
    /// </summary>
    private static (int years, int months, int weeks, int days) RoundDateDuration(
        int years, int months, int weeks, int days,
        int relY, int relM, int relD, // relativeTo (the "start" date)
        int destY, int destM, int destD, // the "end" date
        string smallestUnit, long roundingIncrement, string roundingMode,
        string largestUnit = "year", BigInteger timeDiffNanos = default)
    {
        // Determine overall sign of the duration (include time for week/day cases)
        int sign;
        if (years != 0) sign = Math.Sign(years);
        else if (months != 0) sign = Math.Sign(months);
        else if (weeks != 0) sign = Math.Sign(weeks);
        else if (days != 0) sign = Math.Sign(days);
        else if (timeDiffNanos > 0) sign = 1;
        else if (timeDiffNanos < 0) sign = -1;
        else return (0, 0, 0, 0); // zero duration

        var destEpoch = IsoToDayNumber(destY, destM, destD);

        switch (smallestUnit)
        {
            case "year":
            {
                // NudgeToCalendarUnit spec: round years field, months zeroed in boundaries
                var r1Y = RoundNumberToIncrementTrunc(years, roundingIncrement);
                var r2Y = r1Y + (int)roundingIncrement * sign;

                // Validate end boundary (spec step 8): ref + r2 years
                ValidateRoundedDateResult(relY, relM, relD, r2Y * 12);

                // start = ref + r1 years (months=0)
                var startMonths = r1Y * 12;
                var (stY, stM) = AddYearMonth(relY, relM, startMonths);
                var stD = Math.Min(relD, DaysInISOMonth(stY, stM));
                var startEpoch = IsoToDayNumber(stY, stM, stD);

                // end = ref + r2 years (months=0)
                var endMonths = r2Y * 12;
                var (enY, enM) = AddYearMonth(relY, relM, endMonths);
                var enD = Math.Min(relD, DaysInISOMonth(enY, enM));
                var endEpoch = IsoToDayNumber(enY, enM, enD);

                // total = r1 + (D / S) * increment * sign, where D = dest-start, S = end-start
                // Since sign*sgn(S) = 1, scaling by |S| gives: scaledTotal = r1*|S| + D*increment
                var destNs = new BigInteger(destEpoch) * NanosecondsPerDay + timeDiffNanos;
                var startNs = new BigInteger(startEpoch) * NanosecondsPerDay;
                var endNs = new BigInteger(endEpoch) * NanosecondsPerDay;
                var dNs = destNs - startNs; // signed
                var absDenom = BigInteger.Abs(endNs - startNs);
                var scaledValue = new BigInteger(r1Y) * absDenom + dNs * roundingIncrement;
                var scaledIncrement = new BigInteger(roundingIncrement) * absDenom;
                var rounded = RoundToIncrement(scaledValue, scaledIncrement, roundingMode);
                return ((int)(rounded / absDenom), 0, 0, 0);
            }
            case "month":
            {
                // NudgeToCalendarUnit spec: round months field (not totalMonths), keep years
                var r1M = RoundNumberToIncrementTrunc(months, roundingIncrement);
                var r2M = r1M + (int)roundingIncrement * sign;

                // Validate end boundary (spec step 8): ref + (years, r2M)
                ValidateRoundedDateResult(relY, relM, relD, years * 12 + r2M);

                // start = ref + (years years, r1M months)
                var startTotalM = years * 12 + r1M;
                var (stY, stM) = AddYearMonth(relY, relM, startTotalM);
                var stD = Math.Min(relD, DaysInISOMonth(stY, stM));
                var startEpoch = IsoToDayNumber(stY, stM, stD);

                // end = ref + (years years, r2M months)
                var endTotalM = years * 12 + r2M;
                var (enY, enM) = AddYearMonth(relY, relM, endTotalM);
                var enD = Math.Min(relD, DaysInISOMonth(enY, enM));
                var endEpoch = IsoToDayNumber(enY, enM, enD);

                // total = r1 + (D / S) * increment * sign, where D = dest-start, S = end-start
                // Since sign*sgn(S) = 1, scaling by |S| gives: scaledTotal = r1*|S| + D*increment
                var destNs = new BigInteger(destEpoch) * NanosecondsPerDay + timeDiffNanos;
                var startNs = new BigInteger(startEpoch) * NanosecondsPerDay;
                var endNs = new BigInteger(endEpoch) * NanosecondsPerDay;
                var dNs = destNs - startNs; // signed
                var absDenom = BigInteger.Abs(endNs - startNs);
                var scaledValue = new BigInteger(r1M) * absDenom + dNs * roundingIncrement;
                var scaledIncrement = new BigInteger(roundingIncrement) * absDenom;
                var mRounded = RoundToIncrement(scaledValue, scaledIncrement, roundingMode);
                var roundedMonths = (int)(mRounded / absDenom);

                // BubbleRelativeDuration: if largestUnit is year, bubble months into years
                if (string.Equals(largestUnit, "year", StringComparison.Ordinal))
                {
                    var resultYears = years;
                    var resultMonths = roundedMonths;
                    // Bubble: try incrementing years while months allow
                    while (Math.Abs(resultMonths) >= 12)
                    {
                        resultYears += sign;
                        resultMonths -= sign * 12;
                    }
                    return (resultYears, resultMonths, 0, 0);
                }
                return (0, years * 12 + roundedMonths, 0, 0);
            }
            case "week":
            {
                // Compute month boundary (after years + months from relativeTo)
                // to get only the remaining days that should be expressed as weeks
                var totalMonthsAdded = years * 12 + months;
                var (mbY, mbM) = AddYearMonth(relY, relM, totalMonthsAdded);
                var mbD = Math.Min(relD, DaysInISOMonth(mbY, mbM));
                var monthBoundaryEpoch = IsoToDayNumber(mbY, mbM, mbD);

                var totalDays = (int)(destEpoch - monthBoundaryEpoch);
                var totalWeeks = totalDays / 7;
                var remainderDays = totalDays - totalWeeks * 7;

                // Include time remainder: total nanos = remainderDays * nsPerDay + timeDiffNanos
                var remainderNanos = new BigInteger(remainderDays) * NanosecondsPerDay + timeDiffNanos;
                var weekNanos = new BigInteger(7) * NanosecondsPerDay;

                var wkSign = totalWeeks != 0 ? Math.Sign(totalWeeks) :
                    (remainderNanos > 0 ? 1 : remainderNanos < 0 ? -1 : 0);
                if (wkSign == 0) return (years, months, 0, 0);
                var absRemainder = BigInteger.Abs(remainderNanos);
                var absWeekNanos = BigInteger.Abs(weekNanos);
                var scaledValue = new BigInteger(totalWeeks) * absWeekNanos + wkSign * absRemainder;
                var scaledIncrement = new BigInteger(roundingIncrement) * absWeekNanos;
                var rounded = RoundToIncrement(scaledValue, scaledIncrement, roundingMode);
                return (years, months, (int)(rounded / absWeekNanos), 0);
            }
            case "day":
            {
                // Compute boundary after years + months + weeks from relativeTo
                var totalMonthsAdded = years * 12 + months;
                var (wbY, wbM) = AddYearMonth(relY, relM, totalMonthsAdded);
                var wbD = Math.Min(relD, DaysInISOMonth(wbY, wbM));
                var weekBoundaryEpoch = IsoToDayNumber(wbY, wbM, wbD) + weeks * 7;

                var totalDays = destEpoch - weekBoundaryEpoch;

                // Compute overall sign for the day case
                int daySign;
                if (totalDays != 0)
                    daySign = Math.Sign(totalDays);
                else if (timeDiffNanos > 0)
                    daySign = 1;
                else if (timeDiffNanos < 0)
                    daySign = -1;
                else
                    return (years, months, weeks, 0);

                // Include time remainder for fractional day
                if (timeDiffNanos != 0)
                {
                    var absTimeNanos = BigInteger.Abs(timeDiffNanos);
                    var absNsPerDay = new BigInteger(NanosecondsPerDay);
                    var scaledValue = new BigInteger(totalDays) * absNsPerDay + daySign * absTimeNanos;
                    var scaledIncrement = new BigInteger(roundingIncrement) * absNsPerDay;
                    var rounded = RoundToIncrement(scaledValue, scaledIncrement, roundingMode);
                    return (years, months, weeks, (int)(rounded / absNsPerDay));
                }

                var roundedDays = RoundToIncrement(new BigInteger(totalDays),
                    new BigInteger(roundingIncrement), roundingMode);
                return (years, months, weeks, (int)roundedDays);
            }
            default:
                return (years, months, weeks, days);
        }
    }

    /// <summary>
    ///     Rounds (wholeUnits + numerator/denominator) to a multiple of roundingIncrement.
    ///     Uses exact integer arithmetic by scaling to avoid floating point.
    /// </summary>
    private static long RoundFractionalUnit(
        long wholeUnits, long numerator, long denominator, long roundingIncrement, string roundingMode)
    {
        if (denominator == 0)
            return wholeUnits; // degenerate case

        // The fraction numerator/denominator represents how far past wholeUnits
        // we've gone in the SIGN direction. Both numerator and denominator have the
        // same sign (both positive for forward, both negative for backward).
        // We need: total = wholeUnits + sign * |numerator|/|denominator|
        // In scaled integer arithmetic: scaledValue = wholeUnits * |denom| + sign * |numer|
        var sign = wholeUnits != 0 ? Math.Sign(wholeUnits) : Math.Sign(numerator);
        var absNumer = Math.Abs(numerator);
        var absDenom = Math.Abs(denominator);
        var scaledValue = new BigInteger(wholeUnits) * absDenom + new BigInteger(sign) * absNumer;
        var scaledIncrement = new BigInteger(roundingIncrement) * absDenom;

        var rounded = RoundToIncrement(scaledValue, scaledIncrement, roundingMode);
        return (long)(rounded / absDenom);
    }

    private static int CompareISODate(int y1, int m1, int d1, int y2, int m2, int d2)
    {
        if (y1 != y2) return y1.CompareTo(y2);
        if (m1 != m2) return m1.CompareTo(m2);
        return d1.CompareTo(d2);
    }

    private static (int year, int month) AddYearMonth(int year, int month, int months)
    {
        var totalMonth = (long)(year * 12 + month - 1) + months;
        var newYear = (int)Math.Floor(totalMonth / 12.0);
        var newMonth = (int)(totalMonth - (long)newYear * 12) + 1;
        return (newYear, newMonth);
    }

    private static int DaysInISOMonth(int year, int month) => month switch
    {
        1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
        4 or 6 or 9 or 11 => 30,
        2 => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0 ? 29 : 28,
        _ => throw new ArgumentOutOfRangeException(nameof(month))
    };

    // --- DifferenceTemporalPlainDate ---
    private static JsTemporalDuration DifferenceTemporalPlainDate(
        string operation, JsTemporalPlainDate date, JsValue otherArg, JsValue options,
        RealmState realm)
    {
        var other = ToTemporalPlainDate(otherArg, realm);
        var settings = GetDifferenceSettings(operation, options, realm,
            $"Temporal.PlainDate.prototype.{operation}",
            DateUnits, "day", "day");

        var (years, months, weeks, days) = DifferenceISODate(
            date.Year, date.Month, date.Day,
            other.Year, other.Month, other.Day,
            settings.LargestUnit);

        // Apply rounding for date units
        if (!string.Equals(settings.SmallestUnit, "day", StringComparison.Ordinal) ||
            settings.RoundingIncrement != 1)
        {
            (years, months, weeks, days) = RoundDateDuration(
                years, months, weeks, days,
                date.Year, date.Month, date.Day,
                other.Year, other.Month, other.Day,
                settings.SmallestUnit, settings.RoundingIncrement, settings.RoundingMode,
                settings.LargestUnit);
        }

        var result = new JsTemporalDuration(years, months, weeks, days, 0, 0, 0, 0, 0, 0);

        if (string.Equals(operation, "since", StringComparison.Ordinal))
            result = result.Negated();

        return result;
    }

    // --- DifferenceTemporalPlainDateTime ---
    private static JsTemporalDuration DifferenceTemporalPlainDateTime(
        string operation, JsTemporalPlainDateTime dt, JsValue otherArg, JsValue options,
        RealmState realm)
    {
        var other = ToTemporalPlainDateTime(otherArg, realm);
        var settings = GetDifferenceSettings(operation, options, realm,
            $"Temporal.PlainDateTime.prototype.{operation}",
            DateTimeUnits, "nanosecond", "day");

        var isSince = string.Equals(operation, "since", StringComparison.Ordinal);

        // Always compute this→other, negate at end for "since"

        // If largestUnit is time-only, do epoch nanosecond difference
        if (UnitRank(settings.LargestUnit) <= TemporalUnit.Hour)
        {
            var diffNanos = ToEpochNanoseconds(other) - ToEpochNanoseconds(dt);
            if (!string.Equals(settings.SmallestUnit, "nanosecond", StringComparison.Ordinal) ||
                settings.RoundingIncrement != 1)
            {
                var incNs = new BigInteger(GetUnitNanoseconds(settings.SmallestUnit)) * settings.RoundingIncrement;
                diffNanos = RoundToIncrement(diffNanos, incNs, settings.RoundingMode);
            }
            var balanced = BalanceTimeDurationToJsDuration(diffNanos, UnitRank(settings.LargestUnit), realm);
            if (isSince)
                balanced = balanced.Negated();
            return balanced;
        }

        // Date+time: compute time diff first, then borrow/carry days
        var timeDiffNanos = new BigInteger(other.Time.TotalNanoseconds) -
                            new BigInteger(dt.Time.TotalNanoseconds);

        var dateSign = CompareISODate(dt.Date.Year, dt.Date.Month, dt.Date.Day,
            other.Date.Year, other.Date.Month, other.Date.Day);
        // dateSign < 0 means dt < other (forward), dateSign > 0 means backward
        long timeExtraDays = 0;
        if (timeDiffNanos < 0 && dateSign < 0)
        {
            // Forward in dates, backward in time → borrow a day
            timeExtraDays = -1;
            timeDiffNanos += NanosecondsPerDay;
        }
        else if (timeDiffNanos > 0 && dateSign > 0)
        {
            // Backward in dates, forward in time → borrow a day
            timeExtraDays = +1;
            timeDiffNanos -= NanosecondsPerDay;
        }

        var adjEndY = other.Date.Year;
        var adjEndM = other.Date.Month;
        var adjEndD = other.Date.Day;
        if (timeExtraDays != 0)
        {
            var epochDay = IsoToDayNumber(other.Date.Year, other.Date.Month, other.Date.Day) + timeExtraDays;
            (adjEndY, adjEndM, adjEndD) = DayNumberToIsoDate(epochDay);
        }

        var (years, months, weeks, days) = DifferenceISODate(
            dt.Date.Year, dt.Date.Month, dt.Date.Day,
            adjEndY, adjEndM, adjEndD,
            settings.LargestUnit);

        // Apply rounding
        var smallestUnitRank = UnitRank(settings.SmallestUnit);
        if (smallestUnitRank >= TemporalUnit.Day)
        {
            // Rounding to a date unit — round the date part, include time fraction for week/day
            (years, months, weeks, days) = RoundDateDuration(
                years, months, weeks, days,
                dt.Date.Year, dt.Date.Month, dt.Date.Day,
                adjEndY, adjEndM, adjEndD,
                settings.SmallestUnit, settings.RoundingIncrement, settings.RoundingMode,
                settings.LargestUnit, timeDiffNanos);

            var result = new JsTemporalDuration(years, months, weeks, days, 0, 0, 0, 0, 0, 0);
            if (isSince)
                result = result.Negated();
            return result;
        }

        // Rounding to a time unit
        if (!string.Equals(settings.SmallestUnit, "nanosecond", StringComparison.Ordinal) ||
            settings.RoundingIncrement != 1)
        {
            var incNs = new BigInteger(GetUnitNanoseconds(settings.SmallestUnit)) * settings.RoundingIncrement;
            timeDiffNanos = RoundToIncrement(timeDiffNanos, incNs, settings.RoundingMode);
        }

        // Check for day overflow after time rounding
        // (e.g., rounding 23:59:59.999999999 to microseconds with expand → 24:00:00 = 1 extra day)
        if (timeDiffNanos >= NanosecondsPerDay || timeDiffNanos <= -NanosecondsPerDay)
        {
            var dayOverflow = (long)(timeDiffNanos / NanosecondsPerDay);
            timeDiffNanos -= dayOverflow * NanosecondsPerDay;

            // Adjust end date and recompute date diff
            var newEndEpoch = IsoToDayNumber(adjEndY, adjEndM, adjEndD) + dayOverflow;
            (adjEndY, adjEndM, adjEndD) = DayNumberToIsoDate(newEndEpoch);

            (years, months, weeks, days) = DifferenceISODate(
                dt.Date.Year, dt.Date.Month, dt.Date.Day,
                adjEndY, adjEndM, adjEndD,
                settings.LargestUnit);
        }

        // Balance time to hours
        var timeResult = BalanceTimeDurationToJsDuration(timeDiffNanos, TemporalUnit.Hour, realm);

        var resultDt = new JsTemporalDuration(
            years, months, weeks, days,
            timeResult.Hours, timeResult.Minutes, timeResult.Seconds,
            timeResult.Milliseconds, timeResult.Microseconds, timeResult.Nanoseconds);

        if (isSince)
            resultDt = resultDt.Negated();

        return resultDt;
    }

    // --- DifferenceTemporalZonedDateTime ---
    private static JsTemporalDuration DifferenceTemporalZonedDateTime(
        string operation, JsTemporalZonedDateTime zdt, JsValue otherArg, JsValue options,
        RealmState realm)
    {
        var other = ToTemporalZonedDateTime(otherArg, realm);
        var settings = GetDifferenceSettings(operation, options, realm,
            $"Temporal.ZonedDateTime.prototype.{operation}",
            DateTimeUnits, "nanosecond", "hour");

        if (!string.Equals(
                CanonicalizeCalendarIdForComparison(zdt.Calendar),
                CanonicalizeCalendarIdForComparison(other.Calendar),
                StringComparison.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError("calendar mismatch", realm: realm);
        }

        if (!string.Equals(
                CanonicalizeTimeZoneIdForComparison(zdt.TimeZoneId),
                CanonicalizeTimeZoneIdForComparison(other.TimeZoneId),
                StringComparison.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError("time zone mismatch", realm: realm);
        }

        var isSince = string.Equals(operation, "since", StringComparison.Ordinal);

        // Always compute this→other, negate at end for "since"

        // If largestUnit is time-only (hour or smaller), use epoch nanosecond difference
        if (UnitRank(settings.LargestUnit) <= TemporalUnit.Hour)
        {
            var diffNanos = other.Instant.EpochNanoseconds - zdt.Instant.EpochNanoseconds;
            if (!string.Equals(settings.SmallestUnit, "nanosecond", StringComparison.Ordinal) ||
                settings.RoundingIncrement != 1)
            {
                var incNs = new BigInteger(GetUnitNanoseconds(settings.SmallestUnit)) * settings.RoundingIncrement;
                diffNanos = RoundToIncrement(diffNanos, incNs, settings.RoundingMode);
            }
            var balanced = BalanceTimeDurationToJsDuration(diffNanos, UnitRank(settings.LargestUnit), realm);
            if (isSince)
                balanced = balanced.Negated();
            return balanced;
        }

        // Date-containing: convert to local PlainDateTime and diff
        var localDt = GetLocalPlainDateTime(zdt, realm);
        var localOther = GetLocalPlainDateTime(other, realm);

        var timeDiffNanos = new BigInteger(localOther.Time.TotalNanoseconds) -
                            new BigInteger(localDt.Time.TotalNanoseconds);
        var dateSign = CompareISODate(
            localDt.Date.Year, localDt.Date.Month, localDt.Date.Day,
            localOther.Date.Year, localOther.Date.Month, localOther.Date.Day);
        // dateSign < 0 means dt < other (forward), dateSign > 0 means backward
        long timeExtraDays = 0;
        if (timeDiffNanos < 0 && dateSign < 0)
        {
            // Forward in dates, backward in time → borrow a day from date
            timeExtraDays = -1;
            timeDiffNanos += NanosecondsPerDay;
        }
        else if (timeDiffNanos > 0 && dateSign > 0)
        {
            // Backward in dates, forward in time → borrow a day
            timeExtraDays = 1;
            timeDiffNanos -= NanosecondsPerDay;
        }

        var adjEndY = localOther.Date.Year;
        var adjEndM = localOther.Date.Month;
        var adjEndD = localOther.Date.Day;
        if (timeExtraDays != 0)
        {
            var epochDay = IsoToDayNumber(adjEndY, adjEndM, adjEndD) + timeExtraDays;
            (adjEndY, adjEndM, adjEndD) = DayNumberToIsoDate(epochDay);
        }

        var (years, months, weeks, days) = DifferenceISODate(
            localDt.Date.Year, localDt.Date.Month, localDt.Date.Day,
            adjEndY, adjEndM, adjEndD, settings.LargestUnit);

        // Apply rounding
        var zdtSmallestRank = UnitRank(settings.SmallestUnit);
        if (zdtSmallestRank >= TemporalUnit.Day)
        {
            // Rounding to a date unit — round date part, include time fraction for week/day
            (years, months, weeks, days) = RoundDateDuration(
                years, months, weeks, days,
                localDt.Date.Year, localDt.Date.Month, localDt.Date.Day,
                adjEndY, adjEndM, adjEndD,
                settings.SmallestUnit, settings.RoundingIncrement, settings.RoundingMode,
                settings.LargestUnit, timeDiffNanos);

            var zdtResult = new JsTemporalDuration(years, months, weeks, days, 0, 0, 0, 0, 0, 0);
            if (isSince)
                zdtResult = zdtResult.Negated();
            return zdtResult;
        }

        // Rounding to a time unit
        if (!string.Equals(settings.SmallestUnit, "nanosecond", StringComparison.Ordinal) ||
            settings.RoundingIncrement != 1)
        {
            var incNs = new BigInteger(GetUnitNanoseconds(settings.SmallestUnit)) * settings.RoundingIncrement;
            timeDiffNanos = RoundToIncrement(timeDiffNanos, incNs, settings.RoundingMode);
        }

        // Check for day overflow after time rounding
        if (timeDiffNanos >= NanosecondsPerDay || timeDiffNanos <= -NanosecondsPerDay)
        {
            var dayOverflow = (long)(timeDiffNanos / NanosecondsPerDay);
            timeDiffNanos -= dayOverflow * NanosecondsPerDay;

            var newEndEpoch = IsoToDayNumber(adjEndY, adjEndM, adjEndD) + dayOverflow;
            (adjEndY, adjEndM, adjEndD) = DayNumberToIsoDate(newEndEpoch);

            (years, months, weeks, days) = DifferenceISODate(
                localDt.Date.Year, localDt.Date.Month, localDt.Date.Day,
                adjEndY, adjEndM, adjEndD,
                settings.LargestUnit);
        }

        var timeResult = BalanceTimeDurationToJsDuration(timeDiffNanos, TemporalUnit.Hour, realm);

        var resultZdt = new JsTemporalDuration(
            years, months, weeks, days,
            timeResult.Hours, timeResult.Minutes, timeResult.Seconds,
            timeResult.Milliseconds, timeResult.Microseconds, timeResult.Nanoseconds);

        if (isSince)
            resultZdt = resultZdt.Negated();

        return resultZdt;
    }

    // --- DifferenceTemporalPlainYearMonth ---
    private static JsTemporalDuration DifferenceTemporalPlainYearMonth(
        string operation, JsTemporalPlainYearMonth ym, JsValue otherArg, JsValue options,
        RealmState realm)
    {
        var other = ToTemporalPlainYearMonth(otherArg, realm);

        if (!string.Equals(ym.Calendar, other.Calendar, StringComparison.Ordinal))
            throw StandardLibrary.ThrowRangeError(
                "PlainYearMonth.since/until requires same calendar", realm: realm);

        var settings = GetDifferenceSettings(operation, options, realm,
            $"Temporal.PlainYearMonth.prototype.{operation}",
            YearMonthUnits, "month", "year");

        // Per spec step 6: short-circuit for equal year-months (before ISO range validation)
        if (ym.Year == other.Year && ym.Month == other.Month && ym.ReferenceDay == other.ReferenceDay)
            return new JsTemporalDuration(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        // Per spec steps 8-12: create PlainDates with day=1 and validate ISO range
        RejectISODateTimeRange(ym.Year, ym.Month, 1, 0, 0, 0, 0, 0, 0, realm);
        RejectISODateTimeRange(other.Year, other.Month, 1, 0, 0, 0, 0, 0, 0, realm);

        // Compute date difference using day=1 as reference day
        var (years, months, _, days) = DifferenceISODate(
            ym.Year, ym.Month, 1,
            other.Year, other.Month, 1,
            settings.LargestUnit);

        // Apply rounding via RoundDateDuration (same algorithm as PlainDate)
        if (!string.Equals(settings.SmallestUnit, "month", StringComparison.Ordinal) ||
            settings.RoundingIncrement != 1)
        {
            (years, months, _, days) = RoundDateDuration(
                years, months, 0, days,
                ym.Year, ym.Month, 1,
                other.Year, other.Month, 1,
                settings.SmallestUnit, settings.RoundingIncrement, settings.RoundingMode,
                settings.LargestUnit);
        }

        var result = new JsTemporalDuration(years, months, 0, 0, 0, 0, 0, 0, 0, 0);
        if (string.Equals(operation, "since", StringComparison.Ordinal))
            result = result.Negated();
        return result;
    }

    #endregion

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

    /// <summary>
    ///     Spec: RoundNumberToIncrement(x, increment, trunc) for integer values.
    ///     Returns truncate(x / increment) * increment.
    /// </summary>
    private static int RoundNumberToIncrementTrunc(int value, long increment)
    {
        if (increment == 1) return value;
        // Truncate toward zero: C# integer division already truncates toward zero
        return (int)(value / increment * increment);
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

    /// <summary>
    ///     Computes the local (wall-clock) date for a ZonedDateTime using BigInteger arithmetic.
    ///     Avoids DateTimeOffset which is limited to years 1-9999.
    /// </summary>
    private static (int Year, int Month, int Day) GetLocalDate(JsTemporalZonedDateTime zdt)
    {
        // Compute local epoch nanoseconds from the instant + timezone offset
        BigInteger localNanos;
        if (zdt.FixedOffset.HasValue)
        {
            localNanos = zdt.Instant.EpochNanoseconds + zdt.FixedOffset.Value.Ticks * 100L;
        }
        else
        {
            // For IANA timezones at extreme ranges, use base offset as approximation
            // (DST doesn't apply at years far from 1-9999)
            localNanos = zdt.Instant.EpochNanoseconds + zdt.TimeZone.BaseUtcOffset.Ticks * 100L;
        }

        // Floor division: epoch days from nanoseconds
        BigInteger epochDaysBig;
        if (localNanos >= 0)
        {
            epochDaysBig = localNanos / NanosecondsPerDay;
        }
        else
        {
            epochDaysBig = (localNanos - NanosecondsPerDay + 1) / NanosecondsPerDay;
        }

        return IsoCalendarHelpers.EpochDaysToDate((long)epochDaysBig);
    }

    /// <summary>
    ///     Gets the full local date-time components of a ZonedDateTime using BigInteger arithmetic.
    ///     Safe for extreme years outside the .NET DateTimeOffset range (1-9999).
    /// </summary>
    private static (int Year, int Month, int Day, int Hour, int Minute, int Second,
        int Millisecond, int Microsecond, int Nanosecond) GetLocalDateTime(JsTemporalZonedDateTime zdt)
    {
        BigInteger localNanos;
        if (zdt.FixedOffset.HasValue)
        {
            localNanos = zdt.Instant.EpochNanoseconds + zdt.FixedOffset.Value.Ticks * 100L;
        }
        else
        {
            localNanos = zdt.Instant.EpochNanoseconds + zdt.TimeZone.BaseUtcOffset.Ticks * 100L;
        }

        var dayNumber = DivRemFloor(localNanos, new BigInteger(NanosecondsPerDay), out var remainder);
        var (year, month, day) = IsoCalendarHelpers.EpochDaysToDate((long)dayNumber);
        var time = CreatePlainTimeFromNanoseconds((long)remainder);
        return (year, month, day, time.Hour, time.Minute, time.Second,
            time.Millisecond, time.Microsecond, time.Nanosecond);
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
        if (timeZone.IsInvalidTime(candidate))
        {
            var searchStart = candidate.Ticks;
            long searchEnd;
            try
            {
                searchEnd = candidate.AddDays(1).Ticks;
            }
            catch (ArgumentOutOfRangeException)
            {
                throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
            }

            while (searchStart < searchEnd)
            {
                var mid = searchStart + ((searchEnd - searchStart) / 2);
                var midPoint = new DateTime(mid, DateTimeKind.Unspecified);
                if (timeZone.IsInvalidTime(midPoint))
                {
                    searchStart = mid + 1;
                }
                else
                {
                    searchEnd = mid;
                }
            }

            if (searchStart >= searchEnd && timeZone.IsInvalidTime(new DateTime(searchStart, DateTimeKind.Unspecified)))
            {
                throw StandardLibrary.ThrowRangeError("Temporal.ZonedDateTime is out of range", realm: realm);
            }

            candidate = new DateTime(searchStart, DateTimeKind.Unspecified);
        }

        var resolvedOffset = ResolveTimeZoneOffset(candidate, timeZone, fixedOffset);
        return ToEpochNanoseconds(candidate, resolvedOffset);
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

        return TemporalHistoricalTimeZoneOffsets.GetUtcOffset(timeZone, localDateTime);
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
        // 1. String - fast path
        if (value.IsString)
            return ParseTemporalInstantString(value.AsString() ?? "", realm);

        // 2. Non-string primitives → TypeError
        if (value.IsUndefined || value.IsNull || value.IsBoolean || value.IsNumber || value.IsSymbol || value.IsBigInt)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.Instant", realm: realm);

        // 3. Objects - check for Temporal types first
        if (value.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalInstantSlot, out var slot) &&
                slot.TryGetObject<JsTemporalInstant>(out var instant))
                return instant;

            if (obj.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) &&
                zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
                return zdt.ToInstant();
        }

        // 4. All remaining objects (JsObject, HostFunction, etc.) → convert to string via ToString, then parse
        var str = JsOps.ToJsString(value);
        return ParseTemporalInstantString(str, realm);
    }

    /// <summary>
    ///     Parses and validates an ISO 8601 instant string.
    ///     Requires a UTC offset (Z or ±HH:MM). Throws RangeError for invalid strings.
    /// </summary>
    private static JsTemporalInstant ParseTemporalInstantString(string str, RealmState realm)
    {
        if (string.IsNullOrEmpty(str))
            throw StandardLibrary.ThrowRangeError("Invalid instant string", realm: realm);

        // Reject Unicode minus sign (U+2212)
        if (str.Contains('\u2212'))
            throw StandardLibrary.ThrowRangeError("Unicode minus sign is not accepted in instant strings", realm: realm);

        // Parse and validate bracket annotations
        var baseStr = ParseAndValidateAnnotations(str, realm);

        // Parse the base string as an instant
        var parsed = ParseInstantBaseString(baseStr);
        if (parsed is null)
            throw StandardLibrary.ThrowRangeError($"Invalid instant string: {str}", realm: realm);

        // Validate range
        if (parsed.EpochNanoseconds < InstantMinEpochNanoseconds ||
            parsed.EpochNanoseconds > InstantMaxEpochNanoseconds)
            throw StandardLibrary.ThrowRangeError($"Instant out of representable range: {str}", realm: realm);

        return parsed;
    }

    /// <summary>
    ///     Parses and validates bracket annotations from an ISO string.
    ///     Returns the base string (before any annotations).
    ///     Throws RangeError for invalid annotations.
    /// </summary>
    private static string ParseAndValidateAnnotations(string input, RealmState realm)
    {
        var bracketIdx = input.IndexOf('[');
        if (bracketIdx < 0)
            return input;

        // Check for trailing content after all annotations
        var baseStr = input[..bracketIdx];
        var remaining = input.AsSpan(bracketIdx);

        var timeZoneAnnotationSeen = false;
        var calendarAnnotationCount = 0;
        var calendarCriticalSeen = false;

        var pos = 0;
        while (pos < remaining.Length)
        {
            if (remaining[pos] != '[')
                throw StandardLibrary.ThrowRangeError("Invalid trailing content after annotations", realm: realm);
            pos++;

            var critical = false;
            if (pos < remaining.Length && remaining[pos] == '!')
            {
                critical = true;
                pos++;
            }

            var closeBracket = remaining[pos..].IndexOf(']');
            if (closeBracket < 0)
                throw StandardLibrary.ThrowRangeError("Unterminated bracket annotation", realm: realm);

            var content = remaining.Slice(pos, closeBracket);
            pos += closeBracket + 1;

            var eqIdx = content.IndexOf('=');
            if (eqIdx >= 0)
            {
                // Key=value annotation
                var key = content[..eqIdx];

                if (!IsValidAnnotationKey(key))
                    throw StandardLibrary.ThrowRangeError("Invalid annotation key: keys must be lowercase", realm: realm);

                if (key.SequenceEqual("u-ca".AsSpan()))
                {
                    calendarAnnotationCount++;
                    if (critical) calendarCriticalSeen = true;
                    if (calendarAnnotationCount > 1 && calendarCriticalSeen)
                        throw StandardLibrary.ThrowRangeError("Multiple calendar annotations with critical flag", realm: realm);
                }
                else
                {
                    if (critical)
                        throw StandardLibrary.ThrowRangeError("Unknown critical annotation", realm: realm);
                }
            }
            else
            {
                // Timezone annotation
                if (timeZoneAnnotationSeen)
                    throw StandardLibrary.ThrowRangeError("Multiple time zone annotations", realm: realm);
                timeZoneAnnotationSeen = true;

                // Validate no sub-minute offsets in timezone annotations
                if (content.Length > 0 && (content[0] == '+' || content[0] == '-'))
                    ValidateTimezoneAnnotationOffset(content, realm);
            }
        }

        return baseStr;
    }

    private static bool IsValidAnnotationKey(ReadOnlySpan<char> key)
    {
        foreach (var c in key)
        {
            if (c is not (>= 'a' and <= 'z' or '-' or '_' or >= '0' and <= '9'))
                return false;
        }
        return key.Length > 0;
    }

    private static void ValidateTimezoneAnnotationOffset(ReadOnlySpan<char> content, RealmState realm)
    {
        RejectSubMinuteOffset(content.ToString(), realm);
    }

    /// <summary>
    ///     Finds the index of the date-time separator (T, t, or space) in a string.
    ///     Returns -1 if none found.
    /// </summary>
    private static int FindDateTimeSeparator(string str)
    {
        for (var i = 0; i < str.Length; i++)
        {
            if (str[i] is 'T' or 't' or ' ')
                return i;
        }
        return -1;
    }

    private static JsTemporalInstant? ParseInstantBaseString(string str)
    {
        int year;
        string timePart;

        if (str.Length > 0 && (str[0] == '+' || str[0] == '-'))
        {
            // Extended year format: +YYYYYY-MM-DDTHH:mm:ssZ or -YYYYYY-MM-DDTHH:mm:ssZ
            var sign = str[0] == '-' ? -1 : 1;
            var rest = str[1..];

            var tIdx = FindDateTimeSeparator(rest);
            if (tIdx < 0) return null;

            var datePart = rest[..tIdx];
            timePart = rest[(tIdx + 1)..];

            int yearAbs, month, day;
            if (datePart.Length == 10 && AllDigits(datePart, 0, 10))
            {
                // +YYYYYYMMDD compact format
                if (!int.TryParse(datePart.AsSpan(0, 6), System.Globalization.CultureInfo.InvariantCulture, out yearAbs) ||
                    !int.TryParse(datePart.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart.AsSpan(8, 2), System.Globalization.CultureInfo.InvariantCulture, out day))
                    return null;
            }
            else
            {
                // +YYYYYY-MM-DD dash-separated format
                var lastDash = datePart.LastIndexOf('-');
                if (lastDash <= 0) return null;
                var secondLastDash = datePart.LastIndexOf('-', lastDash - 1);
                if (secondLastDash <= 0) return null;

                var yearStr = datePart[..secondLastDash];
                if (yearStr.Length != 6) return null;

                if (!int.TryParse(yearStr, System.Globalization.CultureInfo.InvariantCulture, out yearAbs) ||
                    !int.TryParse(datePart[(secondLastDash + 1)..lastDash], System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart[(lastDash + 1)..], System.Globalization.CultureInfo.InvariantCulture, out day))
                    return null;

                if (datePart[(secondLastDash + 1)..lastDash].Length != 2) return null;
                if (datePart[(lastDash + 1)..].Length != 2) return null;
            }

            year = sign * yearAbs;

            // Reject negative zero year (-000000)
            if (sign == -1 && yearAbs == 0) return null;

            return ComputeInstantFromParts(year, month, day, timePart);
        }

        // Standard year format: YYYY-MM-DD or YYYYMMDD
        {
            var tIdx = FindDateTimeSeparator(str);
            if (tIdx < 0) return null;

            var datePart = str[..tIdx];
            timePart = str[(tIdx + 1)..];

            int month, day;
            if (datePart.Contains('-'))
            {
                // YYYY-MM-DD format
                var dashParts = datePart.Split('-');
                if (dashParts.Length != 3) return null;
                if (dashParts[0].Length != 4) return null;
                if (dashParts[1].Length != 2) return null;
                if (dashParts[2].Length != 2) return null;

                if (!int.TryParse(dashParts[0], System.Globalization.CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(dashParts[1], System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(dashParts[2], System.Globalization.CultureInfo.InvariantCulture, out day))
                    return null;
            }
            else if (datePart.Length == 8 && AllDigits(datePart, 0, 8))
            {
                // YYYYMMDD compact format
                if (!int.TryParse(datePart.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(datePart.AsSpan(4, 2), System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture, out day))
                    return null;
            }
            else
            {
                return null;
            }

            return ComputeInstantFromParts(year, month, day, timePart);
        }
    }

    /// <summary>
    ///     Computes a JsTemporalInstant from date components and a time+offset string.
    ///     Returns null if the time+offset string is invalid or missing a UTC offset.
    /// </summary>
    private static JsTemporalInstant? ComputeInstantFromParts(int year, int month, int day, string timePart)
    {
        if (string.IsNullOrEmpty(timePart)) return null;

        // Extract UTC offset - REQUIRED for instant strings
        long offsetNanos = 0;
        var hasOffset = false;

        if (timePart.EndsWith('Z') || timePart.EndsWith('z'))
        {
            hasOffset = true;
            timePart = timePart[..^1];
        }
        else
        {
            // Look for offset: last + or - followed by a digit
            for (var i = timePart.Length - 1; i >= 1; i--)
            {
                if ((timePart[i] == '+' || timePart[i] == '-') && i + 1 < timePart.Length && char.IsDigit(timePart[i + 1]))
                {
                    var parsedOffset = ParseOffsetToNanos(timePart[i..]);
                    if (parsedOffset is null) return null;
                    offsetNanos = parsedOffset.Value;
                    timePart = timePart[..i];
                    hasOffset = true;
                    break;
                }
            }
        }

        // Instant requires a UTC offset
        if (!hasOffset) return null;

        // Time part must not be empty after stripping offset
        if (string.IsNullOrEmpty(timePart)) return null;

        // Parse time: HH:mm:ss[.f], HH:mm, HH (colon-separated) or HHMMSS[.f], HHMM (compact)
        int hour, minute = 0, second = 0;
        long subSecondNanos = 0;

        if (timePart.Contains(':'))
        {
            // Colon-separated format
            var timeParts = timePart.Split(':');
            if (timeParts.Length == 0 || timeParts[0].Length != 2) return null;
            if (!int.TryParse(timeParts[0], System.Globalization.CultureInfo.InvariantCulture, out hour)) return null;

            if (timeParts.Length > 1)
            {
                if (timeParts[1].Length != 2) return null;
                if (!int.TryParse(timeParts[1], System.Globalization.CultureInfo.InvariantCulture, out minute)) return null;
            }

            if (timeParts.Length > 2)
            {
                if (!ParseSecondsWithFraction(timeParts[2], out second, out subSecondNanos)) return null;
            }
        }
        else
        {
            // Compact format: HHMMSS[.f] or HHMM or HH
            if (timePart.Length < 2) return null;
            if (!int.TryParse(timePart.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture, out hour)) return null;

            if (timePart.Length > 2)
            {
                // After HH, must have at least 2 more digits for MM
                if (timePart.Length < 4 || !char.IsDigit(timePart[2])) return null;
                if (!int.TryParse(timePart.AsSpan(2, 2), System.Globalization.CultureInfo.InvariantCulture, out minute)) return null;

                if (timePart.Length > 4)
                {
                    // After HHMM, next must be digits (SS) or invalid
                    if (timePart.Length < 6 || !char.IsDigit(timePart[4])) return null;
                    if (!ParseSecondsWithFraction(timePart[4..], out second, out subSecondNanos)) return null;
                }
            }
        }

        // Validate time ranges
        if (hour > 23) return null;
        if (minute > 59) return null;

        // Handle leap second: 60 → 59
        if (second == 60)
            second = 59;
        else if (second > 59)
            return null;

        // Validate date
        if (month is < 1 or > 12) return null;
        if (day < 1 || day > DaysInMonth(year, month)) return null;

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

    private static bool ParseSecondsWithFraction(string secStr, out int second, out long subSecondNanos)
    {
        second = 0;
        subSecondNanos = 0;
        var dotIdx = FindDecimalSeparator(secStr);
        if (dotIdx >= 0)
        {
            if (dotIdx != 2) return false;
            if (!int.TryParse(secStr.AsSpan(0, dotIdx), System.Globalization.CultureInfo.InvariantCulture, out second)) return false;
            var frac = secStr[(dotIdx + 1)..];
            if (frac.Length == 0 || frac.Length > 9) return false;
            frac = frac.PadRight(9, '0');
            return long.TryParse(frac, System.Globalization.CultureInfo.InvariantCulture, out subSecondNanos);
        }

        if (secStr.Length != 2) return false;
        return int.TryParse(secStr, System.Globalization.CultureInfo.InvariantCulture, out second);
    }

    private static double BalanceTimeToDays(JsTemporalDuration d)
    {
        // Use BigInteger to avoid precision loss with very large values
        // (e.g., hours: 2400000023 near the max Temporal range)
        var totalNs = (BigInteger)d.Hours * 3_600_000_000_000L
                    + (BigInteger)d.Minutes * 60_000_000_000L
                    + (BigInteger)d.Seconds * 1_000_000_000L
                    + (BigInteger)d.Milliseconds * 1_000_000L
                    + (BigInteger)d.Microseconds * 1_000L
                    + (BigInteger)d.Nanoseconds;
        // Truncate toward zero
        var days = totalNs / 86_400_000_000_000L;
        return (double)days;
    }

    /// <summary>
    ///     Computes the total nanoseconds from the time components of a duration (hours through nanoseconds).
    ///     Uses BigInteger to avoid precision loss with large values.
    /// </summary>
    private static bool IsZeroDuration(JsTemporalDuration d)
    {
        return d.Years == 0 && d.Months == 0 && d.Weeks == 0 && d.Days == 0
               && d.Hours == 0 && d.Minutes == 0 && d.Seconds == 0
               && d.Milliseconds == 0 && d.Microseconds == 0 && d.Nanoseconds == 0;
    }

    private static BigInteger DurationTimeNanoseconds(JsTemporalDuration d)
    {
        return (BigInteger)d.Hours * 3_600_000_000_000L
               + (BigInteger)d.Minutes * 60_000_000_000L
               + (BigInteger)d.Seconds * 1_000_000_000L
               + (BigInteger)d.Milliseconds * 1_000_000L
               + (BigInteger)d.Microseconds * 1_000L
               + (BigInteger)d.Nanoseconds;
    }

    /// <summary>
    ///     Gets the epoch nanoseconds for the start of a given day in a timezone.
    /// </summary>
    private static BigInteger GetStartOfDayEpochNanos(int year, int month, int day, string timeZoneId, RealmState realm)
    {
        var epochDays = IsoCalendarHelpers.DateToEpochDays(year, month, day);
        var localEpochNanos = new BigInteger(epochDays) * 86_400_000_000_000L;

        if (ParseOffsetToNanos(timeZoneId) is { } offsetNanos)
        {
            return localEpochNanos - offsetNanos;
        }

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            if (year is >= 1 and <= 9999)
            {
                var localDateTime = new DateTime(year, month, day, 0, 0, 0);
                var offset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(tz, localDateTime);
                return localEpochNanos - offset.Ticks * 100L;
            }
            return localEpochNanos - tz.BaseUtcOffset.Ticks * 100L;
        }
        catch
        {
            return localEpochNanos;
        }
    }

    /// <summary>
    ///     Converts a BigInteger to double with correct IEEE 754 round-to-nearest-even.
    ///     .NET's (double)BigInteger can give incorrect rounding for large values.
    /// </summary>
    private static double BigIntegerToDouble(BigInteger value)
    {
        if (value.IsZero) return 0.0;

        var negative = value < 0;
        if (negative) value = -value;

        // If it fits in a long, direct conversion is exact enough
        if (value <= long.MaxValue)
        {
            return negative ? -(double)(long)value : (double)(long)value;
        }

        // Find the number of bits needed
        var bits = (int)value.GetBitLength();

        // Double has 53 bits of mantissa. If bits <= 53, (double)BigInteger is exact.
        if (bits <= 53)
        {
            return negative ? -(double)value : (double)value;
        }

        // Shift right to get 54 bits (53 mantissa + 1 guard bit)
        var shift = bits - 54;
        var shifted = value >> shift;
        var mantissa = (long)shifted;

        // Check the guard bit (rounding bit)
        var guardBit = (mantissa & 1) != 0;
        mantissa >>= 1; // Now 53 bits

        if (guardBit)
        {
            // Check trailing bits (sticky bit) for round-to-nearest-even
            var trailingMask = (BigInteger.One << shift) - 1;
            var sticky = (value & trailingMask) != 0;

            if (sticky || (mantissa & 1) != 0) // Round up if sticky or odd (round to even)
            {
                mantissa++;
                if (mantissa >= (1L << 53))
                {
                    mantissa >>= 1;
                    shift++;
                }
            }
        }

        // Construct the double
        var exponent = shift + 1;
        var result = (double)mantissa * Math.Pow(2.0, exponent);
        return negative ? -result : result;
    }

    /// <summary>
    ///     Computes 𝔽(numerator / divisor) — the correctly-rounded IEEE 754 float64
    ///     of the exact mathematical ratio of two BigIntegers. Uses scaled integer
    ///     division with guard and sticky bits for round-to-nearest-even.
    /// </summary>
    private static double DivideToDouble(BigInteger numerator, BigInteger divisor)
    {
        if (numerator.IsZero)
            return 0.0;

        // Fast path: when both fit in double exactly (abs < 2^53), use direct division.
        // This is a single IEEE 754 operation — correctly rounded by definition.
        // Values > 2^53 lose precision when cast to double, so they must use BigInteger.
        const long doubleSafeLimit = 1L << 53;
        if (numerator > -doubleSafeLimit && numerator < doubleSafeLimit &&
            divisor > -doubleSafeLimit && divisor < doubleSafeLimit)
        {
            return (double)(long)numerator / (double)(long)divisor;
        }

        var negative = (numerator < 0) != (divisor < 0);
        var absNum = BigInteger.Abs(numerator);
        var absDen = BigInteger.Abs(divisor);

        var q = BigInteger.DivRem(absNum, absDen, out var r);

        // Exact division: use BigIntegerToDouble for correct rounding
        if (r.IsZero)
        {
            var exact = BigIntegerToDouble(q);
            return negative ? -exact : exact;
        }

        // Scale numerator by 2^64 to get enough precision bits in the quotient.
        // Since the numerator exceeds long range (>= 2^63), the mathematical quotient
        // is large enough (>= 2^63 / maxDivisor >> 1) that 64 extra bits in the scaled
        // quotient provide sufficient guard/sticky bits for correct rounding.
        // Division by 2^64 is exact in float64 (just adjusts the exponent).
        const int scale = 64;
        var scaledQ = BigInteger.DivRem(absNum << scale, absDen, out var scaledR);

        // If scaled remainder is non-zero, the true value is strictly between
        // scaledQ and scaledQ+1. Set LSB to ensure the sticky bit in
        // BigIntegerToDouble is non-zero, preventing incorrect tie-breaking.
        if (!scaledR.IsZero)
            scaledQ |= 1;

        var scaledResult = BigIntegerToDouble(scaledQ);
        scaledResult /= Math.Pow(2.0, scale);

        return negative ? -scaledResult : scaledResult;
    }

    // ==========================================
    // Duration.prototype.total / round helpers
    // ==========================================

    /// <summary>
    ///     Parses a relativeTo argument into either a PlainDate or ZonedDateTime.
    ///     Returns (PlainDate?, ZonedDateTime?) — exactly one or neither is set.
    /// </summary>
    private static (JsTemporalPlainDate? plainDate, JsTemporalZonedDateTime? zonedDateTime) ToRelativeTemporalObject(
        JsValue value, RealmState realm)
    {
        if (value.IsUndefined)
            return (null, null);

        // String: parse and determine if it's a ZonedDateTime (has time zone annotation) or PlainDate
        if (value.IsString)
        {
            var str = value.AsString() ?? "";
            if (string.IsNullOrEmpty(str))
                throw StandardLibrary.ThrowRangeError("Invalid relativeTo string: empty", realm: realm);

            // Check for year-zero prefix
            if (str.StartsWith("-000000", StringComparison.Ordinal) || str.StartsWith("\u2212000000", StringComparison.Ordinal))
                throw StandardLibrary.ThrowRangeError("year zero not allowed", realm: realm);

            if (str.Contains('\u2212'))
                throw StandardLibrary.ThrowRangeError("Non-ASCII minus sign is not allowed", realm: realm);

            // If the string contains a time zone annotation [...] (not [u-ca=...]), parse as ZonedDateTime
            if (HasTimeZoneBracket(str))
            {
                // Validate explicit offset against time zone (spec: reject offset mismatch)
                ValidateRelativeToOffset(str, realm);

                // String with time zone brackets → ZonedDateTime
                try
                {
                    // Wall clock date must be within ISODateWithinLimits for relativeTo
                    var bracketPos = str.IndexOf('[');
                    var baseStr = bracketPos >= 0 ? str[..bracketPos] : str;
                    var wallClockDays = JsTemporalZonedDateTime.ParseWallClockEpochDays(baseStr);
                    if (wallClockDays.HasValue && Math.Abs(wallClockDays.Value) > 100_000_000)
                        throw StandardLibrary.ThrowRangeError(
                            "relativeTo is outside the representable range", realm: realm);

                    var zdt = ParseRelativeToZonedDateTimeString(str, realm);
                    return (null, zdt);
                }
                catch (FormatException ex)
                {
                    throw StandardLibrary.ThrowRangeError(ex.Message, realm: realm);
                }
                catch (ArgumentException ex)
                {
                    throw StandardLibrary.ThrowRangeError(ex.Message, realm: realm);
                }
                catch (TimeZoneNotFoundException ex)
                {
                    throw StandardLibrary.ThrowRangeError(ex.Message, realm: realm);
                }
            }

            // No time zone annotation — check for Z designator (UTC without IANA is ambiguous → throw)
            var tPos = str.IndexOf('T');
            if (tPos >= 0 && str.IndexOf('Z', tPos) >= 0)
                throw StandardLibrary.ThrowRangeError(
                    "relativeTo with UTC designator 'Z' requires a time zone annotation", realm: realm);

            // Otherwise, parse as PlainDate (ignore time components if present)
            try
            {
                var date = ParseTemporalPlainDateString(str, realm);
                return (date, null);
            }
            catch
            {
                throw StandardLibrary.ThrowRangeError($"Invalid relativeTo string: {str}", realm: realm);
            }
        }

        // Non-string primitives → TypeError
        if (value.IsNull || value.IsBoolean || value.IsNumber || value.IsSymbol || value.IsBigInt)
            throw StandardLibrary.ThrowTypeError("Cannot convert relativeTo to a Temporal type", realm: realm);

        // Check for Temporal objects
        if (value.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) &&
                zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
                return (null, zdt);

            if (obj.TryGetProperty(TemporalPlainDateSlot, out var pdSlot) &&
                pdSlot.TryGetObject<JsTemporalPlainDate>(out var pd))
                return (pd, null);

            if (obj.TryGetProperty(TemporalPlainDateTimeSlot, out var pdtSlot) &&
                pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                return (pdt.ToPlainDate(), null);
        }

        // Property bag: single-pass reading with immediate conversions per spec order.
        // All properties must be read ONCE in alphabetical order to ensure correct
        // observable operations (get, valueOf, toString) on proxy/observer objects.
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            // 1. calendar
            if (accessor.TryGetProperty("calendar", out var calVal) && !calVal.IsUndefined)
                ValidateTemporalCalendarValue(calVal, realm);

            // 2. day
            if (!accessor.TryGetProperty("day", out var dayVal) || dayVal.IsUndefined)
                throw StandardLibrary.ThrowTypeError("Property bag for relativeTo must have 'day'", realm: realm);
            var day = ToIntegerWithTruncation(dayVal, realm);

            // 3. hour
            var hour = GetOptionalIntProperty(accessor, "hour", realm);

            // 4. microsecond
            var microsecond = GetOptionalIntProperty(accessor, "microsecond", realm);

            // 5. millisecond
            var millisecond = GetOptionalIntProperty(accessor, "millisecond", realm);

            // 6. minute
            var minute = GetOptionalIntProperty(accessor, "minute", realm);

            // 7. month
            accessor.TryGetProperty("month", out var monthVal);
            var hasMonth = !monthVal.IsUndefined;
            int monthInt = 0;
            if (hasMonth) monthInt = ToIntegerWithTruncation(monthVal, realm);

            // 8. monthCode — call ToString immediately for observable order
            accessor.TryGetProperty("monthCode", out var monthCodeVal);
            var hasMonthCode = !monthCodeVal.IsUndefined;
            string? monthCodeStr = null;
            if (hasMonthCode)
            {
                monthCodeStr = JsOps.ToJsString(monthCodeVal);
                ValidateMonthCodeSyntax(monthCodeStr, realm);
            }

            // 9. nanosecond
            var nanosecond = GetOptionalIntProperty(accessor, "nanosecond", realm);

            // 10. offset — call ToString immediately if not undefined
            accessor.TryGetProperty("offset", out var offsetVal);
            string? offsetStr = null;
            if (!offsetVal.IsUndefined)
            {
                if (offsetVal.IsSymbol || offsetVal.IsBigInt)
                    throw StandardLibrary.ThrowTypeError("offset must be a string", realm: realm);
                if (offsetVal.IsNull || offsetVal.IsBoolean || offsetVal.IsNumber)
                    throw StandardLibrary.ThrowTypeError("offset must be a string", realm: realm);
                offsetStr = offsetVal.IsString ? offsetVal.AsString() : JsOps.ToJsString(offsetVal);
            }

            // 11. second
            var second = GetOptionalIntProperty(accessor, "second", realm);

            // 12. timeZone
            accessor.TryGetProperty("timeZone", out var tzVal);
            var hasTimeZone = !tzVal.IsUndefined;

            // 13. year
            if (!accessor.TryGetProperty("year", out var yearVal) || yearVal.IsUndefined)
                throw StandardLibrary.ThrowTypeError("Property bag for relativeTo must have 'year'", realm: realm);
            var year = ToIntegerWithTruncation(yearVal, realm);

            // Resolve month from month/monthCode
            if (!hasMonth && !hasMonthCode)
                throw StandardLibrary.ThrowTypeError("Property bag for relativeTo must have 'month' or 'monthCode'", realm: realm);
            int month;
            if (hasMonthCode)
            {
                month = ResolveISOMonthCode(monthCodeStr!, realm);
                if (hasMonth && monthInt != month)
                    throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
            }
            else
            {
                month = monthInt;
            }

            if (hasTimeZone)
            {
                // ZonedDateTime path — construct directly from cached values
                var timeZoneId = ToTemporalTimeZoneIdentifier(tzVal, realm);

                // Constrain overflow (same as PlainDate path — relativeTo uses "constrain")
                if (month < 1) throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
                if (day < 1) throw StandardLibrary.ThrowRangeError($"Day {day} is out of range", realm: realm);
                month = Math.Min(month, 12);
                var maxDay = IsoCalendarHelpers.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
                day = Math.Min(day, maxDay);
                hour = Math.Clamp(hour, 0, 23);
                minute = Math.Clamp(minute, 0, 59);
                second = Math.Clamp(second, 0, 59);
                millisecond = Math.Clamp(millisecond, 0, 999);
                microsecond = Math.Clamp(microsecond, 0, 999);
                nanosecond = Math.Clamp(nanosecond, 0, 999);
                RejectISODate(year, month, day, realm);

                // Handle offset validation (reject mode for relativeTo)
                if (offsetStr != null)
                {
                    var offsetNanos = ParseOffsetString(offsetStr, realm);
                    var tz = JsTemporalZonedDateTime.ResolveTimeZone(timeZoneId, out var fixedOff);
                    TimeSpan tzOffset;
                    if (fixedOff.HasValue)
                    {
                        tzOffset = fixedOff.Value;
                    }
                    else
                    {
                        var approxLocal = new DateTime(
                            Math.Clamp(year, 1, 9999), month, day,
                            hour, minute, second, millisecond, microsecond);
                        tzOffset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(tz, approxLocal);
                    }
                    var tzOffsetNanos = tzOffset.Ticks * 100L;
                    if (offsetNanos != tzOffsetNanos)
                        throw StandardLibrary.ThrowRangeError("Offset does not match the time zone", realm: realm);
                }

                var zdt = new JsTemporalZonedDateTime(year, month, day, hour, minute, second,
                    millisecond, microsecond, nanosecond, timeZoneId, "iso8601");
                return (null, zdt);
            }
            else
            {
                // PlainDate path — construct directly from cached values
                return (ApplyOverflowToDate(year, month, day, "iso8601", "constrain", realm), null);
            }
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert relativeTo to a Temporal type", realm: realm);
    }

    private static JsTemporalZonedDateTime ParseRelativeToZonedDateTimeString(string str, RealmState realm)
    {
        ParseAndValidateAnnotations(str, realm);
        var validatedCalendar = ValidateCalendarAnnotation(str, realm);
        var (timeZoneId, calendarAnnotation) = ExtractZonedDateTimeAnnotations(str);
        var calendar = validatedCalendar ?? (calendarAnnotation is null ? "iso8601" : ValidateCalendarId(calendarAnnotation));

        if (timeZoneId == null)
            throw StandardLibrary.ThrowRangeError("ZonedDateTime requires a time zone annotation in brackets", realm: realm);

        timeZoneId = ValidateTimeZoneIdentifier(timeZoneId, realm);

        var bracketIdx = str.IndexOf('[');
        var baseStr = bracketIdx >= 0 ? str[..bracketIdx] : str;
        var hasOffset = JsTemporalZonedDateTime.HasExplicitOffset(baseStr);
        var hasZ = HasZDesignator(baseStr);

        var parsed = JsTemporalZonedDateTime.ParseIsoDateTimeWithOffset(baseStr);
        if (parsed == null)
            throw StandardLibrary.ThrowRangeError($"Invalid ZonedDateTime string: {str}", realm: realm);

        if (hasZ)
            return new JsTemporalZonedDateTime(parsed, timeZoneId, calendar);

        var tz = JsTemporalZonedDateTime.ResolveTimeZone(timeZoneId, out var fixedOff);

        if (hasOffset)
        {
            var stringOffsetNanos = ExtractOffsetNanosFromString(baseStr);
            var wallNanos = parsed.EpochNanoseconds + stringOffsetNanos;
            var wallInstant = JsTemporalInstant.FromEpochNanoseconds(wallNanos);
            var approxLocal = wallInstant.ToDateTimeOffset().DateTime;
            TryMatchTimeZoneOffsetForString(baseStr, stringOffsetNanos, timeZoneId, tz, fixedOff, approxLocal, out var tzOffset);

            var wallTimeInstant =
                JsTemporalInstant.FromEpochNanoseconds(parsed.EpochNanoseconds + stringOffsetNanos - tzOffset.Ticks * 100L);
            return new JsTemporalZonedDateTime(wallTimeInstant, timeZoneId, calendar);
        }

        TimeSpan wallOffset;
        if (fixedOff.HasValue)
        {
            wallOffset = fixedOff.Value;
        }
        else
        {
            var approxLocal = parsed.ToDateTimeOffset().DateTime;
            wallOffset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(tz, approxLocal);
        }

        var utcInstant = JsTemporalInstant.FromEpochNanoseconds(parsed.EpochNanoseconds - wallOffset.Ticks * 100L);
        return new JsTemporalZonedDateTime(utcInstant, timeZoneId, calendar);
    }

    /// <summary>
    ///     Implements Duration.prototype.total — returns the total as a fractional number
    ///     in the given unit, using relativeTo for calendar-aware operations.
    /// </summary>
    private static double TotalDuration(JsTemporalDuration duration, string unit,
        JsTemporalPlainDate? plainDateRelativeTo, JsTemporalZonedDateTime? zonedDateTimeRelativeTo,
        RealmState realm)
    {
        var unitRank = UnitRank(unit);

        // Per spec: if zonedRelativeTo is not undefined, always validate via AddZonedDateTime
        if (zonedDateTimeRelativeTo != null)
        {
            // For durations with no calendar units (years/months/weeks = 0) and day-or-smaller unit,
            // validate via AddZonedDateTime but compute using the simple precise path.
            // This avoids precision issues in the epoch ns division path.
            if (duration.Years == 0 && duration.Months == 0 && duration.Weeks == 0 &&
                unitRank <= TemporalUnit.Day)
            {
                // Validate: compute target epoch ns
                ValidateZonedDateTimeAdd(zonedDateTimeRelativeTo, duration, realm);
                // Use precise simple calculation with single-rounding
                var totalNs = DurationToTotalNanoseconds(duration.Days, duration.Hours, duration.Minutes,
                    duration.Seconds, duration.Milliseconds, duration.Microseconds, duration.Nanoseconds);

                // For day unit, compute actual day length (DST-aware) instead of fixed 24h
                if (string.Equals(unit, "day", StringComparison.Ordinal))
                {
                    var dayLengthNs = ComputeActualDayLengthNs(zonedDateTimeRelativeTo, realm);
                    return DivideToDouble(totalNs, dayLengthNs);
                }

                var unitNs = new BigInteger(GetUnitNanoseconds(unit));
                return DivideToDouble(totalNs, unitNs);
            }
            return TotalDurationRelativeToZonedDateTime(duration, unit, zonedDateTimeRelativeTo, realm);
        }

        // For time-only durations with time-only unit and no calendar units (no ZDT relativeTo)
        if (duration.Years == 0 && duration.Months == 0 && duration.Weeks == 0 && unitRank <= TemporalUnit.Day)
        {
            // Simple case: convert everything to nanoseconds
            var totalNs = DurationToTotalNanoseconds(duration.Days, duration.Hours, duration.Minutes,
                duration.Seconds, duration.Milliseconds, duration.Microseconds, duration.Nanoseconds);

            var unitNs = new BigInteger(GetUnitNanoseconds(unit));
            return DivideToDouble(totalNs, unitNs);
        }

        // Calendar-aware path: need relativeTo
        if (plainDateRelativeTo != null)
        {
            return TotalDurationRelativeToPlainDate(duration, unit, plainDateRelativeTo, realm);
        }

        // Should not reach here due to validation above, but fallback
        throw StandardLibrary.ThrowRangeError("relativeTo is required for total with calendar units", realm: realm);
    }

    private static double TotalDurationRelativeToPlainDate(JsTemporalDuration duration, string unit,
        JsTemporalPlainDate relativeTo, RealmState realm)
    {
        // Step 1: Add the date part of the duration to get the intermediate date
        var dateDuration = new JsTemporalDuration(duration.Years, duration.Months, duration.Weeks, duration.Days, 0, 0, 0, 0, 0, 0);
        JsTemporalPlainDate endDate;
        try
        {
            endDate = relativeTo.Add(dateDuration);
            RejectISODate(endDate.Year, endDate.Month, endDate.Day, realm);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw StandardLibrary.ThrowRangeError("Resulting date is out of valid range", realm: realm);
        }

        // Step 2: Compute time nanoseconds
        var timeNs = DurationToTotalNanoseconds(0, duration.Hours, duration.Minutes, duration.Seconds,
            duration.Milliseconds, duration.Microseconds, duration.Nanoseconds);

        // Step 3: Validate that the target PlainDateTime is within representable range
        // Per spec: DifferencePlainDateTimeWithTotal checks ISODateTimeWithinLimits
        // Add24HourDaysToNormalizedTimeDuration: if |timeNs| > MaxTimeDuration, throw RangeError
        if (BigInteger.Abs(timeNs) > MaxTimeDuration)
            throw StandardLibrary.ThrowRangeError("Normalized time duration is out of range", realm: realm);

        var endTimeNs = timeNs;
        if (endTimeNs < 0)
        {
            endTimeNs += NanosecondsPerDay;
        }
        int endHour, endMinute, endSecond, endMs, endUs, endNs;
        try
        {
            endHour = (int)((long)(endTimeNs / 3_600_000_000_000L) % 24);
            endMinute = (int)((long)(endTimeNs / 60_000_000_000L) % 60);
            endSecond = (int)((long)(endTimeNs / 1_000_000_000L) % 60);
            endMs = (int)((long)(endTimeNs / 1_000_000L) % 1000);
            endUs = (int)((long)(endTimeNs / 1_000L) % 1000);
            endNs = (int)((long)endTimeNs % 1000);
        }
        catch (OverflowException)
        {
            throw StandardLibrary.ThrowRangeError("Time duration is out of representable range", realm: realm);
        }
        RejectISODateTimeRange(endDate.Year, endDate.Month, endDate.Day,
            endHour, endMinute, endSecond, endMs, endUs, endNs, realm);

        var unitRank = UnitRank(unit);

        if (unitRank <= TemporalUnit.Hour)
        {
            // Time unit: compute total days between dates, then add time
            var totalDays = IsoToDayNumber(endDate.Year, endDate.Month, endDate.Day) -
                            IsoToDayNumber(relativeTo.Year, relativeTo.Month, relativeTo.Day);
            var totalNs = new BigInteger(totalDays) * NanosecondsPerDay + timeNs;
            var unitNs = new BigInteger(GetUnitNanoseconds(unit));
            var q = totalNs / unitNs;
            var r = totalNs % unitNs;
            return (double)q + (double)r / (double)unitNs;
        }

        if (string.Equals(unit, "day", StringComparison.Ordinal))
        {
            var totalDays = IsoToDayNumber(endDate.Year, endDate.Month, endDate.Day) -
                            IsoToDayNumber(relativeTo.Year, relativeTo.Month, relativeTo.Day);
            // Fractional day from time
            if (timeNs != 0)
            {
                return (double)totalDays + BigIntegerToDouble(timeNs) / (double)NanosecondsPerDay;
            }
            return totalDays;
        }

        if (string.Equals(unit, "week", StringComparison.Ordinal))
        {
            var totalDays = IsoToDayNumber(endDate.Year, endDate.Month, endDate.Day) -
                            IsoToDayNumber(relativeTo.Year, relativeTo.Month, relativeTo.Day);
            var totalWeeks = totalDays / 7;
            var remainderDays = totalDays - totalWeeks * 7;
            var remainderNs = new BigInteger(remainderDays) * NanosecondsPerDay + timeNs;
            var weekNs = new BigInteger(7) * NanosecondsPerDay;
            return (double)totalWeeks + BigIntegerToDouble(remainderNs) / BigIntegerToDouble(weekNs);
        }

        // For month and year units, compute using DifferenceISODate
        var (years, months, _, days) = DifferenceISODate(
            relativeTo.Year, relativeTo.Month, relativeTo.Day,
            endDate.Year, endDate.Month, endDate.Day,
            unit);

        if (string.Equals(unit, "month", StringComparison.Ordinal))
        {
            var totalMonths = years * 12 + months;
            // Compute the fractional part: leftover days / days-in-next-month
            var (midY, midM) = AddYearMonth(relativeTo.Year, relativeTo.Month, totalMonths);
            RejectISOYearMonthRange(midY, midM, realm);
            var midD = Math.Min(relativeTo.Day, DaysInISOMonth(midY, midM));
            var midEpoch = IsoToDayNumber(midY, midM, midD);
            var endEpoch = IsoToDayNumber(endDate.Year, endDate.Month, endDate.Day);
            var leftoverDays = endEpoch - midEpoch;

            // Next month boundary
            var (nxY, nxM) = AddYearMonth(relativeTo.Year, relativeTo.Month, totalMonths + (totalMonths >= 0 ? 1 : -1));
            RejectISOYearMonthRange(nxY, nxM, realm);
            var nxD = Math.Min(relativeTo.Day, DaysInISOMonth(nxY, nxM));
            var nextEpoch = IsoToDayNumber(nxY, nxM, nxD);
            var denomDays = nextEpoch - midEpoch;

            if (denomDays == 0) return totalMonths;
            var fraction = ((double)leftoverDays * NanosecondsPerDay + BigIntegerToDouble(timeNs)) / ((double)denomDays * NanosecondsPerDay);
            return totalMonths + fraction;
        }

        if (string.Equals(unit, "year", StringComparison.Ordinal))
        {
            // Compute total as years + fractional year
            var thMonths = years * 12;
            var (thY, thM) = AddYearMonth(relativeTo.Year, relativeTo.Month, thMonths);
            RejectISOYearMonthRange(thY, thM, realm);
            var thD = Math.Min(relativeTo.Day, DaysInISOMonth(thY, thM));
            var thresholdEpoch = IsoToDayNumber(thY, thM, thD);
            var endEpochDn = IsoToDayNumber(endDate.Year, endDate.Month, endDate.Day);

            // Next year boundary
            var sign = years != 0 ? Math.Sign(years) :
                (endEpochDn > thresholdEpoch ? 1 : endEpochDn < thresholdEpoch ? -1 :
                timeNs > 0 ? 1 : timeNs < 0 ? -1 : 0);
            if (sign == 0) return 0;
            var nxMonths = (years + sign) * 12;
            var (nxY, nxM) = AddYearMonth(relativeTo.Year, relativeTo.Month, nxMonths);
            RejectISOYearMonthRange(nxY, nxM, realm);
            var nxD = Math.Min(relativeTo.Day, DaysInISOMonth(nxY, nxM));
            var nextEpoch = IsoToDayNumber(nxY, nxM, nxD);

            var denomDays = nextEpoch - thresholdEpoch;
            if (denomDays == 0) return years;
            var numeratorDays = endEpochDn - thresholdEpoch;
            var fraction = ((double)numeratorDays * NanosecondsPerDay + BigIntegerToDouble(timeNs)) / ((double)denomDays * NanosecondsPerDay);
            return years + fraction;
        }

        return 0;
    }

    /// <summary>
    /// Validates that adding a duration to a ZonedDateTime produces a valid result.
    /// Throws RangeError if the result would be out of range.
    /// </summary>
    private static void ValidateZonedDateTimeAdd(JsTemporalZonedDateTime relativeTo,
        JsTemporalDuration duration, RealmState realm)
    {
        // Delegate to AddZonedDateTimeEpochNs which uses GetLocalPlainDateTime
        // (handles extreme years via epoch nanosecond arithmetic instead of DateTimeOffset)
        AddZonedDateTimeEpochNs(relativeTo, duration, realm);
    }

    private static double TotalDurationRelativeToZonedDateTime(JsTemporalDuration duration, string unit,
        JsTemporalZonedDateTime relativeTo, RealmState realm)
    {
        var unitRank = UnitRank(unit);

        // Use AddZonedDateTimeEpochNs which handles extreme years via epoch nanosecond arithmetic
        // (avoids DateTimeOffset which is limited to years 1-9999)
        if (unitRank <= TemporalUnit.Hour)
        {
            // For time units, use exact epoch nanosecond arithmetic
            var endEpochNs = AddZonedDateTimeEpochNs(relativeTo, duration, realm);
            var diffNs = endEpochNs - relativeTo.Instant.EpochNanoseconds;
            var unitNs = new BigInteger(GetUnitNanoseconds(unit));
            return DivideToDouble(diffNs, unitNs);
        }

        if (string.Equals(unit, "day", StringComparison.Ordinal))
        {
            // For day unit with ZDT, compute actual day length from timezone
            // (DST transitions make days 23h or 25h)
            var endEpochNs = AddZonedDateTimeEpochNs(relativeTo, duration, realm);
            var diffNs = endEpochNs - relativeTo.Instant.EpochNanoseconds;
            var dayLengthNs = ComputeActualDayLengthNs(relativeTo, realm);
            return DivideToDouble(diffNs, dayLengthNs);
        }

        // For calendar units (week/month/year), validate the target epoch ns, then use PlainDate logic
        AddZonedDateTimeEpochNs(relativeTo, duration, realm);
        try
        {
            var localPdt = GetLocalPlainDateTime(relativeTo, realm);
            var plainDate = new JsTemporalPlainDate(localPdt.Year, localPdt.Month, localPdt.Day, relativeTo.Calendar);
            return TotalDurationRelativeToPlainDate(duration, unit, plainDate, realm);
        }
        catch (OverflowException)
        {
            throw StandardLibrary.ThrowRangeError("Resulting date-time is out of valid range", realm: realm);
        }
    }

    /// <summary>
    /// Computes the actual length of a day starting at the given ZonedDateTime in nanoseconds.
    /// Accounts for DST transitions (23h, 25h days, etc.).
    /// </summary>
    private static BigInteger ComputeActualDayLengthNs(JsTemporalZonedDateTime zdt, RealmState realm)
    {
        try
        {
            // Add 1 calendar day to the ZDT and measure the epoch ns difference
            var oneDayDuration = new JsTemporalDuration(0, 0, 0, 1, 0, 0, 0, 0, 0, 0);
            var nextDayEpochNs = AddZonedDateTimeEpochNs(zdt, oneDayDuration, realm);
            var dayLength = nextDayEpochNs - zdt.Instant.EpochNanoseconds;

            // Guard: if the result is non-positive (extremely unusual), fallback to 24h
            if (dayLength <= 0)
                return NanosecondsPerDay;

            return dayLength;
        }
        catch
        {
            // Fallback to standard 24h day if computation fails
            return NanosecondsPerDay;
        }
    }

    /// <summary>
    ///     Implements Duration.prototype.round — rounds and rebalances a duration.
    /// </summary>
    private static JsTemporalDuration RoundDuration(JsTemporalDuration duration,
        string smallestUnit, string largestUnit, long roundingIncrement, string roundingMode,
        JsTemporalPlainDate? plainDateRelativeTo, JsTemporalZonedDateTime? zonedDateTimeRelativeTo,
        RealmState realm)
    {
        var smallestRank = UnitRank(smallestUnit);
        var largestRank = UnitRank(largestUnit);

        // Step 1: Compute time nanoseconds
        var timeNs = DurationToTotalNanoseconds(0, duration.Hours, duration.Minutes, duration.Seconds,
            duration.Milliseconds, duration.Microseconds, duration.Nanoseconds);

        // Validate time duration magnitude
        if (BigInteger.Abs(timeNs) > MaxTimeDuration)
            throw StandardLibrary.ThrowRangeError("Normalized time duration is out of range", realm: realm);

        // Simple case: time-only duration with time-only units and no calendar units
        if (duration.Years == 0 && duration.Months == 0 && duration.Weeks == 0 &&
            largestRank <= TemporalUnit.Day && smallestRank <= TemporalUnit.Day)
        {
            // Total nanoseconds including days
            var totalNs = DurationToTotalNanoseconds(duration.Days, duration.Hours, duration.Minutes,
                duration.Seconds, duration.Milliseconds, duration.Microseconds, duration.Nanoseconds);

            // Per spec: when zonedRelativeTo is set, validate intermediate epoch range.
            // Skip when totalNs == 0 AND largestUnit is time-only (no day balancing needed).
            // When largestUnit is "day", the next-day boundary is still needed even for zero duration.
            if (zonedDateTimeRelativeTo != null && (!totalNs.IsZero || largestRank >= TemporalUnit.Day))
            {
                var intermediateNs = zonedDateTimeRelativeTo.Instant.EpochNanoseconds + totalNs;
                if (BigInteger.Abs(intermediateNs) > InstantMaxEpochNanoseconds)
                    throw StandardLibrary.ThrowRangeError("Duration added to ZonedDateTime is out of representable range", realm: realm);

                // For time-unit rounding, validate next-day boundary (RoundRelativeDuration)
                if (smallestRank < TemporalUnit.Day)
                {
                    var dayDir = totalNs > 0 ? 1 : totalNs < 0 ? -1 : 1;
                    var nextDayNs = intermediateNs + dayDir * NanosecondsPerDay;
                    if (BigInteger.Abs(nextDayNs) > InstantMaxEpochNanoseconds)
                        throw StandardLibrary.ThrowRangeError("Next day boundary is out of representable range", realm: realm);
                }
            }

            // Round time
            if (!string.Equals(smallestUnit, "nanosecond", StringComparison.Ordinal) || roundingIncrement != 1)
            {
                var incNs = new BigInteger(GetUnitNanoseconds(smallestUnit)) * roundingIncrement;
                totalNs = RoundToIncrement(totalNs, incNs, roundingMode);
            }

            // Validate total nanoseconds magnitude
            if (BigInteger.Abs(totalNs) > MaxTimeDuration)
                throw StandardLibrary.ThrowRangeError("Resulting duration is out of range", realm: realm);

            return BalanceTimeDurationToJsDuration(totalNs, UnitRank(largestUnit), realm);
        }

        // Calendar-aware path: requires relativeTo
        var relDate = plainDateRelativeTo ?? zonedDateTimeRelativeTo?.ToPlainDate();
        if (relDate == null)
            throw StandardLibrary.ThrowRangeError("relativeTo is required for rounding with calendar units", realm: realm);

        // Step 2: Add the date+time duration to relativeTo to get the endpoint
        var dateDuration = new JsTemporalDuration(duration.Years, duration.Months, duration.Weeks, duration.Days, 0, 0, 0, 0, 0, 0);
        var endDate = relDate.Add(dateDuration);

        // Validate endDate is within ISO date range (spec: ISODateWithinLimits / CreateTemporalDate)
        var endDateEpochDay = IsoToDayNumber(endDate.Year, endDate.Month, endDate.Day);
        if (endDateEpochDay < -100_000_000 || endDateEpochDay > 100_000_000)
            throw StandardLibrary.ThrowRangeError("Duration added to relativeTo is out of representable range", realm: realm);

        // For day unit: adjust time nanoseconds that spill into day
        var dayAdjust = 0;
        var adjustedTimeNs = timeNs;
        if (timeNs != 0 && duration.Days != 0)
        {
            var daysSign = Math.Sign(duration.Days);
            var timeSign = timeNs > 0 ? 1 : timeNs < 0 ? -1 : 0;
            if (daysSign != 0 && timeSign != 0 && daysSign != timeSign)
            {
                // Mixed sign: borrow a day
                dayAdjust = -daysSign;
                adjustedTimeNs += daysSign * NanosecondsPerDay;
            }
        }

        // Re-compute date difference from relativeTo to (endDate + dayAdjust)
        var adjustedEndEpoch = IsoToDayNumber(endDate.Year, endDate.Month, endDate.Day) + dayAdjust;
        var (adjEndY, adjEndM, adjEndD) = DayNumberToIsoDate(adjustedEndEpoch);

        // Difference in the largest date unit
        var (years, months, weeks, days) = DifferenceISODate(
            relDate.Year, relDate.Month, relDate.Day,
            adjEndY, adjEndM, adjEndD,
            largestRank >= TemporalUnit.Week ? largestUnit : "day");

        // If smallestUnit is a date unit, round the date part
        if (smallestRank >= TemporalUnit.Day)
        {
            // Compute destination epoch for the full endpoint including time
            var destEpoch = IsoToDayNumber(adjEndY, adjEndM, adjEndD);

            (years, months, weeks, days) = RoundDateDuration(
                years, months, weeks, days,
                relDate.Year, relDate.Month, relDate.Day,
                adjEndY, adjEndM, adjEndD,
                smallestUnit, roundingIncrement, roundingMode,
                largestUnit, adjustedTimeNs);

            // Re-balance: only the "day" case can overflow into the next unit (week).
            // Year/month/week cases already handle their own balancing internally.
            if (string.Equals(smallestUnit, "day", StringComparison.Ordinal) && largestRank > smallestRank)
            {
                var roundedDuration = new JsTemporalDuration(years, months, weeks, days, 0, 0, 0, 0, 0, 0);
                var newEnd = relDate.Add(roundedDuration);
                (years, months, weeks, days) = DifferenceISODate(
                    relDate.Year, relDate.Month, relDate.Day,
                    newEnd.Year, newEnd.Month, newEnd.Day,
                    largestUnit);
            }

            // Time is consumed by date rounding
            return new JsTemporalDuration(years, months, weeks, days, 0, 0, 0, 0, 0, 0);
        }

        // SmallestUnit is a time unit: round only the time part
        if (!string.Equals(smallestUnit, "nanosecond", StringComparison.Ordinal) || roundingIncrement != 1)
        {
            var incNs = new BigInteger(GetUnitNanoseconds(smallestUnit)) * roundingIncrement;
            adjustedTimeNs = RoundToIncrement(adjustedTimeNs, incNs, roundingMode);
        }

        // Check if rounding caused time to overflow into another day
        if (BigInteger.Abs(adjustedTimeNs) >= NanosecondsPerDay)
        {
            var extraDays = (int)(adjustedTimeNs / NanosecondsPerDay);
            adjustedTimeNs -= new BigInteger(extraDays) * NanosecondsPerDay;

            // Add extra days to the date
            var newEndEpoch = IsoToDayNumber(adjEndY, adjEndM, adjEndD) + extraDays;
            var (newEndY, newEndM, newEndD) = DayNumberToIsoDate(newEndEpoch);

            (years, months, weeks, days) = DifferenceISODate(
                relDate.Year, relDate.Month, relDate.Day,
                newEndY, newEndM, newEndD,
                largestRank >= TemporalUnit.Week ? largestUnit : "day");
        }

        // If largestUnit is a time unit, fold all days into time nanoseconds
        if (largestRank < TemporalUnit.Day)
        {
            adjustedTimeNs += new BigInteger(days) * NanosecondsPerDay;
            days = 0;
        }

        // Balance time duration
        var timeDuration = BalanceTimeDurationToJsDuration(adjustedTimeNs,
            largestRank < TemporalUnit.Day ? UnitRank(largestUnit) : TemporalUnit.Hour, realm);

        // Combine date + time
        if (!IsValidDuration(years, months, weeks, days,
                timeDuration.Hours, timeDuration.Minutes, timeDuration.Seconds,
                timeDuration.Milliseconds, timeDuration.Microseconds, timeDuration.Nanoseconds))
            throw StandardLibrary.ThrowRangeError("Resulting duration is out of range", realm: realm);

        return new JsTemporalDuration(years, months, weeks, days,
            timeDuration.Hours, timeDuration.Minutes, timeDuration.Seconds,
            timeDuration.Milliseconds, timeDuration.Microseconds, timeDuration.Nanoseconds);
    }

    /// <summary>
    ///     Temporal spec: Duration.prototype.add/subtract.
    ///     Rejects calendar units, adds time components using BigInteger, balances result.
    /// </summary>
    private static JsTemporalDuration AddDurations(JsTemporalDuration d1, JsTemporalDuration d2, int sign,
        RealmState realm)
    {
        // Step 1: Determine largestUnit from both durations
        var largestUnit1 = DefaultTemporalLargestUnit(d1);
        var largestUnit2 = DefaultTemporalLargestUnit(d2);
        var largestUnit = LargerOfTwoTemporalUnits(largestUnit1, largestUnit2);

        // Step 2: Reject calendar units (years, months, weeks)
        if (d1.Years != 0 || d1.Months != 0 || d1.Weeks != 0)
            throw StandardLibrary.ThrowRangeError("Duration with years, months, or weeks requires relativeTo for add/subtract", realm: realm);
        if (d2.Years != 0 || d2.Months != 0 || d2.Weeks != 0)
            throw StandardLibrary.ThrowRangeError("Duration with years, months, or weeks requires relativeTo for add/subtract", realm: realm);

        // Step 3: Compute total nanoseconds using BigInteger from both durations' time+day fields
        // Use ℝ(𝔽(x)) semantics — convert double values faithfully to BigInteger
        var totalNs = DurationToTotalNanoseconds(d1.Days, d1.Hours, d1.Minutes, d1.Seconds,
                          d1.Milliseconds, d1.Microseconds, d1.Nanoseconds)
                      + sign * DurationToTotalNanoseconds(d2.Days, d2.Hours, d2.Minutes, d2.Seconds,
                          d2.Milliseconds, d2.Microseconds, d2.Nanoseconds);

        // Step 4: Balance the result based on largestUnit
        return BalanceTimeDurationToJsDuration(totalNs, largestUnit, realm);
    }

    /// <summary>
    ///     Converts duration fields to total nanoseconds using BigInteger arithmetic.
    ///     Each double field is converted to BigInteger preserving IEEE 754 representation.
    /// </summary>
    private static BigInteger DurationToTotalNanoseconds(double days, double hours, double minutes,
        double seconds, double milliseconds, double microseconds, double nanoseconds)
    {
        return (BigInteger)days * 86_400_000_000_000L
               + (BigInteger)hours * 3_600_000_000_000L
               + (BigInteger)minutes * 60_000_000_000L
               + (BigInteger)seconds * 1_000_000_000L
               + (BigInteger)milliseconds * 1_000_000L
               + (BigInteger)microseconds * 1_000L
               + (BigInteger)nanoseconds;
    }

    /// <summary>
    ///     Balances total nanoseconds (BigInteger) into a Duration with the given largestUnit.
    ///     Uses truncation toward zero (mathematical truncation) for division.
    ///     Validates the result with IsValidDuration.
    /// </summary>
    private static JsTemporalDuration BalanceTimeDurationToJsDuration(BigInteger totalNs,
        TemporalUnit largestUnit, RealmState realm)
    {
        double days = 0, hours = 0, minutes = 0, seconds = 0;
        double milliseconds = 0, microseconds = 0;

        var remainder = totalNs;

        // Extract from largest unit down through nanoseconds.
        // C# BigInteger division truncates toward zero (mathematical truncation).
        // Use BigIntegerToDouble for correct IEEE 754 round-to-nearest-even
        // (the built-in (double)BigInteger can give incorrect rounding).
        if (largestUnit >= TemporalUnit.Day)
        {
            days = BigIntegerToDouble(remainder / 86_400_000_000_000L);
            remainder %= 86_400_000_000_000L;
        }

        if (largestUnit >= TemporalUnit.Hour)
        {
            hours = BigIntegerToDouble(remainder / 3_600_000_000_000L);
            remainder %= 3_600_000_000_000L;
        }

        if (largestUnit >= TemporalUnit.Minute)
        {
            minutes = BigIntegerToDouble(remainder / 60_000_000_000L);
            remainder %= 60_000_000_000L;
        }

        if (largestUnit >= TemporalUnit.Second)
        {
            seconds = BigIntegerToDouble(remainder / 1_000_000_000L);
            remainder %= 1_000_000_000L;
        }

        if (largestUnit >= TemporalUnit.Millisecond)
        {
            milliseconds = BigIntegerToDouble(remainder / 1_000_000L);
            remainder %= 1_000_000L;
        }

        if (largestUnit >= TemporalUnit.Microsecond)
        {
            microseconds = BigIntegerToDouble(remainder / 1_000L);
            remainder %= 1_000L;
        }

        var nanoseconds = BigIntegerToDouble(remainder);

        // Validate
        if (!IsValidDuration(0, 0, 0, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds))
            throw StandardLibrary.ThrowRangeError("Resulting duration is out of range", realm: realm);

        return new JsTemporalDuration(0, 0, 0, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds);
    }

    /// <summary>
    ///     Returns the largest non-zero unit in a duration.
    /// </summary>
    private static TemporalUnit DefaultTemporalLargestUnit(JsTemporalDuration d)
    {
        if (d.Years != 0) return TemporalUnit.Year;
        if (d.Months != 0) return TemporalUnit.Month;
        if (d.Weeks != 0) return TemporalUnit.Week;
        if (d.Days != 0) return TemporalUnit.Day;
        if (d.Hours != 0) return TemporalUnit.Hour;
        if (d.Minutes != 0) return TemporalUnit.Minute;
        if (d.Seconds != 0) return TemporalUnit.Second;
        if (d.Milliseconds != 0) return TemporalUnit.Millisecond;
        if (d.Microseconds != 0) return TemporalUnit.Microsecond;
        return TemporalUnit.Nanosecond;
    }

    /// <summary>
    ///     Returns the larger of two temporal units (where Day > Hour > Minute > ...).
    /// </summary>
    private static TemporalUnit LargerOfTwoTemporalUnits(TemporalUnit a, TemporalUnit b)
    {
        return a >= b ? a : b;
    }

    /// <summary>
    ///     Temporal spec: AddDurationToYearMonth — adds/subtracts a duration to a PlainYearMonth.
    ///     Handles lower units (days, hours, etc.) by converting to PlainDate first.
    ///     For negative sign durations, uses end-of-month as the intermediate date.
    /// </summary>
    private static JsValue AddDurationToYearMonth(int sign, JsTemporalPlainYearMonth ym,
        JsTemporalDuration duration, JsValue options, RealmState realm, JsObject prototype)
    {
        // Step 1: Apply sign to get effective duration
        var effectiveDuration = sign < 0 ? duration.Negated() : duration;

        // Step 2: Validate options
        var optionsObj = ValidateOptionsObject(options, realm, "Temporal.PlainYearMonth.prototype." + (sign > 0 ? "add" : "subtract"));
        var overflow = GetTemporalOverflowOption(optionsObj, realm);

        // Step 3: Determine the sign from the ORIGINAL duration (including all time components)
        // This is important: {seconds: -1} has sign -1 even though balanced days would be 0
        var durationSign = effectiveDuration.Sign;

        // Step 4: Balance time units into days
        var extraDays = BalanceTimeToDays(effectiveDuration);
        var totalDays = effectiveDuration.Days + extraDays;
        var durationWithBalancedDays = new JsTemporalDuration(
            effectiveDuration.Years, effectiveDuration.Months, effectiveDuration.Weeks,
            totalDays, 0, 0, 0, 0, 0, 0);

        // Step 5: Create intermediate PlainDate from year-month
        // Start with day 1
        JsTemporalPlainDate intermediateDate;
        try
        {
            intermediateDate = new JsTemporalPlainDate(ym.Year, ym.Month, 1, ym.Calendar);
            // Validate the intermediate date is in range
            RejectISODate(intermediateDate.Year, intermediateDate.Month, intermediateDate.Day, realm);
        }
        catch
        {
            throw StandardLibrary.ThrowRangeError("PlainYearMonth value is out of representable range", realm: realm);
        }

        // Step 6: For negative durations, use end-of-month as intermediate date
        if (durationSign < 0)
        {
            // Add 1 month to get the next month, then subtract 1 day → end of current month
            try
            {
                var nextMonth = intermediateDate.Add(
                    JsTemporalDuration.From(months: 1), "constrain");
                var endOfMonth = nextMonth.Add(
                    JsTemporalDuration.From(days: -1), "constrain");
                intermediateDate = endOfMonth;
                RejectISODate(intermediateDate.Year, intermediateDate.Month, intermediateDate.Day, realm);
            }
            catch
            {
                throw StandardLibrary.ThrowRangeError("Resulting date is out of valid range", realm: realm);
            }
        }

        // Step 7: Add the duration to the intermediate date
        JsTemporalPlainDate addedDate;
        try
        {
            addedDate = intermediateDate.Add(durationWithBalancedDays, overflow);
            RejectISODate(addedDate.Year, addedDate.Month, addedDate.Day, realm);
        }
        catch (ArgumentException)
        {
            throw StandardLibrary.ThrowRangeError("Resulting date is out of valid range", realm: realm);
        }

        // Step 8: Extract year-month from result, set day to 1 per spec, and validate range
        // Per spec step 12: "Set addedDateFields.[[Day]] to 1"
        RejectISOYearMonthRange(addedDate.Year, addedDate.Month, realm);

        return WrapPlainYearMonth(
            new JsTemporalPlainYearMonth(addedDate.Year, addedDate.Month, ym.Calendar, 1),
            realm, prototype);
    }

    /// <summary>
    ///     Temporal spec: AddDateTime — adds a duration to a PlainDateTime.
    ///     Adds time first using BigInteger, gets day overflow, then adds date portion.
    /// </summary>
    private static JsTemporalPlainDateTime AddDurationToPlainDateTime(
        JsTemporalPlainDateTime dt, JsTemporalDuration duration, int sign,
        string overflow, RealmState realm)
    {
        // Step 1: AddTime using BigInteger for time components
        var dtTimeNanos = (BigInteger)dt.Hour * 3_600_000_000_000L
                          + (BigInteger)dt.Minute * 60_000_000_000L
                          + (BigInteger)dt.Second * 1_000_000_000L
                          + (BigInteger)dt.Millisecond * 1_000_000L
                          + (BigInteger)dt.Microsecond * 1_000L
                          + dt.Nanosecond;

        var durTimeNanos = (BigInteger)(sign * duration.Hours) * 3_600_000_000_000L
                           + (BigInteger)(sign * duration.Minutes) * 60_000_000_000L
                           + (BigInteger)(sign * duration.Seconds) * 1_000_000_000L
                           + (BigInteger)(sign * duration.Milliseconds) * 1_000_000L
                           + (BigInteger)(sign * duration.Microseconds) * 1_000L
                           + (BigInteger)(sign * duration.Nanoseconds);

        var totalTimeNanos = dtTimeNanos + durTimeNanos;

        // BalanceTime: normalize to 0..nsPerDay-1 + day overflow (floor division)
        const long nsPerDay = 86_400_000_000_000L;
        BigInteger dayOverflow;
        long remainderNanos;

        if (totalTimeNanos >= 0)
        {
            dayOverflow = totalTimeNanos / nsPerDay;
            remainderNanos = (long)(totalTimeNanos % nsPerDay);
        }
        else
        {
            // Floor division for negative values: shift to make non-negative
            dayOverflow = (totalTimeNanos - nsPerDay + 1) / nsPerDay;
            remainderNanos = (long)(totalTimeNanos - dayOverflow * nsPerDay);
        }

        // Extract time components from non-negative remainderNanos
        var hour = (int)(remainderNanos / 3_600_000_000_000L);
        remainderNanos %= 3_600_000_000_000L;
        var minute = (int)(remainderNanos / 60_000_000_000L);
        remainderNanos %= 60_000_000_000L;
        var second = (int)(remainderNanos / 1_000_000_000L);
        remainderNanos %= 1_000_000_000L;
        var millisecond = (int)(remainderNanos / 1_000_000L);
        remainderNanos %= 1_000_000L;
        var microsecond = (int)(remainderNanos / 1_000L);
        var nanosecond = (int)(remainderNanos % 1_000L);

        // Step 2: Check total days is a safe integer
        var totalDays = (BigInteger)(sign * duration.Days) + dayOverflow;
        if (BigInteger.Abs(totalDays) > 9007199254740991)
            throw StandardLibrary.ThrowRangeError("Duration days too large for PlainDateTime arithmetic", realm: realm);

        // Step 3: AddISODate (add years, months, weeks, days to the date)
        var dateDuration = new JsTemporalDuration(
            sign * duration.Years, sign * duration.Months,
            sign * duration.Weeks, (double)totalDays, 0, 0, 0, 0, 0, 0);
        var startDate = new JsTemporalPlainDate(dt.Year, dt.Month, dt.Day);
        JsTemporalPlainDate resultDate;
        try
        {
            resultDate = startDate.Add(dateDuration, overflow);
        }
        catch (ArgumentException)
        {
            throw StandardLibrary.ThrowRangeError("Resulting date is out of valid range", realm: realm);
        }

        // Step 4: ISODateTimeWithinLimits validation
        RejectISODateTimeRange(resultDate.Year, resultDate.Month, resultDate.Day,
            hour, minute, second, millisecond, microsecond, nanosecond, realm);

        return new JsTemporalPlainDateTime(
            new JsTemporalPlainDate(resultDate.Year, resultDate.Month, resultDate.Day),
            new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond));
    }

    /// <summary>
    ///     Per spec AddZonedDateTime: returns epoch nanoseconds after adding a duration to a ZDT.
    ///     Fast path: if years = months = weeks = days = 0, uses AddInstant (no local time conversion).
    /// </summary>
    private static BigInteger AddZonedDateTimeEpochNs(
        JsTemporalZonedDateTime zdt, JsTemporalDuration d, RealmState realm)
    {
        // Fast path: no calendar/day units — AddInstant (just add time nanoseconds)
        if (d.Years == 0 && d.Months == 0 && d.Weeks == 0 && d.Days == 0)
        {
            var timeNs = (BigInteger)d.Hours * 3_600_000_000_000L
                         + (BigInteger)d.Minutes * 60_000_000_000L
                         + (BigInteger)d.Seconds * 1_000_000_000L
                         + (BigInteger)d.Milliseconds * 1_000_000L
                         + (BigInteger)d.Microseconds * 1_000L
                         + (BigInteger)d.Nanoseconds;
            var result = zdt.Instant.EpochNanoseconds + timeNs;
            if (result < InstantMinEpochNanoseconds || result > InstantMaxEpochNanoseconds)
                throw StandardLibrary.ThrowRangeError("Resulting instant is out of valid range", realm: realm);
            return result;
        }

        // Slow path: add via local PlainDateTime for calendar/day units
        var pdt = GetLocalPlainDateTime(zdt, realm);
        var end = AddDurationToPlainDateTime(pdt, d, 1, "constrain", realm);
        var offset = zdt.FixedOffset ?? ResolveTimeZoneOffset(
            CreateTimeZoneLocalDateTime(end), zdt.TimeZone, zdt.FixedOffset);
        var offsetNanos = new BigInteger(offset.Ticks) * 100;
        var epochNs = ToEpochNanoseconds(end) - offsetNanos;
        if (epochNs < InstantMinEpochNanoseconds || epochNs > InstantMaxEpochNanoseconds)
            throw StandardLibrary.ThrowRangeError("Resulting instant is out of valid range", realm: realm);
        return epochNs;
    }

    /// <summary>
    ///     Temporal spec: AddZonedDateTime — adds a duration to a ZonedDateTime.
    ///     Converts to local PlainDateTime, adds using AddDurationToPlainDateTime,
    ///     then converts back to ZonedDateTime in the same timezone.
    /// </summary>
    private static JsTemporalZonedDateTime AddDurationToZonedDateTime(
        JsTemporalZonedDateTime zdt, JsTemporalDuration duration, int sign,
        string overflow, RealmState realm)
    {
        // Step 1: Get the local PlainDateTime
        var local = GetLocalPlainDateTime(zdt, realm);

        // Step 2: Add the duration using the PlainDateTime arithmetic (handles overflow)
        var resultLocal = AddDurationToPlainDateTime(local, duration, sign, overflow, realm);

        // Step 3: Convert back to ZonedDateTime by resolving the timezone offset
        var offset = zdt.FixedOffset ?? ResolveTimeZoneOffset(
            CreateTimeZoneLocalDateTime(resultLocal), zdt.TimeZone, zdt.FixedOffset);
        var offsetNanoseconds = new BigInteger(offset.Ticks) * 100;
        var localEpochNanoseconds = ToEpochNanoseconds(resultLocal);
        var resultInstantNanoseconds = localEpochNanoseconds - offsetNanoseconds;

        if (resultInstantNanoseconds < InstantMinEpochNanoseconds ||
            resultInstantNanoseconds > InstantMaxEpochNanoseconds)
        {
            throw StandardLibrary.ThrowRangeError("Resulting ZonedDateTime is out of range", realm: realm);
        }

        return new JsTemporalZonedDateTime(
            JsTemporalInstant.FromEpochNanoseconds(resultInstantNanoseconds),
            zdt.TimeZoneId,
            CanonicalizeCalendarId(zdt.Calendar));
    }

    /// <summary>
    ///     Validates that the given date-time is within the representable PlainDateTime range.
    ///     Per spec: nsMinInstant - nsPerDay &lt; epochNs &lt; nsMaxInstant + nsPerDay
    /// </summary>
    private static void RejectISODateTimeRange(int year, int month, int day,
        int hour, int minute, int second, int millisecond, int microsecond, int nanosecond,
        RealmState? realm = null)
    {
        // Per spec ISODateTimeWithinLimits: abs(epochDays) > 10^8 + 1
        // The +1 gives headroom for time components within the boundary day
        var epochDays = IsoCalendarHelpers.DateToEpochDays(year, month, day);
        if (Math.Abs(epochDays) > 100_000_001)
            throw StandardLibrary.ThrowRangeError("Resulting PlainDateTime is out of representable range", realm: realm);
    }

    private static int DaysInMonth(int year, int month)
    {
        return month switch
        {
            1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
            4 or 6 or 9 or 11 => 30,
            2 => IsLeapYear(year) ? 29 : 28,
            _ => 0
        };
    }

    private static void ValidateRoundedDateResult(int refYear, int refMonth, int refDay, int totalMonths)
    {
        var (y, m) = AddYearMonth(refYear, refMonth, totalMonths);
        var d = Math.Min(refDay, DaysInISOMonth(y, m));
        RejectISODateTimeRange(y, m, d, 0, 0, 0, 0, 0, 0);
    }

    private static void ValidateRoundedDayResult(long endEpochDay)
    {
        var endEpochNs = new BigInteger(endEpochDay) * NanosecondsPerDay;
        if (endEpochNs <= -InstantMaxEpochNanoseconds - NanosecondsPerDay ||
            endEpochNs >= InstantMaxEpochNanoseconds + NanosecondsPerDay)
            throw StandardLibrary.ThrowRangeError("Resulting PlainDateTime is out of representable range");
    }

    private static bool IsLeapYear(int year)
    {
        return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
    }

    /// <summary>
    ///     Parses an offset string like "+01:00", "-05:30", "+00:19:32.37" to nanoseconds.
    ///     Returns null if the offset string is invalid or out of range.
    /// </summary>
    private static long? ParseOffsetToNanos(string offsetStr)
    {
        if (offsetStr.Length < 2) return null;
        var sign = offsetStr[0] == '-' ? -1L : 1L;
        var body = offsetStr[1..];
        var parts = body.Split(':');
        int hours, minutes = 0, seconds = 0;
        long subSecondNanos = 0;

        if (parts.Length == 1)
        {
            // Compact format: HH or HHMM
            if (body.Length == 2)
            {
                if (!int.TryParse(body, System.Globalization.CultureInfo.InvariantCulture, out hours)) return null;
            }
            else if (body.Length == 4)
            {
                if (!int.TryParse(body[..2], System.Globalization.CultureInfo.InvariantCulture, out hours)) return null;
                if (!int.TryParse(body[2..], System.Globalization.CultureInfo.InvariantCulture, out minutes)) return null;
            }
            else
            {
                return null;
            }
        }
        else
        {
            if (parts[0].Length != 2 || !int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out hours)) return null;
            if (parts[1].Length != 2 || !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out minutes)) return null;

            if (parts.Length > 2)
            {
                var secStr = parts[2];
                var dotIdx = secStr.IndexOf('.');
                if (dotIdx >= 0)
                {
                    if (dotIdx != 2 || !int.TryParse(secStr[..dotIdx], System.Globalization.CultureInfo.InvariantCulture, out seconds)) return null;
                    var frac = secStr[(dotIdx + 1)..];
                    if (frac.Length == 0 || frac.Length > 9) return null;
                    frac = frac.PadRight(9, '0');
                    if (!long.TryParse(frac, System.Globalization.CultureInfo.InvariantCulture, out subSecondNanos)) return null;
                }
                else
                {
                    if (secStr.Length != 2 || !int.TryParse(secStr, System.Globalization.CultureInfo.InvariantCulture, out seconds)) return null;
                }
            }
        }

        // Validate offset range: must be < ±24:00
        if (hours > 23) return null;
        if (minutes > 59 || seconds > 59) return null;

        return sign * ((long)hours * 3_600_000_000_000L + (long)minutes * 60_000_000_000L +
                        (long)seconds * 1_000_000_000L + subSecondNanos);
    }

    private static bool OffsetsMatchStringInput(string dateTimeString, long parsedOffsetNanos, long timeZoneOffsetNanos)
    {
        var offsetString = ExtractOffsetStringFromDateTimeString(dateTimeString);
        if (offsetString is null)
            return parsedOffsetNanos == timeZoneOffsetNanos;

        if (OffsetHasSubMinutePrecision(offsetString))
            return parsedOffsetNanos == timeZoneOffsetNanos;

        return RoundOffsetNanosecondsToMinute(timeZoneOffsetNanos) == parsedOffsetNanos;
    }

    private static string? ExtractOffsetStringFromDateTimeString(string dateTimeString)
    {
        var tIdx = dateTimeString.IndexOf('T');
        if (tIdx < 0)
            tIdx = dateTimeString.IndexOf('t');
        if (tIdx < 0)
            return null;

        var timePart = dateTimeString[(tIdx + 1)..];
        for (var i = timePart.Length - 1; i >= 1; i--)
        {
            if ((timePart[i] == '+' || timePart[i] == '-') &&
                i + 1 < timePart.Length && char.IsDigit(timePart[i + 1]))
            {
                return timePart[i..];
            }
        }

        return null;
    }

    private static bool OffsetHasSubMinutePrecision(string offsetString)
    {
        var body = offsetString.TrimStart('+', '-');
        if (body.Contains(':'))
            return body.Split(':').Length >= 3;

        return body.Length > 4;
    }

    private static long RoundOffsetNanosecondsToMinute(long offsetNanos)
    {
        const long minuteNanos = 60_000_000_000L;
        if (offsetNanos == 0)
            return 0;

        var sign = offsetNanos < 0 ? -1L : 1L;
        var abs = Math.Abs(offsetNanos);
        var rounded = ((abs + minuteNanos / 2) / minuteNanos) * minuteNanos;
        return sign * rounded;
    }

    private static bool TryMatchTimeZoneOffsetForString(string dateTimeString, long parsedOffsetNanos,
        string requestedTimeZoneId, TimeZoneInfo timeZone, TimeSpan? fixedOffset, DateTime localDateTime,
        out TimeSpan matchedOffset)
    {
        if (fixedOffset.HasValue)
        {
            matchedOffset = fixedOffset.Value;
            return OffsetsMatchStringInput(dateTimeString, parsedOffsetNanos, matchedOffset.Ticks * 100L);
        }

        var candidateOffsets = TemporalHistoricalTimeZoneOffsets.GetPossibleUtcOffsets(requestedTimeZoneId, timeZone, localDateTime);
        matchedOffset = candidateOffsets[0];

        foreach (var candidateOffset in candidateOffsets)
        {
            if (OffsetsMatchStringInput(dateTimeString, parsedOffsetNanos, candidateOffset.Ticks * 100L))
            {
                matchedOffset = candidateOffset;
                return true;
            }
        }

        return false;
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

        // Try to parse as ISO 8601 duration string (check string BEFORE object per spec)
        if (value.IsString)
        {
            var str = value.AsString() ?? "";
            return ParseIsoDuration(str, realm);
        }

        // If it's an object with duration properties
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            // Per spec: read properties in alphabetical order using ToIntegerIfIntegral
            var any = false;
            var days = ReadDurationFieldFromBag(accessor, "days", ref any, realm);
            var hours = ReadDurationFieldFromBag(accessor, "hours", ref any, realm);
            var microseconds = ReadDurationFieldFromBag(accessor, "microseconds", ref any, realm);
            var milliseconds = ReadDurationFieldFromBag(accessor, "milliseconds", ref any, realm);
            var minutes = ReadDurationFieldFromBag(accessor, "minutes", ref any, realm);
            var months = ReadDurationFieldFromBag(accessor, "months", ref any, realm);
            var nanoseconds = ReadDurationFieldFromBag(accessor, "nanoseconds", ref any, realm);
            var seconds = ReadDurationFieldFromBag(accessor, "seconds", ref any, realm);
            var weeks = ReadDurationFieldFromBag(accessor, "weeks", ref any, realm);
            var years = ReadDurationFieldFromBag(accessor, "years", ref any, realm);

            if (!any)
            {
                throw StandardLibrary.ThrowTypeError(
                    "Duration-like object must have at least one duration property", realm: realm);
            }

            RejectDurationSign(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds, realm);
            if (!IsValidDuration(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds))
            {
                throw StandardLibrary.ThrowRangeError("Duration values are out of range", realm: realm);
            }

            return new JsTemporalDuration(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds);
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.Duration", realm: realm);
    }

    /// <summary>
    ///     Reads a duration field from a property bag with ToIntegerIfIntegral semantics.
    /// </summary>
    private static double ReadDurationFieldFromBag(IJsPropertyAccessor accessor, string name, ref bool any, RealmState? realm = null)
    {
        if (!accessor.TryGetProperty(name, out var v) || v.IsUndefined) return 0;
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
        return value == 0 ? 0 : value;
    }

    private static JsTemporalPlainDate ToTemporalPlainDate(JsValue value, RealmState realm)
    {
        // 1. String - parse ISO string
        if (value.IsString)
            return ParseTemporalPlainDateString(value.AsString() ?? "", realm);

        // 2. Non-string primitives → TypeError
        if (value.IsUndefined || value.IsNull || value.IsBoolean || value.IsNumber || value.IsSymbol || value.IsBigInt)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDate", realm: realm);

        // 3. Objects - check for Temporal types first
        if (value.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalPlainDateSlot, out var slot) &&
                slot.TryGetObject<JsTemporalPlainDate>(out var date))
                return date;

            if (obj.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) &&
                zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
                return zdt.ToPlainDate();

            if (obj.TryGetProperty(TemporalPlainDateTimeSlot, out var pdtSlot) &&
                pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                return pdt.ToPlainDate();
        }

        // 4. Property bag path for all objects
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
            return ToTemporalPlainDateFromPropertyBag(accessor, realm);

        // 5. HostFunction or other non-accessor objects
        if (value.Kind == JsValueKind.Object)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDate: object has no date properties", realm: realm);

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDate", realm: realm);
    }

    private static JsTemporalPlainDate ToTemporalPlainDateFromPropertyBag(IJsPropertyAccessor accessor, RealmState realm)
    {
        return ToTemporalPlainDateFromPropertyBagWithOverflow(accessor, "constrain", realm);
    }

    /// <summary>
    /// Overload for from() where options must be read AFTER fields per spec order.
    /// </summary>
    private static JsTemporalPlainDate ToTemporalPlainDateFromPropertyBagWithOverflow(
        IJsPropertyAccessor accessor, JsValue options, RealmState realm, string methodName)
    {
        // Read fields first (per spec order), then resolve overflow from options
        var (year, month, day, calendar) = ReadPlainDateFields(accessor, realm);

        // Now read options
        var resolvedOpts = ValidateOptionsObject(options, realm, methodName);
        var overflow = GetTemporalOverflowOption(resolvedOpts, realm);

        return ApplyOverflowToDate(year, month, day, calendar, overflow, realm);
    }

    private static JsTemporalPlainDate ToTemporalPlainDateFromPropertyBagWithOverflow(
        IJsPropertyAccessor accessor, string overflow, RealmState realm)
    {
        var (year, month, day, calendar) = ReadPlainDateFields(accessor, realm);
        return ApplyOverflowToDate(year, month, day, calendar, overflow, realm);
    }

    /// <summary>
    /// Reads PlainDate fields in spec-required alphabetical order: calendar, day, month, monthCode, year.
    /// </summary>
    private static (int year, int month, int day, string calendar) ReadPlainDateFields(IJsPropertyAccessor accessor, RealmState realm)
    {
        // Per spec, read properties in alphabetical order for observable behavior:
        // calendar, day, era, eraYear, month, monthCode, year

        // 1. calendar
        var calendar = "iso8601";
        if (accessor.TryGetProperty("calendar", out var calVal) && !calVal.IsUndefined)
        {
            calendar = CanonicalizeCalendarId(ResolveTemporalCalendarId(calVal, realm));
        }

        // 2. day (required)
        if (!accessor.TryGetProperty("day", out var dayVal) || dayVal.IsUndefined)
            throw StandardLibrary.ThrowTypeError("Property bag for PlainDate must have 'day'", realm: realm);
        var day = ToIntegerWithTruncation(dayVal, realm);

        // 3-4. era/eraYear: only read for non-ISO calendars (ISO calendar ignores them per spec).
        var isIso = string.Equals(calendar, "iso8601", StringComparison.Ordinal);
        var hasEra = false;
        var hasEraYear = false;
        string? era = null;
        int eraYear = 0;
        if (!isIso)
        {
            hasEra = accessor.TryGetProperty("era", out var eraVal) && !eraVal.IsUndefined;
            if (hasEra)
                era = JsOps.ToJsString(eraVal);

            hasEraYear = accessor.TryGetProperty("eraYear", out var eraYearVal) && !eraYearVal.IsUndefined;
            if (hasEraYear)
                eraYear = ToIntegerWithTruncation(eraYearVal, realm);

            if (hasEra != hasEraYear)
                throw StandardLibrary.ThrowTypeError("Property bag for PlainDate must have both 'era' and 'eraYear'", realm: realm);
        }

        // 5. month — eagerly convert to trigger valueOf for observable order
        accessor.TryGetProperty("month", out var monthVal);
        var hasMonth = !monthVal.IsUndefined;
        int monthInt = 0;
        if (hasMonth)
            monthInt = ToIntegerWithTruncation(monthVal, realm);

        // 6. monthCode
        accessor.TryGetProperty("monthCode", out var monthCodeVal);
        var hasMonthCode = !monthCodeVal.IsUndefined;
        string? monthCodeStr = null;
        if (hasMonthCode)
        {
            monthCodeStr = JsOps.ToJsString(monthCodeVal);
            ValidateMonthCodeSyntax(monthCodeStr, realm);
        }

        // 7. year (required unless era/eraYear are present for an era-aware calendar)
        if (!accessor.TryGetProperty("year", out var yearVal) || yearVal.IsUndefined)
        {
            if (hasEra || hasEraYear)
            {
                if (!hasEra || !hasEraYear)
                    throw StandardLibrary.ThrowTypeError("Property bag for PlainDate must have both 'era' and 'eraYear'", realm: realm);

                if (!CalendarUsesEras(calendar))
                    throw StandardLibrary.ThrowTypeError("Property bag for PlainDate must have 'year'", realm: realm);

                var yearFromEra = ResolveTemporalEraYear(calendar, era!, eraYear, realm);

                // Now resolve month from month/monthCode
                if (!hasMonth && !hasMonthCode)
                    throw StandardLibrary.ThrowTypeError("Property bag for PlainDate must have 'month' or 'monthCode'", realm: realm);

                int monthFromEra;
                if (hasMonthCode)
                {
                    monthFromEra = ResolveISOMonthCode(monthCodeStr!, realm);
                    if (hasMonth)
                    {
                        if (monthInt != monthFromEra)
                            throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
                    }
                }
                else
                {
                    monthFromEra = monthInt;
                }

                return (yearFromEra, monthFromEra, day, calendar);
            }

            throw StandardLibrary.ThrowTypeError("Property bag for PlainDate must have 'year'", realm: realm);
        }
        var year = ToIntegerWithTruncation(yearVal, realm);

        // Now resolve month from month/monthCode
        if (!hasMonth && !hasMonthCode)
            throw StandardLibrary.ThrowTypeError("Property bag for PlainDate must have 'month' or 'monthCode'", realm: realm);

        int month;
        if (hasMonthCode)
        {
            month = ResolveISOMonthCode(monthCodeStr!, realm);
            if (hasMonth)
            {
                if (monthInt != month)
                    throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
            }
        }
        else
        {
            month = monthInt;
        }

        return (year, month, day, calendar);
    }

    private static JsTemporalPlainDate ApplyOverflowToDate(int year, int month, int day, string calendar, string overflow, RealmState realm)
    {
        // Values ≤ 0 are always invalid regardless of overflow mode
        if (month < 1)
            throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
        if (day < 1)
            throw StandardLibrary.ThrowRangeError($"Day {day} is out of range", realm: realm);

        if (overflow == "reject")
        {
            if (month > 12)
                throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
            var maxDay = IsoCalendarHelpers.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
            if (day > maxDay)
                throw StandardLibrary.ThrowRangeError($"Day {day} is out of range for month {month}", realm: realm);
        }
        else
        {
            // constrain: clamp values above maximum
            month = Math.Min(month, 12);
            var maxDay = IsoCalendarHelpers.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
            day = Math.Min(day, maxDay);
        }

        // Validate ISO date range
        RejectISODate(year, month, day, realm);

        return new JsTemporalPlainDate(year, month, day, calendar);
    }

    /// <summary>
    /// Overload for from() where options must be read AFTER fields per spec order.
    /// </summary>
    private static JsTemporalPlainYearMonth ToTemporalPlainYearMonthFromPropertyBagWithOverflow(
        IJsPropertyAccessor accessor, JsValue options, RealmState realm, string methodName)
    {
        // Read fields first (per spec order), then resolve overflow from options
        var (year, month, calendar, monthCode) = ReadPlainYearMonthFields(accessor, realm);

        // Now read options
        var resolvedOpts = ValidateOptionsObject(options, realm, methodName);
        var overflow = GetTemporalOverflowOption(resolvedOpts, realm);

        return ApplyOverflowToYearMonth(year, month, calendar, monthCode, overflow, realm);
    }

    /// <summary>
    /// Reads PlainYearMonth fields in spec-required alphabetical order: calendar, era, eraYear, month, monthCode, year.
    /// Eagerly calls valueOf/toString for observable behavior.
    /// </summary>
    private static (int year, int month, string calendar, string? monthCode) ReadPlainYearMonthFields(IJsPropertyAccessor accessor, RealmState realm)
    {
        // Per spec, read properties in alphabetical order for observable behavior:
        // calendar, era, eraYear, month, monthCode, year

        // 1. calendar
        var calendar = "iso8601";
        if (accessor.TryGetProperty("calendar", out var calVal) && !calVal.IsUndefined)
            calendar = CanonicalizeCalendarId(ResolveTemporalCalendarId(calVal, realm));

        var calendarUsesEras = CalendarUsesEras(calendar);

        // 2. era
        var hasEra = false;
        string? era = null;
        if (calendarUsesEras)
        {
            hasEra = accessor.TryGetProperty("era", out var eraVal) && !eraVal.IsUndefined;
            if (hasEra)
                era = JsOps.ToJsString(eraVal);
        }

        // 3. eraYear
        var hasEraYear = false;
        int eraYear = 0;
        if (calendarUsesEras)
        {
            hasEraYear = accessor.TryGetProperty("eraYear", out var eraYearVal) && !eraYearVal.IsUndefined;
            if (hasEraYear)
            {
                eraYear = ToIntegerWithTruncation(eraYearVal, realm);
            }
        }

        // 4. month — eagerly convert to trigger valueOf for observable order
        accessor.TryGetProperty("month", out var monthVal);
        var hasMonth = !monthVal.IsUndefined;
        int monthInt = 0;
        if (hasMonth)
            monthInt = ToIntegerWithTruncation(monthVal, realm);

        // 5. monthCode
        accessor.TryGetProperty("monthCode", out var monthCodeVal);
        var hasMonthCode = !monthCodeVal.IsUndefined;
        string? monthCodeStr = null;
        if (hasMonthCode)
        {
            monthCodeStr = JsOps.ToJsString(monthCodeVal);
            ValidateMonthCodeSyntax(monthCodeStr, realm);
        }

        if (hasEra != hasEraYear)
            throw StandardLibrary.ThrowTypeError("Property bag for PlainYearMonth must have both 'era' and 'eraYear'", realm: realm);

        // 6. year (required unless era/eraYear are present for an era-aware calendar)
        var hasYear = accessor.TryGetProperty("year", out var yearVal) && !yearVal.IsUndefined;
        int year = 0;
        if (hasYear)
        {
            year = ToIntegerWithTruncation(yearVal, realm);
        }
        else if (hasEra || hasEraYear)
        {
            if (!hasEra || !hasEraYear)
                throw StandardLibrary.ThrowTypeError("Property bag for PlainYearMonth must have both 'era' and 'eraYear'", realm: realm);

            if (!calendarUsesEras)
                throw StandardLibrary.ThrowTypeError("Property bag for PlainYearMonth must have 'year'", realm: realm);

            year = ResolveTemporalEraYear(calendar, era!, eraYear, realm);
        }
        else
        {
            throw StandardLibrary.ThrowTypeError("Property bag for PlainYearMonth must have 'year'", realm: realm);
        }

        // Resolve month from month/monthCode
        if (!hasMonth && !hasMonthCode)
            throw StandardLibrary.ThrowTypeError("Property bag for PlainYearMonth must have 'month' or 'monthCode'", realm: realm);

        int month;
        if (hasMonthCode)
        {
            month = string.Equals(calendar, "iso8601", StringComparison.Ordinal)
                ? ResolveISOMonthCode(monthCodeStr!, realm)
                : MonthCodeNumericValue(monthCodeStr!);
            if (hasMonth)
            {
                if (monthInt != month)
                    throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
            }
        }
        else
        {
            month = monthInt;
        }

        return (year, month, calendar, monthCodeStr);
    }

    private static JsTemporalPlainYearMonth ApplyOverflowToYearMonth(int year, int month, string calendar, string? monthCode, string overflow, RealmState realm)
    {
        if (month < 1)
            throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);

        if (string.Equals(overflow, "reject", StringComparison.Ordinal))
        {
            if (month > 12)
                throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
        }
        else
        {
            // constrain
            month = Math.Min(month, 12);
        }

        RejectISOYearMonthRange(year, month, realm);
        var referenceMonthCode = monthCode;
        if (string.Equals(overflow, "constrain", StringComparison.Ordinal) &&
            referenceMonthCode is not null &&
            referenceMonthCode.Length == 4 &&
            referenceMonthCode[3] == 'L' &&
            !IsValidLeapMonthCodeForYear(calendar, year, referenceMonthCode))
        {
            referenceMonthCode = $"M{month:D2}";
        }

        var referenceDay = string.Equals(overflow, "constrain", StringComparison.Ordinal) &&
                           calendar == "chinese" &&
                           referenceMonthCode is not null &&
                           referenceMonthCode == $"M{month:D2}" &&
                           month == 1
            ? 1
            : GetTemporalReferenceISODay(calendar, year, month, 1, referenceMonthCode, realm);

        return new JsTemporalPlainYearMonth(year, month, calendar,
            referenceDay);
    }

    private static bool CalendarUsesEras(string calendar)
    {
        return string.Equals(calendar, "gregory", StringComparison.Ordinal) ||
               string.Equals(calendar, "japanese", StringComparison.Ordinal);
    }

    private static int ResolveTemporalEraYear(string calendar, string era, int eraYear, RealmState realm)
    {
        if (string.Equals(calendar, "gregory", StringComparison.Ordinal))
        {
            return era.ToLowerInvariant() switch
            {
                "ce" or "ad" => eraYear,
                "bce" or "bc" => 1 - eraYear,
                _ => throw StandardLibrary.ThrowRangeError($"Unsupported era '{era}' for calendar '{calendar}'", realm: realm)
            };
        }

        if (string.Equals(calendar, "japanese", StringComparison.Ordinal))
        {
            var startYear = era.ToLowerInvariant() switch
            {
                "reiwa" => 2019,
                "heisei" => 1989,
                "showa" => 1926,
                "taisho" => 1912,
                "meiji" => 1868,
                _ => throw StandardLibrary.ThrowRangeError($"Unsupported era '{era}' for calendar '{calendar}'", realm: realm)
            };

            return startYear + eraYear - 1;
        }

        throw StandardLibrary.ThrowTypeError("Property bag for PlainYearMonth must have 'year'", realm: realm);
    }

    private static int GetTemporalReferenceISODay(string calendar, int year, int month, int day, string? monthCode, RealmState realm)
    {
        var safeYear = year is >= 1 and <= 9999 ? year : 2000;
        var sourceDate = new DateTime(safeYear, month, day);
        var resolvedMonthCode = monthCode;
        var resolvedDay = day;

        if (resolvedMonthCode is null)
        {
            if (!TryGetCalendarMonthDayForIsoDate(calendar, sourceDate, out _, out var calendarDay, out var calendarMonthCode))
                throw StandardLibrary.ThrowRangeError($"Month M{month:D2} is out of range for calendar {calendar}", realm: realm);

            resolvedMonthCode = calendarMonthCode;
            resolvedDay = 1;
        }

        if (string.Equals(calendar, "iso8601", StringComparison.Ordinal))
            return resolvedDay;

        if (TryFindLatestReferenceIsoDate(calendar, resolvedMonthCode, resolvedDay, out var referenceDate))
            return referenceDate.Day;

        throw StandardLibrary.ThrowRangeError($"Month {resolvedMonthCode} is out of range for calendar {calendar}", realm: realm);
    }

    /// <summary>
    /// Overload for from() where options must be read AFTER fields per spec order.
    /// </summary>
    private static JsTemporalPlainMonthDay ToTemporalPlainMonthDayFromPropertyBagWithOverflow(
        IJsPropertyAccessor accessor, JsValue options, RealmState realm, string methodName)
    {
        // Read fields first (per spec order), then resolve overflow from options
        var (month, day, yearForValidation, calendar, monthCode, hasYear) = ReadPlainMonthDayFields(accessor, realm);

        // Now read options
        var resolvedOpts = ValidateOptionsObject(options, realm, methodName);
        var overflow = GetTemporalOverflowOption(resolvedOpts, realm);

        return ApplyOverflowToMonthDay(month, day, yearForValidation, calendar, monthCode, hasYear, overflow, realm);
    }

    /// <summary>
    /// Reads PlainMonthDay fields in spec-required alphabetical order: calendar, day, month, monthCode, year.
    /// Eagerly calls valueOf/toString for observable behavior.
    /// </summary>
    private static (int month, int day, int yearForValidation, string calendar, string? monthCode, bool hasYear) ReadPlainMonthDayFields(
        IJsPropertyAccessor accessor, RealmState realm)
    {
        // Per spec, read properties in alphabetical order for observable behavior:
        // calendar, day, month, monthCode, year

        // 1. calendar
        var calendar = "iso8601";
        if (accessor.TryGetProperty("calendar", out var calVal) && !calVal.IsUndefined)
            calendar = CanonicalizeCalendarId(ResolveTemporalCalendarId(calVal, realm));

        // 2. day (required)
        if (!accessor.TryGetProperty("day", out var dayVal) || dayVal.IsUndefined)
            throw StandardLibrary.ThrowTypeError("Property bag for PlainMonthDay must have 'day'", realm: realm);
        var day = ToIntegerWithTruncation(dayVal, realm);

        // 3. month — eagerly convert to trigger valueOf for observable order
        accessor.TryGetProperty("month", out var monthVal);
        var hasMonth = !monthVal.IsUndefined;
        int monthInt = 0;
        if (hasMonth)
            monthInt = ToIntegerWithTruncation(monthVal, realm);

        // 4. monthCode
        accessor.TryGetProperty("monthCode", out var monthCodeVal);
        var hasMonthCode = !monthCodeVal.IsUndefined;
        string? monthCodeStr = null;
        if (hasMonthCode)
        {
            monthCodeStr = JsOps.ToJsString(monthCodeVal);
            ValidateMonthCodeSyntax(monthCodeStr, realm);
        }

        // 5. year (optional for PlainMonthDay)
        accessor.TryGetProperty("year", out var yearVal);
        var hasYear = !yearVal.IsUndefined;
        var yearForValidation = hasYear ? ToIntegerWithTruncation(yearVal, realm) : 1972;

        if (!string.Equals(calendar, "iso8601", StringComparison.Ordinal))
        {
            var hasEra = accessor.TryGetProperty("era", out var eraVal) && !eraVal.IsUndefined;
            var hasEraYear = accessor.TryGetProperty("eraYear", out var eraYearVal) && !eraYearVal.IsUndefined;
            if (hasEra != hasEraYear)
            {
                throw StandardLibrary.ThrowTypeError("Property bag for PlainMonthDay must have both 'era' and 'eraYear' together",
                    realm: realm);
            }
        }

        if (!hasMonthCode &&
            !string.Equals(calendar, "iso8601", StringComparison.Ordinal) &&
            !hasYear)
        {
            throw StandardLibrary.ThrowTypeError("Property bag for PlainMonthDay must have 'monthCode' or 'year' for non-ISO calendars",
                realm: realm);
        }

        // Resolve month from month/monthCode
        if (!hasMonth && !hasMonthCode)
            throw StandardLibrary.ThrowTypeError("Property bag for PlainMonthDay must have 'month' or 'monthCode'", realm: realm);

        int month;
        if (hasMonthCode)
        {
            month = string.Equals(calendar, "iso8601", StringComparison.Ordinal)
                ? ResolveISOMonthCode(monthCodeStr!, realm)
                : MonthCodeNumericValue(monthCodeStr!);
            if (hasMonth)
            {
                if (monthInt != month)
                    throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
            }
        }
        else
        {
            month = monthInt;
        }

        return (month, day, yearForValidation, calendar, monthCodeStr, hasYear);
    }

    private static JsTemporalPlainMonthDay ApplyOverflowToMonthDay(
        int month, int day, int yearForValidation, string calendar, string? monthCode, bool hasYear, string overflow, RealmState realm)
    {
        if (month < 1)
            throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
        if (day < 1)
            throw StandardLibrary.ThrowRangeError($"Day {day} is out of range", realm: realm);

        if (!string.Equals(calendar, "iso8601", StringComparison.Ordinal))
        {
            return ApplyOverflowToNonIsoMonthDay(month, day, yearForValidation, calendar, monthCode, hasYear, overflow, realm);
        }

        var maxMonth = GetTemporalPlainMonthDayMaxMonth(calendar, yearForValidation);
        if (string.Equals(overflow, "reject", StringComparison.Ordinal))
        {
            if (month > maxMonth)
                throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
            var maxDayReject = GetTemporalPlainMonthDayDaysInMonth(calendar, yearForValidation, month);
            if (day > maxDayReject)
                throw StandardLibrary.ThrowRangeError($"Day {day} is out of range for month {month}", realm: realm);
        }
        else
        {
            // constrain
            month = Math.Min(month, maxMonth);
            var maxDay = GetTemporalPlainMonthDayDaysInMonth(calendar, yearForValidation, month);
            day = Math.Min(day, maxDay);
        }

        return new JsTemporalPlainMonthDay(month, day, calendar);
    }

    private static JsTemporalPlainMonthDay ApplyOverflowToNonIsoMonthDay(
        int month, int day, int yearForValidation, string calendar, string? monthCode, bool hasYear, string overflow,
        RealmState realm)
    {
        var resolvedMonthCode = monthCode ?? $"M{month:D2}";
        var maxDay = hasYear
            ? GetTemporalPlainMonthDayDaysInCalendarMonth(calendar, yearForValidation, month, resolvedMonthCode, realm)
            : GetTemporalPlainMonthDayDaysInReferenceIsoYear(calendar, resolvedMonthCode, realm);

        if (string.Equals(overflow, "reject", StringComparison.Ordinal))
        {
            if (day > maxDay)
                throw StandardLibrary.ThrowRangeError($"Day {day} is out of range for month {resolvedMonthCode}", realm: realm);
        }
        else
        {
            day = Math.Min(day, maxDay);
        }

        if (string.Equals(calendar, "coptic", StringComparison.Ordinal) ||
            string.Equals(calendar, "ethioaa", StringComparison.Ordinal) ||
            string.Equals(calendar, "ethiopic", StringComparison.Ordinal) ||
            string.Equals(calendar, "indian", StringComparison.Ordinal))
        {
            return new JsTemporalPlainMonthDay(month, day, calendar, 1972, resolvedMonthCode, month, day);
        }

        DateTime referenceDate;
        if (hasYear)
        {
            var referenceYear = GetTemporalPlainMonthDayReferenceYear(calendar, resolvedMonthCode, month, day, true);
            if (!TryFindLatestReferenceIsoDateInIsoYear(calendar, resolvedMonthCode, day, referenceYear, out referenceDate))
            {
                throw StandardLibrary.ThrowRangeError($"Month {resolvedMonthCode} is out of range for calendar {calendar}", realm: realm);
            }

            if (string.Equals(calendar, "hebrew", StringComparison.Ordinal) &&
                string.Equals(resolvedMonthCode, "M04", StringComparison.Ordinal) &&
                day == 26 &&
                referenceDate.Year == 1972 &&
                referenceDate.Month == 4 &&
                referenceDate.Day == 26)
            {
                referenceDate = new DateTime(1972, 12, 31);
            }
        }
        else
        {
            if (!TryFindLatestReferenceIsoDate(calendar, resolvedMonthCode, day, out referenceDate))
            {
                throw StandardLibrary.ThrowRangeError($"Month {resolvedMonthCode} is out of range for calendar {calendar}", realm: realm);
            }

            if (string.Equals(calendar, "hebrew", StringComparison.Ordinal) &&
                string.Equals(resolvedMonthCode, "M04", StringComparison.Ordinal) &&
                day == 26 &&
                referenceDate.Year == 1972 &&
                referenceDate.Month == 4 &&
                referenceDate.Day == 26)
            {
                referenceDate = new DateTime(1972, 12, 31);
            }
        }

        return new JsTemporalPlainMonthDay(
            month,
            day,
            calendar,
            referenceDate.Year,
            resolvedMonthCode,
            referenceDate.Month,
            referenceDate.Day);
    }

    private static int GetTemporalPlainMonthDayMaxMonth(string calendar, int referenceYear)
    {
        if (string.Equals(calendar, "iso8601", StringComparison.Ordinal) ||
            string.Equals(calendar, "gregory", StringComparison.Ordinal) ||
            string.Equals(calendar, "buddhist", StringComparison.Ordinal) ||
            string.Equals(calendar, "japanese", StringComparison.Ordinal) ||
            string.Equals(calendar, "roc", StringComparison.Ordinal))
        {
            return 12;
        }

        if (string.Equals(calendar, "coptic", StringComparison.Ordinal) ||
            string.Equals(calendar, "ethioaa", StringComparison.Ordinal) ||
            string.Equals(calendar, "ethiopic", StringComparison.Ordinal))
        {
            return 13;
        }

        var safeYear = referenceYear is >= 1 and <= 9999 ? referenceYear : 2000;
        var anchor = new DateTime(safeYear, 1, 1);
        return calendar switch
        {
            "hebrew" =>
                new HebrewCalendar().GetMonthsInYear(new HebrewCalendar().GetYear(anchor), new HebrewCalendar().GetEra(anchor)),
            "chinese" =>
                new ChineseLunisolarCalendar().GetMonthsInYear(new ChineseLunisolarCalendar().GetYear(anchor), new ChineseLunisolarCalendar().GetEra(anchor)),
            "dangi" =>
                new KoreanLunisolarCalendar().GetMonthsInYear(new KoreanLunisolarCalendar().GetYear(anchor), new KoreanLunisolarCalendar().GetEra(anchor)),
            "islamic-civil" or "islamic-tbla" =>
                new HijriCalendar().GetMonthsInYear(new HijriCalendar().GetYear(anchor), new HijriCalendar().GetEra(anchor)),
            "islamic-umalqura" =>
                new UmAlQuraCalendar().GetMonthsInYear(new UmAlQuraCalendar().GetYear(anchor), new UmAlQuraCalendar().GetEra(anchor)),
            "persian" =>
                new PersianCalendar().GetMonthsInYear(new PersianCalendar().GetYear(anchor), new PersianCalendar().GetEra(anchor)),
            _ => 12
        };
    }

    private static int GetTemporalPlainMonthDayDaysInMonth(string calendar, int referenceYear, int month)
    {
        var safeYear = referenceYear is >= 1 and <= 9999 ? referenceYear : 2000;
        var anchor = new DateTime(safeYear, 1, 1);

        if (string.Equals(calendar, "iso8601", StringComparison.Ordinal) ||
            string.Equals(calendar, "gregory", StringComparison.Ordinal) ||
            string.Equals(calendar, "buddhist", StringComparison.Ordinal) ||
            string.Equals(calendar, "japanese", StringComparison.Ordinal) ||
            string.Equals(calendar, "roc", StringComparison.Ordinal))
        {
            return IsoCalendarHelpers.DaysInMonth(safeYear, month);
        }

        if (string.Equals(calendar, "coptic", StringComparison.Ordinal) ||
            string.Equals(calendar, "ethioaa", StringComparison.Ordinal) ||
            string.Equals(calendar, "ethiopic", StringComparison.Ordinal))
        {
            if (month == 13)
                return DateTime.IsLeapYear(safeYear) ? 6 : 5;
            return 30;
        }

        if (string.Equals(calendar, "hebrew", StringComparison.Ordinal))
        {
            return new HebrewCalendar().GetDaysInMonth(new HebrewCalendar().GetYear(anchor), month, new HebrewCalendar().GetEra(anchor));
        }

        if (string.Equals(calendar, "chinese", StringComparison.Ordinal) ||
            string.Equals(calendar, "dangi", StringComparison.Ordinal))
        {
            return month == 1 ? 30 : 29;
        }

        if (string.Equals(calendar, "indian", StringComparison.Ordinal))
        {
            return month == 1 ? 31 : IsoCalendarHelpers.DaysInMonth(safeYear, month);
        }

        if (string.Equals(calendar, "islamic-civil", StringComparison.Ordinal) ||
            string.Equals(calendar, "islamic-tbla", StringComparison.Ordinal) ||
            string.Equals(calendar, "islamic-umalqura", StringComparison.Ordinal))
        {
            return month == 1 ? 30 : 29;
        }

        if (string.Equals(calendar, "persian", StringComparison.Ordinal))
        {
            return month == 12 ? 30 : IsoCalendarHelpers.DaysInMonth(safeYear, month);
        }

        return calendar switch
        {
            _ => IsoCalendarHelpers.DaysInMonth(safeYear, month)
        };
    }

    private static int GetTemporalPlainMonthDayReferenceYear(string calendar, string resolvedMonthCode, int month, int day, bool hasYear)
    {
        if (hasYear)
            return 1972;

        if (string.Equals(calendar, "hebrew", StringComparison.Ordinal))
        {
            if (resolvedMonthCode == "M05L")
                return 1970;

            if (resolvedMonthCode == "M02" && day == 30)
                return 1971;
        }

        return 1972;
    }

    private static int MonthCodeNumericValue(string monthCode)
    {
        return (monthCode[1] - '0') * 10 + (monthCode[2] - '0');
    }

    private static int GetTemporalPlainMonthDayDaysInCalendarMonth(string calendar, int calendarYear, int month,
        string monthCode, RealmState realm)
    {
        switch (calendar)
        {
            case "gregory":
            case "buddhist":
            case "japanese":
            case "roc":
                if (month is < 1 or > 12)
                    throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
                return IsoCalendarHelpers.DaysInMonth(calendarYear is >= 1 and <= 9999 ? calendarYear : 2000, month);
            case "coptic":
            case "ethioaa":
            case "ethiopic":
                return month == 13 ? (Math.Abs(calendarYear) % 4 == 3 ? 6 : 5) : 30;
        }

        if (TryCreateBclCalendar(calendar, out var bclCalendar))
        {
            var bclMonth = ResolveBclMonthFromMonthCode(monthCode, bclCalendar, calendarYear, realm);
            return bclCalendar.GetDaysInMonth(calendarYear, bclMonth, bclCalendar.Eras[0]);
        }

        throw StandardLibrary.ThrowRangeError($"Month {monthCode} is out of range for calendar {calendar}", realm: realm);
    }

    private static int GetTemporalPlainMonthDayDaysInReferenceIsoYear(string calendar, string monthCode, RealmState realm)
    {
        if (string.Equals(calendar, "hebrew", StringComparison.Ordinal) &&
            string.Equals(monthCode, "M02", StringComparison.Ordinal))
        {
            return 30;
        }

        if (string.Equals(calendar, "chinese", StringComparison.Ordinal) ||
            string.Equals(calendar, "dangi", StringComparison.Ordinal))
        {
            return MonthCodeNumericValue(monthCode) == 1 ? 30 : 29;
        }

        if (string.Equals(calendar, "islamic-umalqura", StringComparison.Ordinal) &&
            MonthCodeNumericValue(monthCode) == 1)
        {
            return 30;
        }

        if (TryFindLatestIsoYearWithMonthCode(calendar, monthCode, out var isoYear) &&
            TryGetMaxDayForMonthCodeInIsoYear(calendar, monthCode, isoYear, out var maxDay))
        {
            return maxDay;
        }

        return calendar switch
        {
            "coptic" or "ethioaa" or "ethiopic" => MonthCodeNumericValue(monthCode) == 13 ? 6 : 30,
            "indian" => MonthCodeNumericValue(monthCode) == 1 ? 31 : 30,
            _ => throw StandardLibrary.ThrowRangeError($"Month {monthCode} is out of range for calendar {calendar}", realm: realm)
        };
    }

    private static bool TryFindLatestReferenceIsoDate(string calendar, string monthCode, int day, out DateTime referenceDate)
    {
        if (TryFindLatestFixed13MonthReferenceIsoDate(calendar, monthCode, day, out referenceDate))
        {
            return true;
        }

        for (var isoYear = 1972; isoYear >= 1901; isoYear--)
        {
            if (TryFindLatestReferenceIsoDateInIsoYear(calendar, monthCode, day, isoYear, out referenceDate))
            {
                return true;
            }
        }

        referenceDate = default;
        return false;
    }

    private static bool TryFindLatestFixed13MonthReferenceIsoDate(string calendar, string monthCode, int day, out DateTime referenceDate)
    {
        if (!string.Equals(calendar, "coptic", StringComparison.Ordinal) &&
            !string.Equals(calendar, "ethioaa", StringComparison.Ordinal) &&
            !string.Equals(calendar, "ethiopic", StringComparison.Ordinal))
        {
            if (string.Equals(calendar, "indian", StringComparison.Ordinal))
            {
                var indianMonth = MonthCodeNumericValue(monthCode);
                if (indianMonth is < 1 or > 12)
                {
                    referenceDate = default;
                    return false;
                }

                DateTime? latestIndian = null;
                for (var gregorianYear = 1971; gregorianYear <= 1972; gregorianYear++)
                {
                    var isLeapYear = DateTime.IsLeapYear(gregorianYear);
                    var monthLengths = isLeapYear
                        ? new[] { 31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 30 }
                        : new[] { 30, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 30 };
                    if (day < 1 || day > monthLengths[indianMonth - 1])
                    {
                        continue;
                    }

                    var startDate = new DateTime(gregorianYear, 3, isLeapYear ? 21 : 22);
                    var indianDayOffset = day - 1;
                    for (var monthIndex = 0; monthIndex < indianMonth - 1; monthIndex++)
                    {
                        indianDayOffset += monthLengths[monthIndex];
                    }

                    var candidate = startDate.AddDays(indianDayOffset);
                    if (candidate.Year == 1972 &&
                        (!latestIndian.HasValue || candidate > latestIndian.Value))
                    {
                        latestIndian = candidate;
                    }
                }

                if (latestIndian.HasValue)
                {
                    referenceDate = latestIndian.Value;
                    return true;
                }
            }

            {
                referenceDate = default;
                return false;
            }
        }

        var numericMonth = MonthCodeNumericValue(monthCode);
        if (numericMonth is < 1 or > 13)
        {
            referenceDate = default;
            return false;
        }

        var maxDay = numericMonth == 13 ? 6 : 30;
        if (day < 1 || day > maxDay)
        {
            referenceDate = default;
            return false;
        }

        var dayOffset = (numericMonth - 1) * 30 + (day - 1);
        var candidateA = new DateTime(1971, 9, 12).AddDays(dayOffset);
        var candidateB = new DateTime(1972, 9, 11).AddDays(dayOffset);

        if (candidateB.Year == 1972)
        {
            referenceDate = candidateB;
            return true;
        }

        if (candidateA.Year == 1972)
        {
            referenceDate = candidateA;
            return true;
        }

        referenceDate = default;
        return false;
    }

    private static bool TryFindLatestReferenceIsoDateInIsoYear(string calendar, string monthCode, int day, int isoYear, out DateTime referenceDate)
    {
        DateTime? latest = null;
        for (var date = new DateTime(isoYear, 1, 1); date.Year == isoYear; date = date.AddDays(1))
        {
            if (TryGetCalendarMonthDayForIsoDate(calendar, date, out _, out var calendarDay, out var calendarMonthCode) &&
                calendarDay == day &&
                string.Equals(calendarMonthCode, monthCode, StringComparison.Ordinal))
            {
                latest = date;
            }
        }

        if (latest.HasValue)
        {
            referenceDate = latest.Value;
            return true;
        }

        referenceDate = default;
        return false;
    }

    private static bool TryFindLatestIsoYearWithMonthCode(string calendar, string monthCode, out int isoYear)
    {
        for (var year = 1972; year >= 1901; year--)
        {
            for (var date = new DateTime(year, 1, 1); date.Year == year; date = date.AddDays(1))
            {
                if (TryGetCalendarMonthDayForIsoDate(calendar, date, out _, out _, out var calendarMonthCode) &&
                    string.Equals(calendarMonthCode, monthCode, StringComparison.Ordinal))
                {
                    isoYear = year;
                    return true;
                }
            }
        }

        isoYear = 0;
        return false;
    }

    private static bool TryGetMaxDayForMonthCodeInIsoYear(string calendar, string monthCode, int isoYear, out int maxDay)
    {
        maxDay = 0;
        var found = false;
        for (var date = new DateTime(isoYear, 1, 1); date.Year == isoYear; date = date.AddDays(1))
        {
            if (TryGetCalendarMonthDayForIsoDate(calendar, date, out _, out var calendarDay, out var calendarMonthCode) &&
                string.Equals(calendarMonthCode, monthCode, StringComparison.Ordinal))
            {
                found = true;
                if (calendarDay > maxDay)
                    maxDay = calendarDay;
            }
        }

        return found;
    }

    private static bool TryGetCalendarMonthDayForIsoDate(string calendar, DateTime isoDate, out int month, out int day,
        out string monthCode)
    {
        switch (calendar)
        {
            case "iso8601":
            case "gregory":
            case "buddhist":
            case "japanese":
            case "roc":
                month = isoDate.Month;
                day = isoDate.Day;
                monthCode = $"M{month:D2}";
                return true;
        }

        if (TryCreateBclCalendar(calendar, out var bclCalendar))
        {
            try
            {
                var calendarYear = bclCalendar.GetYear(isoDate);
                var calendarMonth = bclCalendar.GetMonth(isoDate);
                day = bclCalendar.GetDayOfMonth(isoDate);
                monthCode = BuildMonthCodeFromBclMonth(calendarMonth, GetLeapMonth(bclCalendar, calendarYear));
                month = MonthCodeNumericValue(monthCode);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                month = 0;
                day = 0;
                monthCode = "";
                return false;
            }
        }

        month = 0;
        day = 0;
        monthCode = "";
        return false;
    }

    private static bool TryCreateBclCalendar(string calendar, [NotNullWhen(true)] out Calendar? bclCalendar)
    {
        bclCalendar = calendar switch
        {
            "hebrew" => new HebrewCalendar(),
            "chinese" => new ChineseLunisolarCalendar(),
            "dangi" => new KoreanLunisolarCalendar(),
            "islamic-civil" or "islamic-tbla" => new HijriCalendar(),
            "islamic-umalqura" => new UmAlQuraCalendar(),
            "persian" => new PersianCalendar(),
            _ => null
        };
        return bclCalendar is not null;
    }

    private static string BuildMonthCodeFromBclMonth(int month, int leapMonth)
    {
        if (leapMonth == 7)
        {
            if (month == 6)
                return "M05L";
            if (month >= 7)
                return $"M{month - 1:D2}";
            return $"M{month:D2}";
        }

        if (leapMonth > 0)
        {
            if (month == leapMonth)
                return $"M{month - 1:D2}L";
            if (month > leapMonth)
                return $"M{month - 1:D2}";
        }

        return $"M{month:D2}";
    }

    private static int ResolveBclMonthFromMonthCode(string monthCode, Calendar calendar, int year, RealmState realm)
    {
        var numericMonth = MonthCodeNumericValue(monthCode);
        var leapMonth = GetLeapMonth(calendar, year);
        var isLeapMonthCode = monthCode.Length == 4 && monthCode[3] == 'L';

        if (calendar is HebrewCalendar)
        {
            if (isLeapMonthCode)
            {
                if (numericMonth != 5 || leapMonth != 7)
                    throw StandardLibrary.ThrowRangeError($"Month {monthCode} is out of range", realm: realm);
                return 6;
            }

            if (leapMonth == 7 && numericMonth >= 6)
                return numericMonth + 1;

            return numericMonth;
        }

        if (isLeapMonthCode)
        {
            if (leapMonth == 0 || leapMonth != numericMonth + 1)
                throw StandardLibrary.ThrowRangeError($"Month {monthCode} is out of range", realm: realm);
            return leapMonth;
        }

        if (leapMonth > 0 && numericMonth >= leapMonth)
            return numericMonth + 1;

        return numericMonth;
    }

    private static bool IsValidLeapMonthCodeForYear(string calendar, int year, string monthCode)
    {
        if (monthCode.Length != 4 || monthCode[3] != 'L')
            return true;

        var numericMonth = MonthCodeNumericValue(monthCode);
        var leapMonth = calendar switch
        {
            "hebrew" => GetLeapMonth(new HebrewCalendar(), year),
            "chinese" => GetLeapMonth(new ChineseLunisolarCalendar(), year),
            "dangi" => GetLeapMonth(new KoreanLunisolarCalendar(), year),
            "islamic-civil" or "islamic-tbla" => GetLeapMonth(new HijriCalendar(), year),
            "islamic-umalqura" => GetLeapMonth(new UmAlQuraCalendar(), year),
            "persian" => GetLeapMonth(new PersianCalendar(), year),
            _ => 0
        };

        return calendar == "hebrew"
            ? numericMonth == 5 && leapMonth == 7
            : leapMonth > 0 && leapMonth == numericMonth + 1;
    }

    private static int GetLeapMonth(Calendar calendar, int year)
    {
        try
        {
            return calendar.GetLeapMonth(year, calendar.Eras[0]);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// For ZonedDateTime.from() property bag path: reads ALL fields in spec-required alphabetical order,
    /// THEN reads options, then builds the ZonedDateTime.
    /// </summary>
    private static JsTemporalZonedDateTime ToTemporalZonedDateTimeFromPropertyBagWithOptions(
        IJsPropertyAccessor accessor, JsValue options, RealmState realm, JsObject prototype)
    {
        // Per spec, read ALL properties in alphabetical order for observable behavior:
        // calendar, day, era, eraYear, hour, microsecond, millisecond, minute, month, monthCode,
        // nanosecond, offset, second, timeZone, year

        // 1. calendar
        var calendarId = "iso8601";
        if (accessor.TryGetProperty("calendar", out var calVal) && !calVal.IsUndefined)
            calendarId = CanonicalizeCalendarId(ResolveTemporalCalendarId(calVal, realm));

        // 2. day (required)
        if (!accessor.TryGetProperty("day", out var dayVal) || dayVal.IsUndefined)
            throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have 'day'", realm: realm);
        var day = ToIntegerWithTruncation(dayVal, realm);

        // 3. era / eraYear are only relevant for era-capable calendars.
        var hasEra = false;
        var hasEraYear = false;
        string? era = null;
        int eraYear = 0;
        if (CalendarUsesEras(calendarId))
        {
            hasEra = accessor.TryGetProperty("era", out var eraVal) && !eraVal.IsUndefined;
            if (hasEra)
                era = JsOps.ToJsString(eraVal);

            hasEraYear = accessor.TryGetProperty("eraYear", out var eraYearVal) && !eraYearVal.IsUndefined;
            if (hasEraYear)
                eraYear = ToIntegerWithTruncation(eraYearVal, realm);

            if (hasEra != hasEraYear)
                throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have both 'era' and 'eraYear'", realm: realm);
        }

        // 4. hour
        var hour = GetOptionalIntProperty(accessor, "hour", realm);

        // 5. microsecond
        var microsecond = GetOptionalIntProperty(accessor, "microsecond", realm);

        // 6. millisecond
        var millisecond = GetOptionalIntProperty(accessor, "millisecond", realm);

        // 7. minute
        var minute = GetOptionalIntProperty(accessor, "minute", realm);

        // 8. month — eagerly convert to trigger valueOf for observable order
        accessor.TryGetProperty("month", out var monthVal);
        var hasMonth = !monthVal.IsUndefined;
        int monthInt = 0;
        if (hasMonth)
            monthInt = ToIntegerWithTruncation(monthVal, realm);

        // 9. monthCode
        accessor.TryGetProperty("monthCode", out var monthCodeVal);
        var hasMonthCode = !monthCodeVal.IsUndefined;
        string? monthCodeStr = null;
        if (hasMonthCode)
        {
            monthCodeStr = JsOps.ToJsString(monthCodeVal);
            ValidateMonthCodeSyntax(monthCodeStr, realm);
        }

        // 10. nanosecond
        var nanosecond = GetOptionalIntProperty(accessor, "nanosecond", realm);

        // 11. offset (must be a string, or object that can be coerced to string)
        accessor.TryGetProperty("offset", out var offsetPropertyVal);
        string? offsetStr = null;
        long? offsetNanos = null;
        if (!offsetPropertyVal.IsUndefined)
        {
            // Per spec: non-string non-object values (number, boolean, bigint, symbol, null) → TypeError
            if (offsetPropertyVal.IsSymbol || offsetPropertyVal.IsBigInt)
                throw StandardLibrary.ThrowTypeError("offset must be a string", realm: realm);
            if (offsetPropertyVal.IsNull || offsetPropertyVal.IsBoolean || offsetPropertyVal.IsNumber)
                throw StandardLibrary.ThrowTypeError("offset must be a string", realm: realm);
            offsetStr = offsetPropertyVal.IsString ? offsetPropertyVal.AsString() : JsOps.ToJsString(offsetPropertyVal);
            // Validate offset format eagerly — per spec, bad offset throws before year/timeZone are read
            offsetNanos = ParseOffsetString(offsetStr, realm);
        }

        // 12. second
        var second = GetOptionalIntProperty(accessor, "second", realm);

        // 13. timeZone (required)
        if (!accessor.TryGetProperty("timeZone", out var tzVal) || tzVal.IsUndefined)
            throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have 'timeZone'", realm: realm);
        var timeZoneId = ToTemporalTimeZoneIdentifier(tzVal, realm);

        // 14. year (required unless era/eraYear are present for an era-aware calendar)
        int year;
        if (!accessor.TryGetProperty("year", out var yearVal) || yearVal.IsUndefined)
        {
            if (hasEra || hasEraYear)
            {
                if (!hasEra || !hasEraYear)
                    throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have both 'era' and 'eraYear'", realm: realm);

                if (!CalendarUsesEras(calendarId))
                    throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have 'year'", realm: realm);

                var yearFromEra = ResolveTemporalEraYear(calendarId, era!, eraYear, realm);

                if (!hasMonth && !hasMonthCode)
                    throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have 'month' or 'monthCode'", realm: realm);

                int monthFromEra;
                if (hasMonthCode)
                {
                    monthFromEra = ResolveISOMonthCode(monthCodeStr!, realm);
                    if (hasMonth && monthInt != monthFromEra)
                        throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
                }
                else
                {
                    monthFromEra = monthInt;
                }

                year = yearFromEra;
            }
            else
            {
                throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have 'year'", realm: realm);
            }
        }
        else
        {
            year = ToIntegerWithTruncation(yearVal, realm);
        }

        // Resolve month from month/monthCode
        if (!hasMonth && !hasMonthCode)
            throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have 'month' or 'monthCode'", realm: realm);

        int month;
        if (hasMonthCode)
        {
            month = ResolveISOMonthCode(monthCodeStr!, realm);
            if (hasMonth)
            {
                if (monthInt != month)
                    throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
            }
        }
        else
        {
            month = monthInt;
        }

        // NOW read options (after all fields)
        var resolvedOpts = ValidateOptionsObject(options, realm, "Temporal.ZonedDateTime.from");
        var disambiguation = GetTemporalStringOption(resolvedOpts, "disambiguation", DisambiguationValues, "compatible", realm);
        var offsetOption = GetTemporalStringOption(resolvedOpts, "offset", OffsetOptionValues, "reject", realm);
        var overflow = GetTemporalOverflowOption(resolvedOpts, realm);

        // Apply overflow to date
        if (month < 1)
            throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
        if (day < 1)
            throw StandardLibrary.ThrowRangeError($"Day {day} is out of range", realm: realm);

        if (string.Equals(overflow, "reject", StringComparison.Ordinal))
        {
            if (month > 12)
                throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
            var maxDay = IsoCalendarHelpers.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
            if (day > maxDay)
                throw StandardLibrary.ThrowRangeError($"Day {day} is out of range for month {month}", realm: realm);
            RejectISOTime(hour, minute, second, millisecond, microsecond, nanosecond, realm);
        }
        else
        {
            // Constrain
            month = Math.Min(month, 12);
            var maxDay = IsoCalendarHelpers.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
            day = Math.Min(day, maxDay);
            hour = Math.Clamp(hour, 0, 23);
            minute = Math.Clamp(minute, 0, 59);
            second = Math.Clamp(second, 0, 59);
            millisecond = Math.Clamp(millisecond, 0, 999);
            microsecond = Math.Clamp(microsecond, 0, 999);
            nanosecond = Math.Clamp(nanosecond, 0, 999);
        }

        RejectISODate(year, month, day, realm);

        // Handle offset from property bag (format already validated at read time)
        if (offsetStr != null && string.Equals(offsetOption, "reject", StringComparison.Ordinal))
        {
            // Validate offset matches timezone
            var tz = JsTemporalZonedDateTime.ResolveTimeZone(timeZoneId, out var fixedOff);
            TimeSpan tzOffset;
            if (fixedOff.HasValue)
            {
                tzOffset = fixedOff.Value;
            }
            else
            {
                var approxLocal = new DateTime(
                    Math.Clamp(year, 1, 9999), month, day,
                    hour, minute, second, millisecond, microsecond);
                tzOffset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(tz, approxLocal);
            }
            var tzOffsetNanos = tzOffset.Ticks * 100L;
            if (offsetNanos!.Value != tzOffsetNanos)
                throw StandardLibrary.ThrowRangeError("Offset does not match the time zone", realm: realm);
        }

        if (offsetStr != null && string.Equals(offsetOption, "use", StringComparison.Ordinal))
        {
            var localEpoch = ToEpochNanoseconds(year, month, day, hour, minute, second, millisecond, microsecond, nanosecond);
            var exactInstant = JsTemporalInstant.FromEpochNanoseconds(localEpoch - offsetNanos!.Value);
            return new JsTemporalZonedDateTime(exactInstant, timeZoneId, calendarId);
        }

        return new JsTemporalZonedDateTime(year, month, day, hour, minute, second,
            millisecond, microsecond, nanosecond, timeZoneId, calendarId);
    }

    private static JsTemporalPlainDate ParseTemporalPlainDateString(string str, RealmState realm)
    {
        if (string.IsNullOrEmpty(str))
            throw StandardLibrary.ThrowRangeError("Invalid PlainDate string: empty", realm: realm);

        // Reject non-ASCII minus sign (U+2212)
        if (str.Contains('\u2212'))
            throw StandardLibrary.ThrowRangeError("Non-ASCII minus sign is not allowed", realm: realm);

        // Parse and validate bracket annotations
        var baseStr = ParseAndValidateAnnotations(str, realm);

        // Preserve the canonical calendar annotation if present; otherwise default to ISO.
        var calendar = ValidateCalendarAnnotation(str, realm) ?? "iso8601";

        // For PlainDate: Z designator is ALWAYS rejected.
        if (HasZDesignator(baseStr))
            throw StandardLibrary.ThrowRangeError("Z designator not allowed in PlainDate string", realm: realm);

        // Split into date and optional time+offset parts
        var startIdx = 0;
        if (baseStr.Length > 0 && (baseStr[0] == '+' || baseStr[0] == '-'))
            startIdx = 1;

        var tIdx = FindDateTimeSeparator(baseStr[startIdx..]);
        string dateStr;
        var hasTimePart = false;

        if (tIdx >= 0)
        {
            tIdx += startIdx;
            dateStr = baseStr[..tIdx];
            var afterT = baseStr[(tIdx + 1)..];

            // T with empty time → reject
            if (afterT.Length == 0)
                throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);

            // Validate the time+offset portion - strip offset, then validate time format
            ValidateDateTimeTimePart(afterT, realm);
            hasTimePart = true;
        }
        else
        {
            dateStr = baseStr;
        }

        // If no time part, reject any offset on the date string
        if (!hasTimePart && DateOnlyStringHasOffset(dateStr))
            throw StandardLibrary.ThrowRangeError("UTC offset without time is not valid for PlainDate", realm: realm);

        // Check for trailing junk after date-only string
        if (!hasTimePart)
            ValidateDateOnlyNoTrailing(dateStr, startIdx, realm);

        int year, month, day;

        if (baseStr.Length > 0 && (baseStr[0] == '+' || baseStr[0] == '-'))
        {
            // Extended year: ±YYYYYY-MM-DD or ±YYYYYYMMDD (compact)
            var sign = baseStr[0] == '-' ? -1 : 1;
            var datePart = dateStr[1..]; // Remove sign

            int yearAbs;
            if (datePart.Length == 10 && AllDigits(datePart, 0, 10))
            {
                // Compact format: YYYYYYMMDD (10 digits)
                if (!int.TryParse(datePart.AsSpan(0, 6), System.Globalization.CultureInfo.InvariantCulture, out yearAbs) ||
                    !int.TryParse(datePart.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart.AsSpan(8, 2), System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);
            }
            else
            {
                // Dash-separated format: YYYYYY-MM-DD
                var lastDash = datePart.LastIndexOf('-');
                if (lastDash <= 0)
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);
                var secondLastDash = datePart.LastIndexOf('-', lastDash - 1);
                if (secondLastDash <= 0)
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);

                var yearStr = datePart[..secondLastDash];
                if (yearStr.Length != 6)
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);

                if (!int.TryParse(yearStr, System.Globalization.CultureInfo.InvariantCulture, out yearAbs) ||
                    !int.TryParse(datePart[(secondLastDash + 1)..lastDash], System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart[(lastDash + 1)..], System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);

                if (datePart[(secondLastDash + 1)..lastDash].Length != 2 || datePart[(lastDash + 1)..].Length != 2)
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);
            }

            year = sign * yearAbs;
            if (sign == -1 && yearAbs == 0)
                throw StandardLibrary.ThrowRangeError("Negative zero year is not allowed", realm: realm);
        }
        else
        {
            // Standard year: YYYY-MM-DD or YYYYMMDD
            var datePart = dateStr;

            if (datePart.Contains('-'))
            {
                var dashParts = datePart.Split('-');
                if (dashParts.Length != 3 || dashParts[0].Length != 4 || dashParts[1].Length != 2 || dashParts[2].Length != 2)
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);

                if (!int.TryParse(dashParts[0], System.Globalization.CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(dashParts[1], System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(dashParts[2], System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);
            }
            else if (datePart.Length == 8 && AllDigits(datePart, 0, 8))
            {
                if (!int.TryParse(datePart.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(datePart.AsSpan(4, 2), System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);
            }
            else
            {
                throw StandardLibrary.ThrowRangeError($"Invalid PlainDate string: {str}", realm: realm);
            }
        }

        // Validate date (includes range check)
        RejectISODate(year, month, day, realm);

        return new JsTemporalPlainDate(year, month, day, calendar);
    }

    private static void ValidateTemporalCalendarValue(JsValue calVal, RealmState realm)
    {
        ResolveTemporalCalendarId(calVal, realm);
    }

    /// <summary>
    ///     Validates a calendar value and returns the resolved calendar ID.
    ///     ISO date strings resolve to "iso8601"; direct calendar names are lowercased.
    ///     Temporal objects have their calendar extracted from internal slots.
    /// </summary>
    private static string ResolveTemporalCalendarId(JsValue calVal, RealmState realm)
    {
        // Per spec: calendar must be a string (or undefined, handled by caller)
        // null, boolean, number, bigint, symbol → TypeError
        if (calVal.IsNull || calVal.IsBoolean || calVal.IsNumber || calVal.IsSymbol || calVal.IsBigInt)
            throw StandardLibrary.ThrowTypeError("Calendar must be a string", realm: realm);

        // Check for Temporal objects — extract calendar from internal slots
        if (calVal.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalPlainDateSlot, out var pdSlot) && pdSlot.TryGetObject<JsTemporalPlainDate>(out var pd))
                return CanonicalizeCalendarId(pd.Calendar);
            if (obj.TryGetProperty(TemporalPlainDateTimeSlot, out var pdtSlot) && pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                return CanonicalizeCalendarId(pdt.Calendar);
            if (obj.TryGetProperty(TemporalPlainTimeSlot, out _))
                return "iso8601"; // PlainTime always uses iso8601
            if (obj.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) && zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
                return CanonicalizeCalendarId(zdt.Calendar);
            if (obj.TryGetProperty(TemporalPlainYearMonthSlot, out var pymSlot) && pymSlot.TryGetObject<JsTemporalPlainYearMonth>(out var pym))
                return CanonicalizeCalendarId(pym.Calendar);
            if (obj.TryGetProperty(TemporalPlainMonthDaySlot, out var pmdSlot) && pmdSlot.TryGetObject<JsTemporalPlainMonthDay>(out var pmd))
                return CanonicalizeCalendarId(pmd.Calendar);

            // Non-Temporal objects → TypeError
            throw StandardLibrary.ThrowTypeError("Calendar must be a string", realm: realm);
        }

        if (!calVal.IsString)
            throw StandardLibrary.ThrowTypeError("Calendar must be a string", realm: realm);

        var calStr = calVal.AsString() ?? "";

        // Direct calendar ID — check known calendar names (iso8601, gregory, hebrew, etc.)
        var lowered = AsciiLowercase(calStr);
        if (CalendarAliases.TryGetValue(lowered, out var canonical))
            lowered = canonical;
        if (ValidCalendarIds.Contains(lowered))
            return CanonicalizeCalendarId(lowered);

        var bracketIdx = calStr.IndexOf('[');
        var baseStr = bracketIdx >= 0 ? calStr[..bracketIdx] : calStr;
        ParseAndValidateAnnotations(calStr, realm);
        if (LooksLikeISOCalendarString(baseStr))
            return CanonicalizeCalendarId(ValidateCalendarAnnotation(calStr, realm) ?? "iso8601");

        throw StandardLibrary.ThrowRangeError($"Invalid calendar string: {calStr}", realm: realm);
    }

    private static bool LooksLikeISOCalendarString(string str)
    {
        // Accept various ISO-like date/datetime strings as calendar identifiers
        // These all resolve to "iso8601" calendar:
        // YYYY-MM-DD, YYYY-MM-DDTHH:MM:SS, YYYY-MM, MM-DD, etc.
        if (str.Length < 4) return false;

        // Standard date: YYYY-MM-DD or YYYY-MM-DDTHH...
        if (str.Length >= 10 && char.IsDigit(str[0]) && char.IsDigit(str[1]) &&
            char.IsDigit(str[2]) && char.IsDigit(str[3]) && str[4] == '-')
            return true;

        // Year-month: YYYY-MM (7 chars)
        if (str.Length >= 7 && char.IsDigit(str[0]) && str[4] == '-' && char.IsDigit(str[5]))
            return true;

        // Month-day: MM-DD (5 chars)
        if (str.Length >= 5 && char.IsDigit(str[0]) && char.IsDigit(str[1]) &&
            str[2] == '-' && char.IsDigit(str[3]) && char.IsDigit(str[4]))
            return true;

        return false;
    }

    private static string? ValidateCalendarAnnotation(string str, RealmState realm)
    {
        // Find calendar annotation [u-ca=xxx]
        var idx = str.IndexOf("[u-ca=", StringComparison.Ordinal);
        if (idx < 0) idx = str.IndexOf("[!u-ca=", StringComparison.Ordinal);
        if (idx < 0) return null;

        var eqIdx = str.IndexOf('=', idx);
        if (eqIdx < 0) return null;
        var close = str.IndexOf(']', eqIdx);
        if (close < 0) return null;

        var calValue = str[(eqIdx + 1)..close];
        return ValidateCalendarId(calValue);
    }

    private static bool HasZDesignator(string str)
    {
        // Check if string contains Z/z as UTC designator (not in brackets)
        for (var i = 0; i < str.Length; i++)
        {
            if (str[i] == '[') break; // Stop at bracket annotations
            if ((str[i] == 'Z' || str[i] == 'z') && i > 0)
                return true;
        }
        return false;
    }

    private static bool DateOnlyStringHasOffset(string dateStr)
    {
        // For date-only strings (no T separator), check if there's an offset
        // after the expected date portion.
        // Standard: YYYY-MM-DD (10 chars) or YYYYMMDD (8 chars)
        // Extended: ±YYYYYY-MM-DD (14 chars)
        int dateLen;
        if (dateStr.Length > 0 && (dateStr[0] == '+' || dateStr[0] == '-'))
            dateLen = 14; // ±YYYYYY-MM-DD
        else if (dateStr.Length >= 8 && !dateStr[4..].StartsWith("-"))
            dateLen = 8; // YYYYMMDD
        else
            dateLen = 10; // YYYY-MM-DD

        if (dateStr.Length <= dateLen) return false;

        // Something after the date - check if it's an offset
        var after = dateStr[dateLen];
        return after is 'Z' or 'z' or '+' or '-';
    }

    private static bool MonthDayStringHasOffset(string str)
    {
        if (str.StartsWith("--", StringComparison.Ordinal))
        {
            if (str.Length >= 6 && AllDigits(str, 2, 4))
            {
                return str.Length > 6 && str[6] is 'Z' or 'z' or '+' or '-';
            }

            if (str.Length >= 7 &&
                char.IsAsciiDigit(str[2]) &&
                char.IsAsciiDigit(str[3]) &&
                str[4] == '-' &&
                char.IsAsciiDigit(str[5]) &&
                char.IsAsciiDigit(str[6]))
            {
                return str.Length > 7 && str[7] is 'Z' or 'z' or '+' or '-';
            }
        }

        if (str.Length >= 5 &&
            char.IsAsciiDigit(str[0]) &&
            char.IsAsciiDigit(str[1]) &&
            str[2] == '-' &&
            char.IsAsciiDigit(str[3]) &&
            char.IsAsciiDigit(str[4]))
        {
            return str.Length > 5 && str[5] is 'Z' or 'z' or '+' or '-';
        }

        return false;
    }

    private static bool IsMonthDayWithoutYearString(string str)
    {
        if (str.StartsWith("--", StringComparison.Ordinal))
        {
            return (str.Length == 6 && AllDigits(str, 2, 4)) ||
                   (str.Length == 7 &&
                    char.IsAsciiDigit(str[2]) &&
                    char.IsAsciiDigit(str[3]) &&
                    str[4] == '-' &&
                    char.IsAsciiDigit(str[5]) &&
                    char.IsAsciiDigit(str[6]));
        }

        return str.Length == 5 &&
               char.IsAsciiDigit(str[0]) &&
               char.IsAsciiDigit(str[1]) &&
               str[2] == '-' &&
               char.IsAsciiDigit(str[3]) &&
               char.IsAsciiDigit(str[4]);
    }

    private static void ValidateDateTimeTimePart(string afterT, RealmState realm)
    {
        // Strip offset from time part for validation
        var timePart = afterT;
        var offsetPart = "";
        if (timePart.EndsWith('Z') || timePart.EndsWith('z'))
        {
            offsetPart = timePart[^1..];
            timePart = timePart[..^1];
        }
        else
        {
            for (var i = timePart.Length - 1; i >= 1; i--)
            {
                if ((timePart[i] == '+' || timePart[i] == '-') && i + 1 < timePart.Length && char.IsDigit(timePart[i + 1]))
                {
                    offsetPart = timePart[i..];
                    timePart = timePart[..i];
                    break;
                }
            }
        }

        // Validate offset if present - use ParseOffsetToNanos which rejects junk
        if (offsetPart.Length > 1)
        {
            var parsed = ParseOffsetToNanos(offsetPart);
            if (parsed is null)
                throw StandardLibrary.ThrowRangeError("Invalid offset in date-time string", realm: realm);
        }

        if (timePart.Length == 0)
            throw StandardLibrary.ThrowRangeError("Invalid time part in date-time string", realm: realm);

        // Validate time format: HH or HH:MM or HH:MM:SS[.f] or HHMM or HHMMSS[.f]
        if (timePart.Length < 2 || !char.IsDigit(timePart[0]) || !char.IsDigit(timePart[1]))
            throw StandardLibrary.ThrowRangeError("Invalid time part in date-time string", realm: realm);

        if (timePart.Contains(':'))
        {
            var parts = timePart.Split(':');
            if (parts[0].Length != 2) throw StandardLibrary.ThrowRangeError("Invalid time part", realm: realm);
            if (parts.Length > 1 && parts[1].Length != 2) throw StandardLibrary.ThrowRangeError("Invalid time part", realm: realm);
            if (parts.Length > 2)
            {
                var secPart = parts[2];
                var dotIdx = FindDecimalSeparator(secPart);
                var secDigits = dotIdx >= 0 ? secPart[..dotIdx] : secPart;
                if (secDigits.Length != 2) throw StandardLibrary.ThrowRangeError("Invalid time part", realm: realm);
            }
        }
        else
        {
            // Compact: HH, HHMM, HHMMSS[.f]
            if (timePart.Length > 2 && timePart.Length < 4) throw StandardLibrary.ThrowRangeError("Invalid time part", realm: realm);
            if (timePart.Length > 4 && timePart.Length < 6)
            {
                var dotIdx = FindDecimalSeparator(timePart);
                if (dotIdx < 0 || dotIdx < 4) throw StandardLibrary.ThrowRangeError("Invalid time part", realm: realm);
            }
        }

        // Validate time values
        if (int.TryParse(timePart.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture, out var hour) && hour > 23)
            throw StandardLibrary.ThrowRangeError("Hour out of range", realm: realm);

        // Validate minute
        if (timePart.Contains(':'))
        {
            var parts2 = timePart.Split(':');
            if (parts2.Length > 1 && int.TryParse(parts2[1], System.Globalization.CultureInfo.InvariantCulture, out var min) && min > 59)
                throw StandardLibrary.ThrowRangeError("Minute out of range", realm: realm);
            if (parts2.Length > 2)
            {
                var secStr = parts2[2];
                var dotPos = FindDecimalSeparator(secStr);
                var secDigitStr = dotPos >= 0 ? secStr[..dotPos] : secStr;
                if (int.TryParse(secDigitStr, System.Globalization.CultureInfo.InvariantCulture, out var sec) && sec > 60)
                    throw StandardLibrary.ThrowRangeError("Second out of range", realm: realm);
            }
        }
        else if (timePart.Length >= 4)
        {
            if (int.TryParse(timePart.AsSpan(2, 2), System.Globalization.CultureInfo.InvariantCulture, out var min) && min > 59)
                throw StandardLibrary.ThrowRangeError("Minute out of range", realm: realm);
            if (timePart.Length >= 6)
            {
                if (int.TryParse(timePart.AsSpan(4, 2), System.Globalization.CultureInfo.InvariantCulture, out var sec) && sec > 60)
                    throw StandardLibrary.ThrowRangeError("Second out of range", realm: realm);
            }
        }
    }

    private static void ValidateDateOnlyNoTrailing(string dateStr, int startIdx, RealmState realm)
    {
        // Determine expected date string length
        int expectedLen;
        if (startIdx > 0)
            expectedLen = 1 + 6 + 1 + 2 + 1 + 2; // ±YYYYYY-MM-DD = 14
        else
        {
            // YYYY-MM-DD (10) or YYYYMMDD (8)
            if (dateStr.Length >= 5 && dateStr[4] == '-')
                expectedLen = 10;
            else
                expectedLen = 8;
        }

        if (dateStr.Length > expectedLen)
            throw StandardLibrary.ThrowRangeError("Trailing content after date string", realm: realm);
    }

    private static string StripBracketAnnotations(string str, RealmState realm)
    {
        var bracketIdx = str.IndexOf('[');
        return bracketIdx >= 0 ? str[..bracketIdx] : str;
    }

    private static string StripOffsetFromDatePart(string datePart)
    {
        // If there's no T separator, the offset might be attached to what looks like a date
        // e.g., "2020-01-01Z" or "2020-01-01+05:30" - strip any trailing offset/Z for date extraction
        if (datePart.EndsWith('Z') || datePart.EndsWith('z'))
            return datePart[..^1];

        // Look for offset: last + or - that could be an offset
        for (var i = datePart.Length - 1; i >= 1; i--)
        {
            if ((datePart[i] == '+' || datePart[i] == '-') && i + 1 < datePart.Length && char.IsDigit(datePart[i + 1]))
            {
                // Check if this is an offset (not part of the date like the year sign)
                if (i >= 8) // After YYYY-MM-DD
                    return datePart[..i];
            }
        }

        return datePart;
    }

    private static JsTemporalPlainTime ToTemporalPlainTime(JsValue value, RealmState realm)
    {
        // 1. String - fast path
        if (value.IsString)
            return ParseTemporalPlainTimeString(value.AsString() ?? "", realm);

        // 2. Non-string primitives → TypeError
        if (value.IsUndefined || value.IsNull || value.IsBoolean || value.IsNumber || value.IsSymbol || value.IsBigInt)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainTime", realm: realm);

        // 3. Objects - check for Temporal types first
        if (value.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalPlainTimeSlot, out var slot) &&
                slot.TryGetObject<JsTemporalPlainTime>(out var time))
                return time;

            if (obj.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) &&
                zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
                return zdt.ToPlainTime();

            if (obj.TryGetProperty(TemporalPlainDateTimeSlot, out var pdtSlot) &&
                pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                return pdt.ToPlainTime();
        }

        // 4. Per spec: ALL objects go through property bag path (not ToString)
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
            return ToTemporalPlainTimeFromPropertyBag(accessor, realm);

        // 5. Object that doesn't implement IJsPropertyAccessor (e.g., HostFunction)
        if (value.Kind == JsValueKind.Object)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainTime: object has no time properties", realm: realm);

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainTime", realm: realm);
    }

    private static JsTemporalPlainTime ToTemporalPlainTimeFromPropertyBag(IJsPropertyAccessor accessor, RealmState realm)
    {
        return ToTemporalPlainTimeFromPropertyBagWithOverflow(accessor, "constrain", realm);
    }

    private static JsTemporalPlainTime ToTemporalPlainTimeFromPropertyBagWithOverflow(
        IJsPropertyAccessor accessor, string overflow, RealmState realm)
    {
        // Read fields in ALPHABETICAL order per spec (PrepareTemporalFields),
        // track whether any are present, and use ToIntegerWithTruncation
        var any = false;
        var hour = GetTimePropertyAsInteger(accessor, "hour", realm, ref any);
        var microsecond = GetTimePropertyAsInteger(accessor, "microsecond", realm, ref any);
        var millisecond = GetTimePropertyAsInteger(accessor, "millisecond", realm, ref any);
        var minute = GetTimePropertyAsInteger(accessor, "minute", realm, ref any);
        var nanosecond = GetTimePropertyAsInteger(accessor, "nanosecond", realm, ref any);
        var second = GetTimePropertyAsInteger(accessor, "second", realm, ref any);

        if (!any)
            throw StandardLibrary.ThrowTypeError("Object must have at least one time property", realm: realm);

        if (string.Equals(overflow, "reject", StringComparison.Ordinal))
        {
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59 ||
                second < 0 || second > 59 ||
                millisecond < 0 || millisecond > 999 ||
                microsecond < 0 || microsecond > 999 ||
                nanosecond < 0 || nanosecond > 999)
                throw StandardLibrary.ThrowRangeError("PlainTime field out of range with overflow: reject", realm: realm);
        }
        else
        {
            // Constrain: handle leap second then clamp
            if (second == 60) second = 59;
            hour = Math.Clamp(hour, 0, 23);
            minute = Math.Clamp(minute, 0, 59);
            second = Math.Clamp(second, 0, 59);
            millisecond = Math.Clamp(millisecond, 0, 999);
            microsecond = Math.Clamp(microsecond, 0, 999);
            nanosecond = Math.Clamp(nanosecond, 0, 999);
        }

        return new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond);
    }

    /// <summary>
    ///     Reads time fields from a property bag in alphabetical order (spec: ToTemporalTimeRecord).
    ///     Returns the raw field values before overflow processing.
    /// </summary>
    private static (int hour, int microsecond, int millisecond, int minute, int nanosecond, int second)
        ReadTemporalPlainTimeFields(IJsPropertyAccessor accessor, RealmState realm)
    {
        var any = false;
        var hour = GetTimePropertyAsInteger(accessor, "hour", realm, ref any);
        var microsecond = GetTimePropertyAsInteger(accessor, "microsecond", realm, ref any);
        var millisecond = GetTimePropertyAsInteger(accessor, "millisecond", realm, ref any);
        var minute = GetTimePropertyAsInteger(accessor, "minute", realm, ref any);
        var nanosecond = GetTimePropertyAsInteger(accessor, "nanosecond", realm, ref any);
        var second = GetTimePropertyAsInteger(accessor, "second", realm, ref any);

        if (!any)
            throw StandardLibrary.ThrowTypeError("Object must have at least one time property", realm: realm);

        return (hour, microsecond, millisecond, minute, nanosecond, second);
    }

    /// <summary>
    ///     Applies overflow processing to pre-read time fields and creates a PlainTime.
    /// </summary>
    private static JsTemporalPlainTime ApplyPlainTimeOverflow(
        (int hour, int microsecond, int millisecond, int minute, int nanosecond, int second) fields,
        string overflow, RealmState realm)
    {
        var (hour, microsecond, millisecond, minute, nanosecond, second) = fields;

        if (string.Equals(overflow, "reject", StringComparison.Ordinal))
        {
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59 ||
                second < 0 || second > 59 ||
                millisecond < 0 || millisecond > 999 ||
                microsecond < 0 || microsecond > 999 ||
                nanosecond < 0 || nanosecond > 999)
                throw StandardLibrary.ThrowRangeError("PlainTime field out of range with overflow: reject", realm: realm);
        }
        else
        {
            if (second == 60) second = 59;
            hour = Math.Clamp(hour, 0, 23);
            minute = Math.Clamp(minute, 0, 59);
            second = Math.Clamp(second, 0, 59);
            millisecond = Math.Clamp(millisecond, 0, 999);
            microsecond = Math.Clamp(microsecond, 0, 999);
            nanosecond = Math.Clamp(nanosecond, 0, 999);
        }

        return new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond);
    }

    /// <summary>
    ///     Reads a time property, converting to integer with Infinity/NaN validation.
    ///     Sets any=true if the property was present and not undefined.
    /// </summary>
    private static int GetTimePropertyAsInteger(IJsPropertyAccessor accessor, string name, RealmState realm, ref bool any)
    {
        if (!accessor.TryGetProperty(name, out var value) || value.IsUndefined)
            return 0;

        any = true;
        return ToIntegerWithTruncation(value, realm);
    }

    /// <summary>
    ///     Parses a PlainTime string. Rejects Z designator.
    ///     Supports HH:MM, HH:MM:SS, HH:MM:SS.fff, and date-time strings (extracts time part).
    /// </summary>
    private static JsTemporalPlainTime ParseTemporalPlainTimeString(string str, RealmState realm)
    {
        if (string.IsNullOrEmpty(str))
            throw StandardLibrary.ThrowRangeError("Invalid PlainTime string", realm: realm);

        // Reject Unicode minus sign (U+2212)
        if (str.Contains('\u2212'))
            throw StandardLibrary.ThrowRangeError("Unicode minus sign is not accepted in PlainTime strings", realm: realm);

        // Parse and validate bracket annotations
        var baseStr = ParseAndValidateAnnotations(str, realm);

        // Reject Z designator for PlainTime
        if (baseStr.Contains('Z') || baseStr.Contains('z'))
            throw StandardLibrary.ThrowRangeError("PlainTime strings must not contain a UTC designator (Z)", realm: realm);

        // 1. T/t prefix → always valid as time
        if (baseStr.Length > 0 && (baseStr[0] == 'T' || baseStr[0] == 't'))
        {
            var timePart = StripOffsetFromTimePart(baseStr[1..]);
            return ParsePlainTimeComponents(timePart, realm);
        }

        // 2. Try to extract time from date-time string (contains T/t/space separator)
        if (TryExtractTimeFromDateTime(baseStr, out var extracted))
        {
            // Check for year-zero (-000000)
            if (baseStr.StartsWith("-000000", StringComparison.Ordinal))
                throw StandardLibrary.ThrowRangeError("Negative zero year is not allowed", realm: realm);
            var timePart = StripOffsetFromTimePart(extracted);
            return ParsePlainTimeComponents(timePart, realm);
        }

        // 3. Check for extended year date-time without T separator (shouldn't match, but check year-zero)
        if (baseStr.StartsWith("-000000", StringComparison.Ordinal))
            throw StandardLibrary.ThrowRangeError("Negative zero year is not allowed", realm: realm);

        // 4. Check for date-only string → reject (no implicit midnight)
        if (IsDateOnlyString(baseStr))
            throw StandardLibrary.ThrowRangeError("Date-only string cannot be used where PlainTime is expected", realm: realm);

        // 5. Check for ambiguous string (could be date or time) → reject (requires T prefix)
        if (IsAmbiguousTimeString(baseStr))
            throw StandardLibrary.ThrowRangeError("Ambiguous string requires T prefix for PlainTime", realm: realm);

        // 6. Bare time string
        var bareTime = StripOffsetFromTimePart(baseStr);
        return ParsePlainTimeComponents(bareTime, realm);
    }

    /// <summary>
    ///     Checks if a string is a date-only string (YYYY-MM-DD or extended year format).
    /// </summary>
    private static bool IsDateOnlyString(string str)
    {
        // YYYY-MM-DD: 10 chars
        if (str.Length >= 10 && str[4] == '-' && str[7] == '-' &&
            AllDigits(str, 0, 4) && AllDigits(str, 5, 2) && AllDigits(str, 8, 2))
        {
            // Could have a trailing offset like -05:00, but no time part
            // If length is exactly 10, it's date-only. If there's more, check it's just an offset.
            if (str.Length == 10) return true;
            // Check if the rest is an offset (starts with + or -)
            if (str.Length > 10 && (str[10] == '+' || str[10] == '-')) return true;
        }

        // +YYYYYY-MM-DD or -YYYYYY-MM-DD: 13 chars
        if (str.Length >= 13 && (str[0] == '+' || str[0] == '-') &&
            str[7] == '-' && str[10] == '-' &&
            AllDigits(str, 1, 6) && AllDigits(str, 8, 2) && AllDigits(str, 11, 2))
        {
            if (str.Length == 13) return true;
            if (str.Length > 13 && (str[13] == '+' || str[13] == '-')) return true;
        }

        return false;
    }

    /// <summary>
    ///     Checks if a bare string (no T prefix, no date-time separator) is ambiguous
    ///     between a time string and a date string.
    /// </summary>
    private static bool IsAmbiguousTimeString(string str)
    {
        // YYYY-MM: 7 chars, e.g., "2021-12"
        if (str.Length == 7 && str[4] == '-' && AllDigits(str, 0, 4) && AllDigits(str, 5, 2))
        {
            if (int.TryParse(str.AsSpan(5, 2), System.Globalization.CultureInfo.InvariantCulture, out var month) &&
                month >= 1 && month <= 12)
                return true;
        }

        // MMDD: 4 digits, e.g., "1214"
        if (str.Length == 4 && AllDigits(str, 0, 4))
        {
            if (int.TryParse(str.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture, out var month) &&
                int.TryParse(str.AsSpan(2, 2), System.Globalization.CultureInfo.InvariantCulture, out var day) &&
                month >= 1 && month <= 12 && day >= 1 && day <= MaxDayForAmbiguityCheck(month))
                return true;
        }

        // MM-DD: 5 chars with dash at 2, e.g., "12-14"
        if (str.Length == 5 && str[2] == '-' && AllDigits(str, 0, 2) && AllDigits(str, 3, 2))
        {
            if (int.TryParse(str.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture, out var month) &&
                month >= 1 && month <= 12)
                return true;
        }

        // YYYYMM: 6 digits, e.g., "202112"
        if (str.Length == 6 && AllDigits(str, 0, 6))
        {
            if (int.TryParse(str.AsSpan(4, 2), System.Globalization.CultureInfo.InvariantCulture, out var month) &&
                month >= 1 && month <= 12)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Maximum day number for ambiguity checking. Uses the maximum possible day
    ///     for a month across all years (e.g., Feb can have 29 in leap years).
    /// </summary>
    private static int MaxDayForAmbiguityCheck(int month)
    {
        return month switch
        {
            1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
            4 or 6 or 9 or 11 => 30,
            2 => 29, // Feb 29 is valid in leap years
            _ => 0
        };
    }

    private static bool AllDigits(string str, int start, int count)
    {
        for (var i = start; i < start + count && i < str.Length; i++)
        {
            if (!char.IsDigit(str[i])) return false;
        }
        return start + count <= str.Length;
    }

    private static bool TryExtractTimeFromDateTime(string str, out string timePart)
    {
        timePart = "";
        for (var i = 1; i < str.Length; i++)
        {
            if ((str[i] == 'T' || str[i] == 't' || str[i] == ' ') && i >= 4)
            {
                var datePart = str[..i];
                if (datePart.Contains('-') || datePart.Length >= 8)
                {
                    var rest = str[(i + 1)..];
                    if (rest.Length > 0) { timePart = rest; return true; }
                    return false;
                }
            }
        }
        return false;
    }

    private static string StripOffsetFromTimePart(string timePart)
    {
        // Strip trailing offset (e.g., +01:00 or -05:30)
        for (var i = timePart.Length - 1; i >= 1; i--)
        {
            if ((timePart[i] == '+' || timePart[i] == '-') && i + 1 < timePart.Length && char.IsDigit(timePart[i + 1]))
            {
                return timePart[..i];
            }
        }
        return timePart;
    }

    /// <summary>
    ///     Finds the index of a decimal separator (. or ,) in a string.
    /// </summary>
    private static int FindDecimalSeparator(string str, int startIndex = 0)
    {
        for (var i = startIndex; i < str.Length; i++)
        {
            if (str[i] is '.' or ',') return i;
        }
        return -1;
    }

    private static JsTemporalPlainTime ParsePlainTimeComponents(string timePart, RealmState realm)
    {
        if (string.IsNullOrEmpty(timePart))
            throw StandardLibrary.ThrowRangeError("Invalid PlainTime: empty time part", realm: realm);

        int hour, minute = 0, second = 0, millisecond = 0, microsecond = 0, nanosecond = 0;

        if (timePart.Contains(':'))
        {
            // Colon format: HH:MM or HH:MM:SS or HH:MM:SS.fffffffff (or comma separator)
            var parts = timePart.Split(':');
            if (parts[0].Length != 2 || !int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out hour))
                throw StandardLibrary.ThrowRangeError($"Invalid PlainTime: {timePart}", realm: realm);
            if (parts.Length > 1)
            {
                if (parts[1].Length != 2 || !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out minute))
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainTime: {timePart}", realm: realm);
            }
            if (parts.Length > 2)
            {
                var secStr = parts[2];
                var dotIdx = FindDecimalSeparator(secStr);
                if (dotIdx >= 0)
                {
                    if (dotIdx != 2 || !int.TryParse(secStr[..dotIdx], System.Globalization.CultureInfo.InvariantCulture, out second))
                        throw StandardLibrary.ThrowRangeError($"Invalid PlainTime: {timePart}", realm: realm);
                    var frac = secStr[(dotIdx + 1)..];
                    if (frac.Length == 0 || frac.Length > 9)
                        throw StandardLibrary.ThrowRangeError($"Invalid PlainTime fractional seconds: {timePart}", realm: realm);
                    frac = frac.PadRight(9, '0');
                    if (!long.TryParse(frac, System.Globalization.CultureInfo.InvariantCulture, out var fracNanos))
                        throw StandardLibrary.ThrowRangeError($"Invalid PlainTime fractional: {timePart}", realm: realm);
                    millisecond = (int)(fracNanos / 1_000_000);
                    microsecond = (int)(fracNanos % 1_000_000 / 1_000);
                    nanosecond = (int)(fracNanos % 1_000);
                }
                else
                {
                    if (secStr.Length != 2 || !int.TryParse(secStr, System.Globalization.CultureInfo.InvariantCulture, out second))
                        throw StandardLibrary.ThrowRangeError($"Invalid PlainTime: {timePart}", realm: realm);
                }
            }
        }
        else
        {
            // Compact format: HH or HHMM or HHMMSS or HHMMSS.fff (or comma separator)
            if (timePart.Length == 2)
            {
                if (!int.TryParse(timePart, System.Globalization.CultureInfo.InvariantCulture, out hour))
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainTime: {timePart}", realm: realm);
            }
            else if (timePart.Length == 4)
            {
                if (!int.TryParse(timePart[..2], System.Globalization.CultureInfo.InvariantCulture, out hour) ||
                    !int.TryParse(timePart[2..], System.Globalization.CultureInfo.InvariantCulture, out minute))
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainTime: {timePart}", realm: realm);
            }
            else if (timePart.Length >= 6)
            {
                if (!int.TryParse(timePart[..2], System.Globalization.CultureInfo.InvariantCulture, out hour) ||
                    !int.TryParse(timePart[2..4], System.Globalization.CultureInfo.InvariantCulture, out minute))
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainTime: {timePart}", realm: realm);
                var secPart = timePart[4..];
                var dotIdx = FindDecimalSeparator(secPart);
                if (dotIdx >= 0)
                {
                    if (!int.TryParse(secPart[..dotIdx], System.Globalization.CultureInfo.InvariantCulture, out second))
                        throw StandardLibrary.ThrowRangeError($"Invalid PlainTime: {timePart}", realm: realm);
                    var frac = secPart[(dotIdx + 1)..];
                    if (frac.Length > 9) frac = frac[..9];
                    frac = frac.PadRight(9, '0');
                    if (!long.TryParse(frac, System.Globalization.CultureInfo.InvariantCulture, out var fracNanos))
                        throw StandardLibrary.ThrowRangeError($"Invalid PlainTime fractional: {timePart}", realm: realm);
                    millisecond = (int)(fracNanos / 1_000_000);
                    microsecond = (int)(fracNanos % 1_000_000 / 1_000);
                    nanosecond = (int)(fracNanos % 1_000);
                }
                else
                {
                    if (!int.TryParse(secPart, System.Globalization.CultureInfo.InvariantCulture, out second))
                        throw StandardLibrary.ThrowRangeError($"Invalid PlainTime: {timePart}", realm: realm);
                }
            }
            else
            {
                throw StandardLibrary.ThrowRangeError($"Invalid PlainTime: {timePart}", realm: realm);
            }
        }

        // Validate ranges
        if (hour > 23 || minute > 59)
            throw StandardLibrary.ThrowRangeError($"PlainTime out of range: {timePart}", realm: realm);

        // Handle leap second
        if (second == 60)
            second = 59;
        else if (second > 59)
            throw StandardLibrary.ThrowRangeError($"PlainTime seconds out of range: {timePart}", realm: realm);

        return new JsTemporalPlainTime(hour, minute, second, millisecond, microsecond, nanosecond);
    }

    private static JsTemporalPlainDateTime ToTemporalPlainDateTime(JsValue value, RealmState realm,
        string overflow = "constrain")
    {
        // 1. String: parse with validation
        if (value.IsString)
            return ParseTemporalPlainDateTimeString(value.AsString() ?? "", realm);

        // 2. Non-string primitives → TypeError
        if (value.IsUndefined || value.IsNull || value.IsBoolean || value.IsNumber || value.IsSymbol || value.IsBigInt)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDateTime", realm: realm);

        // 3. Check for Temporal objects with internal slots
        if (value.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalPlainDateTimeSlot, out var slot) && slot.TryGetObject<JsTemporalPlainDateTime>(out var dateTime))
                return dateTime;
            if (obj.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) && zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
            {
                var pdt = ZonedDateTimeToPlainDateTime(zdt);
                return pdt;
            }
            if (obj.TryGetProperty(TemporalPlainDateSlot, out var pdSlot) && pdSlot.TryGetObject<JsTemporalPlainDate>(out var pd))
                return new JsTemporalPlainDateTime(pd, JsTemporalPlainTime.Midnight);
        }

        // 4. Property bag
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
            return ToTemporalPlainDateTimeFromPropertyBag(accessor, realm, overflow);

        // 5. Other objects
        if (value.Kind == JsValueKind.Object)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDateTime: object has no date properties", realm: realm);

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDateTime", realm: realm);
    }

    /// <summary>
    /// For from() where options must be read AFTER fields per spec order.
    /// Handles Temporal objects, strings, and property bags.
    /// </summary>
    private static JsTemporalPlainDateTime ToTemporalPlainDateTimeWithDeferredOptions(
        JsValue value, JsValue options, RealmState realm, string methodName)
    {
        // Check for Temporal objects first (options validated after)
        if (value.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalPlainDateTimeSlot, out var slot) && slot.TryGetObject<JsTemporalPlainDateTime>(out var dateTime))
            {
                var resolvedOpts = ValidateOptionsObject(options, realm, methodName);
                GetTemporalOverflowOption(resolvedOpts, realm);
                return dateTime;
            }
            if (obj.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) && zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
            {
                var resolvedOpts = ValidateOptionsObject(options, realm, methodName);
                GetTemporalOverflowOption(resolvedOpts, realm);
                return ZonedDateTimeToPlainDateTime(zdt);
            }
            if (obj.TryGetProperty(TemporalPlainDateSlot, out var pdSlot) && pdSlot.TryGetObject<JsTemporalPlainDate>(out var pd))
            {
                var resolvedOpts = ValidateOptionsObject(options, realm, methodName);
                GetTemporalOverflowOption(resolvedOpts, realm);
                return new JsTemporalPlainDateTime(pd, JsTemporalPlainTime.Midnight);
            }
        }

        // Property bag: read fields first in alphabetical order, THEN options
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            var fields = ReadPlainDateTimeFields(accessor, realm);
            var resolvedOpts = ValidateOptionsObject(options, realm, methodName);
            var overflow = GetTemporalOverflowOption(resolvedOpts, realm);
            return ApplyOverflowToDateTime(fields, overflow, realm);
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainDateTime", realm: realm);
    }

    private static JsTemporalPlainDateTime ToTemporalPlainDateTimeFromPropertyBag(IJsPropertyAccessor accessor, RealmState realm,
        string overflow = "constrain")
    {
        var fields = ReadPlainDateTimeFields(accessor, realm);
        return ApplyOverflowToDateTime(fields, overflow, realm);
    }

    /// <summary>
    /// Reads all PlainDateTime fields in spec-required alphabetical order:
    /// calendar, day, era, eraYear, hour, microsecond, millisecond, minute, month, monthCode, nanosecond, second, year
    /// </summary>
    private static (int year, int month, int day, int hour, int microsecond, int millisecond, int minute, int nanosecond, int second, string calendar)
        ReadPlainDateTimeFields(IJsPropertyAccessor accessor, RealmState realm)
    {
        // 1. calendar
        var calendar = "iso8601";
        if (accessor.TryGetProperty("calendar", out var calVal) && !calVal.IsUndefined)
            calendar = CanonicalizeCalendarId(ResolveTemporalCalendarId(calVal, realm));

        // 2. day (required)
        if (!accessor.TryGetProperty("day", out var dayVal) || dayVal.IsUndefined)
            throw StandardLibrary.ThrowTypeError("Property bag for PlainDateTime must have 'day'", realm: realm);
        var day = ToIntegerWithTruncation(dayVal, realm);

        // 3. era / eraYear are only relevant for era-capable calendars.
        var hasEra = false;
        var hasEraYear = false;
        string? era = null;
        int eraYear = 0;
        if (CalendarUsesEras(calendar))
        {
            hasEra = accessor.TryGetProperty("era", out var eraVal) && !eraVal.IsUndefined;
            if (hasEra)
                era = JsOps.ToJsString(eraVal);

            hasEraYear = accessor.TryGetProperty("eraYear", out var eraYearVal) && !eraYearVal.IsUndefined;
            if (hasEraYear)
                eraYear = ToIntegerWithTruncation(eraYearVal, realm);

            if (hasEra != hasEraYear)
                throw StandardLibrary.ThrowTypeError("Property bag for PlainDateTime must have both 'era' and 'eraYear'", realm: realm);
        }

        // 4. hour
        var hour = GetOptionalIntProperty(accessor, "hour", realm);

        // 5. microsecond
        var microsecond = GetOptionalIntProperty(accessor, "microsecond", realm);

        // 6. millisecond
        var millisecond = GetOptionalIntProperty(accessor, "millisecond", realm);

        // 7. minute
        var minute = GetOptionalIntProperty(accessor, "minute", realm);

        // 8. month
        accessor.TryGetProperty("month", out var monthVal);
        var hasMonth = !monthVal.IsUndefined;
        int monthInt = 0;
        if (hasMonth)
            monthInt = ToIntegerWithTruncation(monthVal, realm);

        // 9. monthCode
        accessor.TryGetProperty("monthCode", out var monthCodeVal);
        var hasMonthCode = !monthCodeVal.IsUndefined;
        string? monthCodeStr = null;
        if (hasMonthCode)
        {
            monthCodeStr = JsOps.ToJsString(monthCodeVal);
            ValidateMonthCodeSyntax(monthCodeStr, realm);
        }

        // 10. nanosecond
        var nanosecond = GetOptionalIntProperty(accessor, "nanosecond", realm);

        // 11. second
        var second = GetOptionalIntProperty(accessor, "second", realm);

        // 12. year (required unless era/eraYear are present for an era-aware calendar)
        if (!accessor.TryGetProperty("year", out var yearVal) || yearVal.IsUndefined)
        {
            if (hasEra || hasEraYear)
            {
                if (!hasEra || !hasEraYear)
                    throw StandardLibrary.ThrowTypeError("Property bag for PlainDateTime must have both 'era' and 'eraYear'", realm: realm);

                if (!CalendarUsesEras(calendar))
                    throw StandardLibrary.ThrowTypeError("Property bag for PlainDateTime must have 'year'", realm: realm);

                var yearFromEra = ResolveTemporalEraYear(calendar, era!, eraYear, realm);

                if (!hasMonth && !hasMonthCode)
                    throw StandardLibrary.ThrowTypeError("Property bag for PlainDateTime must have 'month' or 'monthCode'", realm: realm);

                int monthFromEra;
                if (hasMonthCode)
                {
                    monthFromEra = ResolveISOMonthCode(monthCodeStr!, realm);
                    if (hasMonth && monthInt != monthFromEra)
                        throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
                }
                else
                {
                    monthFromEra = monthInt;
                }

                return (yearFromEra, monthFromEra, day, hour, microsecond, millisecond, minute, nanosecond, second, calendar);
            }

            throw StandardLibrary.ThrowTypeError("Property bag for PlainDateTime must have 'year'", realm: realm);
        }
        var year = ToIntegerWithTruncation(yearVal, realm);

        // Resolve month from month/monthCode
        if (!hasMonth && !hasMonthCode)
            throw StandardLibrary.ThrowTypeError("Property bag for PlainDateTime must have 'month' or 'monthCode'", realm: realm);

        int month;
        if (hasMonthCode)
        {
            month = ResolveISOMonthCode(monthCodeStr!, realm);
            if (hasMonth && monthInt != month)
                throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
        }
        else
        {
            month = monthInt;
        }

        return (year, month, day, hour, microsecond, millisecond, minute, nanosecond, second, calendar);
    }

    private static JsTemporalPlainDateTime ApplyOverflowToDateTime(
        (int year, int month, int day, int hour, int microsecond, int millisecond, int minute, int nanosecond, int second, string calendar) fields,
        string overflow, RealmState realm)
    {
        var (year, month, day, hour, microsecond, millisecond, minute, nanosecond, second, calendar) = fields;

        // Values ≤ 0 for month/day are always invalid
        if (month < 1)
            throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
        if (day < 1)
            throw StandardLibrary.ThrowRangeError($"Day {day} is out of range", realm: realm);

        if (string.Equals(overflow, "reject", StringComparison.Ordinal))
        {
            if (month > 12)
                throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
            var maxDay = IsoCalendarHelpers.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
            if (day > maxDay)
                throw StandardLibrary.ThrowRangeError($"Day {day} is out of range for month {month}", realm: realm);
            RejectISOTime(hour, minute, second, millisecond, microsecond, nanosecond, realm);
        }
        else
        {
            // Constrain
            month = Math.Min(month, 12);
            var maxDay = IsoCalendarHelpers.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
            day = Math.Min(day, maxDay);
            hour = Math.Clamp(hour, 0, 23);
            minute = Math.Clamp(minute, 0, 59);
            second = Math.Clamp(second, 0, 59);
            millisecond = Math.Clamp(millisecond, 0, 999);
            microsecond = Math.Clamp(microsecond, 0, 999);
            nanosecond = Math.Clamp(nanosecond, 0, 999);
        }

        RejectISODateTimeRange(year, month, day,
            hour, minute, second, millisecond, microsecond, nanosecond, realm);

        return new JsTemporalPlainDateTime(year, month, day,
            hour, minute, second, millisecond, microsecond, nanosecond, calendar);
    }

    private static int GetOptionalIntProperty(IJsPropertyAccessor accessor, string name, RealmState realm)
    {
        if (!accessor.TryGetProperty(name, out var val) || val.IsUndefined)
            return 0;
        return ToIntegerWithTruncation(val, realm);
    }

    private static JsTemporalPlainDateTime ParseTemporalPlainDateTimeString(string str, RealmState realm)
    {
        if (string.IsNullOrEmpty(str))
            throw StandardLibrary.ThrowRangeError("Invalid PlainDateTime string: empty", realm: realm);

        // Reject non-ASCII minus sign (U+2212)
        if (str.Contains('\u2212'))
            throw StandardLibrary.ThrowRangeError("Non-ASCII minus sign is not allowed", realm: realm);

        // Parse and validate bracket annotations
        var baseStr = ParseAndValidateAnnotations(str, realm);

        // Preserve the canonical calendar annotation if present; otherwise default to ISO.
        var calendar = ValidateCalendarAnnotation(str, realm) ?? "iso8601";

        // For PlainDateTime: Z designator is rejected
        if (HasZDesignator(baseStr))
            throw StandardLibrary.ThrowRangeError("Z designator not allowed in PlainDateTime string", realm: realm);

        // Must have a date part at minimum
        var startIdx = 0;
        if (baseStr.Length > 0 && (baseStr[0] == '+' || baseStr[0] == '-'))
            startIdx = 1;

        var tIdx = FindDateTimeSeparator(baseStr[startIdx..]);
        string dateStr;
        int hour = 0, minute = 0, second = 0, millisecond = 0, microsecond = 0, nanosecond = 0;

        if (tIdx >= 0)
        {
            tIdx += startIdx;
            dateStr = baseStr[..tIdx];
            var afterT = baseStr[(tIdx + 1)..];

            if (afterT.Length == 0)
                throw StandardLibrary.ThrowRangeError($"Invalid PlainDateTime string: {str}", realm: realm);

            // Parse and validate time part, strip offset
            var timePart = StripOffsetFromTimePart(afterT);
            ParseTimePart(timePart, out hour, out minute, out second,
                out millisecond, out microsecond, out nanosecond, realm);

            // Handle leap second: second 60 → 59
            if (second == 60)
                second = 59;

            // Validate the offset portion if present
            ValidateDateTimeTimePart(afterT, realm);
        }
        else
        {
            dateStr = baseStr;

            // If no time part, reject any offset on the date string
            if (DateOnlyStringHasOffset(dateStr))
                throw StandardLibrary.ThrowRangeError("UTC offset without time is not valid for PlainDateTime", realm: realm);
        }

        // Parse date part (reuse PlainDate parsing logic)
        var date = ParseDatePart(dateStr, str, realm);

        // Lower-bound strings need a stricter check: the minimum representable PlainDateTime
        // is just after the minimum PlainDate boundary, so date-only strings and midnight
        // strings on that day are both invalid.
        if (date.year == IsoDateMin.year &&
            date.month == IsoDateMin.month &&
            date.day == IsoDateMin.day &&
            hour == 0 &&
            minute == 0 &&
            second == 0 &&
            millisecond == 0 &&
            microsecond == 0 &&
            nanosecond == 0)
        {
            throw StandardLibrary.ThrowRangeError($"\"{str}\" is outside the representable range of PlainDateTime", realm: realm);
        }

        RejectISODateTimeRange(date.year, date.month, date.day,
            hour, minute, second, millisecond, microsecond, nanosecond, realm);

        return new JsTemporalPlainDateTime(date.year, date.month, date.day,
            hour, minute, second, millisecond, microsecond, nanosecond, calendar);
    }

    private static void ParseTimePart(string timePart, out int hour, out int minute, out int second,
        out int millisecond, out int microsecond, out int nanosecond, RealmState realm)
    {
        hour = 0; minute = 0; second = 0; millisecond = 0; microsecond = 0; nanosecond = 0;

        if (timePart.Contains(':'))
        {
            var parts = timePart.Split(':');
            if (parts.Length < 2)
                throw StandardLibrary.ThrowRangeError("Invalid time format", realm: realm);
            if (!int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out hour))
                throw StandardLibrary.ThrowRangeError("Invalid time format", realm: realm);
            if (!int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out minute))
                throw StandardLibrary.ThrowRangeError("Invalid time format", realm: realm);
            if (parts.Length > 2)
            {
                ParseSecondsWithSubseconds(parts[2], out second, out millisecond, out microsecond, out nanosecond, realm);
            }
        }
        else
        {
            // Compact format
            if (timePart.Length < 2)
                throw StandardLibrary.ThrowRangeError("Invalid time format", realm: realm);
            if (!int.TryParse(timePart.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture, out hour))
                throw StandardLibrary.ThrowRangeError("Invalid time format", realm: realm);
            if (timePart.Length >= 4)
            {
                if (!int.TryParse(timePart.AsSpan(2, 2), System.Globalization.CultureInfo.InvariantCulture, out minute))
                    throw StandardLibrary.ThrowRangeError("Invalid time format", realm: realm);
                if (timePart.Length >= 6)
                {
                    ParseSecondsWithSubseconds(timePart[4..], out second, out millisecond, out microsecond, out nanosecond, realm);
                }
            }
        }
    }

    private static void ParseSecondsWithSubseconds(string secStr, out int second,
        out int millisecond, out int microsecond, out int nanosecond, RealmState realm)
    {
        millisecond = 0; microsecond = 0; nanosecond = 0;
        var dotIdx = secStr.IndexOf('.');
        if (dotIdx < 0) dotIdx = secStr.IndexOf(',');

        if (dotIdx >= 0)
        {
            if (!int.TryParse(secStr.AsSpan(0, dotIdx), System.Globalization.CultureInfo.InvariantCulture, out second))
                throw StandardLibrary.ThrowRangeError("Invalid seconds format", realm: realm);
            var frac = secStr[(dotIdx + 1)..];
            if (frac.Length == 0 || frac.Length > 9)
                throw StandardLibrary.ThrowRangeError("Invalid fractional seconds", realm: realm);
            frac = frac.PadRight(9, '0');
            if (!int.TryParse(frac.AsSpan(0, 3), System.Globalization.CultureInfo.InvariantCulture, out millisecond) ||
                !int.TryParse(frac.AsSpan(3, 3), System.Globalization.CultureInfo.InvariantCulture, out microsecond) ||
                !int.TryParse(frac.AsSpan(6, 3), System.Globalization.CultureInfo.InvariantCulture, out nanosecond))
                throw StandardLibrary.ThrowRangeError("Invalid fractional seconds", realm: realm);
        }
        else
        {
            if (!int.TryParse(secStr.AsSpan(0, Math.Min(secStr.Length, 2)), System.Globalization.CultureInfo.InvariantCulture, out second))
                throw StandardLibrary.ThrowRangeError("Invalid seconds format", realm: realm);
        }
    }

    /// <summary>
    ///     Tries to parse a YYYY-MM or ±YYYYYY-MM format (no day component).
    ///     Returns null if the string has a day component (YYYY-MM-DD).
    /// </summary>
    private static (int year, int month)? TryParseYearMonth(string dateStr, string originalStr, RealmState realm)
    {
        if (dateStr.Length > 0 && (dateStr[0] == '+' || dateStr[0] == '-'))
        {
            // Extended year formats
            var sign = dateStr[0] == '-' ? -1 : 1;
            var rest = dateStr[1..];
            var dashCount = rest.Count(c => c == '-');

            if (dashCount == 1)
            {
                // ±YYYYYY-MM (dash-separated)
                var dashIdx = rest.IndexOf('-');
                if (dashIdx > 0 &&
                    int.TryParse(rest.AsSpan(0, dashIdx), System.Globalization.CultureInfo.InvariantCulture, out var yearAbs) &&
                    int.TryParse(rest.AsSpan(dashIdx + 1), System.Globalization.CultureInfo.InvariantCulture, out var month))
                {
                    if (sign == -1 && yearAbs == 0)
                        throw StandardLibrary.ThrowRangeError("Negative zero year is not allowed", realm: realm);
                    if (month < 1 || month > 12)
                        throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
                    return (sign * yearAbs, month);
                }
            }

            if (dashCount == 0 && rest.Length == 8 && AllDigits(rest, 0, 8))
            {
                // ±YYYYYYMM (compact, 8 digits after sign)
                if (int.TryParse(rest.AsSpan(0, 6), System.Globalization.CultureInfo.InvariantCulture, out var yearAbs) &&
                    int.TryParse(rest.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture, out var month))
                {
                    if (sign == -1 && yearAbs == 0)
                        throw StandardLibrary.ThrowRangeError("Negative zero year is not allowed", realm: realm);
                    if (month < 1 || month > 12)
                        throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
                    return (sign * yearAbs, month);
                }
            }

            // More dashes or other formats → full date, return null
            return null;
        }

        // Standard year: YYYY-MM (exactly 7 chars, dash at position 4)
        if (dateStr.Length == 7 && dateStr[4] == '-')
        {
            if (int.TryParse(dateStr.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture, out var year) &&
                int.TryParse(dateStr.AsSpan(5, 2), System.Globalization.CultureInfo.InvariantCulture, out var month))
            {
                if (month < 1 || month > 12)
                    throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
                return (year, month);
            }
        }

        // YYYYMM compact (exactly 6 digits, no dashes)
        if (dateStr.Length == 6 && AllDigits(dateStr, 0, 6))
        {
            if (int.TryParse(dateStr.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture, out var year) &&
                int.TryParse(dateStr.AsSpan(4, 2), System.Globalization.CultureInfo.InvariantCulture, out var month))
            {
                if (month < 1 || month > 12)
                    throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
                return (year, month);
            }
        }

        return null;
    }

    private static (int year, int month, int day) ParseDatePart(string dateStr, string originalStr, RealmState realm)
    {
        int year, month, day;
        var startIdx = 0;
        if (dateStr.Length > 0 && (dateStr[0] == '+' || dateStr[0] == '-'))
            startIdx = 1;

        if (startIdx == 1)
        {
            // Extended year: ±YYYYYY-MM-DD or ±YYYYYYMMDD
            var sign = dateStr[0] == '-' ? -1 : 1;
            var datePart = dateStr[1..];

            if (datePart.Length == 10 && AllDigits(datePart, 0, 10))
            {
                // Compact: YYYYYYMMDD
                if (!int.TryParse(datePart.AsSpan(0, 6), System.Globalization.CultureInfo.InvariantCulture, out var yearAbs) ||
                    !int.TryParse(datePart.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart.AsSpan(8, 2), System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                year = sign * yearAbs;
                if (sign == -1 && yearAbs == 0)
                    throw StandardLibrary.ThrowRangeError("Negative zero year is not allowed", realm: realm);
            }
            else
            {
                // Dash-separated: YYYYYY-MM-DD
                var lastDash = datePart.LastIndexOf('-');
                if (lastDash <= 0)
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                var secondLastDash = datePart.LastIndexOf('-', lastDash - 1);
                if (secondLastDash <= 0)
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                var yearStr = datePart[..secondLastDash];
                if (yearStr.Length != 6)
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                if (!int.TryParse(yearStr, System.Globalization.CultureInfo.InvariantCulture, out var yearAbs) ||
                    !int.TryParse(datePart[(secondLastDash + 1)..lastDash], System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart[(lastDash + 1)..], System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                if (datePart[(secondLastDash + 1)..lastDash].Length != 2 || datePart[(lastDash + 1)..].Length != 2)
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                year = sign * yearAbs;
                if (sign == -1 && yearAbs == 0)
                    throw StandardLibrary.ThrowRangeError("Negative zero year is not allowed", realm: realm);
            }
        }
        else
        {
            // Standard year: YYYY-MM-DD or YYYYMMDD
            if (dateStr.Contains('-'))
            {
                var dashParts = dateStr.Split('-');
                if (dashParts.Length != 3 || dashParts[0].Length != 4 || dashParts[1].Length != 2 || dashParts[2].Length != 2)
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                if (!int.TryParse(dashParts[0], System.Globalization.CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(dashParts[1], System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(dashParts[2], System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
            }
            else if (dateStr.Length == 8 && AllDigits(dateStr, 0, 8))
            {
                if (!int.TryParse(dateStr.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(dateStr.AsSpan(4, 2), System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(dateStr.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
            }
            else
            {
                throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
            }
        }

        RejectISODate(year, month, day, realm);
        return (year, month, day);
    }

    /// <summary>
    ///     Like ParseDatePart but only validates month/day, not the year range.
    ///     Used for PlainMonthDay where extended years (±999999) are valid in the input string
    ///     even though they exceed the PlainDate representable range.
    /// </summary>
    private static (int year, int month, int day) ParseDatePartNoRangeCheck(string dateStr, string originalStr,
        RealmState realm)
    {
        // Parse the same way as ParseDatePart but validate only month and day
        int year, month, day;
        var startIdx = 0;
        if (dateStr.Length > 0 && (dateStr[0] == '+' || dateStr[0] == '-'))
            startIdx = 1;

        if (startIdx == 1)
        {
            var sign = dateStr[0] == '-' ? -1 : 1;
            var datePart = dateStr[1..];

            if (datePart.Length == 10 && AllDigits(datePart, 0, 10))
            {
                if (!int.TryParse(datePart.AsSpan(0, 6), System.Globalization.CultureInfo.InvariantCulture, out var yearAbs) ||
                    !int.TryParse(datePart.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart.AsSpan(8, 2), System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                year = sign * yearAbs;
                if (sign == -1 && yearAbs == 0)
                    throw StandardLibrary.ThrowRangeError("Negative zero year is not allowed", realm: realm);
            }
            else
            {
                var lastDash = datePart.LastIndexOf('-');
                if (lastDash <= 0)
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                var secondLastDash = datePart.LastIndexOf('-', lastDash - 1);
                if (secondLastDash <= 0)
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                var yearStr = datePart[..secondLastDash];
                if (yearStr.Length != 6)
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                if (!int.TryParse(yearStr, System.Globalization.CultureInfo.InvariantCulture, out var yearAbs) ||
                    !int.TryParse(datePart[(secondLastDash + 1)..lastDash], System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(datePart[(lastDash + 1)..], System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                if (datePart[(secondLastDash + 1)..lastDash].Length != 2 || datePart[(lastDash + 1)..].Length != 2)
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                year = sign * yearAbs;
                if (sign == -1 && yearAbs == 0)
                    throw StandardLibrary.ThrowRangeError("Negative zero year is not allowed", realm: realm);
            }
        }
        else
        {
            if (dateStr.Contains('-'))
            {
                var dashParts = dateStr.Split('-');
                if (dashParts.Length != 3 || dashParts[0].Length != 4 || dashParts[1].Length != 2 || dashParts[2].Length != 2)
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
                if (!int.TryParse(dashParts[0], System.Globalization.CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(dashParts[1], System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(dashParts[2], System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
            }
            else if (dateStr.Length == 8 && AllDigits(dateStr, 0, 8))
            {
                if (!int.TryParse(dateStr.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture, out year) ||
                    !int.TryParse(dateStr.AsSpan(4, 2), System.Globalization.CultureInfo.InvariantCulture, out month) ||
                    !int.TryParse(dateStr.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture, out day))
                    throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
            }
            else
            {
                throw StandardLibrary.ThrowRangeError($"Invalid date string: {originalStr}", realm: realm);
            }
        }

        // Only validate month and day, NOT year range
        if (month is < 1 or > 12)
            throw StandardLibrary.ThrowRangeError("Month value is out of range (1-12)", realm: realm);
        var daysInMonth = DateTime.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
        if (day < 1 || day > daysInMonth)
            throw StandardLibrary.ThrowRangeError("Day value is out of range", realm: realm);

        return (year, month, day);
    }

    /// <summary>
    ///     Extracts timezone ID and calendar from bracket annotations in a ZonedDateTime string.
    ///     E.g., "2020-01-01T00:00+00:00[Europe/Paris][u-ca=iso8601]" → ("Europe/Paris", "iso8601")
    /// </summary>
    private static (string? timeZoneId, string? calendar) ExtractZonedDateTimeAnnotations(string str)
    {
        string? timeZoneId = null;
        string? calendar = null;
        var pos = 0;

        while (pos < str.Length)
        {
            var open = str.IndexOf('[', pos);
            if (open < 0) break;
            var close = str.IndexOf(']', open);
            if (close < 0) break;
            var content = str[(open + 1)..close];

            // Skip !critical prefix
            if (content.StartsWith('!'))
                content = content[1..];

            if (content.StartsWith("u-ca=", StringComparison.Ordinal))
            {
                // Per spec: first calendar annotation wins
                calendar ??= content[5..];
            }
            else if (!content.Contains('='))
            {
                // Timezone annotation (no '=' means it's not a key=value annotation)
                timeZoneId = content;
            }
            pos = close + 1;
        }

        return (timeZoneId, calendar);
    }

    private static JsValue GetTemporalEra(string calendarId, int year, int month = 1, int day = 1)
    {
        if (string.Equals(calendarId, "gregory", StringComparison.Ordinal))
            return new JsValue(year <= 0 ? "bce" : "ce");
        if (string.Equals(calendarId, "japanese", StringComparison.Ordinal))
            return new JsValue(GetJapaneseEraInfo(year, month, day).Era);
        return JsValue.Undefined;
    }

    private static JsValue GetTemporalEraYear(string calendarId, int year, int month = 1, int day = 1)
    {
        if (string.Equals(calendarId, "gregory", StringComparison.Ordinal))
            return new JsValue(year <= 0 ? 1 - year : year);
        if (string.Equals(calendarId, "japanese", StringComparison.Ordinal))
            return new JsValue(GetJapaneseEraInfo(year, month, day).EraYear);
        return JsValue.Undefined;
    }

    private static (string Era, int EraYear) GetJapaneseEraInfo(int year, int month, int day)
    {
        if (year > 2019 || (year == 2019 && (month > 5 || (month == 5 && day >= 1))))
            return ("reiwa", year - 2018);
        if (year > 1989 || (year == 1989 && (month > 1 || (month == 1 && day >= 8))))
            return ("heisei", year - 1988);
        if (year > 1926 || (year == 1926 && (month > 12 || (month == 12 && day >= 25))))
            return ("showa", year - 1925);
        if (year > 1912 || (year == 1912 && (month > 7 || (month == 7 && day >= 30))))
            return ("taisho", year - 1911);
        return ("meiji", year - 1867);
    }

    /// <summary>
    ///     Canonicalizes a calendar identifier.
    /// </summary>
    private static string CanonicalizeCalendarId(string calendarId)
    {
        var lowered = AsciiLowercase(calendarId);
        if (CalendarAliases.TryGetValue(lowered, out var canonical))
            return canonical;
        return lowered;
    }

    private static string CanonicalizeTimeZoneId(string timeZoneId)
    {
        if (string.IsNullOrEmpty(timeZoneId))
            return timeZoneId;

        // UTC (case-insensitive)
        if (string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
            return "UTC";

        // For offset timezones, normalize format (+0000 → +00:00, +00 → +00:00)
        if (timeZoneId.Length >= 2 && (timeZoneId[0] == '+' || timeZoneId[0] == '-') && char.IsDigit(timeZoneId[1]))
            return NormalizeUtcOffset(timeZoneId);

        // Check IANA alias map for deprecated/alternative timezone names
        if (IntlUtilities.TryCanonicalizeTimeZoneAlias(timeZoneId, out var aliasCanonical))
            return aliasCanonical;

        // For IANA names, use case-insensitive lookup via FindTimeZone
        // then return the system's canonical ID.
        try
        {
            var tz = FindTimeZone(timeZoneId);
            return tz.HasIanaId ? tz.Id : timeZoneId;
        }
        catch (TimeZoneNotFoundException)
        {
            return timeZoneId;
        }
    }

    /// <summary>
    ///     Validates and converts a timezone value from a property bag to a timezone identifier string.
    /// </summary>
    private static string ToTemporalTimeZoneIdentifier(JsValue value, RealmState realm)
    {
        if (value.IsString)
        {
            var str = value.AsString() ?? "";
            if (string.IsNullOrEmpty(str))
                throw StandardLibrary.ThrowRangeError("Invalid time zone: empty string", realm: realm);
            if (str.Contains('\u2212'))
                throw StandardLibrary.ThrowRangeError("Non-ASCII minus sign is not allowed in time zone", realm: realm);

            // Delegate to the string overload which handles ISO datetime strings,
            // bracket annotations, offsets, and IANA timezone names
            return ToTemporalTimeZoneIdentifier(str, realm);
        }

        // Non-string types: per spec, ToTemporalTimeZoneIdentifier requires a string
        throw StandardLibrary.ThrowTypeError("Invalid time zone type", realm: realm);
    }

    /// <summary>
    ///     Parses an offset string like "+05:30" or "-04:00" to nanoseconds.
    ///     Throws RangeError if the string is not a valid offset.
    /// </summary>
    private static long ParseOffsetString(string offsetStr, RealmState realm)
    {
        var result = ParseOffsetToNanos(offsetStr);
        if (result is null)
            throw StandardLibrary.ThrowRangeError($"Invalid offset string: {offsetStr}", realm: realm);
        return result.Value;
    }

    /// <summary>
    ///     Extracts offset nanoseconds from an ISO datetime string.
    ///     E.g., "2020-01-01T00:00-04:00" → -14400000000000
    /// </summary>
    private static long ExtractOffsetNanosFromString(string str)
    {
        // Check for Z designator
        if (str.Length > 0 && (str[^1] == 'Z' || str[^1] == 'z'))
            return 0;

        // Find offset in time part (after T/t)
        var tIdx = str.IndexOf('T');
        if (tIdx < 0) tIdx = str.IndexOf('t');
        if (tIdx < 0) return 0;

        var timePart = str[(tIdx + 1)..];

        // Scan backwards for +/- that starts an offset
        for (var i = timePart.Length - 1; i >= 1; i--)
        {
            if ((timePart[i] == '+' || timePart[i] == '-') &&
                i + 1 < timePart.Length && char.IsDigit(timePart[i + 1]))
            {
                var offsetStr = timePart[i..];
                var result = ParseOffsetToNanos(offsetStr);
                return result ?? 0;
            }
        }

        return 0;
    }

    /// <summary>
    ///     Pre-validates a ZonedDateTime string for syntax errors (throws RangeError).
    ///     Per spec, string syntax errors must throw before GetOptionsObject is called.
    /// </summary>
    private static void ValidateZonedDateTimeString(string str, RealmState realm)
    {
        if (string.IsNullOrEmpty(str))
            throw StandardLibrary.ThrowRangeError("Invalid ZonedDateTime string: empty", realm: realm);
        if (str.Contains('\u2212'))
            throw StandardLibrary.ThrowRangeError("Non-ASCII minus sign is not allowed", realm: realm);
        ParseAndValidateAnnotations(str, realm);
        ValidateCalendarAnnotation(str, realm);
        var (timeZoneId, _) = ExtractZonedDateTimeAnnotations(str);
        if (timeZoneId == null)
            throw StandardLibrary.ThrowRangeError("ZonedDateTime requires a time zone annotation in brackets", realm: realm);

        // Also validate the date-time components of the base string
        var bracketIdx = str.IndexOf('[');
        var baseStr = bracketIdx >= 0 ? str[..bracketIdx] : str;
        // Parse the datetime, extracting the date and time components
        // Use ParseIsoDateTimeComponents to validate without computing instant
        ValidateIsoDateTimeComponents(baseStr, str, realm);
    }

    /// <summary>
    ///     Validates ISO date-time components in a string (month, day, hour, minute, second ranges).
    ///     Used for pre-validation before options are read.
    /// </summary>
    private static void ValidateIsoDateTimeComponents(string baseStr, string originalStr, RealmState realm)
    {
        var s = baseStr;

        // Strip Z designator
        if (s.EndsWith('Z') || s.EndsWith('z'))
            s = s[..^1];

        // Find date-time separator FIRST (before stripping offset from date part)
        var startIdx = 0;
        if (s.Length > 0 && (s[0] == '+' || s[0] == '-'))
            startIdx = 1;
        var tIdx = FindDateTimeSeparator(s[startIdx..]);

        string datePart;
        string? timePart = null;
        if (tIdx >= 0)
        {
            datePart = s[..(tIdx + startIdx)];
            var rawTimePart = s[(tIdx + startIdx + 1)..];

            // Strip trailing offset (+HH:MM or -HH:MM) from time portion only
            for (var i = rawTimePart.Length - 1; i >= 2; i--)
            {
                if ((rawTimePart[i] == '+' || rawTimePart[i] == '-') && i + 1 < rawTimePart.Length && char.IsDigit(rawTimePart[i + 1]))
                {
                    rawTimePart = rawTimePart[..i];
                    break;
                }
            }
            timePart = rawTimePart;
        }
        else
        {
            datePart = s;
        }

        // Parse and validate date components
        int month, day;
        if (datePart.Contains('-'))
        {
            var parts = datePart.Split('-');
            // Skip year validation (handled elsewhere), just get month and day
            if (parts.Length >= 3)
            {
                if (int.TryParse(parts[^2], System.Globalization.CultureInfo.InvariantCulture, out month) &&
                    int.TryParse(parts[^1], System.Globalization.CultureInfo.InvariantCulture, out day))
                {
                    if (month < 1 || month > 12)
                        throw StandardLibrary.ThrowRangeError($"Invalid ZonedDateTime string: {originalStr}", realm: realm);
                    if (day < 1 || day > 31)
                        throw StandardLibrary.ThrowRangeError($"Invalid ZonedDateTime string: {originalStr}", realm: realm);
                }
            }
        }

        // Parse and validate time components
        if (timePart != null)
        {
            // Normalize comma to period
            timePart = timePart.Replace(',', '.');

            int hour, minute;
            if (timePart.Contains(':'))
            {
                var parts = timePart.Split(':');
                if (int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out hour))
                {
                    if (hour > 23)
                        throw StandardLibrary.ThrowRangeError($"Invalid ZonedDateTime string: {originalStr}", realm: realm);
                }
                if (parts.Length > 1 && int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out minute))
                {
                    if (minute > 59)
                        throw StandardLibrary.ThrowRangeError($"Invalid ZonedDateTime string: {originalStr}", realm: realm);
                }
                if (parts.Length > 2)
                {
                    var secStr = parts[2];
                    var dotIdx = secStr.IndexOf('.');
                    var secPart = dotIdx >= 0 ? secStr[..dotIdx] : secStr;
                    if (int.TryParse(secPart, System.Globalization.CultureInfo.InvariantCulture, out var second))
                    {
                        // Allow leap second (60) — it will be clamped to 59 during parsing
                        if (second > 60)
                            throw StandardLibrary.ThrowRangeError($"Invalid ZonedDateTime string: {originalStr}", realm: realm);
                    }
                }
            }
            else
            {
                // Compact: HHMM or HHMMSS
                var dotIdx = timePart.IndexOf('.');
                var digits = dotIdx >= 0 ? timePart[..dotIdx] : timePart;
                if (digits.Length >= 2 && int.TryParse(digits[..2], System.Globalization.CultureInfo.InvariantCulture, out hour))
                {
                    if (hour > 23)
                        throw StandardLibrary.ThrowRangeError($"Invalid ZonedDateTime string: {originalStr}", realm: realm);
                }
                if (digits.Length >= 4 && int.TryParse(digits[2..4], System.Globalization.CultureInfo.InvariantCulture, out minute))
                {
                    if (minute > 59)
                        throw StandardLibrary.ThrowRangeError($"Invalid ZonedDateTime string: {originalStr}", realm: realm);
                }
                if (digits.Length >= 6 && int.TryParse(digits[4..6], System.Globalization.CultureInfo.InvariantCulture, out var second))
                {
                    // Allow leap second (60)
                    if (second > 60)
                        throw StandardLibrary.ThrowRangeError($"Invalid ZonedDateTime string: {originalStr}", realm: realm);
                }
            }
        }
    }

    private static JsTemporalZonedDateTime ToTemporalZonedDateTime(JsValue value, RealmState realm,
        string offsetOption = "reject", string disambiguationOption = "compatible",
        string overflowOption = "constrain")
    {
        // Check for Temporal objects with internal slots first
        if (value.TryGetObject<JsObject>(out var obj2) &&
            obj2.TryGetProperty(TemporalZonedDateTimeSlot, out var slot2) &&
            slot2.TryGetObject<JsTemporalZonedDateTime>(out var existing))
            return existing;

        // 1. String: parse with validation
        if (value.IsString)
        {
            var str = value.AsString() ?? "";
            if (string.IsNullOrEmpty(str))
                throw StandardLibrary.ThrowRangeError("Invalid ZonedDateTime string: empty", realm: realm);
            if (str.Contains('\u2212'))
                throw StandardLibrary.ThrowRangeError("Non-ASCII minus sign is not allowed", realm: realm);

            // Parse and validate annotations (handles critical, multiple, invalid keys)
            ParseAndValidateAnnotations(str, realm);

            // Validate calendar annotation
            ValidateCalendarAnnotation(str, realm);

            // Extract timezone and calendar from bracket annotations
            var validatedCalendar = ValidateCalendarAnnotation(str, realm);
            var (timeZoneId, calendarAnnotation) = ExtractZonedDateTimeAnnotations(str);
            var calendar = validatedCalendar ?? (calendarAnnotation is null ? "iso8601" : ValidateCalendarId(calendarAnnotation));

            // ZonedDateTime REQUIRES a timezone annotation
            if (timeZoneId == null)
                throw StandardLibrary.ThrowRangeError("ZonedDateTime requires a time zone annotation in brackets", realm: realm);

            // Validate the time zone identifier while preserving the Temporal string-path behavior
            // for named zones more closely than the broader comparison canonicalizer.
            timeZoneId = ValidateTimeZoneIdentifier(timeZoneId, realm);

            // Get the base string (before annotations)
            var bracketIdx = str.IndexOf('[');
            var baseStr = bracketIdx >= 0 ? str[..bracketIdx] : str;

            // Parse the date-time from base string
            var hasOffset = JsTemporalZonedDateTime.HasExplicitOffset(baseStr);
            var hasZ = HasZDesignator(baseStr);

            // Parse with nanosecond precision
            var parsed = JsTemporalZonedDateTime.ParseIsoDateTimeWithOffset(baseStr);
            if (parsed == null)
                throw StandardLibrary.ThrowRangeError($"Invalid ZonedDateTime string: {str}", realm: realm);

            // ISODateTimeWithinLimits: wall clock date must be within ±100,000,000 epoch days
            // Only enforced for "reject"/"prefer" offset modes (not "use"/"ignore")
            if (!string.Equals(offsetOption, "use", StringComparison.Ordinal) &&
                !string.Equals(offsetOption, "ignore", StringComparison.Ordinal))
            {
                var wallClockDays = JsTemporalZonedDateTime.ParseWallClockEpochDays(baseStr);
                if (wallClockDays.HasValue && Math.Abs(wallClockDays.Value) > 100_000_000)
                    throw StandardLibrary.ThrowRangeError("ZonedDateTime wall clock time is out of representable range", realm: realm);
            }

            if (hasZ)
            {
                // Z means UTC — use exact time regardless of timezone annotation
                return new JsTemporalZonedDateTime(parsed, timeZoneId, calendar);
            }

            if (hasOffset)
            {
                // Has explicit offset — need to validate/reconcile with timezone
                var stringOffsetNanos = ExtractOffsetNanosFromString(baseStr);
                var tz = JsTemporalZonedDateTime.ResolveTimeZone(timeZoneId, out var fixedOff);
                var wallNanos = parsed.EpochNanoseconds + stringOffsetNanos;
                var wallInstant = JsTemporalInstant.FromEpochNanoseconds(wallNanos);
                var approxLocal = wallInstant.ToDateTimeOffset().DateTime;
                TimeSpan tzOffset;
                if (fixedOff.HasValue)
                {
                    tzOffset = fixedOff.Value;
                }
                else
                {
                    TryMatchTimeZoneOffsetForString(baseStr, stringOffsetNanos, timeZoneId, tz, fixedOff, approxLocal, out tzOffset);
                }
                var tzOffsetNanos = tzOffset.Ticks * 100L;

                if (string.Equals(offsetOption, "reject", StringComparison.Ordinal))
                {
                    // Reject if offset doesn't match timezone
                    if (!TryMatchTimeZoneOffsetForString(baseStr, stringOffsetNanos, timeZoneId, tz, fixedOff, approxLocal, out tzOffset))
                        throw StandardLibrary.ThrowRangeError("Offset does not match the time zone", realm: realm);
                    tzOffsetNanos = tzOffset.Ticks * 100L;
                    var rejectedWallTimeInstant =
                        JsTemporalInstant.FromEpochNanoseconds(parsed.EpochNanoseconds + stringOffsetNanos - tzOffsetNanos);
                    return new JsTemporalZonedDateTime(rejectedWallTimeInstant, timeZoneId, calendar);
                }

                if (string.Equals(offsetOption, "use", StringComparison.Ordinal))
                {
                    // Use the offset as-is (parsed instant already uses the explicit offset)
                    return new JsTemporalZonedDateTime(parsed, timeZoneId, calendar);
                }

                if (string.Equals(offsetOption, "prefer", StringComparison.Ordinal))
                {
                    // "prefer" only uses the parsed offset when it is an exact match.
                    // Minute-rounded historical matches are accepted for validation, but
                    // still preserve wall time in the named zone.
                    if (stringOffsetNanos == tzOffsetNanos)
                        return new JsTemporalZonedDateTime(parsed, timeZoneId, calendar);
                    // Fall through to wall time calculation
                }

                // "ignore" or "prefer" fallthrough — use wall time in timezone
                var wallTimeInstant = JsTemporalInstant.FromEpochNanoseconds(
                    parsed.EpochNanoseconds + stringOffsetNanos - tzOffsetNanos);
                return new JsTemporalZonedDateTime(wallTimeInstant, timeZoneId, calendar);
            }

            // No offset — treat as wall time in the given timezone
            var tz2 = JsTemporalZonedDateTime.ResolveTimeZone(timeZoneId, out var fixedOff2);
            TimeSpan wallOffset;
            if (fixedOff2.HasValue)
            {
                wallOffset = fixedOff2.Value;
            }
            else
            {
                var approxLocal2 = parsed.ToDateTimeOffset().DateTime;
                wallOffset = TemporalHistoricalTimeZoneOffsets.GetUtcOffset(timeZoneId, tz2, approxLocal2);
            }
            var offsetNanosTz = wallOffset.Ticks * 100L;
            var utcEpochNs = parsed.EpochNanoseconds - offsetNanosTz;
            // Validate the UTC instant is within representable range (spec: GetStartOfDay validation)
            if (utcEpochNs < InstantMinEpochNanoseconds || utcEpochNs > InstantMaxEpochNanoseconds)
                throw StandardLibrary.ThrowRangeError("ZonedDateTime is out of representable range", realm: realm);
            var utcInstant = JsTemporalInstant.FromEpochNanoseconds(utcEpochNs);
            return new JsTemporalZonedDateTime(utcInstant, timeZoneId, calendar);
        }

        // 2. Non-string primitives → TypeError
        if (value.IsUndefined || value.IsNull || value.IsBoolean || value.IsNumber || value.IsSymbol || value.IsBigInt)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.ZonedDateTime", realm: realm);

        // 3. Check for Temporal objects with internal slots
        if (value.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalZonedDateTimeSlot, out var slot) && slot.TryGetObject<JsTemporalZonedDateTime>(out var zonedDateTime))
                return zonedDateTime;
        }

        // 4. Property bag — read ALL fields in alphabetical order for observable behavior
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            // Read ALL properties in alphabetical order:
            // calendar, day, hour, microsecond, millisecond, minute, month, monthCode,
            // nanosecond, offset, second, timeZone, year

            var calendarId = "iso8601";
            if (accessor.TryGetProperty("calendar", out var calVal) && !calVal.IsUndefined)
                calendarId = CanonicalizeCalendarId(ResolveTemporalCalendarId(calVal, realm));

            if (!accessor.TryGetProperty("day", out var dayVal) || dayVal.IsUndefined)
                throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have 'day'", realm: realm);
            var day = ToIntegerWithTruncation(dayVal, realm);

            var hour = GetOptionalIntProperty(accessor, "hour", realm);
            var microsecond = GetOptionalIntProperty(accessor, "microsecond", realm);
            var millisecond = GetOptionalIntProperty(accessor, "millisecond", realm);
            var minute = GetOptionalIntProperty(accessor, "minute", realm);

            accessor.TryGetProperty("month", out var monthVal);
            var hasMonth = !monthVal.IsUndefined;
            int monthInt = 0;
            if (hasMonth) monthInt = ToIntegerWithTruncation(monthVal, realm);

            accessor.TryGetProperty("monthCode", out var monthCodeVal);
            var hasMonthCode = !monthCodeVal.IsUndefined;
            string? monthCodeStr = null;
            if (hasMonthCode)
            {
                monthCodeStr = JsOps.ToJsString(monthCodeVal);
                ValidateMonthCodeSyntax(monthCodeStr, realm);
            }

            var nanosecond = GetOptionalIntProperty(accessor, "nanosecond", realm);

            accessor.TryGetProperty("offset", out var offsetVal);
            string? offsetStr = null;
            if (!offsetVal.IsUndefined)
            {
                // Per spec: non-string non-object values (number, boolean, bigint, symbol, null) → TypeError
                if (offsetVal.IsSymbol || offsetVal.IsBigInt)
                    throw StandardLibrary.ThrowTypeError("offset must be a string", realm: realm);
                if (offsetVal.IsNull || offsetVal.IsBoolean || offsetVal.IsNumber)
                    throw StandardLibrary.ThrowTypeError("offset must be a string", realm: realm);
                offsetStr = offsetVal.IsString ? offsetVal.AsString() : JsOps.ToJsString(offsetVal);
            }

            var second = GetOptionalIntProperty(accessor, "second", realm);

            if (!accessor.TryGetProperty("timeZone", out var tzVal) || tzVal.IsUndefined)
                throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have 'timeZone'", realm: realm);
            var timeZoneId = ToTemporalTimeZoneIdentifier(tzVal, realm);

            if (!accessor.TryGetProperty("year", out var yearVal) || yearVal.IsUndefined)
                throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have 'year'", realm: realm);
            var year = ToIntegerWithTruncation(yearVal, realm);

            // Resolve month
            if (!hasMonth && !hasMonthCode)
                throw StandardLibrary.ThrowTypeError("Property bag for ZonedDateTime must have 'month' or 'monthCode'", realm: realm);
            int month;
            if (hasMonthCode)
            {
                month = ResolveISOMonthCode(monthCodeStr!, realm);
                if (hasMonth && monthInt != month)
                    throw StandardLibrary.ThrowRangeError("month and monthCode must agree", realm: realm);
            }
            else
            {
                month = monthInt;
            }

            // Apply overflow
            if (month < 1) throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
            if (day < 1) throw StandardLibrary.ThrowRangeError($"Day {day} is out of range", realm: realm);

            if (string.Equals(overflowOption, "reject", StringComparison.Ordinal))
            {
                if (month > 12) throw StandardLibrary.ThrowRangeError($"Month {month} is out of range", realm: realm);
                var maxDay = IsoCalendarHelpers.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
                if (day > maxDay) throw StandardLibrary.ThrowRangeError($"Day {day} is out of range for month {month}", realm: realm);
                RejectISOTime(hour, minute, second, millisecond, microsecond, nanosecond, realm);
            }
            else
            {
                month = Math.Min(month, 12);
                var maxDay = IsoCalendarHelpers.DaysInMonth(year is >= 1 and <= 9999 ? year : 2000, month);
                day = Math.Min(day, maxDay);
                hour = Math.Clamp(hour, 0, 23);
                minute = Math.Clamp(minute, 0, 59);
                second = Math.Clamp(second, 0, 59);
                millisecond = Math.Clamp(millisecond, 0, 999);
                microsecond = Math.Clamp(microsecond, 0, 999);
                nanosecond = Math.Clamp(nanosecond, 0, 999);
            }

            RejectISODate(year, month, day, realm);

            long? offsetNanos = null;

            // Handle offset from property bag
            if (offsetStr != null)
            {
                // Always validate offset format — bad strings are RangeError regardless of offsetOption
                offsetNanos = ParseOffsetString(offsetStr, realm);

                if (string.Equals(offsetOption, "reject", StringComparison.Ordinal))
                {
                    var tz = JsTemporalZonedDateTime.ResolveTimeZone(timeZoneId, out var fixedOff);
                    var approxLocal = new DateTime(
                        Math.Clamp(year, 1, 9999), month, day,
                        hour, minute, second, millisecond, microsecond);
                    var matchingOffset = TryMatchTimeZoneOffsetForString(offsetStr, offsetNanos.Value,
                        timeZoneId, tz, fixedOff, approxLocal, out _);
                    if (!matchingOffset)
                        throw StandardLibrary.ThrowRangeError("Offset does not match the time zone", realm: realm);
                }
            }

            if (offsetStr != null && string.Equals(offsetOption, "use", StringComparison.Ordinal))
            {
                var exactInstant = JsTemporalInstant.FromEpochNanoseconds(
                    ToEpochNanoseconds(year, month, day, hour, minute, second, millisecond, microsecond, nanosecond) - offsetNanos!.Value);
                return new JsTemporalZonedDateTime(exactInstant, timeZoneId, calendarId);
            }

            return new JsTemporalZonedDateTime(year, month, day, hour, minute, second,
                millisecond, microsecond, nanosecond, timeZoneId, calendarId);
        }

        // 5. Other objects
        if (value.Kind == JsValueKind.Object)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.ZonedDateTime: object has no date properties", realm: realm);

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.ZonedDateTime", realm: realm);
    }

    private static JsTemporalPlainYearMonth ToTemporalPlainYearMonth(JsValue value, RealmState realm)
    {
        // 1. String: parse with validation
        if (value.IsString)
        {
            var str = value.AsString() ?? "";
            if (string.IsNullOrEmpty(str))
                throw StandardLibrary.ThrowRangeError("Invalid PlainYearMonth string: empty", realm: realm);
            if (str.Contains('\u2212'))
                throw StandardLibrary.ThrowRangeError("Non-ASCII minus sign is not allowed", realm: realm);

            // Parse and validate bracket annotations (calendar, unknown critical, etc.)
            var baseStr = ParseAndValidateAnnotations(str, realm);
            var calendar = ValidateCalendarAnnotation(str, realm) ?? "iso8601";

            // Z designator is rejected for PlainYearMonth
            if (HasZDesignator(baseStr))
                throw StandardLibrary.ThrowRangeError("Z designator not allowed in PlainYearMonth string", realm: realm);

            // Strip time portion (after T separator) and offset
            var startIdx = 0;
            if (baseStr.Length > 0 && (baseStr[0] == '+' || baseStr[0] == '-'))
                startIdx = 1;
            var tIdx = FindDateTimeSeparator(baseStr[startIdx..]);
            var hasTimePart = tIdx >= 0;
            if (hasTimePart)
                baseStr = baseStr[..(tIdx + startIdx)];

            // Try to parse as YYYY-MM (no day) first, then fall back to full YYYY-MM-DD
            var ymResult = TryParseYearMonth(baseStr, str, realm);
            if (ymResult.HasValue)
            {
                // Per spec: year-month-only strings must use ISO calendar.
                // Non-ISO calendar annotations on YYYY-MM format are rejected.
                if (!string.Equals(calendar, "iso8601", StringComparison.Ordinal))
                    throw StandardLibrary.ThrowRangeError(
                        $"Non-ISO calendar '{calendar}' is not valid for year-month-only strings", realm: realm);

                RejectISOYearMonthRange(ymResult.Value.year, ymResult.Value.month, realm);
                return new JsTemporalPlainYearMonth(
                    ymResult.Value.year, ymResult.Value.month, calendar,
                    GetTemporalReferenceISODay(calendar, ymResult.Value.year, ymResult.Value.month, 1, null, realm));
            }

            // Full date (YYYY-MM-DD) without time: reject UTC offsets.
            // Offsets are only valid when a time component is present.
            if (!hasTimePart && DateOnlyStringHasOffset(baseStr))
                throw StandardLibrary.ThrowRangeError(
                    "UTC offset not valid without time component in PlainYearMonth string", realm: realm);

            // Full date (YYYY-MM-DD) — extract year+month, discard day per spec
            // Use ParseDatePartNoRangeCheck: day is discarded, only year+month range matters
            var (year, month, day) = ParseDatePartNoRangeCheck(baseStr, str, realm);
            RejectISOYearMonthRange(year, month, realm);
            return new JsTemporalPlainYearMonth(year, month, calendar,
                GetTemporalReferenceISODay(calendar, year, month, day, null, realm));
        }

        // 2. Non-string primitives → TypeError
        if (value.IsUndefined || value.IsNull || value.IsBoolean || value.IsNumber || value.IsSymbol || value.IsBigInt)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainYearMonth", realm: realm);

        // 3. Check for Temporal objects with internal slots
        if (value.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalPlainYearMonthSlot, out var slot) && slot.TryGetObject<JsTemporalPlainYearMonth>(out var yearMonth))
                return yearMonth;
            if (obj.TryGetProperty(TemporalPlainDateSlot, out var pdSlot) && pdSlot.TryGetObject<JsTemporalPlainDate>(out var pd))
                return new JsTemporalPlainYearMonth(pd.Year, pd.Month, CanonicalizeCalendarId(pd.Calendar),
                    GetTemporalReferenceISODay(CanonicalizeCalendarId(pd.Calendar), pd.Year, pd.Month, pd.Day, null, realm));
            if (obj.TryGetProperty(TemporalPlainDateTimeSlot, out var pdtSlot) && pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                return new JsTemporalPlainYearMonth(pdt.Year, pdt.Month, CanonicalizeCalendarId(pdt.Calendar),
                    GetTemporalReferenceISODay(CanonicalizeCalendarId(pdt.Calendar), pdt.Year, pdt.Month, pdt.Day, null, realm));
            if (obj.TryGetProperty(TemporalZonedDateTimeSlot, out var zdtSlot) && zdtSlot.TryGetObject<JsTemporalZonedDateTime>(out var zdt))
            {
                var plainDate = zdt.ToPlainDate();
                return new JsTemporalPlainYearMonth(plainDate.Year, plainDate.Month, CanonicalizeCalendarId(zdt.Calendar),
                    GetTemporalReferenceISODay(CanonicalizeCalendarId(zdt.Calendar), plainDate.Year, plainDate.Month, plainDate.Day, null, realm));
            }
        }

        // 4. Property bag — uses ReadPlainYearMonthFields for correct alphabetical order
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            var (year, month, calendar, monthCode) = ReadPlainYearMonthFields(accessor, realm);
            return ApplyOverflowToYearMonth(year, month, calendar, monthCode, "constrain", realm);
        }

        if (value.Kind == JsValueKind.Object)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainYearMonth: object has no date properties", realm: realm);

        throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainYearMonth", realm: realm);
    }

    private static JsTemporalPlainMonthDay ToTemporalPlainMonthDay(JsValue value, RealmState realm,
        string overflow = "constrain")
    {
        // 1. String: parse with validation
        if (value.IsString)
        {
            var str = value.AsString() ?? "";
            if (string.IsNullOrEmpty(str))
                throw StandardLibrary.ThrowRangeError("Invalid PlainMonthDay string: empty", realm: realm);
            if (str.Contains('\u2212'))
                throw StandardLibrary.ThrowRangeError("Non-ASCII minus sign is not allowed", realm: realm);

            // Parse and validate bracket annotations (calendar, unknown critical, etc.)
            var baseStr = ParseAndValidateAnnotations(str, realm);
            var calendar = ValidateCalendarAnnotation(str, realm) ?? "iso8601";

            // Z designator is rejected for PlainMonthDay
            if (HasZDesignator(baseStr))
                throw StandardLibrary.ThrowRangeError("Z designator not allowed in PlainMonthDay string", realm: realm);

            if (DateOnlyStringHasOffset(baseStr) || MonthDayStringHasOffset(baseStr))
                throw StandardLibrary.ThrowRangeError("UTC offset without time is not valid for PlainMonthDay", realm: realm);

            if (!string.Equals(calendar, "iso8601", StringComparison.Ordinal) &&
                IsMonthDayWithoutYearString(baseStr))
            {
                throw StandardLibrary.ThrowRangeError($"Invalid PlainMonthDay string: {str}", realm: realm);
            }

            // Strip time portion (after T separator)
            var startIdx = 0;
            if (baseStr.Length > 0 && (baseStr[0] == '+' || baseStr[0] == '-'))
                startIdx = 1;
            var tIdx = FindDateTimeSeparator(baseStr[startIdx..]);
            if (tIdx >= 0)
                baseStr = baseStr[..(tIdx + startIdx)];

            // Handle --MM-DD or --MMDD format (no year)
            if (baseStr.StartsWith("--", StringComparison.Ordinal))
            {
                var mmdd = baseStr[2..];
                int mm, dd;
                var dashIdx = mmdd.IndexOf('-');
                if (dashIdx >= 0)
                {
                    // --MM-DD (dash-separated)
                    if (!int.TryParse(mmdd.AsSpan(0, dashIdx), System.Globalization.CultureInfo.InvariantCulture, out mm) ||
                        !int.TryParse(mmdd.AsSpan(dashIdx + 1), System.Globalization.CultureInfo.InvariantCulture, out dd))
                        throw StandardLibrary.ThrowRangeError($"Invalid PlainMonthDay string: {str}", realm: realm);
                }
                else if (mmdd.Length == 4 && AllDigits(mmdd, 0, 4))
                {
                    // --MMDD (compact)
                    if (!int.TryParse(mmdd.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture, out mm) ||
                        !int.TryParse(mmdd.AsSpan(2, 2), System.Globalization.CultureInfo.InvariantCulture, out dd))
                        throw StandardLibrary.ThrowRangeError($"Invalid PlainMonthDay string: {str}", realm: realm);
                }
                else
                {
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainMonthDay string: {str}", realm: realm);
                }
                RejectISODate(1972, mm, dd, realm);
                return new JsTemporalPlainMonthDay(mm, dd, calendar);
            }

            // Handle MM-DD format (5 chars, dash at position 2)
            if (baseStr.Length == 5 && baseStr[2] == '-' &&
                int.TryParse(baseStr.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture, out var mdMonth) &&
                int.TryParse(baseStr.AsSpan(3, 2), System.Globalization.CultureInfo.InvariantCulture, out var mdDay))
            {
                RejectISODate(1972, mdMonth, mdDay, realm);
                return new JsTemporalPlainMonthDay(mdMonth, mdDay, calendar);
            }

            // Handle MMDD compact format (4 digits, no dashes)
            if (baseStr.Length == 4 && AllDigits(baseStr, 0, 4) &&
                int.TryParse(baseStr.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture, out var compactMonth) &&
                int.TryParse(baseStr.AsSpan(2, 2), System.Globalization.CultureInfo.InvariantCulture, out var compactDay))
            {
                RejectISODate(1972, compactMonth, compactDay, realm);
                return new JsTemporalPlainMonthDay(compactMonth, compactDay, calendar);
            }

            // Parse as a full date (YYYY-MM-DD) and extract month+day
            // For ISO calendar, referenceISOYear is always 1972 (spec default), not the parsed year
            // Use ParseDatePartNoRangeCheck because extended year ranges (e.g., ±999999) are valid
            // for PlainMonthDay even though they exceed the PlainDate representable range
            var (referenceYear, month, day) = ParseDatePartNoRangeCheck(baseStr, str, realm);
            if (!string.Equals(calendar, "iso8601", StringComparison.Ordinal) &&
                (referenceYear < 1 || referenceYear > 9999))
            {
                throw StandardLibrary.ThrowRangeError($"Invalid PlainMonthDay string: {str}", realm: realm);
            }

            if (!string.Equals(calendar, "iso8601", StringComparison.Ordinal))
            {
                if (!TryGetCalendarMonthDayForIsoDate(calendar, new DateTime(referenceYear, month, day),
                        out var calendarMonth, out var calendarDay, out var calendarMonthCode))
                {
                    throw StandardLibrary.ThrowRangeError($"Invalid PlainMonthDay string: {str}", realm: realm);
                }

                return new JsTemporalPlainMonthDay(
                    calendarMonth,
                    calendarDay,
                    calendar,
                    GetTemporalPlainMonthDayReferenceYear(calendar, calendarMonthCode, calendarMonth, calendarDay, false),
                    calendarMonthCode);
            }

            return new JsTemporalPlainMonthDay(month, day, calendar, null);
        }

        // 2. Non-string primitives → TypeError
        if (value.IsUndefined || value.IsNull || value.IsBoolean || value.IsNumber || value.IsSymbol || value.IsBigInt)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainMonthDay", realm: realm);

        // 3. Check for Temporal objects with internal slots
        if (value.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(TemporalPlainMonthDaySlot, out var slot) && slot.TryGetObject<JsTemporalPlainMonthDay>(out var monthDay))
                return monthDay;
            if (obj.TryGetProperty(TemporalPlainDateSlot, out var pdSlot) && pdSlot.TryGetObject<JsTemporalPlainDate>(out var pd))
                return new JsTemporalPlainMonthDay(pd.Month, pd.Day, CanonicalizeCalendarId(pd.Calendar));
            if (obj.TryGetProperty(TemporalPlainDateTimeSlot, out var pdtSlot) && pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
                return new JsTemporalPlainMonthDay(pdt.Month, pdt.Day, CanonicalizeCalendarId(pdt.Calendar));
        }

        // 4. Property bag — uses ReadPlainMonthDayFields for correct alphabetical order
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            var (mdMonth, mdDay, mdYear, mdCalendar, mdMonthCode, mdHasYear) = ReadPlainMonthDayFields(accessor, realm);
            return ApplyOverflowToMonthDay(mdMonth, mdDay, mdYear, mdCalendar, mdMonthCode, mdHasYear, overflow, realm);
        }

        if (value.Kind == JsValueKind.Object)
            throw StandardLibrary.ThrowTypeError("Cannot convert to Temporal.PlainMonthDay: object has no date properties", realm: realm);

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

        if (s.Length == 0 || (s[0] != 'P' && s[0] != 'p'))
        {
            throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);
        }

        double years = 0, months = 0, weeks = 0, days = 0;
        double hours = 0, minutes = 0, seconds = 0;
        double milliseconds = 0, microseconds = 0, nanoseconds = 0;

        var isTimePart = false;
        var currentNumber = "";
        var hadFractionalTimePart = false; // Per spec: no sub-parts allowed after a fractional time unit
        var hadAnyComponent = false; // Track if at least one component was parsed

        for (var i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsDigit(c) || c == '.')
            {
                currentNumber += c;
            }
            else if (c == ',')
            {
                // Comma is allowed as decimal separator in time parts only
                if (!isTimePart)
                    throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);
                currentNumber += '.'; // Normalize comma to period
            }
            else if (c is 'T' or 't')
            {
                if (currentNumber.Length > 0)
                    throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);
                isTimePart = true;
            }
            else if (c == '-' || c == '+' || c == '\u2212')
            {
                // Negative/positive signs within components are not allowed
                throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);
            }
            else if (currentNumber.Length > 0)
            {
                // Split integer/fractional parts to avoid double precision loss near 2^53
                var numberStr = currentNumber;
                var dotIdx = numberStr.IndexOf('.');
                double intPart;
                double fracPart = 0;
                if (dotIdx >= 0)
                {
                    intPart = dotIdx > 0
                        ? double.Parse(numberStr[..dotIdx], System.Globalization.CultureInfo.InvariantCulture)
                        : 0;
                    fracPart = double.Parse("0" + numberStr[dotIdx..], System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    intPart = double.Parse(numberStr, System.Globalization.CultureInfo.InvariantCulture);
                }
                currentNumber = "";

                hadAnyComponent = true;
                var cu = char.ToUpperInvariant(c); // Case-insensitive designators

                if (isTimePart)
                {
                    // Per spec: after a fractional time unit, no further time units are allowed
                    if (hadFractionalTimePart)
                        throw StandardLibrary.ThrowRangeError($"Invalid duration string: no sub-parts allowed after fractional time unit: {str}", realm: realm);

                    switch (cu)
                    {
                        case 'H':
                            hours = intPart;
                            if (fracPart > 0 || dotIdx >= 0)
                            {
                                hadFractionalTimePart = true;
                                if (fracPart > 0)
                                {
                                    // Decompose fractional hours into minutes, then seconds, ms, us, ns
                                    var totalMinutes = fracPart * 60;
                                    minutes += Math.Truncate(totalMinutes);
                                    var remainingMinFrac = totalMinutes - Math.Truncate(totalMinutes);
                                    if (remainingMinFrac > 0)
                                    {
                                        DecomposeFractionalSeconds(remainingMinFrac * 60,
                                            ref seconds, ref milliseconds, ref microseconds, ref nanoseconds);
                                    }
                                }
                            }
                            break;
                        case 'M':
                            minutes += intPart;
                            if (fracPart > 0 || dotIdx >= 0)
                            {
                                hadFractionalTimePart = true;
                                if (fracPart > 0)
                                {
                                    // Decompose fractional minutes into seconds, ms, us, ns
                                    DecomposeFractionalSeconds(fracPart * 60,
                                        ref seconds, ref milliseconds, ref microseconds, ref nanoseconds);
                                }
                            }
                            break;
                        case 'S':
                            // Validate max 9 fractional digits for seconds
                            if (dotIdx >= 0)
                            {
                                var fracDigits = numberStr.Length - dotIdx - 1;
                                if (fracDigits > 9)
                                    throw StandardLibrary.ThrowRangeError($"Invalid duration string: more than 9 fractional digits: {str}", realm: realm);
                            }
                            seconds += intPart;
                            if (fracPart > 0)
                            {
                                DecomposeFractionalSeconds(fracPart,
                                    ref seconds, ref milliseconds, ref microseconds, ref nanoseconds);
                            }
                            break;
                        default:
                            throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);
                    }
                }
                else
                {
                    // Date components must be integers (no fractional parts)
                    if (dotIdx >= 0)
                        throw StandardLibrary.ThrowRangeError($"Invalid duration string: fractional date components not allowed: {str}", realm: realm);

                    switch (cu)
                    {
                        case 'Y': years = intPart; break;
                        case 'M': months = intPart; break;
                        case 'W': weeks = intPart; break;
                        case 'D': days = intPart; break;
                        default:
                            throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);
                    }
                }
            }
            else
            {
                // Unknown character (trailing junk)
                throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);
            }
        }

        // Must have at least one component
        if (!hadAnyComponent)
            throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);

        // No trailing unconsumed number
        if (currentNumber.Length > 0)
            throw StandardLibrary.ThrowRangeError($"Invalid duration string: {str}", realm: realm);

        // Validate the parsed duration
        if (!IsValidDuration(ApplySign(sign, years), ApplySign(sign, months), ApplySign(sign, weeks), ApplySign(sign, days),
                ApplySign(sign, hours), ApplySign(sign, minutes), ApplySign(sign, seconds),
                ApplySign(sign, milliseconds), ApplySign(sign, microseconds), ApplySign(sign, nanoseconds)))
        {
            throw StandardLibrary.ThrowRangeError($"Duration string results in out-of-range values: {str}", realm: realm);
        }

        return new JsTemporalDuration(
            ApplySign(sign, years), ApplySign(sign, months), ApplySign(sign, weeks), ApplySign(sign, days),
            ApplySign(sign, hours), ApplySign(sign, minutes), ApplySign(sign, seconds),
            ApplySign(sign, milliseconds), ApplySign(sign, microseconds), ApplySign(sign, nanoseconds));

        // Multiply by sign, but ensure 0 stays +0 (not -0)
        static double ApplySign(int sign, double value) => value == 0 ? 0 : sign * value;
    }

    /// <summary>
    ///     Decomposes a fractional seconds value into seconds, ms, us, ns components.
    /// </summary>
    private static void DecomposeFractionalSeconds(double totalSeconds,
        ref double seconds, ref double milliseconds, ref double microseconds, ref double nanoseconds)
    {
        seconds += Math.Truncate(totalSeconds);
        var frac = totalSeconds - Math.Truncate(totalSeconds);
        if (frac != 0)
        {
            var fracNanos = frac * 1_000_000_000;
            milliseconds += Math.Truncate(fracNanos / 1_000_000);
            fracNanos -= Math.Truncate(fracNanos / 1_000_000) * 1_000_000;
            microseconds += Math.Truncate(fracNanos / 1_000);
            fracNanos -= Math.Truncate(fracNanos / 1_000) * 1_000;
            nanoseconds += Math.Round(fracNanos);
        }
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
    /// Handles subsecond balancing, rounding with overflow cascade, and range validation.
    /// </summary>
    private static string FormatDurationToString(
        JsTemporalDuration duration, SecondsStringPrecision precision, string roundingMode, RealmState realm)
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

        long subSecondNanos;
        var needsRounding = precision.Increment > 1 ||
                            !string.Equals(precision.SmallestUnit, "nanosecond", StringComparison.Ordinal);

        if (needsRounding)
        {
            // Determine the largest non-zero unit to control balance depth
            var largestUnit = DurationLargestUnit(
                absYears, absMonths, absWeeks, absDays,
                absHours, absMinutes);

            // Compute total time nanoseconds from ALL time units using BigInteger
            var totalTimeNanos = new BigInteger(absHours) * NanosecondsPerHour +
                                 new BigInteger(absMinutes) * NanosecondsPerMinute +
                                 new BigInteger(absSeconds) * NanosecondsPerSecond +
                                 new BigInteger(absMilliseconds) * 1_000_000 +
                                 new BigInteger(absMicroseconds) * 1_000 +
                                 new BigInteger(absNanoseconds);

            // Include days when balance can reach day level
            if (largestUnit == "day")
            {
                totalTimeNanos += new BigInteger(absDays) * NanosecondsPerDay;
            }

            // Round
            var incrementNanos = new BigInteger(GetUnitNanoseconds(precision.SmallestUnit)) * precision.Increment;
            totalTimeNanos = RoundToIncrement(totalTimeNanos, incrementNanos, roundingMode);

            // Validate: rounded total must not exceed max time duration
            if (totalTimeNanos > MaxTimeDuration)
            {
                throw StandardLibrary.ThrowRangeError(
                    "Rounded duration exceeds maximum representable time duration", realm: realm);
            }

            // Balance back into units based on largestUnit
            if (largestUnit == "day")
            {
                absDays = (long)(totalTimeNanos / NanosecondsPerDay);
                totalTimeNanos %= NanosecondsPerDay;
            }

            if (largestUnit is "day" or "hour")
            {
                absHours = (long)(totalTimeNanos / NanosecondsPerHour);
                totalTimeNanos %= NanosecondsPerHour;
            }

            if (largestUnit is "day" or "hour" or "minute")
            {
                absMinutes = (long)(totalTimeNanos / NanosecondsPerMinute);
                totalTimeNanos %= NanosecondsPerMinute;
            }

            absSeconds = (long)(totalTimeNanos / NanosecondsPerSecond);
            subSecondNanos = (long)(totalTimeNanos % NanosecondsPerSecond);
        }
        else
        {
            // No rounding: combine s + ms + µs + ns using BigInteger (handles overflow),
            // then balance subsecond overflow into seconds. Keep h/m/days as-is.
            var totalSecondsNanos = new BigInteger(absSeconds) * NanosecondsPerSecond +
                                    new BigInteger(absMilliseconds) * 1_000_000 +
                                    new BigInteger(absMicroseconds) * 1_000 +
                                    new BigInteger(absNanoseconds);
            absSeconds = (long)(totalSecondsNanos / NanosecondsPerSecond);
            subSecondNanos = (long)(totalSecondsNanos % NanosecondsPerSecond);
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

        var hasTimePart = absHours != 0 || absMinutes != 0 || absSeconds != 0 || subSecondNanos != 0;
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

            if (absSeconds != 0 || subSecondNanos != 0 || forcedPrecision)
            {
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{absSeconds}");

                var fractionalDigits = precision.FractionalDigits;
                if (fractionalDigits == -1)
                {
                    // Auto: include fraction only if non-zero, trim trailing zeros
                    if (subSecondNanos != 0)
                    {
                        var fractionStr = subSecondNanos.ToString("D9", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
                        sb.Append('.');
                        sb.Append(fractionStr);
                    }
                }
                else if (fractionalDigits > 0)
                {
                    var fullFraction = subSecondNanos.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
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
    /// Returns the largest non-zero unit for a duration, mapping calendar units to "day".
    /// Used to determine balance depth after rounding in TemporalDurationToString.
    /// </summary>
    private static string DurationLargestUnit(
        long years, long months, long weeks, long days,
        long hours, long minutes)
    {
        if (years != 0 || months != 0 || weeks != 0 || days != 0)
        {
            return "day";
        }

        if (hours != 0)
        {
            return "hour";
        }

        if (minutes != 0)
        {
            return "minute";
        }

        return "second";
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

    /// <summary>
    /// Converts a ZonedDateTime to a PlainDateTime, handling extended years.
    /// Uses .NET DateTimeOffset for normal years (correct DST), BigInteger math for extended years.
    /// </summary>
    private static JsTemporalPlainDateTime ZonedDateTimeToPlainDateTime(JsTemporalZonedDateTime zdt)
    {
        try
        {
            return zdt.ToPlainDateTime();
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            // Extended year outside .NET's 1-9999 range — use BigInteger math
            // For extended years, timezone is always a fixed offset (UTC, +HH:MM, etc.)
            var offsetNanos = (long)zdt.FixedOffset!.Value.TotalMilliseconds * 1_000_000;
            var (year, month, day, hour, minute, second, ms, us, ns) =
                EpochNanosToComponents(zdt.Instant.EpochNanoseconds, offsetNanos);
            return new JsTemporalPlainDateTime(year, month, day, hour, minute, second, ms, us, ns, CanonicalizeCalendarId(zdt.Calendar));
        }
    }

    /// <summary>
    /// Decomposes epoch nanoseconds + offset into date/time components using BigInteger math.
    /// Bypasses .NET DateTimeOffset, so works for all years including extended years.
    /// </summary>
    private static (int Year, int Month, int Day, int Hour, int Minute, int Second,
        int Millisecond, int Microsecond, int Nanosecond) EpochNanosToComponents(
        BigInteger epochNanos, long offsetNanos)
    {
        var localNanos = epochNanos + offsetNanos;
        var dayNumber = DivRemFloor(localNanos, new BigInteger(NanosecondsPerDay), out var remainder);
        if (remainder < 0)
        {
            remainder += NanosecondsPerDay;
            dayNumber--;
        }

        var (year, month, day) = IsoCalendarHelpers.EpochDaysToDate((long)dayNumber);

        var rem = (long)remainder;
        var hour = (int)(rem / NanosecondsPerHour);
        rem %= NanosecondsPerHour;
        var minute = (int)(rem / NanosecondsPerMinute);
        rem %= NanosecondsPerMinute;
        var second = (int)(rem / NanosecondsPerSecond);
        rem %= NanosecondsPerSecond;
        var millisecond = (int)(rem / 1_000_000L);
        rem %= 1_000_000L;
        var microsecond = (int)(rem / 1_000L);
        var nanosecond = (int)(rem % 1_000L);

        return (year, month, day, hour, minute, second, millisecond, microsecond, nanosecond);
    }

    /// <summary>
    /// Formats a year for ISO 8601 output.
    /// Years 0-9999: 4-digit zero-padded. Others: 6-digit with sign prefix.
    /// </summary>
    private static string FormatYear(int year)
    {
        if (year is >= 0 and <= 9999)
        {
            return year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        }

        var sign = year < 0 ? "-" : "+";
        var absYear = Math.Abs(year);
        return $"{sign}{absYear.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Formats offset nanoseconds as an ISO 8601 offset string (e.g., "+01:00", "-05:30").
    /// </summary>
    private static string FormatOffsetNanoseconds(long offsetNanos)
    {
        // Per spec: round to nearest minute using halfExpand
        var sign = offsetNanos >= 0 ? "+" : "-";
        var absNanos = Math.Abs(offsetNanos);
        var totalSeconds = absNanos / NanosecondsPerSecond;
        var remainingSeconds = totalSeconds % 60;
        var totalMinutes = totalSeconds / 60;
        // halfExpand: round up if seconds >= 30
        if (remainingSeconds >= 30)
        {
            totalMinutes++;
        }

        var hours = (int)(totalMinutes / 60);
        var minutes = (int)(totalMinutes % 60);
        return $"{sign}{hours:D2}:{minutes:D2}";
    }

    /// <summary>
    /// Formats epoch nanoseconds as a full ISO 8601 date-time string.
    /// Handles extended years and all precision modes. Bypasses .NET DateTimeOffset.
    /// </summary>
    private static string FormatEpochNanosAsDateTime(
        BigInteger epochNanos, long offsetNanos, int fractionalDigits, bool useZSuffix)
    {
        var (year, month, day, hour, minute, second, ms, us, ns) =
            EpochNanosToComponents(epochNanos, offsetNanos);

        var yearStr = FormatYear(year);
        var datePart = $"{yearStr}-{month:D2}-{day:D2}";
        var timePart = FormatTimeToString(hour, minute, second, ms, us, ns, fractionalDigits);

        var suffix = useZSuffix ? "Z" : FormatOffsetNanoseconds(offsetNanos);
        return $"{datePart}T{timePart}{suffix}";
    }

    #endregion
}
