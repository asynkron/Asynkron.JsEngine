#region

using System.Globalization;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

internal static partial class IntlUtilities
{
    private const long MaxArrayLikeLength = 9007199254740991L;

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
    private static readonly string[] EmptyValues = [];

    private static readonly Lazy<HashSet<string>> CurrencySet =
        new(static () => new HashSet<string>(CurrencyValues.Value, StringComparer.Ordinal));

    private static readonly Lazy<TimeZoneRegistry> TimeZoneRegistryCache = new(BuildSupportedTimeZones);
    private static readonly RealmState CanonicalizationRealm = new() { Options = JsEngineOptions.Default };
    private static readonly Lazy<HashSet<string>> AvailableLocales = new(BuildAvailableLocales);
    private static readonly Lazy<string> DefaultLocale = new(DetermineDefaultLocale);

    private static readonly Regex LanguageTagRegex = MyRegex();

    private static readonly Regex DuplicateSingletonRegex = MyRegex1();

    private static readonly Regex DuplicateVariantRegex = MyRegex2();

    private static readonly Regex TransformKeyRegex = MyRegex3();

    static IntlUtilities()
    {
        Array.Sort(NumberingSystemValues, StringComparer.Ordinal);
    }

    public static IReadOnlyList<string> CanonicalizeLocaleList(JsValue locales, RealmState realm)
    {
        if (locales.Kind == JsValueKind.Undefined)
        {
            return [];
        }

        if (locales.Kind == JsValueKind.Null)
        {
            throw StandardLibrary.ThrowTypeError("Intl locale list cannot be null", realm: realm);
        }

        var seen = new List<string>();

        if (locales is { Kind: JsValueKind.String, ObjectValue: string single })
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

            if (!JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(localeObject), propertyKey, out var elementJs))
            {
                continue;
            }

