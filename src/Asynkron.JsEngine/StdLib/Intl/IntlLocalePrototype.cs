using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.Locale", ToStringTag = "Intl.Locale")]
public sealed partial class IntlLocalePrototype
{
    internal const string BrandKey = "__localeBrand__";
    internal const string TagSlot = "__tag__";
    internal const string LanguageSlot = "__language__";
    internal const string ScriptSlot = "__script__";
    internal const string RegionSlot = "__region__";
    internal const string VariantsSlot = "__variants__";
    internal const string KeywordsSlot = "__keywords__";
    internal const string WeekendSlot = "__weekend__";
    internal const string TextDirectionSlot = "__textDirection__";

    private static readonly string[] DefaultHourCycles = ["h12", "h23"];
    private static readonly string[] DefaultCalendars = ["gregory"];
    private static readonly string[] DefaultNumberingSystems = ["latn"];
    private static readonly int[] DefaultWeekend = [6, 7];
    private static readonly Dictionary<string, int> WeekdayMap = new(StringComparer.Ordinal)
    {
        ["sun"] = 7,
        ["mon"] = 1,
        ["tue"] = 2,
        ["wed"] = 3,
        ["thu"] = 4,
        ["fri"] = 5,
        ["sat"] = 6
    };

    [JsHostGetter("baseName", DisplayName = "get baseName")]
    private JsValue GetBaseName(JsValue thisValue)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        return BuildBaseName(locale);
    }

    [JsHostGetter("language", DisplayName = "get language")]
    private JsValue GetLanguage(JsValue thisValue)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var language = locale.TryGetProperty(LanguageSlot, out var value) && value is string lang && lang.Length > 0
            ? lang
            : "und";
        return language;
    }

    [JsHostGetter("script", DisplayName = "get script")]
    private JsValue GetScript(JsValue thisValue)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        if (locale.TryGetProperty(ScriptSlot, out var value) && value is string script && script.Length > 0)
        {
            return script;
        }

        return JsValue.Undefined;
    }

    [JsHostGetter("region", DisplayName = "get region")]
    private JsValue GetRegion(JsValue thisValue)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        if (locale.TryGetProperty(RegionSlot, out var value) && value is string region && region.Length > 0)
        {
            return region;
        }

        return JsValue.Undefined;
    }

    [JsHostGetter("variants", DisplayName = "get variants")]
    private JsValue GetVariants(JsValue thisValue)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var variants = GetLocaleVariants(locale);
        var result = new JsArray(Realm);
        foreach (var variant in variants)
        {
            result.Push(variant);
        }

        return JsValue.FromObject(result);
    }

    [JsHostGetter("calendar", DisplayName = "get calendar")]
    private JsValue GetCalendar(JsValue thisValue)
    {
        return GetKeywordValue(thisValue, "ca");
    }

    [JsHostGetter("numberingSystem", DisplayName = "get numberingSystem")]
    private JsValue GetNumberingSystem(JsValue thisValue)
    {
        return GetKeywordValue(thisValue, "nu");
    }

    [JsHostGetter("collation", DisplayName = "get collation")]
    private JsValue GetCollation(JsValue thisValue)
    {
        return GetKeywordValue(thisValue, "co");
    }

    [JsHostGetter("hourCycle", DisplayName = "get hourCycle")]
    private JsValue GetHourCycle(JsValue thisValue)
    {
        return GetKeywordValue(thisValue, "hc");
    }

    [JsHostGetter("caseFirst", DisplayName = "get caseFirst")]
    private JsValue GetCaseFirst(JsValue thisValue)
    {
        return GetKeywordValue(thisValue, "kf");
    }

    [JsHostGetter("numeric", DisplayName = "get numeric")]
    private JsValue GetNumeric(JsValue thisValue)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var keywords = GetLocaleKeywords(locale);
        if (keywords.TryGetValue("kn", out var keyword))
        {
            return string.Equals(keyword, "true", StringComparison.Ordinal);
        }

        return JsValue.Undefined;
    }

    [JsHostGetter("firstDayOfWeek", DisplayName = "get firstDayOfWeek")]
    private JsValue GetFirstDayOfWeek(JsValue thisValue)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        return ResolveFirstDayOfWeek(locale);
    }

    [JsHostMethod("getCalendars", Length = 0d)]
    private JsValue GetCalendars(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var preferred = GetKeyword(locale, "ca");
        var result = new JsArray(Realm);
        if (!string.IsNullOrEmpty(preferred))
        {
            result.Push(preferred);
        }

        var supported = IntlUtilities.GetSupportedValues("calendar", Realm);
        foreach (var entry in supported)
        {
            if (!string.Equals(entry, preferred, StringComparison.Ordinal))
            {
                result.Push(entry);
            }
        }

        if (result.Length == 0)
        {
            result.Push(DefaultCalendars[0]);
        }

        return new JsValue(result);
    }

    [JsHostMethod("getCollations", Length = 0d)]
    private JsValue GetCollations(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var result = new JsArray(Realm);
        var preferred = GetKeyword(locale, "co");
        if (!string.IsNullOrEmpty(preferred) && !string.Equals(preferred, "default", StringComparison.Ordinal))
        {
            result.Push(preferred);
        }

        result.Push("default");
        return new JsValue(result);
    }

    [JsHostMethod("getHourCycles", Length = 0d)]
    private JsValue GetHourCycles(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var result = new JsArray(Realm);
        var preferred = GetKeyword(locale, "hc");
        if (!string.IsNullOrEmpty(preferred))
        {
            result.Push(preferred);
        }

        foreach (var cycle in DefaultHourCycles)
        {
            if (!string.Equals(preferred, cycle, StringComparison.Ordinal))
            {
                result.Push(cycle);
            }
        }

        return new JsValue(result);
    }

    [JsHostMethod("getNumberingSystems", Length = 0d)]
    private JsValue GetNumberingSystems(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var result = new JsArray(Realm);
        var preferred = GetKeyword(locale, "nu");
        if (!string.IsNullOrEmpty(preferred))
        {
            result.Push(preferred);
        }

        foreach (var system in DefaultNumberingSystems)
        {
            if (!string.Equals(preferred, system, StringComparison.Ordinal))
            {
                result.Push(system);
            }
        }

        return new JsValue(result);
    }

    [JsHostMethod("getTextInfo", Length = 0d)]
    private JsValue GetTextInfo(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var direction = locale.TryGetProperty(TextDirectionSlot, out var value) && value is string dir && dir.Length > 0
            ? dir
            : "ltr";
        var info = new JsObject(Realm.ObjectPrototype);
        info.SetProperty("direction", direction);
        return new JsValue(info);
    }

    [JsHostMethod("getTimeZones", Length = 0d)]
    private JsValue GetTimeZones(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var hasRegion = locale.TryGetProperty(RegionSlot, out var regionValue) && regionValue is string region &&
                        region.Length > 0;
        if (!hasRegion)
        {
            return JsValue.Undefined;
        }

        var result = new JsArray(Realm);
        var zones = IntlUtilities.GetSupportedValues("timeZone", Realm);
        foreach (var zone in zones)
        {
            result.Push(zone);
        }

        if (result.Length == 0)
        {
            result.Push("UTC");
        }

        return new JsValue(result);
    }

    [JsHostMethod("getWeekInfo", Length = 0d)]
    private JsValue GetWeekInfo(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var info = new JsObject(Realm.ObjectPrototype);
        var firstDay = ResolveFirstDayOfWeek(locale);
        info.SetProperty("firstDay", ConvertWeekdayToNumber(firstDay));

        var weekend = ResolveWeekendDays(locale);
        info.SetProperty("weekend", CreateWeekendArray(weekend));
        info.SetProperty("minimalDays", ResolveMinimalDays(locale));
        return new JsValue(info);
    }

    [JsHostMethod("toString", Length = 0d)]
    private JsValue ToStringLocale(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        return GetCanonicalTag(locale);
    }

    [JsHostMethod("maximize", Length = 0d)]
    private JsValue MaximizeLocale(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var tag = GetCanonicalTag(locale);
        var maximized = IntlLocaleLikelySubtags.AddLikelySubtags(tag);
        return new JsValue(IntlLocaleConstructor.CreateLocaleFromCanonical(maximized, Realm, locale.Prototype));
    }

    [JsHostMethod("minimize", Length = 0d)]
    private JsValue MinimizeLocale(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var tag = GetCanonicalTag(locale);
        var minimized = IntlLocaleLikelySubtags.RemoveLikelySubtags(tag);
        return new JsValue(IntlLocaleConstructor.CreateLocaleFromCanonical(minimized, Realm, locale.Prototype));
    }

    internal static bool TryBuildLocaleIdentifier(JsObject candidate, out string identifier)
    {
        identifier = string.Empty;
        if (!candidate.TryGetProperty(BrandKey, out var marker) || marker is not true)
        {
            return false;
        }

        if (!candidate.TryGetProperty(TagSlot, out var baseTagValue) || baseTagValue is not string tag ||
            string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        identifier = tag;
        return true;
    }

    private JsObject ValidateLocaleReceiver(JsValue thisValue)
    {
        if (thisValue is JsObject obj && obj.TryGetProperty(BrandKey, out var marker) && marker is true)
        {
            return obj;
        }

        throw ThrowTypeError("Intl.Locale method called on incompatible receiver", realm: Realm);
    }

    private JsValue GetKeywordValue(JsValue thisValue, string keyword)
    {
        var locale = ValidateLocaleReceiver(thisValue);
        var value = GetKeyword(locale, keyword);
        return string.IsNullOrEmpty(value) ? JsValue.Undefined : value;
    }

    private static string? GetKeyword(JsObject locale, string keyword)
    {
        var keywords = GetLocaleKeywords(locale);
        return keywords.TryGetValue(keyword, out var value) ? value : null;
    }

    internal static Dictionary<string, string> GetLocaleKeywords(JsObject locale)
    {
        if (locale.TryGetProperty(KeywordsSlot, out var value) && value is Dictionary<string, string> dictionary)
        {
            return dictionary;
        }

        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        locale.SetProperty(KeywordsSlot, empty);
        return empty;
    }

    internal static List<string> GetLocaleVariants(JsObject locale)
    {
        if (locale.TryGetProperty(VariantsSlot, out var value) && value is List<string> list)
        {
            return list;
        }

        var variants = new List<string>();
        locale.SetProperty(VariantsSlot, variants);
        return variants;
    }

    internal static string BuildBaseName(JsObject locale)
    {
        var language = locale.TryGetProperty(LanguageSlot, out var langValue) && langValue is string lang && lang.Length > 0
            ? lang
            : "und";

        var result = language;

        if (locale.TryGetProperty(ScriptSlot, out var scriptValue) && scriptValue is string script &&
            script.Length > 0)
        {
            result += "-" + script;
        }

        if (locale.TryGetProperty(RegionSlot, out var regionValue) && regionValue is string region &&
            region.Length > 0)
        {
            result += "-" + region;
        }

        var variants = GetLocaleVariants(locale);
        if (variants.Count > 0)
        {
            result += "-" + string.Join("-", variants);
        }

        return result;
    }

    private string ResolveFirstDayOfWeek(JsObject locale)
    {
        var keywords = GetLocaleKeywords(locale);
        if (keywords.TryGetValue("fw", out var value) && WeekdayMap.ContainsKey(value))
        {
            return value;
        }

        var region = ResolveLocaleRegion(locale);
        return IntlWeekData.GetFirstDay(region);
    }

    private int[] ResolveWeekendDays(JsObject locale)
    {
        var region = ResolveLocaleRegion(locale);
        if (IntlWeekData.GetWeekend(region) is { } weekend)
        {
            return BuildWeekendRange(weekend.Start, weekend.End);
        }

        return DefaultWeekend;
    }

    private static int ConvertWeekdayToNumber(string weekday)
    {
        return WeekdayMap.TryGetValue(weekday, out var value) ? value : 1;
    }

    private JsArray CreateWeekendArray(IReadOnlyList<int> days)
    {
        var array = new JsArray(Realm);
        foreach (var day in days)
        {
            array.Push(day);
        }

        return array;
    }

    private static int[] BuildWeekendRange(string startToken, string endToken)
    {
        var start = ConvertWeekdayToNumber(startToken);
        var end = ConvertWeekdayToNumber(endToken);
        if (start <= end)
        {
            var length = end - start + 1;
            var range = new int[length];
            for (var i = 0; i < length; i++)
            {
                range[i] = start + i;
            }

            return range;
        }

        var list = new List<int>(7);
        for (var day = start; day <= 7; day++)
        {
            list.Add(day);
        }

        for (var day = 1; day <= end; day++)
        {
            list.Add(day);
        }

        return list.ToArray();
    }

    private int ResolveMinimalDays(JsObject locale)
    {
        var region = ResolveLocaleRegion(locale);
        return IntlWeekData.GetMinimalDays(region);
    }

    private string ResolveLocaleRegion(JsObject locale)
    {
        if (locale.TryGetProperty(RegionSlot, out var value) && value is string region && region.Length > 0)
        {
            return region;
        }

        var canonical = GetCanonicalTag(locale);
        var maximized = IntlLocaleLikelySubtags.AddLikelySubtags(canonical);
        var (_, _, resolvedRegion, _) =
            IntlLocaleConstructor.ParseBaseName(IntlLocaleConstructor.ExtractBaseName(maximized));
        return resolvedRegion ?? string.Empty;
    }

    private string GetCanonicalTag(JsObject locale)
    {
        if (locale.TryGetProperty(TagSlot, out var tag) && tag is string canonical && canonical.Length > 0)
        {
            return canonical;
        }

        throw ThrowTypeError("Intl.Locale has invalid [[LocaleData]]", realm: Realm);
    }
}
