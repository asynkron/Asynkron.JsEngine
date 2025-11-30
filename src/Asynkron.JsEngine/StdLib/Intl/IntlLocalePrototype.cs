using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.Locale")]
public sealed partial class IntlLocalePrototype
{
    internal const string BrandKey = "__localeBrand__";
    internal const string TagSlot = "__tag__";
    internal const string CalendarSlot = "__calendar__";
    internal const string NumberingSystemSlot = "__numberingSystem__";

    [JsHostGetter("calendar", DisplayName = "get calendar")]
    private object? GetCalendar(object? thisValue)
    {
        if (thisValue is JsObject self && self.TryGetProperty(CalendarSlot, out var value))
        {
            return value;
        }

        return Symbol.Undefined;
    }

    [JsHostGetter("numberingSystem", DisplayName = "get numberingSystem")]
    private object? GetNumberingSystem(object? thisValue)
    {
        if (thisValue is JsObject self && self.TryGetProperty(NumberingSystemSlot, out var value))
        {
            return value;
        }

        return Symbol.Undefined;
    }

    internal static bool TryBuildLocaleIdentifier(JsObject candidate, out string identifier)
    {
        identifier = string.Empty;
        if (!candidate.TryGetProperty(BrandKey, out var marker) || marker is not true)
        {
            return false;
        }

        if (!candidate.TryGetProperty(TagSlot, out var baseTagValue) || baseTagValue is not string baseTag ||
            string.IsNullOrWhiteSpace(baseTag))
        {
            return false;
        }

        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        if (candidate.TryGetProperty(CalendarSlot, out var calendarValue) && calendarValue is string calendar)
        {
            overrides["ca"] = calendar;
        }

        if (candidate.TryGetProperty(NumberingSystemSlot, out var numberingValue) &&
            numberingValue is string numbering)
        {
            overrides["nu"] = numbering;
        }

        identifier = IntlUtilities.ApplyUnicodeLocaleOverrides(baseTag, overrides);
        return true;
    }
}
