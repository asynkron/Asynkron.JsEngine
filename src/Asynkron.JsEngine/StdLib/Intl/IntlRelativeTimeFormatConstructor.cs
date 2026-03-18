#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.RelativeTimeFormat", PrototypeType = typeof(IntlRelativeTimeFormatPrototype), Length = 0d,
    DisplayName = "RelativeTimeFormat")]
public sealed partial class IntlRelativeTimeFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);
        var (_, resolvedLocale) = IntlHelper.ResolveIntlLocales(localesArg, Realm);
        var options = NormalizeOptions(optionsArg);
        var (numberingSystem, finalLocale) = ResolveNumberingSystemAndLocale(options, resolvedLocale);
        var numeric = ReadStringOption(options, "numeric", ["always", "auto"], "always");
        var style = ReadStringOption(options, "style", ["long", "short", "narrow"], "long");

        var instance = PrepareThisObject(thisValue);
        IntlRelativeTimeFormatPrototype.InitializeInternalSlots(instance, finalLocale, numberingSystem, numeric,
            style);
        return new JsValue(instance);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        IntlHelper.ConfigureSupportedLocalesOf(constructor, Realm);
    }

    private JsObject? NormalizeOptions(JsValue optionsArg)
    {
        if (optionsArg.IsNullOrUndefined)
        {
            return null;
        }

        if (optionsArg.IsObject && optionsArg.AsObject() is { } jsObject)
        {
            return jsObject;
        }

        throw StandardLibrary.ThrowTypeError("Intl.RelativeTimeFormat options must be an object", realm: Realm);
    }

    private (string NumberingSystem, string Locale) ResolveNumberingSystemAndLocale(JsObject? options,
        string resolvedLocale)
    {
        // Extract unicode extension numbering system from locale (e.g., "en-u-nu-arab" → "arab")
        var unicodeKeywords = IntlUtilities.ParseUnicodeExtensionKeywords(resolvedLocale);
        string? extensionNu = null;
        if (unicodeKeywords.TryGetValue("nu", out var nuValues) && nuValues.Count > 0)
        {
            extensionNu = nuValues[0];
        }

        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(resolvedLocale);

        // Read numberingSystem option
        string? optionNu = null;
        if (options is not null && options.TryGetProperty("numberingSystem", out var value) &&
            !value.IsUndefined)
        {
            if (!value.TryGetString(out var numberingSystem))
            {
                throw StandardLibrary.ThrowTypeError(
                    "Intl.RelativeTimeFormat numberingSystem option must be a string", realm: Realm);
            }

            // Only use option if it's a valid numbering system
            if (IntlUtilities.TryNormalizeNumberingSystem(numberingSystem, out var canonical))
            {
                optionNu = canonical;
            }
        }

        // Resolution: option > unicode extension > default
        if (optionNu is not null)
        {
            // Option value wins. If it matches the extension, keep extension in locale.
            if (extensionNu is not null &&
                string.Equals(optionNu, extensionNu, StringComparison.Ordinal))
            {
                return (optionNu, resolvedLocale);
            }

            // Option differs from extension or no extension: use base locale
            return (optionNu, baseLocale);
        }

        if (extensionNu is not null &&
            IntlUtilities.TryNormalizeNumberingSystem(extensionNu, out var validExtNu))
        {
            // Unicode extension is valid: keep extension in locale
            return (validExtNu, resolvedLocale);
        }

        // Neither option nor extension is valid: use default, strip extensions
        return ("latn", baseLocale);
    }

    private string ReadStringOption(JsObject? options, string propertyName, IReadOnlyList<string> allowed,
        string defaultValue)
    {
        if (options is null || !options.TryGetProperty(propertyName, out var rawValue) ||
            rawValue.IsUndefined)
        {
            return defaultValue;
        }

        if (!rawValue.TryGetString(out var strValue))
        {
            throw StandardLibrary.ThrowTypeError(
                $"Intl.RelativeTimeFormat {propertyName} option must be a string", realm: Realm);
        }

        if (!allowed.Contains(strValue, StringComparer.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError(
                $"Intl.RelativeTimeFormat {propertyName} option '{strValue}' is not supported", realm: Realm);
        }

        return strValue;
    }
}
