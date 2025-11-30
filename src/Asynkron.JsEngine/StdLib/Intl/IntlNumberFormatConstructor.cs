using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.NumberFormat", PrototypeType = typeof(IntlNumberFormatPrototype), Length = 0d,
    DisplayName = "NumberFormat")]
public sealed partial class IntlNumberFormatConstructor(JsObject prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var (_, resolvedLocale) = StandardLibrary.ResolveIntlLocales(args.GetArgument(0), Realm);
        var optionsArg = args.GetArgument(1);
        var options = NormalizeOptions(optionsArg);
        var style = ReadStyleOption(options);
        var numberingSystem = ReadNumberingSystem(options);
        string? currency = null;
        if (style == "currency")
        {
            currency = ReadCurrencyOption(options);
        }
        string? unit = null;
        if (style == "unit")
        {
            unit = ReadUnitOption(options);
        }

        var instance = PrepareThisObject(thisValue);
        IntlNumberFormatPrototype.InitializeInternalSlots(
            instance,
            resolvedLocale,
            numberingSystem,
            style,
            currency,
            unit);
        return instance;
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocalesOf = new HostFunction(SupportedLocalesOf, isConstructor: false);
        supportedLocalesOf.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        supportedLocalesOf.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "supportedLocalesOf", Writable = false, Enumerable = false, Configurable = true
            });

        constructor.DefineProperty("supportedLocalesOf",
            new PropertyDescriptor
            {
                Value = supportedLocalesOf, Writable = true, Enumerable = false, Configurable = true
            });
        supportedLocalesOf.SetPrototype(constructor.Prototype);
        supportedLocalesOf.Delete("prototype");
    }

    private JsArray SupportedLocalesOf(IReadOnlyList<object?> args)
    {
        return StandardLibrary.ResolveSupportedLocales(args.GetArgument(0), Realm);
    }

    private JsObject? NormalizeOptions(object? optionsArg)
    {
        if (optionsArg is null)
        {
            throw StandardLibrary.ThrowTypeError("Intl.NumberFormat options must be an object", realm: Realm);
        }

        if (ReferenceEquals(optionsArg, Symbol.Undefined))
        {
            return null;
        }

        if (optionsArg is JsObject jsObject)
        {
            return jsObject;
        }

        throw StandardLibrary.ThrowTypeError("Intl.NumberFormat options must be an object", realm: Realm);
    }

    private string ReadStyleOption(JsObject? options)
    {
        if (options is null || !options.TryGetProperty("style", out var value) ||
            value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            return "decimal";
        }

        if (value is not string style)
        {
            throw StandardLibrary.ThrowTypeError("Intl.NumberFormat style option must be a string", realm: Realm);
        }

        if (style is not ("decimal" or "currency" or "unit"))
        {
            throw StandardLibrary.ThrowRangeError(
                $"Intl.NumberFormat style option '{style}' is not supported", realm: Realm);
        }

        return style;
    }

    private string ReadNumberingSystem(JsObject? options)
    {
        if (options is null ||
            !options.TryGetProperty("numberingSystem", out var rawValue) ||
            rawValue is null || ReferenceEquals(rawValue, Symbol.Undefined))
        {
            return "latn";
        }

        if (rawValue is not string numberingSystem)
        {
            throw StandardLibrary.ThrowTypeError(
                "Intl.NumberFormat numberingSystem option must be a string", realm: Realm);
        }

        return IntlUtilities.TryNormalizeNumberingSystem(numberingSystem, out var canonical)
            ? canonical
            : "latn";
    }

    private string ReadCurrencyOption(JsObject? options)
    {
        if (options is null || !options.TryGetProperty("currency", out var value) ||
            value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            throw StandardLibrary.ThrowTypeError(
                "Intl.NumberFormat currency option is required when style is 'currency'", realm: Realm);
        }

        if (value is not string currency)
        {
            throw StandardLibrary.ThrowTypeError(
                "Intl.NumberFormat currency option must be a string", realm: Realm);
        }

        if (!IntlUtilities.TryGetCanonicalCurrency(currency, out var canonical))
        {
            throw StandardLibrary.ThrowRangeError($"Invalid currency code '{currency}'", realm: Realm);
        }

        if (!IntlUtilities.IsSupportedCurrency(canonical))
        {
            throw StandardLibrary.ThrowRangeError($"Unsupported currency '{currency}'", realm: Realm);
        }

        return canonical;
    }

    private string ReadUnitOption(JsObject? options)
    {
        if (options is null || !options.TryGetProperty("unit", out var value) ||
            value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            throw StandardLibrary.ThrowTypeError(
                "Intl.NumberFormat unit option is required when style is 'unit'", realm: Realm);
        }

        if (value is not string unit)
        {
            throw StandardLibrary.ThrowTypeError(
                "Intl.NumberFormat unit option must be a string", realm: Realm);
        }

        if (!IntlUtilities.TryGetCanonicalUnit(unit, out var canonical))
        {
            throw StandardLibrary.ThrowRangeError($"Invalid unit '{unit}'", realm: Realm);
        }

        return canonical;
    }
}