            var tag = ResolveLocaleEntry(elementJs, realm);
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
        if (!JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(localeObject), "length", out var lengthValueJs))
        {
            lengthValueJs = JsValue.FromDouble(0d);
        }

        return ToLength(lengthValueJs, realm);
    }

    private static long ToLength(JsValue value, RealmState realm)
    {
        var numericContext = realm.CreateContext();
        var number = JsOps.ToNumber(value, numericContext);
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
        if (locale.Length == 0)
        {
            throw StandardLibrary.ThrowRangeError("Invalid locale", realm: realm);
        }

        if (!IsStructurallyValidLanguageTag(locale))
        {
            throw StandardLibrary.ThrowRangeError($"Invalid locale: {locale}", realm: realm);
        }

        return CanonicalizeLanguageTag(locale);
    }

    public static string ResolveRequestedLocale(IReadOnlyList<string> requestedLocales)
    {
        foreach (var locale in requestedLocales)
        {
            var baseName = RemoveUnicodeExtensions(locale);
            var match = BestAvailableLocale(baseName);
            if (match is not null)
            {
                var extension = ExtractUnicodeExtension(locale);
                return extension.Length == 0 ? match : match + extension;
            }
        }

        return DefaultLocale.Value;
    }

    public static IReadOnlyList<string> FilterSupportedLocales(IReadOnlyList<string> requestedLocales)
    {
        if (requestedLocales.Count == 0)
        {
            return [];
        }

        var supported = new List<string>(requestedLocales.Count);
        foreach (var locale in requestedLocales)
        {
            var baseName = RemoveUnicodeExtensions(locale);
            if (BestAvailableLocale(baseName) is not null)
            {
                supported.Add(locale);
            }
        }

        return supported;
    }

    public static string NormalizeTimeZone(JsValue option, RealmState realm)
    {
        if (option.IsNullOrUndefined)
        {
            return realm.Options.TimeZone.Id;
        }

        if (!option.TryGetString(out var tzString))
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

    /// <summary>
    /// Validates that a string matches the Unicode Locale Identifier type nonterminal:
    /// type = alphanum{3,8} ("-" alphanum{3,8})*
    /// alphanum = [a-zA-Z0-9]
    /// </summary>
    public static bool IsValidUnicodeTypeNonterminal(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = value.Split('-');
        foreach (var part in parts)
        {
            if (part.Length < 3 || part.Length > 8)
            {
                return false;
            }

            foreach (var ch in part)
            {
                if (!((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')))
                {
                    return false;
                }
            }
        }

        return true;
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
            // Must be ASCII letter only (not Unicode letters like ı)
            if (!((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z')))
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

        // Per spec, unit identifiers must be well-formed (lowercase ASCII and hyphens only)
        foreach (var ch in candidate)
        {
            if (!((ch >= 'a' && ch <= 'z') || ch == '-'))
            {
                return false;
            }
        }

        // Simple unit
        if (UnitSet.Contains(candidate))
        {
            canonical = candidate;
            return true;
        }

        // Compound unit: simpleUnit "-per-" simpleUnit
        const string perSeparator = "-per-";
        var perIndex = candidate.IndexOf(perSeparator, StringComparison.Ordinal);
        if (perIndex > 0)
        {
            var numerator = candidate[..perIndex];
            var denominator = candidate[(perIndex + perSeparator.Length)..];
            if (UnitSet.Contains(numerator) && UnitSet.Contains(denominator))
            {
                canonical = candidate;
                return true;
            }
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
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["UTC"] = "UTC" };

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

    private static bool IsStructurallyValidLanguageTag(string locale)
    {
        if (!LanguageTagRegex.IsMatch(locale))
        {
            return false;
        }

        var privateSplit = locale.Split(["-x-"], StringSplitOptions.None);
        var head = privateSplit[0];
        return !DuplicateSingletonRegex.IsMatch(head) && !DuplicateVariantRegex.IsMatch(head);
    }

    private static string CanonicalizeLanguageTag(string locale)
    {
        var lower = locale.ToLowerInvariant();
        if (IntlLocaleData.TagMappings.TryGetValue(lower, out var tagReplacement))
        {
            return tagReplacement;
        }

        var subtags = lower.Split('-');
        var i = 0;
        var language = string.Empty;
        string? script = null;
        string? region = null;

        while (i < subtags.Length)
        {
            var subtag = subtags[i];
            if (i == 0)
            {
                language = subtag;
            }
            else if (subtag.Length == 2 || subtag.Length == 3)
            {
                region = subtag.ToUpperInvariant();
            }
            else if (subtag.Length == 4 && (subtag[0] < '0' || subtag[0] > '9'))
            {
                script = char.ToUpperInvariant(subtag[0]) + subtag[1..];
            }
            else
            {
                break;
            }

            i++;
        }

        if (IntlLocaleData.LanguageMappings.TryGetValue(language, out var languageReplacement))
        {
            language = languageReplacement;
        }
        else if (IntlLocaleData.ComplexLanguageMappings.TryGetValue(language, out var complexLanguage))
        {
            language = complexLanguage.Language;
            if (script is null && complexLanguage.Script is not null)
            {
                script = complexLanguage.Script;
            }

            if (region is null && complexLanguage.Region is not null)
            {
                region = complexLanguage.Region;
            }
        }

        if (region is not null)
        {
            if (IntlLocaleData.RegionMappings.TryGetValue(region, out var regionReplacement))
            {
                region = regionReplacement;
            }
            else if (IntlLocaleData.ComplexRegionMappings.TryGetValue(region, out var complexRegion))
            {
                var mappingKey = language;
                if (script is not null)
                {
                    mappingKey += "-" + script;
                }

                if (complexRegion.TryGetValue(mappingKey, out var mappedRegion))
                {
                    region = mappedRegion;
                }
                else if (complexRegion.TryGetValue(language, out var languageRegion))
                {
                    region = languageRegion;
                }
                else
                {
                    region = complexRegion["default"];
                }
            }
        }

        var variants = new List<string>();
        while (i < subtags.Length && subtags[i].Length > 1)
        {
            var variant = subtags[i];
            if (IntlLocaleData.VariantMappings.TryGetValue(variant, out var variantMapping))
            {
                switch (variantMapping.Type)
                {
                    case "language":
                        language = variantMapping.Replacement.ToLowerInvariant();
                        break;
                    case "region":
                        region = variantMapping.Replacement;
                        break;
                    case "variant":
                        variants.Add(variantMapping.Replacement);
                        break;
                }
            }
            else
            {
                variants.Add(variant);
            }

            i++;
        }

        variants.Sort(StringComparer.Ordinal);
        if (variants.Contains("alalc97"))
        {
            variants.Remove("hepburn");
        }

        var extensions = new List<string>();
        while (i < subtags.Length && !string.Equals(subtags[i], "x", StringComparison.Ordinal))
        {
            var extensionStart = i;
            i++;
            while (i < subtags.Length && subtags[i].Length > 1)
            {
                i++;
            }

            var key = subtags[extensionStart];
            string extension;
            if (string.Equals(key, "u", StringComparison.Ordinal))
            {
                var j = extensionStart + 1;
                while (j < i && subtags[j].Length > 2)
                {
                    j++;
                }

                extension = JoinSubtags(subtags, extensionStart, j);

                while (j < i)
                {
                    var keyStart = j;
                    j++;
                    while (j < i && subtags[j].Length > 2)
                    {
                        j++;
                    }

                    var attributeKey = subtags[keyStart];
                    var value = JoinSubtags(subtags, keyStart + 1, j);
                    if (IntlLocaleData.UnicodeMappings.TryGetValue(attributeKey, out var unicodeMap) &&
                        unicodeMap.TryGetValue(value, out var mappedValue))
                    {
                        value = mappedValue;
                    }

                    extension += "-" + attributeKey;
                    if (!string.IsNullOrEmpty(value) && !string.Equals(value, "true", StringComparison.Ordinal))
                    {
                        extension += "-" + value;
                    }
                }
            }
            else if (string.Equals(key, "t", StringComparison.Ordinal))
            {
                var j = extensionStart + 1;
                while (j < i && !TransformKeyRegex.IsMatch(subtags[j]))
                {
                    j++;
                }

                var keywordMap = new SortedDictionary<string, string>(StringComparer.Ordinal);
                var transformLanguage = JoinSubtags(subtags, extensionStart + 1, j);

                while (j < i)
                {
                    var keyStart = j;
                    j++;
                    while (j < i && subtags[j].Length > 2)
                    {
                        j++;
                    }

                    var transformKey = subtags[keyStart];
                    var value = JoinSubtags(subtags, keyStart + 1, j);
                    if (IntlLocaleData.TransformMappings.TryGetValue(transformKey, out var transformMap) &&
                        transformMap.TryGetValue(value, out var mappedValue))
                    {
                        value = mappedValue;
                    }

                    keywordMap[transformKey] = value;
                }

                extension = "t";
                if (!string.IsNullOrEmpty(transformLanguage))
                {
                    extension += "-" + CanonicalizeLanguageTag(transformLanguage).ToLowerInvariant();
                }

                foreach (var kvp in keywordMap)
                {
                    extension += "-" + kvp.Key;
                    if (!string.IsNullOrEmpty(kvp.Value))
                    {
                        extension += "-" + kvp.Value;
                    }
                }
            }
            else
            {
                extension = JoinSubtags(subtags, extensionStart, i);
            }

            extensions.Add(extension);
        }

        extensions.Sort(StringComparer.Ordinal);

        string? privateUse = null;
        if (i < subtags.Length)
        {
            privateUse = JoinSubtags(subtags, i, subtags.Length);
        }

        var canonical = language;
        if (script is not null)
        {
            canonical += "-" + script;
        }

        if (region is not null)
        {
            canonical += "-" + region;
        }

        if (variants.Count > 0)
        {
            canonical += "-" + string.Join('-', variants);
        }

        if (extensions.Count > 0)
        {
            canonical += "-" + string.Join('-', extensions);
        }

        if (privateUse is not null)
        {
            canonical += "-" + privateUse;
        }

        return canonical;
    }

    private static string JoinSubtags(string[] subtags, int start, int endExclusive)
    {
        if (endExclusive <= start)
        {
            return string.Empty;
        }

        return string.Join('-', subtags, start, endExclusive - start);
    }

    private static string DetermineDefaultLocale()
    {
        var cultureName = CultureInfo.CurrentCulture.Name;
        if (string.IsNullOrEmpty(cultureName))
        {
            return "en";
        }

        if (TryCanonicalizeLocaleTag(cultureName, out var canonical))
        {
            var baseName = RemoveUnicodeExtensions(canonical);
            var match = BestAvailableLocale(baseName);
            return match ?? canonical;
        }

        return "en";
    }

    private static HashSet<string> BuildAvailableLocales()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            if (string.IsNullOrEmpty(culture.Name))
            {
                continue;
            }

            if (!TryCanonicalizeLocaleTag(culture.Name, out var canonical))
            {
                continue;
            }

            var baseName = RemoveUnicodeExtensions(canonical);
            if (!string.IsNullOrEmpty(baseName))
            {
                set.Add(baseName);
                var (language, script, region, _) = IntlLocaleConstructor.ParseBaseName(baseName);
                if (!string.IsNullOrEmpty(language) && !string.IsNullOrEmpty(region) &&
                    !string.IsNullOrEmpty(script))
                {
                    var languageRegion = IntlLocaleConstructor.BuildBaseTag(language, null, region);
                    set.Add(languageRegion);
                }
            }
        }

        set.Add("en");

        return set;
    }

    private static bool TryCanonicalizeLocaleTag(string locale, out string canonical)
    {
        try
        {
            canonical = CanonicalizeLocale(locale, CanonicalizationRealm);
            return true;
        }
        catch (ThrowSignal)
        {
            canonical = string.Empty;
            return false;
        }
    }

    internal static string RemoveUnicodeExtensions(string locale)
    {
        var unicodeIndex = locale.IndexOf("-u-", StringComparison.Ordinal);
        if (unicodeIndex >= 0)
        {
            locale = locale[..unicodeIndex];
        }

        var privateIndex = locale.IndexOf("-x-", StringComparison.Ordinal);
        if (privateIndex >= 0)
        {
            locale = locale[..privateIndex];
        }

        return locale;
    }

    private static string ExtractUnicodeExtension(string locale)
    {
        var unicodeIndex = locale.IndexOf("-u-", StringComparison.Ordinal);
        if (unicodeIndex < 0)
        {
            return string.Empty;
        }

        var privateIndex = locale.IndexOf("-x-", StringComparison.Ordinal);
        if (privateIndex >= 0 && unicodeIndex > privateIndex)
        {
            return string.Empty;
        }

        return locale[unicodeIndex..];
    }

    public static IReadOnlyDictionary<string, List<string>> ParseUnicodeExtensionKeywords(string locale)
    {
        var extension = ExtractUnicodeExtension(locale);
        if (string.IsNullOrEmpty(extension))
        {
            return new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }

        var subtags = extension.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (subtags.Length == 0 || !string.Equals(subtags[0], "u", StringComparison.Ordinal))
        {
            return new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }

        var keywords = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var index = 1;
        while (index < subtags.Length)
        {
            var subtag = subtags[index];
            if (string.Equals(subtag, "x", StringComparison.Ordinal))
            {
                break;
            }

            if (subtag.Length == 2)
            {
                index++;
                var values = new List<string>();
                while (index < subtags.Length)
                {
                    var value = subtags[index];
                    if (value.Length <= 2)
                    {
                        break;
                    }

                    values.Add(value);
                    index++;
                }

                keywords[subtag] = values;
            }
            else
            {
                index++;
            }
        }

        return keywords;
    }

    public static CultureInfo ResolveCulture(string locale)
    {
        var baseName = RemoveUnicodeExtensions(locale);
        foreach (var candidate in EnumerateCultureCandidates(baseName))
        {
            if (TryGetCulture(candidate, out var culture))
            {
                return culture;
            }
        }

        return CultureInfo.InvariantCulture;
    }

    private static IEnumerable<string> EnumerateCultureCandidates(string baseName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Add(baseName))
        {
            yield return baseName;
        }

        var (language, script, region, _) = IntlLocaleConstructor.ParseBaseName(baseName);
        var canonicalBase = IntlLocaleConstructor.BuildBaseTag(language, script, region);
        if (Add(canonicalBase))
        {
            yield return canonicalBase;
        }

        if (!string.IsNullOrEmpty(script))
        {
            var languageScript = IntlLocaleConstructor.BuildBaseTag(language, script, null);
            if (Add(languageScript))
            {
                yield return languageScript;
            }
        }

        if (!string.IsNullOrEmpty(region))
        {
            var languageRegion = IntlLocaleConstructor.BuildBaseTag(language, null, region);
            if (Add(languageRegion))
            {
                yield return languageRegion;
            }
        }

        if (Add(language))
        {
            yield return language;
        }

        var maximized = IntlLocaleLikelySubtags.AddLikelySubtags(baseName);
        var maximizedBase = IntlLocaleConstructor.ExtractBaseName(maximized);
        if (Add(maximizedBase))
        {
            yield return maximizedBase;
        }

        yield break;

        bool Add(string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                return false;
            }

            return seen.Add(candidate);
        }
    }

    private static bool TryGetCulture(string candidate, out CultureInfo culture)
    {
        try
        {
            culture = CultureInfo.GetCultureInfo(candidate);
            return true;
        }
        catch (CultureNotFoundException)
        {
            candidate = candidate.Replace('-', '_');
            try
            {
                culture = CultureInfo.GetCultureInfo(candidate);
                return true;
            }
            catch (CultureNotFoundException)
            {
                culture = CultureInfo.InvariantCulture;
                return false;
            }
        }
    }

    private static string? BestAvailableLocale(string locale)
    {
        var candidate = locale;
        var available = AvailableLocales.Value;

        while (true)
        {
            if (available.Contains(candidate))
            {
                return candidate;
            }

            var pos = candidate.LastIndexOf('-');
            if (pos < 0)
            {
                return null;
            }

            if (pos >= 2 && candidate[pos - 2] == '-')
            {
                pos -= 2;
            }

            candidate = candidate[..pos];
        }
    }

    internal static string ApplyUnicodeLocaleOverrides(string baseTag, IReadOnlyDictionary<string, string> overrides)
    {
        if (string.IsNullOrEmpty(baseTag) || overrides.Count == 0)
        {
            return baseTag;
        }

        var filteredOverrides = overrides
            .Where(static kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.Ordinal);

        if (filteredOverrides.Count == 0)
        {
            return baseTag;
        }

        var subtags = baseTag.Split('-');
        var output = new List<string>(subtags.Length + filteredOverrides.Count * 2);
        var unicodeProcessed = false;

        for (var i = 0; i < subtags.Length;)
        {
            var current = subtags[i];
            output.Add(current);
            i++;

            if (!string.Equals(current, "u", StringComparison.OrdinalIgnoreCase) || unicodeProcessed)
            {
                continue;
            }

            unicodeProcessed = true;
            var attributes = new List<string>();
            var keywords = new Dictionary<string, string>(StringComparer.Ordinal);

            while (i < subtags.Length)
            {
                var next = subtags[i];
                if (next.Length == 1)
                {
                    break;
                }

                if (next.Length >= 3)
                {
                    attributes.Add(next);
                    i++;
                    continue;
                }

                i++;
                var typeParts = new List<string>();
                while (i < subtags.Length && subtags[i].Length > 2)
                {
                    typeParts.Add(subtags[i]);
                    i++;
                }

                var value = string.Join('-', typeParts);
                keywords[next] = value;
            }

            foreach (var kvp in filteredOverrides)
            {
                keywords[kvp.Key] = kvp.Value;
            }

            if (attributes.Count == 0 && keywords.Count == 0)
            {
                output.RemoveAt(output.Count - 1);
                continue;
            }

            attributes.Sort(StringComparer.Ordinal);
            output.AddRange(attributes);

            foreach (var key in keywords.Keys.OrderBy(static k => k, StringComparer.Ordinal))
            {
                output.Add(key);
                var value = keywords[key];
                if (!string.IsNullOrEmpty(value))
                {
                    output.AddRange(value.Split('-'));
                }
            }
        }

        if (!unicodeProcessed)
        {
            var ordered = filteredOverrides.OrderBy(static kvp => kvp.Key, StringComparer.Ordinal).ToList();
            if (ordered.Count > 0)
            {
                output.Add("u");
                foreach (var kvp in ordered)
                {
                    output.Add(kvp.Key);
                    output.AddRange(kvp.Value.Split('-'));
                }
            }
        }

        return string.Join('-', output);
    }

    private static string ResolveLocaleEntry(JsValue candidate, RealmState realm)
    {
        if (candidate.IsNullOrUndefined)
        {
            throw StandardLibrary.ThrowTypeError(
                "Intl locale list entries must be strings or Intl.Locale objects", realm: realm);
        }

        // Reject primitive numeric types and BigInt
        if (candidate.Kind is JsValueKind.Number or JsValueKind.Boolean or JsValueKind.BigInt)
        {
            throw StandardLibrary.ThrowTypeError(
                "Intl locale list entries must be strings or Intl.Locale objects", realm: realm);
        }

        if (candidate.TryGetObject<JsObject>(out var jsObject) &&
            IntlLocalePrototype.TryBuildLocaleIdentifier(jsObject, out var localeIdentifier))
        {
            return localeIdentifier;
        }

        return candidate.ToJsString();
    }

    private sealed class TimeZoneRegistry(
        IReadOnlyList<string> values,
        HashSet<string> members,
        Dictionary<string, string> lookup)
    {
        public IReadOnlyList<string> Values { get; } = values;
        public HashSet<string> Members { get; } = members;
        public Dictionary<string, string> Lookup { get; } = lookup;
    }

    [GeneratedRegex(@"^(([a-z]{2,3}|[a-z]{5,8})(-([a-z]{4}))?(-([a-z]{2}|[0-9]{3}))?(-([a-z0-9]{5,8}|(?:[0-9][a-z0-9]{3})))*(-((u((-([a-z0-9][a-z](-[a-z0-9]{3,8})*))+|((-([a-z0-9]{3,8}))+(-([a-z0-9][a-z](-[a-z0-9]{3,8})*))*)))|(t((-(([a-z]{2,3}|[a-z]{5,8})(-([a-z]{4}))?(-([a-z]{2}|[0-9]{3}))?(-([a-z0-9]{5,8}|(?:[0-9][a-z0-9]{3})))*)(-([a-z][0-9](-[a-z0-9]{3,8})+))*)|(-([a-z][0-9](-[a-z0-9]{3,8})+))+))|(([0-9]|[a-sv-wy-z])(-[a-z0-9]{2,8})+)))*(-(x(-[a-z0-9]{1,8})+))?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "sv-SE")]
    private static partial Regex MyRegex();
    [GeneratedRegex(@"-([0-9]|[a-wy-z])-(.*-)?\1(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled, "sv-SE")]
    private static partial Regex MyRegex1();
    [GeneratedRegex(@"([a-z0-9]{2,8}-)+([a-z0-9]{5,8}|(?:[0-9][a-z0-9]{3}))-([a-z0-9]{2,8}-)*\2(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled, "sv-SE")]
    private static partial Regex MyRegex2();
    [GeneratedRegex(@"^[a-z][0-9]$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "sv-SE")]
    private static partial Regex MyRegex3();
}
