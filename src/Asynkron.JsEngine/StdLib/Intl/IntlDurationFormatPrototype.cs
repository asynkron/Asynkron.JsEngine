using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.DurationFormat", ToStringTag = "Intl.DurationFormat")]
public sealed partial class IntlDurationFormatPrototype
{
    private const string BrandKey = "__durationFormat__";
    private const string LocaleSlot = "__locale__";

    internal static void InitializeInternalSlots(JsObject instance, string locale)
    {
        instance.SetProperty(BrandKey, true);
        instance.SetProperty(LocaleSlot, locale);
    }

    [JsHostMethod("format", Length = 0d)]
    private string Format(object? thisValue, IReadOnlyList<object?> args)
    {
        ValidateReceiver(thisValue);
        return "PT0S";
    }

    [JsHostMethod("formatToParts", Length = 0d)]
    private JsArray FormatToParts(object? thisValue, IReadOnlyList<object?> args)
    {
        ValidateReceiver(thisValue);
        return new JsArray(Realm);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsObject ResolvedOptions(object? thisValue, IReadOnlyList<object?> args)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject();
        obj.SetProperty("numberingSystem", "latn");
        obj.SetProperty("style", "short");
        obj.SetProperty("years", "auto");
        obj.SetProperty("yearsDisplay", "auto");
        obj.SetProperty("months", "auto");
        obj.SetProperty("monthsDisplay", "auto");
        obj.SetProperty("weeks", "auto");
        obj.SetProperty("weeksDisplay", "auto");
        obj.SetProperty("days", "auto");
        obj.SetProperty("daysDisplay", "auto");
        obj.SetProperty("hours", "auto");
        obj.SetProperty("hoursDisplay", "auto");
        obj.SetProperty("minutes", "auto");
        obj.SetProperty("minutesDisplay", "auto");
        obj.SetProperty("seconds", "auto");
        obj.SetProperty("secondsDisplay", "auto");
        obj.SetProperty("milliseconds", "auto");
        obj.SetProperty("millisecondsDisplay", "auto");
        obj.SetProperty("microseconds", "auto");
        obj.SetProperty("microsecondsDisplay", "auto");
        obj.SetProperty("nanoseconds", "auto");
        obj.SetProperty("nanosecondsDisplay", "auto");
        obj.SetProperty("locale",
            instance.TryGetProperty(LocaleSlot, out var locale) ? locale ?? "en" : "en");
        return obj;
    }

    private JsObject ValidateReceiver(object? thisValue)
    {
        return thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.DurationFormat method called on incompatible receiver");
    }
}
