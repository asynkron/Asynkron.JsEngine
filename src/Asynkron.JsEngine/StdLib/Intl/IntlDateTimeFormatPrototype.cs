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
    private HostFunction GetFormat(JsValue thisValue)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        return CreateBoundFormatFunction(value => FormatInternal(value, slotData));
    }

    [JsHostMethod("formatToParts", Length = 1d)]
    private JsValue FormatToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        var formatted = FormatInternal(args.GetArgument(0), slotData);
        var part = new JsObject();
        part.SetProperty("type", "literal");
        part.SetProperty("value", formatted.ToObject());
        var parts = new JsArray(Realm);
        parts.Push(part);
        return new JsValue(parts);
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
        return new JsValue(parts);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _unused)
    {
        var slots = ValidateReceiver(thisValue, out _);
        var obj = new JsObject(Realm.ObjectPrototype);
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

        return new JsValue(obj);
    }

    private DateTimeFormatInternalSlots ValidateReceiver(JsValue thisValue, out JsObject instance)
    {
        var obj = thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.DateTimeFormat method called on incompatible receiver");
        if (!obj.TryGetProperty(SlotsKey, out var slotValue) ||
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

    private HostFunction CreateBoundFormatFunction(Func<JsValue, JsValue> formatter)
    {
        var function = new HostFunction((_, args) => formatter(args.GetArgument(0)), Realm, isConstructor: false);
        DefineFormatFunctionMetadata(function);
        return function;
    }

    private static void DefineFormatFunctionMetadata(HostFunction function)
    {
        function.DefineProperty("length", new PropertyDescriptor
        {
            Value = 1d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        function.DefineProperty("name", new PropertyDescriptor
        {
            Value = string.Empty,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
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

        if (value.Kind == JsValueKind.Object && value.ObjectValue is JsObject jsObject &&
            jsObject.TryGetProperty("_internalDate", out var stored) && stored is double storedMs)
        {
            return storedMs;
        }

        try
        {
            return JsOps.ToNumber(value.ToObject());
        }
        catch
        {
            return double.NaN;
        }
    }

}
