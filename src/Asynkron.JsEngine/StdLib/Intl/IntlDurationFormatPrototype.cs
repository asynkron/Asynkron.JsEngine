using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.DurationFormat", ToStringTag = "Intl.DurationFormat")]
public sealed partial class IntlDurationFormatPrototype : JsPrototype
{

    public IntlDurationFormatPrototype(JsObject prototype, RealmState realm)
        : base(prototype, realm)
    {
        if (realm.ObjectPrototype is not null)
        {
            prototype.SetPrototype(realm.ObjectPrototype);
        }
    }

    [JsHostMethod("format", Length = 0d)]
    public string Format(object? thisValue, IReadOnlyList<object?> args)
    {
        return "PT0S";
    }

    [JsHostMethod("formatToParts", Length = 0d)]
    public JsArray FormatToParts(object? thisValue, IReadOnlyList<object?> args)
    {
        return new JsArray(Realm);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    public JsObject ResolvedOptions(object? thisValue, IReadOnlyList<object?> args)
    {
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
        obj.SetProperty("locale", "en");
        return obj;
    }

}
