using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.DateTimeFormat", ToStringTag = "Intl.DateTimeFormat")]
public sealed partial class IntlDateTimeFormatPrototype
{
    private const string BrandKey = "__dateTimeFormat__";
    private const string SlotsKey = "__dateTimeFormatSlots__";

    internal static void InitializeInternalSlots(JsObject instance, DateTimeFormatInternalSlots slots)
    {
        instance.SetProperty(BrandKey, true);
        instance.SetProperty(SlotsKey, slots);
    }

    [JsHostGetter("format", DisplayName = "get format")]
    public object GetFormat(object? thisValue)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        return new HostFunction((_, args) => FormatInternal(args.Count > 0 ? args[0] : Symbol.Undefined, slotData), Realm)
        {
            IsConstructor = false
        };
    }

    [JsHostMethod("formatToParts", Length = 1d)]
    public JsArray FormatToParts(object? thisValue, IReadOnlyList<object?> args)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        var formatted = FormatInternal(args.Count > 0 ? args[0] : Symbol.Undefined, slotData);
        var part = new JsObject();
        part.SetProperty("type", "literal");
        part.SetProperty("value", formatted);
        var parts = new JsArray(Realm);
        parts.Push(part);
        return parts;
    }

    [JsHostMethod("formatRange", Length = 2d)]
    public string FormatRange(object? thisValue, IReadOnlyList<object?> args)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        var start = FormatInternal(args.Count > 0 ? args[0] : Symbol.Undefined, slotData);
        var end = FormatInternal(args.Count > 1 ? args[1] : Symbol.Undefined, slotData);
        return $"{start} – {end}";
    }

    [JsHostMethod("formatRangeToParts", Length = 2d)]
    public JsArray FormatRangeToParts(object? thisValue, IReadOnlyList<object?> args)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        var start = FormatInternal(args.Count > 0 ? args[0] : Symbol.Undefined, slotData);
        var end = FormatInternal(args.Count > 1 ? args[1] : Symbol.Undefined, slotData);
        var parts = new JsArray(Realm);
        parts.Push(CreateRangePart("startRange", start));
        parts.Push(CreateRangePart("separator", " – "));
        parts.Push(CreateRangePart("endRange", end));
        return parts;
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    public JsObject ResolvedOptions(object? thisValue, IReadOnlyList<object?> _unused)
    {
        var slots = ValidateReceiver(thisValue, out _);
        var obj = new JsObject();
        obj.SetPrototype(Realm.ObjectPrototype);
        obj.SetProperty("locale", slots.Locale);
        obj.SetProperty("calendar", slots.Calendar);
        obj.SetProperty("numberingSystem", slots.NumberingSystem);
        obj.SetProperty("timeZone", slots.TimeZone);
        obj.SetProperty("hourCycle", slots.HourCycle);
        obj.SetProperty("localeMatcher", slots.LocaleMatcher);
        obj.SetProperty("formatMatcher", slots.FormatMatcher);
        obj.SetProperty("dateStyle", slots.DateStyle ?? (object)Symbol.Undefined);
        obj.SetProperty("timeStyle", slots.TimeStyle ?? (object)Symbol.Undefined);
        foreach (var component in DateTimeFormatInternalSlots.ComponentNames)
        {
            obj.SetProperty(component,
                slots.Components.TryGetValue(component, out var value) ? value : Symbol.Undefined);
        }

        return obj;
    }

    private DateTimeFormatInternalSlots ValidateReceiver(object? thisValue, out JsObject instance)
    {
        if (thisValue is not JsObject obj ||
            !obj.TryGetProperty(BrandKey, out var marker) ||
            marker is not true ||
            !obj.TryGetProperty(SlotsKey, out var slotValue) ||
            slotValue is not DateTimeFormatInternalSlots slots)
        {
            throw StandardLibrary.ThrowTypeError("Intl.DateTimeFormat method called on incompatible receiver",
                realm: Realm);
        }

        instance = obj;
        return slots;

    }

    private static JsObject CreateRangePart(string type, string value)
    {
        var obj = new JsObject();
        obj.SetProperty("type", type);
        obj.SetProperty("value", value);
        return obj;
    }

    private static string FormatInternal(object? value, DateTimeFormatInternalSlots slots)
    {
        var epochMilliseconds = ToEpochMilliseconds(value);
        if (double.IsNaN(epochMilliseconds))
        {
            return "Invalid Date";
        }

        var truncated = Math.Truncate(epochMilliseconds);
        try
        {
            var offset = DateTimeOffset.FromUnixTimeMilliseconds((long)truncated);
            var culture = CultureInfo.GetCultureInfo(slots.Locale);
            var format = slots.TimeStyle is "long" or "short" ? "G" : "f";
            return offset.ToString(format, culture);
        }
        catch
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)truncated)
                .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }
    }

    private static double ToEpochMilliseconds(object? value)
    {
        if (value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        if (value is JsObject jsObject &&
            jsObject.TryGetProperty("_internalDate", out var stored) && stored is double storedMs)
        {
            return storedMs;
        }

        try
        {
            return JsOps.ToNumber(value);
        }
        catch
        {
            return double.NaN;
        }
    }

}

internal sealed class DateTimeFormatInternalSlots
{
    public static readonly string[] ComponentNames =
    [
        "weekday", "era", "year", "month", "day", "hour", "minute", "second", "timeZoneName"
    ];

    public string Locale { get; init; } = CultureInfo.CurrentCulture.Name;
    public string TimeZone { get; init; } = TimeZoneInfo.Utc.Id;
    public string Calendar { get; init; } = "gregory";
    public string NumberingSystem { get; init; } = "latn";
    public string HourCycle { get; init; } = "h23";
    public string LocaleMatcher { get; init; } = "best fit";
    public string FormatMatcher { get; init; } = "best fit";
    public string? DateStyle { get; init; }
    public string? TimeStyle { get; init; }
    public Dictionary<string, string> Components { get; } = new();
}
