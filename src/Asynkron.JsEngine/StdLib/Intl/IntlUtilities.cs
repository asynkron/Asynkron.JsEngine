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
    private static readonly Lazy<string[]> CurrencyValues = new(BuildSupportedCurrencies);

    private static readonly string[] NumberingSystemValues =
    [
        "adlm", "ahom", "arab", "arabext", "armn", "armnlow", "bali", "beng", "bhks", "brah", "cakm", "cham",
        "cyrl", "deva", "diak", "ethi", "finance", "fullwide", "gara", "geor", "gong", "gonm", "grek", "greklow",
        "gujr", "gukh", "guru", "hanidays", "hanidec", "hans", "hansfin", "hant", "hantfin", "hebr", "hmng",
        "hmnp", "java", "jpan", "jpanfin", "jpanyear", "kali", "kawi", "khmr", "knda", "krai", "lana",
        "lanatham", "laoo", "latn", "lepc", "limb", "mathbold", "mathdbl", "mathmono", "mathsanb", "mathsans",
        "mlym", "modi", "mong", "mroo", "mtei", "mymr", "mymrepka", "mymrpao", "mymrshan", "mymrtlng", "nagm",
        "native", "newa", "nkoo", "olck", "onao", "orya", "osma", "outlined", "rohg", "roman", "romanlow", "saur",
        "segment", "shrd", "sind", "sinh", "sora", "sund", "sunu", "takr", "talu", "taml", "tamldec", "tnsa",
        "telu", "thai", "tirh", "tibt", "traditio", "vaii", "wara", "wcho"
    ];
    private static readonly HashSet<string> NumberingSystemSet = new(NumberingSystemValues, StringComparer.Ordinal);

    private static readonly string[] UnitValues =
    [
        "acre", "bit", "byte", "celsius", "centimeter", "day", "degree", "fahrenheit", "fluid-ounce", "foot",
        "gallon", "gigabit", "gigabyte", "gram", "hectare", "hour", "inch", "kilobit", "kilobyte", "kilogram",
        "kilometer", "liter", "megabit", "megabyte", "meter", "microsecond", "mile", "mile-scandinavian",
        "milliliter", "millimeter", "millisecond", "minute", "month", "nanosecond", "ounce", "percent",
        "petabyte", "pound", "second", "stone", "terabit", "terabyte", "week", "yard", "year"
    ];
    private static readonly HashSet<string> UnitSet = new(UnitValues, StringComparer.Ordinal);
    private static readonly string[] EmptyValues = Array.Empty<string>();
    private static readonly Lazy<HashSet<string>> CurrencySet =
        new(() => new HashSet<string>(CurrencyValues.Value, StringComparer.Ordinal));
    private static readonly Lazy<TimeZoneRegistry> TimeZoneRegistryCache = new(BuildSupportedTimeZones);

    static IntlUtilities()
    {
        Array.Sort(NumberingSystemValues, StringComparer.Ordinal);
    }

    private const long MaxArrayLikeLength = 9007199254740991L;

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

        var seen = new List<string>();

        if (locales is string single)
        {
            AppendCanonicalLocale(seen, single, realm);
            return seen;
        }

        if (!StandardLibrary.TryGetObject(locales, realm, out var localeObject))
        {
            throw StandardLibrary.ThrowTypeError("Intl locale list must be object-like", realm: realm);
        }

        var length = GetArrayLikeLength(localeObject, realm);
        for (long k = 0; k < length; k++)
        {
            var propertyKey = k.ToString(CultureInfo.InvariantCulture);
            if (!StandardLibrary.HasProperty(localeObject, propertyKey))
            {
                continue;
            }

            if (!JsOps.TryGetPropertyValue(localeObject, propertyKey, out var element))
            {
                continue;
            }

            if (element is null || ReferenceEquals(element, Symbol.Undefined))
            {
                continue;
            }

            var tag = StandardLibrary.JsValueToString(element, realm);
            AppendCanonicalLocale(seen, tag, realm);
        }

        return seen;
    }

    private static void AppendCanonicalLocale(ICollection<string> target, string locale, RealmState realm)
    {
        var canonical = CanonicalizeLocale(locale, realm);
        if (!target.Contains(canonical))
        {
            target.Add(canonical);
        }
    }

    private static long GetArrayLikeLength(IJsPropertyAccessor localeObject, RealmState realm)
    {
        if (!JsOps.TryGetPropertyValue(localeObject, "length", out var lengthValue))
        {
            lengthValue = 0d;
        }

        return ToLength(lengthValue, realm);
    }

    private static long ToLength(object? value, RealmState realm)
    {
        var numericContext = realm.CreateContext();
        var number = JsOps.ToNumberWithContext(value, numericContext);
        if (numericContext.IsThrow)
        {
            throw new ThrowSignal(numericContext.FlowValue);
        }

        if (double.IsNaN(number) || number <= 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(number))
        {
            return MaxArrayLikeLength;
        }

        var truncated = Math.Floor(number);
        return truncated > MaxArrayLikeLength ? MaxArrayLikeLength : (long)truncated;
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

    public static bool TryGetCanonicalCurrency(string? code, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrEmpty(code) || code.Length != 3)
        {
            return false;
        }

        Span<char> buffer = stackalloc char[3];
        for (var i = 0; i < 3; i++)
        {
            var ch = code[i];
            if (!char.IsLetter(ch))
            {
                return false;
            }

            buffer[i] = char.ToUpperInvariant(ch);
        }

        canonical = new string(buffer);
        return true;
    }

    public static bool IsSupportedCurrency(string canonical)
    {
        return CurrencySet.Value.Contains(canonical);
    }

    public static bool TryGetCanonicalUnit(string? candidate, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalized = candidate.Trim().ToLowerInvariant();
        if (UnitSet.Contains(normalized))
        {
            canonical = normalized;
            return true;
        }

        return false;
    }

    public static IReadOnlyList<string> GetSupportedValues(string key, RealmState realm)
    {
        return key switch
        {
            "calendar" => CalendarValues,
            "collation" => EmptyValues,
            "currency" => CurrencyValues.Value,
            "numberingSystem" => NumberingSystemValues,
            "timeZone" => GetTimeZoneValues(realm),
            "unit" => UnitValues,
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

    private static string[] BuildSupportedCurrencies()
    {
        var currencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                var symbol = region.ISOCurrencySymbol;
                if (string.IsNullOrEmpty(symbol) || symbol.Length != 3)
                {
                    continue;
                }

                Span<char> buffer = stackalloc char[3];
                var isLetters = true;
                for (var i = 0; i < 3; i++)
                {
                    var ch = symbol[i];
                    if (!char.IsLetter(ch))
                    {
                        isLetters = false;
                        break;
                    }

                    buffer[i] = char.ToUpperInvariant(ch);
                }

                if (isLetters)
                {
                    currencies.Add(new string(buffer));
                }
            }
            catch
            {
                // Some RegionInfo entries throw on unsupported cultures; ignore them and continue.
            }
        }

        if (currencies.Count == 0)
        {
            currencies.Add("USD");
        }

        var list = currencies.ToList();
        list.Sort(StringComparer.Ordinal);
        return list.ToArray();
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
