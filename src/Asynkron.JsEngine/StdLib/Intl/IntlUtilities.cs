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
    private static readonly Dictionary<string, string> CalendarAliases = new(StringComparer.Ordinal)
    {
        ["islamicc"] = "islamic-civil"
    };
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

        if (locales.TryGetObject<JsObject>(out var localeObjectCandidate) &&
            IntlLocalePrototype.TryBuildLocaleIdentifier(localeObjectCandidate, out var localeIdentifier))
        {
            AppendCanonicalLocale(seen, localeIdentifier, realm);
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

        if (TryNormalizeTimeZoneOffset(tzString, out var normalizedOffset))
        {
            return normalizedOffset;
        }

        if (TryCanonicalizeTimeZone(tzString, out var canonical))
        {
            return canonical;
        }

        if (TryResolveTimeZoneId(tzString, out var resolvedTimeZoneId))
        {
            return CanonicalizeTimeZoneId(resolvedTimeZoneId);
        }

        if (string.Equals(tzString, realm.Options.TimeZone.Id, StringComparison.OrdinalIgnoreCase))
        {
            return realm.Options.TimeZone.Id;
        }

        throw StandardLibrary.ThrowRangeError($"Unsupported timeZone '{tzString}'", realm: realm);
    }

    private static bool TryNormalizeTimeZoneOffset(string timeZoneId, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(timeZoneId) || (timeZoneId[0] != '+' && timeZoneId[0] != '-'))
        {
            return false;
        }

        var sign = timeZoneId[0];
        var rest = timeZoneId.AsSpan(1);
        int hours;
        int minutes;

        if (rest.Length == 2 &&
            int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out hours))
        {
            minutes = 0;
        }
        else if (rest.Length == 4 &&
                 int.TryParse(rest[..2], NumberStyles.None, CultureInfo.InvariantCulture, out hours) &&
                 int.TryParse(rest[2..], NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
        {
        }
        else if (rest.Length == 5 &&
                 rest[2] == ':' &&
                 int.TryParse(rest[..2], NumberStyles.None, CultureInfo.InvariantCulture, out hours) &&
                 int.TryParse(rest[3..], NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
        {
        }
        else
        {
            return false;
        }

        if (hours > 23 || minutes > 59)
        {
            return false;
        }

        if (hours == 0 && minutes == 0)
        {
            sign = '+';
        }

        normalized = string.Create(
            6,
            (sign, hours, minutes),
            static (span, state) =>
            {
                span[0] = state.sign;
                span[1] = (char)('0' + (state.hours / 10));
                span[2] = (char)('0' + (state.hours % 10));
                span[3] = ':';
                span[4] = (char)('0' + (state.minutes / 10));
                span[5] = (char)('0' + (state.minutes % 10));
            });
        return true;
    }

    internal static bool TryResolveTimeZoneId(string timeZoneId, out string resolvedTimeZoneId)
    {
        try
        {
            resolvedTimeZoneId = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId).Id;
            return true;
        }
        catch
        {
            var normalized = CanonicalizeTimeZoneId(timeZoneId);
            if (!string.Equals(normalized, timeZoneId, StringComparison.Ordinal))
            {
                try
                {
                    resolvedTimeZoneId = TimeZoneInfo.FindSystemTimeZoneById(normalized).Id;
                    return true;
                }
                catch
                {
                }
            }

            if (TimeZoneAliases.TryGetValue(normalized, out var aliasTarget))
            {
                try
                {
                    resolvedTimeZoneId = TimeZoneInfo.FindSystemTimeZoneById(aliasTarget).Id;
                    return true;
                }
                catch
                {
                }
            }

            resolvedTimeZoneId = string.Empty;
            return false;
        }
    }

    public static bool TryNormalizeCalendar(string calendar, out string canonical)
    {
        canonical = calendar?.Trim().ToLowerInvariant() ?? string.Empty;
        if (CalendarAliases.TryGetValue(canonical, out var alias))
        {
            canonical = alias;
        }
        return CalendarSet.Contains(canonical);
    }

    /// <summary>
    /// Validates a calendar value for Intl.Locale which accepts any structurally valid
    /// BCP47 calendar subtag (type = alphanum{3,8} ("-" alphanum{3,8})*),
    /// not just the known/supported set used by DateTimeFormat.
    /// </summary>
    public static bool TryNormalizeCalendarPermissive(string calendar, out string canonical)
    {
        canonical = calendar?.Trim().ToLowerInvariant() ?? string.Empty;
        return IsValidUnicodeTypeNonterminal(canonical);
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
        return NumberingSystemSet.Contains(canonical) || IsUnicodeTypeSequence(canonical);
    }

    public static bool TryNormalizeSupportedNumberingSystem(string? numberingSystem, out string canonical)
    {
        canonical = numberingSystem?.Trim().ToLowerInvariant() ?? string.Empty;
        return NumberingSystemSet.Contains(canonical);
    }

    internal static bool IsUnicodeTypeSequence(string value)
    {
        var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length is < 3 or > 8)
            {
                return false;
            }

            foreach (var ch in part)
            {
                if (!char.IsLetterOrDigit(ch))
                {
                    return false;
                }
            }
        }

        return true;
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
            "collation" => IntlCollatorConstructor.GetSupportedValues(),
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

        foreach (var alias in TimeZoneAliases.Keys)
        {
            AddAlias(alias);
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

        void AddAlias(string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var canonical = CanonicalizeTimeZoneId(id);
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

    /// <summary>
    /// IANA timezone alias map: maps deprecated/alternative names to their canonical form.
    /// See IANA Time Zone Database (backward file) and ECMA-402 CanonicalizeTimeZoneName.
    /// </summary>
    private static readonly Dictionary<string, string> TimeZoneAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Etc/GMT variants → UTC
        { "Etc/GMT", "UTC" },
        { "Etc/UTC", "UTC" },
        { "Etc/GMT+0", "UTC" },
        { "Etc/GMT-0", "UTC" },
        { "Etc/GMT0", "UTC" },
        { "Etc/Greenwich", "UTC" },
        { "Etc/UCT", "UTC" },
        { "Etc/Universal", "UTC" },
        { "Etc/Zulu", "UTC" },
        { "GMT", "UTC" },
        { "GMT+0", "UTC" },
        { "GMT-0", "UTC" },
        { "GMT0", "UTC" },
        { "Greenwich", "UTC" },
        { "UCT", "UTC" },
        { "Universal", "UTC" },
        { "Zulu", "UTC" },

        // Australia aliases
        { "Australia/ACT", "Australia/Sydney" },
        { "Australia/Canberra", "Australia/Sydney" },
        { "Australia/LHI", "Australia/Lord_Howe" },
        { "Australia/NSW", "Australia/Sydney" },
        { "Australia/North", "Australia/Darwin" },
        { "Australia/Queensland", "Australia/Brisbane" },
        { "Australia/South", "Australia/Adelaide" },
        { "Australia/Tasmania", "Australia/Hobart" },
        { "Australia/Victoria", "Australia/Melbourne" },
        { "Australia/West", "Australia/Perth" },
        { "Australia/Yancowinna", "Australia/Broken_Hill" },

        // Americas aliases
        { "Brazil/Acre", "America/Rio_Branco" },
        { "Brazil/DeNoronha", "America/Noronha" },
        { "Brazil/East", "America/Sao_Paulo" },
        { "Brazil/West", "America/Manaus" },
        { "Canada/Atlantic", "America/Halifax" },
        { "Canada/Central", "America/Winnipeg" },
        { "Canada/Eastern", "America/Toronto" },
        { "Canada/Mountain", "America/Edmonton" },
        { "Canada/Newfoundland", "America/St_Johns" },
        { "Canada/Pacific", "America/Vancouver" },
        { "Canada/Saskatchewan", "America/Regina" },
        { "Canada/Yukon", "America/Whitehorse" },
        { "Chile/Continental", "America/Santiago" },
        { "Chile/EasterIsland", "Pacific/Easter" },
        { "Cuba", "America/Havana" },
        { "Jamaica", "America/Jamaica" },
        { "Mexico/BajaNorte", "America/Tijuana" },
        { "Mexico/BajaSur", "America/Mazatlan" },
        { "Mexico/General", "America/Mexico_City" },
        { "US/Alaska", "America/Anchorage" },
        { "US/Aleutian", "America/Adak" },
        { "US/Arizona", "America/Phoenix" },
        { "US/Central", "America/Chicago" },
        { "US/East-Indiana", "America/Indiana/Indianapolis" },
        { "US/Eastern", "America/New_York" },
        { "US/Hawaii", "Pacific/Honolulu" },
        { "US/Indiana-Starke", "America/Indiana/Knox" },
        { "US/Michigan", "America/Detroit" },
        { "US/Mountain", "America/Denver" },
        { "US/Pacific", "America/Los_Angeles" },
        { "US/Samoa", "Pacific/Pago_Pago" },

        // Europe aliases
        { "Eire", "Europe/Dublin" },
        { "Europe/Belfast", "Europe/London" },
        { "Europe/Tiraspol", "Europe/Chisinau" },
        { "GB", "Europe/London" },
        { "GB-Eire", "Europe/London" },
        { "Portugal", "Europe/Lisbon" },
        { "Turkey", "Europe/Istanbul" },
        { "W-SU", "Europe/Moscow" },
        { "WET", "Europe/Lisbon" },
        { "CET", "Europe/Paris" },
        { "EET", "Europe/Bucharest" },
        { "MET", "Europe/Paris" },

        // Asia aliases
        { "Hongkong", "Asia/Hong_Kong" },
        { "Iran", "Asia/Tehran" },
        { "Israel", "Asia/Jerusalem" },
        { "Japan", "Asia/Tokyo" },
        { "ROC", "Asia/Taipei" },
        { "ROK", "Asia/Seoul" },
        { "Singapore", "Asia/Singapore" },
        { "Asia/Calcutta", "Asia/Kolkata" },
        { "Asia/Saigon", "Asia/Ho_Chi_Minh" },
        { "Asia/Katmandu", "Asia/Kathmandu" },
        { "Asia/Thimbu", "Asia/Thimphu" },
        { "Asia/Macao", "Asia/Macau" },
        { "Asia/Dacca", "Asia/Dhaka" },
        { "Asia/Rangoon", "Asia/Yangon" },
        { "Asia/Ashkhabad", "Asia/Ashgabat" },
        { "Asia/Chungking", "Asia/Chongqing" },
        { "Asia/Istanbul", "Europe/Istanbul" },

        // Africa aliases
        { "Africa/Asmera", "Africa/Asmara" },
        { "Africa/Timbuktu", "Africa/Bamako" },
        { "Egypt", "Africa/Cairo" },
        { "Libya", "Africa/Tripoli" },

        // Pacific aliases
        { "Kwajalein", "Pacific/Kwajalein" },
        { "NZ", "Pacific/Auckland" },
        { "NZ-CHAT", "Pacific/Chatham" },
        { "Pacific/Johnston", "Pacific/Honolulu" },
        { "Pacific/Ponape", "Pacific/Pohnpei" },
        { "Pacific/Samoa", "Pacific/Pago_Pago" },
        { "Pacific/Truk", "Pacific/Chuuk" },
        { "Pacific/Yap", "Pacific/Chuuk" },

        // Indian aliases
        { "Indian/Antananarivo", "Africa/Nairobi" },
        { "Indian/Comoro", "Africa/Nairobi" },
        { "Indian/Mayotte", "Africa/Nairobi" },

        // Atlantic aliases
        { "Atlantic/Faeroe", "Atlantic/Faroe" },
        { "Atlantic/Jan_Mayen", "Europe/Berlin" },
        { "Iceland", "Atlantic/Reykjavik" },

        // Arctic
        { "Arctic/Longyearbyen", "Europe/Berlin" },

        // Misc
        { "EST", "America/Panama" },
        { "MST", "America/Phoenix" },
        { "HST", "Pacific/Honolulu" },
        { "CST6CDT", "America/Chicago" },
        { "EST5EDT", "America/New_York" },
        { "MST7MDT", "America/Denver" },
        { "PST8PDT", "America/Los_Angeles" },
        { "PRC", "Asia/Shanghai" },
        { "Poland", "Europe/Warsaw" },
        { "Navajo", "America/Denver" },

        // SystemV aliases (link to canonical)
        { "America/Buenos_Aires", "America/Argentina/Buenos_Aires" },
        { "America/Catamarca", "America/Argentina/Catamarca" },
        { "America/Cordoba", "America/Argentina/Cordoba" },
        { "America/Jujuy", "America/Argentina/Jujuy" },
        { "America/Mendoza", "America/Argentina/Mendoza" },
        { "America/Indianapolis", "America/Indiana/Indianapolis" },
        { "America/Louisville", "America/Kentucky/Louisville" },
        { "America/Knox_IN", "America/Indiana/Knox" },
        { "America/Porto_Acre", "America/Rio_Branco" },
        { "America/Rosario", "America/Argentina/Cordoba" },
        { "America/Virgin", "America/Puerto_Rico" },
        { "America/Atka", "America/Adak" },
        { "America/Ensenada", "America/Tijuana" },
        { "America/Fort_Wayne", "America/Indiana/Indianapolis" },
        { "America/Shiprock", "America/Denver" },

        // Europe renames
        { "Europe/Kiev", "Europe/Kyiv" },
        { "Europe/Uzhgorod", "Europe/Kyiv" },
        { "Europe/Zaporozhye", "Europe/Kyiv" },
        { "Europe/Nicosia", "Asia/Nicosia" },
    };

    /// <summary>
    /// Looks up a timezone ID in the IANA alias map and returns the canonical form.
    /// </summary>
    internal static bool TryCanonicalizeTimeZoneAlias(string id, out string canonical)
    {
        return TimeZoneAliases.TryGetValue(id, out canonical!);
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

        if (TryResolveRegularGrandfatheredAlias(ref language, variants))
        {
            variants.Sort(StringComparer.Ordinal);
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

                var attributes = new List<string>(Math.Max(0, j - extensionStart - 1));
                for (var k = extensionStart + 1; k < j; k++)
                {
                    attributes.Add(subtags[k].ToLowerInvariant());
                }

                attributes.Sort(StringComparer.Ordinal);

                var keywords = new SortedDictionary<string, string>(StringComparer.Ordinal);

                while (j < i)
                {
                    var keyStart = j;
                    j++;
                    while (j < i && subtags[j].Length > 2)
                    {
                        j++;
                    }

                    var attributeKey = subtags[keyStart].ToLowerInvariant();
                    var value = JoinSubtags(subtags, keyStart + 1, j).ToLowerInvariant();
                    if (IntlLocaleData.UnicodeMappings.TryGetValue(attributeKey, out var unicodeMap) &&
                        unicodeMap.TryGetValue(value, out var mappedValue))
                    {
                        value = mappedValue;
                    }

                    keywords[attributeKey] = value;
                }

                extension = "u";
                foreach (var attribute in attributes)
                {
                    extension += "-" + attribute;
                }

                foreach (var (attributeKey, value) in keywords)
                {
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
            .Where(static kvp => kvp.Key == "fw" || !string.IsNullOrWhiteSpace(kvp.Value))
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
                if (string.Equals(key, "fw", StringComparison.Ordinal) && string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(value) && !string.Equals(value, "true", StringComparison.Ordinal))
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
                    if (string.Equals(kvp.Key, "fw", StringComparison.Ordinal) && string.IsNullOrEmpty(kvp.Value))
                    {
                        continue;
                    }

                    if (!string.Equals(kvp.Value, "true", StringComparison.Ordinal))
                    {
                        output.AddRange(kvp.Value.Split('-'));
                    }
                }
            }
        }

        return string.Join('-', output);
    }

    private static bool TryResolveRegularGrandfatheredAlias(ref string language, ICollection<string> variants)
    {
        var currentVariants = variants as List<string> ?? variants.ToList();
        for (var i = 0; i < currentVariants.Count; i++)
        {
            var candidate = language + "-" + currentVariants[i];
            if (!IntlLocaleData.TagMappings.TryGetValue(candidate, out var replacement))
            {
                continue;
            }

            language = replacement;
            if (variants is List<string> variantList)
            {
                variantList.RemoveAt(i);
            }
            else
            {
                currentVariants.RemoveAt(i);
                variants.Clear();
                foreach (var item in currentVariants)
                {
                    variants.Add(item);
                }
            }

            return true;
        }

        return false;
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

    /// <summary>
    /// Maps numbering system IDs to the Unicode code point of their zero digit.
    /// Most numbering systems have contiguous digit blocks (0-9 = zeroCodePoint + 0..9).
    /// </summary>
    private static readonly Dictionary<string, int> NumberingSystemZeroDigits = new(StringComparer.Ordinal)
    {
        ["adlm"] = 0x1E950,
        ["ahom"] = 0x11730,
        ["arab"] = 0x0660,
        ["arabext"] = 0x06F0,
        ["bali"] = 0x1B50,
        ["beng"] = 0x09E6,
        ["bhks"] = 0x11C50,
        ["brah"] = 0x11066,
        ["cakm"] = 0x11136,
        ["cham"] = 0xAA50,
        ["deva"] = 0x0966,
        ["diak"] = 0x11950,
        ["fullwide"] = 0xFF10,
        ["gong"] = 0x11DA0,
        ["gonm"] = 0x11D50,
        ["gujr"] = 0x0AE6,
        ["guru"] = 0x0A66,
        ["hmng"] = 0x16B50,
        ["hmnp"] = 0x1E140,
        ["java"] = 0xA9D0,
        ["kali"] = 0xA900,
        ["kawi"] = 0x11F50,
        ["khmr"] = 0x17E0,
        ["knda"] = 0x0CE6,
        ["lana"] = 0x1A80,
        ["lanatham"] = 0x1A90,
        ["laoo"] = 0x0ED0,
        ["lepc"] = 0x1C40,
        ["limb"] = 0x1946,
        ["mathbold"] = 0x1D7CE,
        ["mathdbl"] = 0x1D7D8,
        ["mathmono"] = 0x1D7F6,
        ["mathsanb"] = 0x1D7EC,
        ["mathsans"] = 0x1D7E2,
        ["mlym"] = 0x0D66,
        ["modi"] = 0x11650,
        ["mong"] = 0x1810,
        ["mroo"] = 0x16A60,
        ["mtei"] = 0xABF0,
        ["mymr"] = 0x1040,
        ["mymrshan"] = 0x1090,
        ["mymrtlng"] = 0xA9F0,
        ["nagm"] = 0x1E4F0,
        ["newa"] = 0x11450,
        ["nkoo"] = 0x07C0,
        ["olck"] = 0x1C50,
        ["orya"] = 0x0B66,
        ["osma"] = 0x104A0,
        ["rohg"] = 0x10D30,
        ["saur"] = 0xA8D0,
        ["segment"] = 0x1FBF0,
        ["shrd"] = 0x111D0,
        ["sind"] = 0x112F0,
        ["sinh"] = 0x0DE6,
        ["sora"] = 0x110F0,
        ["sund"] = 0x1BB0,
        ["takr"] = 0x116C0,
        ["talu"] = 0x19D0,
        ["tamldec"] = 0x0BE6,
        ["telu"] = 0x0C66,
        ["thai"] = 0x0E50,
        ["tibt"] = 0x0F20,
        ["tirh"] = 0x114D0,
        ["tnsa"] = 0x16AC0,
        ["vaii"] = 0xA620,
        ["wara"] = 0x118E0,
        ["wcho"] = 0x1E2F0
    };

    /// <summary>
    /// Translate Latin digits (0-9) in a string to the target numbering system's digits.
    /// </summary>
    internal static string TranslateDigits(string input, string numberingSystem)
    {
        if (numberingSystem is "latn" || string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (numberingSystem is "hanidec")
        {
            return TranslateHanidecDigits(input);
        }

        if (!NumberingSystemZeroDigits.TryGetValue(numberingSystem, out var zeroCodePoint))
        {
            return input;
        }

        var sb = new System.Text.StringBuilder(input.Length * 2);
        foreach (var ch in input)
        {
            if (ch >= '0' && ch <= '9')
            {
                var digit = ch - '0';
                sb.Append(char.ConvertFromUtf32(zeroCodePoint + digit));
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Translate Latin digits to Chinese numeral characters (hanidec system).
    /// These are not contiguous in Unicode, so each must be mapped individually.
    /// </summary>
    private static string TranslateHanidecDigits(string input)
    {
        ReadOnlySpan<char> hanDigits = ['〇', '一', '二', '三', '四', '五', '六', '七', '八', '九'];
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch >= '0' && ch <= '9')
            {
                sb.Append(hanDigits[ch - '0']);
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the appropriate decimal separator for a given numbering system.
    /// Arabic numbering systems use the Arabic decimal separator (U+066B).
    /// </summary>
    internal static string GetDecimalSeparator(string numberingSystem)
    {
        return numberingSystem is "arab" or "arabext" ? "\u066B" : ".";
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
