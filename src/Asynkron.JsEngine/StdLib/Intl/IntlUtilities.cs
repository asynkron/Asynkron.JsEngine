using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlUtilities
{
    private static readonly string[] CalendarValues =
    [
        "buddhist", "chinese", "coptic", "dangi", "ethioaa", "ethiopic", "gregory", "hebrew", "indian",
        "islamic", "islamic-civil", "islamic-rgsa", "islamic-tbla", "islamic-umalqura", "iso8601", "japanese",
        "persian", "roc"
    ];
    private static readonly HashSet<string> CalendarSet = new(CalendarValues, StringComparer.Ordinal);

    private static readonly string[] NumberingSystemValues =
    [
        "adlm", "ahom", "arab", "arabext", "bali", "beng", "bhks", "brah", "cakm", "cham", "deva", "diak",
        "fullwide", "gong", "gonm", "gujr", "guru", "hanidec", "hmng", "hmnp", "java", "kali", "kawi", "khmr",
        "knda", "lana", "lanatham", "laoo", "latn", "lepc", "limb", "mathbold", "mathdbl", "mathmono",
        "mathsanb", "mathsans", "mlym", "modi", "mong", "mroo", "mtei", "mymr", "mymrshan", "mymrtlng", "nagm",
        "newa", "nkoo", "olck", "orya", "osma", "rohg", "saur", "segment", "shrd", "sind", "sinh", "sora",
        "sund", "takr", "talu", "tamldec", "telu", "thai", "tibt", "tirh", "tnsa", "vaii", "wara", "wcho"
    ];
    private static readonly HashSet<string> NumberingSystemSet = new(NumberingSystemValues, StringComparer.Ordinal);

    private static readonly string[] EmptyValues = Array.Empty<string>();
    private static readonly Lazy<TimeZoneRegistry> TimeZoneRegistryCache = new(BuildSupportedTimeZones);

    public static IReadOnlyList<string> CanonicalizeLocaleList(object? locales, RealmState realm)
    {
        if (locales is null)
        {
            throw StandardLibrary.ThrowTypeError("Intl locale list cannot be null", realm: realm);
        }

        if (ReferenceEquals(locales, Symbol.Undefined))
        {
            return Array.Empty<string>();
        }

        if (locales is string singleLocale)
        {
            return new[] { CanonicalizeLocale(singleLocale, realm) };
        }

        if (locales is JsArray jsArray)
        {
            var result = new List<string>(jsArray.Items.Count);
            foreach (var entry in jsArray.Items)
            {
                if (entry is null || ReferenceEquals(entry, Symbol.Undefined))
                {
                    continue;
                }

                if (entry is string entryLocale)
                {
                    result.Add(CanonicalizeLocale(entryLocale, realm));
                    continue;
                }

                result.Add(CanonicalizeLocale(entry.ToString() ?? string.Empty, realm));
            }

            return result;
        }

        throw StandardLibrary.ThrowTypeError("Intl locale list must be a string or array", realm: realm);
    }

    public static string CanonicalizeLocale(string locale, RealmState realm)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            throw StandardLibrary.ThrowRangeError("Invalid locale", realm: realm);
        }

        var normalized = locale.Trim();
        if (normalized.Contains('_', StringComparison.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError("Invalid locale", realm: realm);
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(normalized);
            return culture.Name;
        }
        catch (CultureNotFoundException)
        {
            throw StandardLibrary.ThrowRangeError($"Invalid locale: {normalized}", realm: realm);
        }
    }

    public static string ResolveRequestedLocale(IReadOnlyList<string> requestedLocales)
    {
        if (requestedLocales.Count > 0)
        {
            return requestedLocales[0];
        }

        return CultureInfo.CurrentCulture.Name;
    }

    public static string NormalizeTimeZone(object? option, RealmState realm)
    {
        if (option is null || ReferenceEquals(option, Symbol.Undefined))
        {
            return realm.Options.TimeZone.Id;
        }

        if (option is not string tzString)
        {
            throw StandardLibrary.ThrowTypeError("Intl.DateTimeFormat timeZone option must be a string", realm: realm);
        }

        if (TryCanonicalizeTimeZone(tzString, out var canonical))
        {
            return canonical;
        }

        if (string.Equals(tzString, realm.Options.TimeZone.Id, StringComparison.OrdinalIgnoreCase))
        {
            return realm.Options.TimeZone.Id;
        }

        throw StandardLibrary.ThrowRangeError($"Unsupported timeZone '{tzString}'", realm: realm);
    }

    public static bool TryNormalizeCalendar(string calendar, out string canonical)
    {
        canonical = calendar?.Trim().ToLowerInvariant() ?? string.Empty;
        return CalendarSet.Contains(canonical);
    }

    public static bool TryNormalizeNumberingSystem(string? numberingSystem, out string canonical)
    {
        canonical = numberingSystem?.Trim().ToLowerInvariant() ?? string.Empty;
        return NumberingSystemSet.Contains(canonical);
    }

    public static IReadOnlyList<string> GetSupportedValues(string key, RealmState realm)
    {
        return key switch
        {
            "calendar" => CalendarValues,
            "collation" => EmptyValues,
            "currency" => EmptyValues,
            "numberingSystem" => NumberingSystemValues,
            "timeZone" => GetTimeZoneValues(realm),
            "unit" => EmptyValues,
            _ => throw StandardLibrary.ThrowRangeError(
                $"Unsupported Intl.supportedValuesOf key '{key}'", realm: realm)
        };
    }

    private static IReadOnlyList<string> GetTimeZoneValues(RealmState realm)
    {
        var registry = TimeZoneRegistryCache.Value;
        var requestedZone = realm.Options.TimeZone.Id;
        if (string.IsNullOrEmpty(requestedZone))
        {
            return registry.Values;
        }

        var canonical = CanonicalizeTimeZoneId(requestedZone);
        if (registry.Members.Contains(canonical))
        {
            return registry.Values;
        }

        var copy = registry.Values.ToList();
        copy.Add(canonical);
        copy.Sort(StringComparer.Ordinal);
        return copy;
    }

    private static bool TryCanonicalizeTimeZone(string id, out string canonical)
    {
        var registry = TimeZoneRegistryCache.Value;
        if (registry.Lookup.TryGetValue(id, out canonical!))
        {
            return true;
        }

        var normalized = CanonicalizeTimeZoneId(id);
        if (registry.Lookup.TryGetValue(normalized, out canonical!))
        {
            return true;
        }

        canonical = string.Empty;
        return false;
    }

    private static TimeZoneRegistry BuildSupportedTimeZones()
    {
        var zones = new SortedSet<string>(StringComparer.Ordinal) { "UTC" };
        var members = new HashSet<string>(StringComparer.Ordinal) { "UTC" };
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UTC"] = "UTC"
        };

        void AddZone(string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var canonical = CanonicalizeTimeZoneId(id);
            if (zones.Add(canonical))
            {
                members.Add(canonical);
            }

            lookup[canonical] = canonical;
            lookup[id] = canonical;
        }

        try
        {
            foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
            {
                AddZone(tz.Id);
            }
        }
        catch
        {
            // Fall back to the minimal UTC-only set on unsupported platforms.
        }

        foreach (var etcZone in BuildEtcGmtZones())
        {
            AddZone(etcZone);
        }

        return new TimeZoneRegistry(zones.ToArray(), members, lookup);
    }

    private static IEnumerable<string> BuildEtcGmtZones()
    {
        // Spec requires supporting the Etc/GMT +/- offsets along with UTC.
        for (var offset = 1; offset <= 12; offset++)
        {
            yield return $"Etc/GMT+{offset}";
        }

        for (var offset = 1; offset <= 14; offset++)
        {
            yield return $"Etc/GMT-{offset}";
        }
    }

    private static string CanonicalizeTimeZoneId(string id)
    {
        if (string.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "UTC";
        }

        return id.Replace(' ', '_');
    }

    private sealed class TimeZoneRegistry(IReadOnlyList<string> values, HashSet<string> members,
        Dictionary<string, string> lookup)
    {
        public IReadOnlyList<string> Values { get; } = values;
        public HashSet<string> Members { get; } = members;
        public Dictionary<string, string> Lookup { get; } = lookup;
    }
}
