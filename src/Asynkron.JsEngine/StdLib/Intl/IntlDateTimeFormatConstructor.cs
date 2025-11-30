using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DateTimeFormat", PrototypeType = typeof(IntlDateTimeFormatPrototype), Length = 0d,
    DisplayName = "DateTimeFormat")]
public sealed partial class IntlDateTimeFormatConstructor(JsObject prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var localesArg = args.Count > 0 ? args[0] : Symbol.Undefined;
        var optionsArg = args.Count > 1 ? args[1] : Symbol.Undefined;

        var slots = CreateInternalSlots(localesArg, optionsArg);
        var instance = PrepareThisObject(thisValue);
        IntlDateTimeFormatPrototype.InitializeInternalSlots(instance, slots);
        return instance;
    }

    private DateTimeFormatInternalSlots CreateInternalSlots(object? localesArg, object? optionsArg)
    {
        var requestedLocales = IntlUtilities.CanonicalizeLocaleList(localesArg, Realm);
        var resolvedLocale = requestedLocales.Count > 0
            ? requestedLocales[0]
            : CultureInfo.CurrentCulture.Name;

        var options = NormalizeOptions(optionsArg);
        var localeMatcher = ReadStringOption(
            options,
            "localeMatcher",
            ["lookup", "best fit"],
            "best fit");

        var formatMatcher = ReadStringOption(
            options,
            "formatMatcher",
            ["basic", "best fit"],
            "best fit");

        var timeZoneValue = options is null ? Symbol.Undefined : GetOption(options, "timeZone");
        var hourCycle = ReadStringOption(
            options,
            "hourCycle",
            ["h11", "h12", "h23", "h24"],
            "h23");
        var calendar = ReadCalendarOption(options);
        var numberingSystem = ReadNumberingSystem(options);
        var dateStyle = ReadStyleOption(options, "dateStyle");
        var timeStyle = ReadStyleOption(options, "timeStyle");

        var slots = new DateTimeFormatInternalSlots
        {
            Locale = resolvedLocale,
            TimeZone = IntlUtilities.NormalizeTimeZone(timeZoneValue, Realm),
            HourCycle = hourCycle,
            Calendar = calendar,
            NumberingSystem = numberingSystem,
            LocaleMatcher = localeMatcher,
            FormatMatcher = formatMatcher,
            DateStyle = dateStyle,
            TimeStyle = timeStyle
        };

        foreach (var component in DateTimeFormatInternalSlots.ComponentNames)
        {
            var value = ReadComponentOption(options, component);
            if (value is not null)
            {
                slots.Components[component] = value;
            }
        }

        return slots;
    }

    private JsObject? NormalizeOptions(object? optionsArg)
    {
        if (optionsArg is null)
        {
            throw StandardLibrary.ThrowTypeError("Intl.DateTimeFormat options must be an object", realm: Realm);
        }

        if (ReferenceEquals(optionsArg, Symbol.Undefined))
        {
            return null;
        }

        if (optionsArg is JsObject jsObject)
        {
            return jsObject;
        }

        throw StandardLibrary.ThrowTypeError("Intl.DateTimeFormat options must be an object", realm: Realm);
    }

    private static object? GetOption(JsObject options, string propertyName)
    {
        return options.TryGetProperty(propertyName, out var value) ? value : Symbol.Undefined;
    }

    private string ReadStringOption(JsObject? options, string propertyName, IReadOnlyList<string> allowed,
        string defaultValue)
    {
        if (options is null || !options.TryGetProperty(propertyName, out var rawValue) ||
            rawValue is null || ReferenceEquals(rawValue, Symbol.Undefined))
        {
            return defaultValue;
        }

        if (rawValue is not string strValue)
        {
            throw StandardLibrary.ThrowTypeError(
                $"Intl.DateTimeFormat {propertyName} option must be a string", realm: Realm);
        }

        if (!allowed.Contains(strValue, StringComparer.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError(
                $"Intl.DateTimeFormat {propertyName} option '{strValue}' is not supported", realm: Realm);
        }

        return strValue;
    }

    private string ReadCalendarOption(JsObject? options)
    {
        if (options is null || !options.TryGetProperty("calendar", out var value) ||
            value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            return "gregory";
        }

        if (value is not string calendar)
        {
            throw StandardLibrary.ThrowTypeError("Intl.DateTimeFormat calendar option must be a string", realm: Realm);
        }

        if (!string.Equals(calendar, "gregory", StringComparison.OrdinalIgnoreCase))
        {
            throw StandardLibrary.ThrowRangeError($"Unsupported calendar '{calendar}'", realm: Realm);
        }

        return "gregory";
    }

    private string ReadNumberingSystem(JsObject? options)
    {
        if (options is null || !options.TryGetProperty("numberingSystem", out var value) ||
            value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            return "latn";
        }

        if (value is not string system)
        {
            throw StandardLibrary.ThrowTypeError(
                "Intl.DateTimeFormat numberingSystem option must be a string", realm: Realm);
        }

        return system;
    }

    private string? ReadStyleOption(JsObject? options, string propertyName)
    {
        if (options is null || !options.TryGetProperty(propertyName, out var value) ||
            value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            return null;
        }

        if (value is not string style)
        {
            throw StandardLibrary.ThrowTypeError(
                $"Intl.DateTimeFormat {propertyName} option must be a string", realm: Realm);
        }

        return style;
    }

    private string? ReadComponentOption(JsObject? options, string propertyName)
    {
        if (options is null || !options.TryGetProperty(propertyName, out var value) ||
            value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            return null;
        }

        if (value is not string component)
        {
            throw StandardLibrary.ThrowTypeError(
                $"Intl.DateTimeFormat {propertyName} option must be a string", realm: Realm);
        }

        return component switch
        {
            "2-digit" or "numeric" or "narrow" or "short" or "long" => component,
            _ => throw StandardLibrary.ThrowRangeError(
                $"Intl.DateTimeFormat {propertyName} option '{component}' is not supported", realm: Realm)
        };
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocales = new HostFunction((_, args) =>
        {
            var localeList = IntlUtilities.CanonicalizeLocaleList(
                args.Count > 0 ? args[0] : Symbol.Undefined,
                Realm);
            var result = new JsArray(Realm);
            foreach (var locale in localeList)
            {
                result.Push(locale);
            }
            return result;
        }, isConstructor: false);

        supportedLocales.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        supportedLocales.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "supportedLocalesOf", Writable = false, Enumerable = false, Configurable = true
            });

        constructor.DefineProperty("supportedLocalesOf",
            new PropertyDescriptor
            {
                Value = supportedLocales, Writable = true, Enumerable = false, Configurable = true
            });
    }
}
