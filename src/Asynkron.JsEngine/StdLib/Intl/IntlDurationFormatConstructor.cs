#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DurationFormat", PrototypeType = typeof(IntlDurationFormatPrototype), Length = 0d,
    DisplayName = "DurationFormat")]
public sealed partial class IntlDurationFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);
        var (_, resolvedLocale) = ResolveIntlLocales(localesArg, Realm);

        var options = NormalizeOptions(optionsArg);
        var (numberingSystem, finalLocale) = ResolveNumberingSystemAndLocale(options, resolvedLocale);
        var style = ReadStringOption(options, "style", ["long", "short", "narrow", "digital"], "short");

        var instance = PrepareThisObject(thisValue);
        IntlDurationFormatPrototype.InitializeInternalSlots(instance, finalLocale, numberingSystem, style);
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

        throw StandardLibrary.ThrowTypeError("Intl.DurationFormat options must be an object", realm: Realm);
    }

    private (string NumberingSystem, string Locale) ResolveNumberingSystemAndLocale(JsObject? options,
        string resolvedLocale)
    {
        var unicodeKeywords = IntlUtilities.ParseUnicodeExtensionKeywords(resolvedLocale);
        string? extensionNu = null;
        if (unicodeKeywords.TryGetValue("nu", out var nuValues) && nuValues.Count > 0)
        {
            extensionNu = nuValues[0];
        }

        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(resolvedLocale);

        string? optionNu = null;
        if (options is not null && options.TryGetProperty("numberingSystem", out var value) &&
            !value.IsUndefined)
        {
            if (!value.TryGetString(out var numberingSystem))
            {
                throw StandardLibrary.ThrowTypeError(
                    "Intl.DurationFormat numberingSystem option must be a string", realm: Realm);
            }

            if (IntlUtilities.TryNormalizeNumberingSystem(numberingSystem, out var canonical))
            {
                optionNu = canonical;
            }
        }

        if (optionNu is not null)
        {
            if (extensionNu is not null &&
                string.Equals(optionNu, extensionNu, StringComparison.Ordinal))
            {
                return (optionNu, resolvedLocale);
            }

            return (optionNu, baseLocale);
        }

        if (extensionNu is not null &&
            IntlUtilities.TryNormalizeNumberingSystem(extensionNu, out var validExtNu))
        {
            return (validExtNu, resolvedLocale);
        }

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
                $"Intl.DurationFormat {propertyName} option must be a string", realm: Realm);
        }

        if (!allowed.Contains(strValue, StringComparer.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError(
                $"Intl.DurationFormat {propertyName} option '{strValue}' is not supported", realm: Realm);
        }

        return strValue;
    }
}
