#region

using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.DateTimeFormat", ToStringTag = "Intl.DateTimeFormat")]
public sealed partial class IntlDateTimeFormatPrototype
{
    private const string BrandKey = "__dateTimeFormat__";
    private const string SlotsKey = "__dateTimeFormatSlots__";

    internal static void InitializeInternalSlots(JsObject instance, DateTimeFormatInternalSlots slots)
    {
        instance.SetProperty(BrandKey, true);
        instance.SetProperty(SlotsKey, JsValue.FromObjectUnsafe(slots));
    }

    [JsHostGetter("format", DisplayName = "get format")]
    private JsValue GetFormat(JsValue thisValue)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        return (JsValue)CreateBoundFormatFunction(value => FormatInternal(value, slotData));
    }

    [JsHostMethod("formatToParts", Length = 1d)]
    private JsValue FormatToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        var formatted = FormatInternal(args.GetArgument(0), slotData);
        var part = new JsObject();
        part.SetProperty("type", new JsValue("literal"));
        part.SetProperty("value", formatted);
        var parts = new JsArray(Realm);
        parts.Push(part);
        return JsValue.FromJsArray(parts);
    }

    [JsHostMethod("formatRange", Length = 2d)]
    private JsValue FormatRange(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        var start = FormatInternal(args.GetArgument(0), slotData);
        var end = FormatInternal(args.GetArgument(1), slotData);
        return new JsValue($"{start.ObjectValue} – {end.ObjectValue}");
    }

    [JsHostMethod("formatRangeToParts", Length = 2d)]
    private JsValue FormatRangeToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        var start = FormatInternal(args.GetArgument(0), slotData);
        var end = FormatInternal(args.GetArgument(1), slotData);
        var parts = new JsArray(Realm);
        parts.Push(CreateRangePart("startRange", (string)start.ObjectValue!));
        parts.Push(CreateRangePart("separator", " – "));
        parts.Push(CreateRangePart("endRange", (string)end.ObjectValue!));
        return JsValue.FromJsArray(parts);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue)
    {
        var slots = ValidateReceiver(thisValue, out _);
        var obj = new JsObject(Realm.ObjectPrototype);
        obj.SetProperty("locale", new JsValue(slots.Locale));
        obj.SetProperty("calendar", new JsValue(slots.Calendar));
        obj.SetProperty("numberingSystem", new JsValue(slots.NumberingSystem));
        obj.SetProperty("timeZone", new JsValue(slots.TimeZone));
        obj.SetProperty("hourCycle", new JsValue(slots.HourCycle));
        obj.SetProperty("localeMatcher", new JsValue(slots.LocaleMatcher));
        obj.SetProperty("formatMatcher", new JsValue(slots.FormatMatcher));
        obj.SetProperty("dateStyle", slots.DateStyle != null ? new JsValue(slots.DateStyle) : JsValue.Undefined);
        obj.SetProperty("timeStyle", slots.TimeStyle != null ? new JsValue(slots.TimeStyle) : JsValue.Undefined);
        foreach (var component in DateTimeFormatInternalSlots.ComponentNames)
        {
            obj.SetProperty(component,
                slots.Components.TryGetValue(component, out var value) ? new JsValue(value) : JsValue.Undefined);
        }

        return new JsValue(obj);
    }

    private DateTimeFormatInternalSlots ValidateReceiver(JsValue thisValue, out JsObject instance)
    {
        var obj = thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.DateTimeFormat method called on incompatible receiver");
        if (!obj.TryGetProperty(SlotsKey, out var slotValue) ||
            !slotValue.TryGetObject<DateTimeFormatInternalSlots>(out var slots))
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
        obj.SetProperty("type", new JsValue(type));
        obj.SetProperty("value", new JsValue(value));
        return obj;
    }

    private HostFunction CreateBoundFormatFunction(Func<JsValue, JsValue> formatter)
    {
        var function = new HostFunction((_, args) => formatter(args.GetArgument(0)), Realm, false);
        DefineFormatFunctionMetadata(function);
        return function;
    }

    private static void DefineFormatFunctionMetadata(HostFunction function)
    {
        function.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        function.DefineProperty("name",
            new PropertyDescriptor { Value = string.Empty, Writable = false, Enumerable = false, Configurable = true });
    }

    private static JsValue FormatInternal(JsValue value, DateTimeFormatInternalSlots slots)
    {
        var epochMilliseconds = ToEpochMilliseconds(value);
        if (double.IsNaN(epochMilliseconds))
        {
            return new JsValue("Invalid Date");
        }

        var truncated = Math.Truncate(epochMilliseconds);
        try
        {
            var offset = DateTimeOffset.FromUnixTimeMilliseconds((long)truncated);
            var culture = CultureInfo.GetCultureInfo(slots.Locale);
            var format = slots.TimeStyle is "long" or "short" ? "G" : "f";
            return new JsValue(offset.ToString(format, culture));
        }
        catch
        {
            return new JsValue(DateTimeOffset.FromUnixTimeMilliseconds((long)truncated)
                .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
        }
    }

    private static double ToEpochMilliseconds(JsValue value)
    {
        if (value.IsNullOrUndefined)
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        if (value is { Kind: JsValueKind.Object, ObjectValue: JsObject jsObject } &&
            jsObject.TryGetProperty("_internalDate", out var stored) && stored.TryGetDouble(out var storedMs))
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
