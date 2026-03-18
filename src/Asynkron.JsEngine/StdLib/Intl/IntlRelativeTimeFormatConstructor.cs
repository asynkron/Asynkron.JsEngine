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
        // Step 1: If NewTarget is undefined, throw a TypeError exception.
        if (!thisValue.IsObject || thisValue.AsObject() is not { IsConstructing: true })
        {
            throw StandardLibrary.ThrowTypeError(
                "Constructor Intl.RelativeTimeFormat requires 'new'", realm: Realm);
        }

        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);
        var (_, resolvedLocale) = IntlHelper.ResolveIntlLocales(localesArg, Realm);

        // Per spec: Let options be ? GetOptionsObject(options).
        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "RelativeTimeFormat");

        // Read options in spec order: localeMatcher, numberingSystem, style, numeric
        // Step 7: localeMatcher
        _ = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "RelativeTimeFormat",
            ["lookup", "best fit"], "best fit");

        // Step 8-9: numberingSystem (custom validation - not a fixed list)
        var numberingSystem = ResolveNumberingSystem(options, resolvedLocale);

        // Step 14: style
        var style = IntlOptionHelpers.GetStringOption(options, "style", Realm, "RelativeTimeFormat",
            ["long", "short", "narrow"], "long");

        // Step 16: numeric
        var numeric = IntlOptionHelpers.GetStringOption(options, "numeric", Realm, "RelativeTimeFormat",
            ["always", "auto"], "always");

        var instance = PrepareThisObject(thisValue);
        IntlRelativeTimeFormatPrototype.InitializeInternalSlots(instance, numberingSystem.Locale,
            numberingSystem.NumberingSystem, numeric, style);
        return new JsValue(instance);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        IntlHelper.ConfigureSupportedLocalesOf(constructor, Realm);
    }

    private (string NumberingSystem, string Locale) ResolveNumberingSystem(IJsPropertyAccessor? options,
        string resolvedLocale)
    {
        // Extract unicode extension numbering system from locale (e.g., "en-u-nu-arab" -> "arab")
        var unicodeKeywords = IntlUtilities.ParseUnicodeExtensionKeywords(resolvedLocale);
        string? extensionNu = null;
        if (unicodeKeywords.TryGetValue("nu", out var nuValues) && nuValues.Count > 0)
        {
            extensionNu = nuValues[0];
        }

        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(resolvedLocale);

        // Read numberingSystem option with ToString conversion (per spec: GetOption with type "string")
        string? optionNu = null;
        if (options is not null && options.TryGetProperty("numberingSystem", out var rawValue) &&
            !rawValue.IsUndefined)
        {
            var numberingSystem = StandardLibrary.JsValueToString(rawValue, Realm);

            // Per spec step 8a: If numberingSystem does not match the type sequence
            // (from UTS 35 Unicode Locale Identifier, section 3.2), throw a RangeError exception.
            if (!IntlUtilities.IsValidUnicodeTypeNonterminal(numberingSystem))
            {
                throw StandardLibrary.ThrowRangeError(
                    $"Invalid numbering system '{numberingSystem}'", realm: Realm);
            }

            // Only use option if it's a recognized/supported numbering system
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
}
