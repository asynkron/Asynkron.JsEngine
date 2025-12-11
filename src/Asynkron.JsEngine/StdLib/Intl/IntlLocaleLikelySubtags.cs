namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlLocaleLikelySubtags
{
    public static string AddLikelySubtags(string locale)
    {
        var baseName = IntlLocaleConstructor.ExtractBaseName(locale);
        var extension = locale.Length > baseName.Length ? locale[baseName.Length..] : string.Empty;
        var (language, script, region, variants) = IntlLocaleConstructor.ParseBaseName(baseName);
        var variantSuffix = IntlLocaleConstructor.BuildVariantSuffix(variants);

        if (!RequiresReplacement(language) && !string.IsNullOrEmpty(script) && !string.IsNullOrEmpty(region))
        {
            return baseName + extension;
        }

        if (!TryResolve(language, script, region, out var resolved))
        {
            return baseName + extension;
        }

        var maximizedBase = resolved + variantSuffix;
        return maximizedBase + extension;
    }

    public static string RemoveLikelySubtags(string locale)
    {
        var baseName = IntlLocaleConstructor.ExtractBaseName(locale);
        var extension = locale.Length > baseName.Length ? locale[baseName.Length..] : string.Empty;
        var (_, _, _, variants) = IntlLocaleConstructor.ParseBaseName(baseName);
        var variantSuffix = IntlLocaleConstructor.BuildVariantSuffix(variants);

        var maximized = AddLikelySubtags(baseName);
        var maximizedBase = IntlLocaleConstructor.ExtractBaseName(maximized);
        var (languageMax, scriptMax, regionMax, _) = IntlLocaleConstructor.ParseBaseName(maximizedBase);

        foreach (var trial in EnumerateTrials(languageMax, scriptMax, regionMax))
        {
            var trialMaximized = AddLikelySubtags(trial);
            var trialBase = IntlLocaleConstructor.ExtractBaseName(trialMaximized);
            if (string.Equals(trialBase, maximizedBase, StringComparison.Ordinal))
            {
                return trial + variantSuffix + extension;
            }
        }

        return maximizedBase + variantSuffix + extension;
    }

    private static IEnumerable<string> EnumerateTrials(string language, string? script, string? region)
    {
        if (!string.IsNullOrEmpty(language))
        {
            yield return IntlLocaleConstructor.BuildBaseTag(language, null, null);
        }

        if (!string.IsNullOrEmpty(language) && !string.IsNullOrEmpty(region))
        {
            yield return IntlLocaleConstructor.BuildBaseTag(language, null, region);
        }

        if (!string.IsNullOrEmpty(language) && !string.IsNullOrEmpty(script))
        {
            yield return IntlLocaleConstructor.BuildBaseTag(language, script, null);
        }
    }

    private static bool TryResolve(string language, string? script, string? region, out string resolved)
    {
        var lookupLanguage = NormalizeLanguage(language);
        string? match = null;

        foreach (var key in EnumerateLookupKeys(lookupLanguage, script, region))
        {
            if (key is not null && IntlLikelySubtagsData.TryResolve(key, out var candidate))
            {
                match = candidate;
                break;
            }
        }

        if (match is null)
        {
            resolved = string.Empty;
            return false;
        }

        var (matchLanguage, matchScript, matchRegion, _) = IntlLocaleConstructor.ParseBaseName(match);
        var resolvedLanguage = RequiresReplacement(language) ? matchLanguage : lookupLanguage;
        var resolvedScript = string.IsNullOrEmpty(script) ? matchScript : script;
        var resolvedRegion = string.IsNullOrEmpty(region) ? matchRegion : region;

        resolved = IntlLocaleConstructor.BuildBaseTag(resolvedLanguage, resolvedScript, resolvedRegion);
        return true;
    }

    private static IEnumerable<string?> EnumerateLookupKeys(string language, string? script, string? region)
    {
        yield return IntlLocaleConstructor.BuildBaseTag(language, script, region);

        if (!string.IsNullOrEmpty(script))
        {
            yield return IntlLocaleConstructor.BuildBaseTag(language, script, null);
        }

        if (!string.IsNullOrEmpty(region))
        {
            yield return IntlLocaleConstructor.BuildBaseTag(language, null, region);
        }

        yield return IntlLocaleConstructor.BuildBaseTag(language, null, null);
    }

    private static bool RequiresReplacement(string language)
    {
        return string.IsNullOrEmpty(language) || string.Equals(language, "und", StringComparison.Ordinal);
    }

    private static string NormalizeLanguage(string language)
    {
        return RequiresReplacement(language) ? "und" : language;
    }
}
