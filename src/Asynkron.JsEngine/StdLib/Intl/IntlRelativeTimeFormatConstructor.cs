#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.RelativeTimeFormat", PrototypeType = typeof(IntlRelativeTimeFormatPrototype), Length = 0d,
    DisplayName = "RelativeTimeFormat")]
public sealed partial class IntlRelativeTimeFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ConstructCore(thisValue, args);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        // Intl.RelativeTimeFormat step 1: If NewTarget is undefined, throw a TypeError exception.
        constructor.SetInvokeWithContext((args, thisValue, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("Intl.RelativeTimeFormat constructor requires 'new'",
                    realm: Realm);
            }

            return ConstructCore(thisValue, args);
        });

        ConfigureSupportedLocalesOf(constructor, Realm);
    }

    private JsValue ConstructCore(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);

        var (_, resolvedLocale) = ResolveIntlLocales(localesArg, Realm);
        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "RelativeTimeFormat");

        // Per spec option read order: localeMatcher, numberingSystem, style, numeric
        _ = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "RelativeTimeFormat",
            ["lookup", "best fit"], "best fit");

        var numberingSystem = ResolveNumberingSystem(options);

        var style = IntlOptionHelpers.GetStringOption(options, "style", Realm, "RelativeTimeFormat",
            ["long", "short", "narrow"], "long");

        var numeric = IntlOptionHelpers.GetStringOption(options, "numeric", Realm, "RelativeTimeFormat",
            ["always", "auto"], "always");

        // Resolve numbering system from option, unicode extension, or default
        var (resolvedNumberingSystem, finalLocale) =
            ResolveNumberingSystemAndLocale(numberingSystem, resolvedLocale);

        var instance = PrepareThisObject(thisValue);
        IntlRelativeTimeFormatPrototype.InitializeInternalSlots(instance, finalLocale, resolvedNumberingSystem,
            numeric, style);
        return new JsValue(instance);
    }

    private string? ResolveNumberingSystem(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("numberingSystem", out var rawValue) ||
            rawValue.IsUndefined)
        {
            return null;
        }

        var numberingSystem = JsValueToString(rawValue, Realm);

        // Validate against Unicode Locale Identifier type nonterminal: 3*8alphanum *("-" 3*8alphanum)
        if (!IntlUtilities.IsValidUnicodeTypeNonterminal(numberingSystem))
        {
            throw ThrowRangeError($"Invalid numbering system '{numberingSystem}'", realm: Realm);
        }

        return IntlUtilities.TryNormalizeSupportedNumberingSystem(numberingSystem, out var canonical)
            ? canonical
            : null;
    }

    private static (string NumberingSystem, string Locale) ResolveNumberingSystemAndLocale(
        string? optionNu,
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

        // Resolution: option > unicode extension > default
        // Only use a numbering system if it's actually recognized/supported
        if (optionNu is not null && IntlUtilities.TryNormalizeSupportedNumberingSystem(optionNu, out var canonicalOption))
        {
            // Option value wins. If it matches the extension, keep extension in locale.
            if (extensionNu is not null &&
                string.Equals(canonicalOption, extensionNu, StringComparison.Ordinal))
            {
                return (canonicalOption, resolvedLocale);
            }

            // Option differs from extension or no extension: use base locale
            return (canonicalOption, baseLocale);
        }

        if (extensionNu is not null &&
            IntlUtilities.TryNormalizeSupportedNumberingSystem(extensionNu, out var validExtNu))
        {
            // Unicode extension is valid: keep extension in locale
            return (validExtNu, resolvedLocale);
        }

        // Neither option nor extension is recognized: use default, strip extensions
        return ("latn", baseLocale);
    }
}
