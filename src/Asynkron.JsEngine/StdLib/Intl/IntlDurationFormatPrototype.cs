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
    private JsValue Format(JsValue thisValue)
    {
        ValidateReceiver(thisValue);
        return new JsValue("PT0S");
    }

    [JsHostMethod("formatToParts", Length = 0d)]
    private JsValue FormatToParts(JsValue thisValue)
    {
        ValidateReceiver(thisValue);
        return JsValue.FromObjectUnsafe(new JsArray(Realm));
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue)
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
            instance.TryGetProperty(LocaleSlot, out var locale) && locale.TryGetString(out var localeStr) ? localeStr : "en");
        return new JsValue(obj);
    }

    private JsObject ValidateReceiver(JsValue thisValue)
    {
        return thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.DurationFormat method called on incompatible receiver");
    }
}
