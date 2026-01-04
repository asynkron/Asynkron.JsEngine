#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DateTimeFormat", PrototypeType = typeof(IntlDateTimeFormatPrototype), Length = 0d,
    DisplayName = "DateTimeFormat")]
public sealed partial class IntlDateTimeFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);

        var slots = CreateInternalSlots(localesArg, optionsArg);
        var instance = PrepareThisObject(thisValue);
        IntlDateTimeFormatPrototype.InitializeInternalSlots(instance, slots);
        return new JsValue(instance);
    }

    private DateTimeFormatInternalSlots CreateInternalSlots(JsValue localesArg, JsValue optionsArg)
    {
        var (_, resolvedLocale) = ResolveIntlLocales(localesArg, Realm);

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

        var timeZoneValue = options is null ? JsValue.Undefined : GetOption(options, "timeZone");
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

        throw ThrowTypeError("Intl.DateTimeFormat options must be an object", realm: Realm);
    }

    private static JsValue GetOption(JsObject options, string propertyName)
    {
        return options.TryGetProperty(propertyName, out var value) ? value : JsValue.Undefined;
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
            throw ThrowTypeError(
                $"Intl.DateTimeFormat {propertyName} option must be a string", realm: Realm);
        }

        if (!allowed.Contains(strValue, StringComparer.Ordinal))
        {
            throw ThrowRangeError(
                $"Intl.DateTimeFormat {propertyName} option '{strValue}' is not supported", realm: Realm);
        }

        return strValue;
    }

    private string ReadCalendarOption(JsObject? options)
    {
        if (options is null || !options.TryGetProperty("calendar", out var value) ||
            value.IsUndefined)
        {
            return "gregory";
        }

        if (!value.TryGetString(out var calendar))
        {
            throw ThrowTypeError("Intl.DateTimeFormat calendar option must be a string", realm: Realm);
        }

        if (!IntlUtilities.TryNormalizeCalendar(calendar, out var canonical))
        {
            throw ThrowRangeError($"Unsupported calendar '{calendar}'", realm: Realm);
        }

        return canonical;
    }

    private string ReadNumberingSystem(JsObject? options)
    {
        if (options is null || !options.TryGetProperty("numberingSystem", out var value) ||
            value.IsUndefined)
        {
            return "latn";
        }

        if (!value.TryGetString(out var system))
        {
            throw ThrowTypeError(
                "Intl.DateTimeFormat numberingSystem option must be a string", realm: Realm);
        }

        return IntlUtilities.TryNormalizeNumberingSystem(system, out var canonical)
            ? canonical
            : "latn";
    }

    private string? ReadStyleOption(JsObject? options, string propertyName)
    {
        if (options is null || !options.TryGetProperty(propertyName, out var value) ||
            value.IsUndefined)
        {
            return null;
        }

        if (!value.TryGetString(out var stringValue))
        {
            throw ThrowTypeError(
                $"Intl.DateTimeFormat {propertyName} option must be a string", realm: Realm);
        }

        return stringValue;
    }

    private string? ReadComponentOption(JsObject? options, string propertyName)
    {
        if (options is null || !options.TryGetProperty(propertyName, out var value) ||
            value.IsUndefined)
        {
            return null;
        }

        if (!value.TryGetString(out var component))
        {
            throw ThrowTypeError(
                $"Intl.DateTimeFormat {propertyName} option must be a string", realm: Realm);
        }

        var isAllowed = propertyName switch
        {
            "month" => IsMonthAllowed(component),
            "weekday" => component is "narrow" or "short" or "long",
            "era" => component is "narrow" or "short" or "long",
            "timeZoneName" => component is "short" or "long",
            _ => IsWidthAllowed(component)
        };

        if (!isAllowed)
        {
            throw ThrowRangeError(
                $"Intl.DateTimeFormat {propertyName} option '{component}' is not supported", realm: Realm);
        }

        return component;

        bool IsWidthAllowed(string value)
        {
            return value is "2-digit" or "numeric";
        }

        bool IsMonthAllowed(string value)
        {
            return value is "2-digit" or "numeric" or "narrow" or "short" or "long";
        }
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        IntlHelper.ConfigureSupportedLocalesOf(constructor, Realm);
    }
}
