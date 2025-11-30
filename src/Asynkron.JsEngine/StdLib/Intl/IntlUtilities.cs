using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlUtilities
{
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

        if (string.Equals(tzString, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc.Id;
        }

        // For now, only allow the configured engine time zone to avoid platform-specific names.
        if (string.Equals(tzString, realm.Options.TimeZone.Id, StringComparison.OrdinalIgnoreCase))
        {
            return realm.Options.TimeZone.Id;
        }

        throw StandardLibrary.ThrowRangeError($"Unsupported timeZone '{tzString}'", realm: realm);
    }
}
