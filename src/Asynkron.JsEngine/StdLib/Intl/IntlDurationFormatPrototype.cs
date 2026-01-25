#region

using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

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
        return JsValue.FromJsArray(new JsArray(Realm));
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string operation = "Intl.DurationFormat.prototype.resolvedOptions";
        CreateDataPropertyOrThrow(obj, "numberingSystem", "latn", Realm, operation);
        CreateDataPropertyOrThrow(obj, "style", "short", Realm, operation);
        CreateDataPropertyOrThrow(obj, "years", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "yearsDisplay", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "months", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "monthsDisplay", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "weeks", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "weeksDisplay", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "days", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "daysDisplay", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "hours", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "hoursDisplay", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "minutes", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "minutesDisplay", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "seconds", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "secondsDisplay", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "milliseconds", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "millisecondsDisplay", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "microseconds", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "microsecondsDisplay", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "nanoseconds", "auto", Realm, operation);
        CreateDataPropertyOrThrow(obj, "nanosecondsDisplay", "auto", Realm, operation);

        var localeValue = instance.TryGetProperty(LocaleSlot, out var locale) && locale.TryGetString(out var localeStr)
            ? localeStr
            : "en";
        CreateDataPropertyOrThrow(obj, "locale", localeValue, Realm, operation);
        return new JsValue(obj);
    }

    private JsObject ValidateReceiver(JsValue thisValue)
    {
        return thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.DurationFormat method called on incompatible receiver");
    }
}
