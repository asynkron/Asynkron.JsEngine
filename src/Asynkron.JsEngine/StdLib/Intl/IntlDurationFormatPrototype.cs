#region

using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.DurationFormat", ToStringTag = "Intl.DurationFormat")]
public sealed partial class IntlDurationFormatPrototype
{
    private const string BrandKey = "__durationFormat__";
    private const string SlotsKey = "__durationFormatSlots__";

    internal static void InitializeInternalSlots(JsObject instance, IntlDurationFormatInternalSlots slots)
    {
        instance.SetProperty(BrandKey, true);
        instance.SetProperty(SlotsKey, JsValue.FromObjectUnsafe(slots));
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
        if (!instance.TryGetProperty(SlotsKey, out var slotsValue) ||
            !slotsValue.TryGetObject<IntlDurationFormatInternalSlots>(out var slots))
        {
            throw ThrowTypeError("Intl.DurationFormat internal slots not found", realm: Realm);
        }
        var obj = new JsObject(Realm.ObjectPrototype);
        const string op = "Intl.DurationFormat.prototype.resolvedOptions";

        CreateDataPropertyOrThrow(obj, "locale", slots.Locale, Realm, op);
        CreateDataPropertyOrThrow(obj, "numberingSystem", slots.NumberingSystem, Realm, op);
        CreateDataPropertyOrThrow(obj, "style", slots.Style, Realm, op);
        CreateDataPropertyOrThrow(obj, "years", slots.YearsStyle, Realm, op);
        CreateDataPropertyOrThrow(obj, "yearsDisplay", slots.YearsDisplay, Realm, op);
        CreateDataPropertyOrThrow(obj, "months", slots.MonthsStyle, Realm, op);
        CreateDataPropertyOrThrow(obj, "monthsDisplay", slots.MonthsDisplay, Realm, op);
        CreateDataPropertyOrThrow(obj, "weeks", slots.WeeksStyle, Realm, op);
        CreateDataPropertyOrThrow(obj, "weeksDisplay", slots.WeeksDisplay, Realm, op);
        CreateDataPropertyOrThrow(obj, "days", slots.DaysStyle, Realm, op);
        CreateDataPropertyOrThrow(obj, "daysDisplay", slots.DaysDisplay, Realm, op);
        CreateDataPropertyOrThrow(obj, "hours", slots.HoursStyle, Realm, op);
        CreateDataPropertyOrThrow(obj, "hoursDisplay", slots.HoursDisplay, Realm, op);
        CreateDataPropertyOrThrow(obj, "minutes", slots.MinutesStyle, Realm, op);
        CreateDataPropertyOrThrow(obj, "minutesDisplay", slots.MinutesDisplay, Realm, op);
        CreateDataPropertyOrThrow(obj, "seconds", slots.SecondsStyle, Realm, op);
        CreateDataPropertyOrThrow(obj, "secondsDisplay", slots.SecondsDisplay, Realm, op);
        CreateDataPropertyOrThrow(obj, "milliseconds", slots.MillisecondsStyle, Realm, op);
        CreateDataPropertyOrThrow(obj, "millisecondsDisplay", slots.MillisecondsDisplay, Realm, op);
        CreateDataPropertyOrThrow(obj, "microseconds", slots.MicrosecondsStyle, Realm, op);
        CreateDataPropertyOrThrow(obj, "microsecondsDisplay", slots.MicrosecondsDisplay, Realm, op);
        CreateDataPropertyOrThrow(obj, "nanoseconds", slots.NanosecondsStyle, Realm, op);
        CreateDataPropertyOrThrow(obj, "nanosecondsDisplay", slots.NanosecondsDisplay, Realm, op);

        // fractionalDigits: only include if set (undefined maps to not present).
        if (slots.FractionalDigits.HasValue)
        {
            CreateDataPropertyOrThrow(obj, "fractionalDigits", slots.FractionalDigits.Value, Realm, op);
        }

        return new JsValue(obj);
    }

    private JsObject ValidateReceiver(JsValue thisValue)
    {
        return thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.DurationFormat method called on incompatible receiver");
    }
}
