#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.NumberFormat", ToStringTag = "Intl.NumberFormat")]
public sealed partial class IntlNumberFormatPrototype
{
    private const string NumberFormatBrand = "__numberFormat__";
    private const string SlotsKey = "__numberFormatSlots__";

    internal static void InitializeInternalSlots(JsObject instance, IntlNumberFormatInternalSlots slots)
    {
        instance.SetProperty(NumberFormatBrand, true);
        instance.SetProperty(SlotsKey, JsValue.FromObjectUnsafe(slots));
    }

    [JsHostGetter("format", DisplayName = "get format")]
    public JsValue GetFormat(JsValue thisValue)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        return (JsValue)CreateBoundFormatFunction(value => new JsValue(FormatNumberValue(nf, value)));
    }

    [JsHostMethod("formatToParts", Length = 1d)]
    public JsValue FormatToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        var value = args.GetArgument(0);
        var result = FormatNumberResult(nf, value);
        var partsArray = new JsArray(Realm);
        var parts = result.Parts ?? [new NumberFormatPart("literal", result.Formatted)];
        foreach (var part in parts)
        {
            var entry = new JsObject(Realm.ObjectPrototype);
            entry.SetProperty("type", (JsValue)part.Type);
            entry.SetProperty("value", (JsValue)part.Value);
            partsArray.Push(entry);
        }

        return JsValue.FromJsArray(partsArray);
    }

    [JsHostMethod("formatRange", Length = 2d)]
    public JsValue FormatRange(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        var x = args.GetArgument(0);
        var y = args.GetArgument(1);

        if (x.IsUndefined || y.IsUndefined)
        {
            throw ThrowTypeError("start and end values are required", realm: Realm);
        }

        var xNum = ConvertToNumericForRange(x);
        var yNum = ConvertToNumericForRange(y);

        if (double.IsNaN(xNum) || double.IsNaN(yNum))
        {
            throw ThrowRangeError("start and end values must not be NaN", realm: Realm);
        }

        var slots = GetSlots(nf);
        var xResult = IntlNumberFormatter.FormatDouble(xNum, slots);
        var yResult = IntlNumberFormatter.FormatDouble(yNum, slots);

        // When both endpoints format to the same string, use approximately equal pattern
        if (string.Equals(xResult.Formatted, yResult.Formatted, StringComparison.Ordinal))
        {
            return (JsValue)$"~{xResult.Formatted}";
        }

        // Simple range format: "x – y" (space + en-dash + space)
        return (JsValue)$"{xResult.Formatted} \u2013 {yResult.Formatted}";
    }

    [JsHostMethod("formatRangeToParts", Length = 2d)]
    public JsValue FormatRangeToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        var x = args.GetArgument(0);
        var y = args.GetArgument(1);

        if (x.IsUndefined || y.IsUndefined)
        {
            throw ThrowTypeError("start and end values are required", realm: Realm);
        }

        var xNum = ConvertToNumericForRange(x);
        var yNum = ConvertToNumericForRange(y);

        if (double.IsNaN(xNum) || double.IsNaN(yNum))
        {
            throw ThrowRangeError("start and end values must not be NaN", realm: Realm);
        }

        var slots = GetSlots(nf);
        var xResult = IntlNumberFormatter.FormatDouble(xNum, slots);
        var yResult = IntlNumberFormatter.FormatDouble(yNum, slots);

        var partsArray = new JsArray(Realm);

        if (string.Equals(xResult.Formatted, yResult.Formatted, StringComparison.Ordinal))
        {
            AddRangePart(partsArray, "approximatelySign", "~", "shared");
            AddRangeParts(partsArray, xResult, "shared");
            return JsValue.FromJsArray(partsArray);
        }

        AddRangeParts(partsArray, xResult, "startRange");
        AddRangePart(partsArray, "literal", " \u2013 ", "shared");
        AddRangeParts(partsArray, yResult, "endRange");

        return JsValue.FromJsArray(partsArray);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    public JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        return new JsValue(CreateNumberFormatResolvedOptions(nf));
    }

    private double ConvertToNumericForRange(JsValue value)
    {
        var context = Realm.CreateContext();
        var numericValue = JsOps.ToNumericAsJsValue(in value, context);
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return numericValue.IsBigInt ? (double)numericValue.AsBigInt().Value : numericValue.NumberValue;
    }

    private void AddRangeParts(JsArray array, IntlNumberFormatResult result, string source)
    {
        var parts = result.Parts ?? [new NumberFormatPart("literal", result.Formatted)];
        foreach (var part in parts)
        {
            AddRangePart(array, part.Type, part.Value, source);
        }
    }

    private void AddRangePart(JsArray array, string type, string value, string source)
    {
        const string operation = "Intl.NumberFormat.prototype.formatRangeToParts";
        var entry = new JsObject(Realm.ObjectPrototype);
        CreateDataPropertyOrThrowJsValue(entry, "type", (JsValue)type, Realm, operation);
        CreateDataPropertyOrThrowJsValue(entry, "value", (JsValue)value, Realm, operation);
        CreateDataPropertyOrThrowJsValue(entry, "source", (JsValue)source, Realm, operation);
        array.Push(entry);
    }

    private JsObject ValidateNumberFormatReceiver(JsValue thisValue)
    {
        return thisValue.EnsureBrand(NumberFormatBrand, Realm,
            "Intl.NumberFormat method called on incompatible receiver");
    }

    private string FormatNumberValue(JsObject nf, JsValue value)
    {
        return FormatNumberResult(nf, value).Formatted;
    }

    private IntlNumberFormatResult FormatNumberResult(JsObject nf, JsValue value)
    {
        var slots = GetSlots(nf);
        if (value.TryGetString(out var stringValue) &&
            IntlNumberFormatter.TryFormatDecimalString(stringValue, slots) is { } decimalStringResult)
        {
            return ApplyNumberingSystem(decimalStringResult, slots);
        }

        var context = Realm.CreateContext();
        JsValue numericValue;
        try
        {
            numericValue = JsOps.ToNumericAsJsValue(in value, context);
        }
        catch (ThrowSignal)
        {
            throw;
        }
        catch
        {
            throw ThrowTypeError("Intl.NumberFormat: value is not a number", context, Realm);
        }

        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        IntlNumberFormatResult result;
        if (numericValue.IsBigInt)
        {
            result = IntlNumberFormatter.FormatBigInteger(numericValue.AsBigInt().Value, slots);
        }
        else
        {
            result = IntlNumberFormatter.FormatDouble(numericValue.NumberValue, slots);
        }

        return ApplyNumberingSystem(result, slots);
    }

    private static IntlNumberFormatResult ApplyNumberingSystem(
        IntlNumberFormatResult result,
        IntlNumberFormatInternalSlots slots)
    {
        // Apply numbering system digit transliteration
        if (!string.Equals(slots.NumberingSystem, "latn", StringComparison.Ordinal))
        {
            result.Formatted = IntlUtilities.TranslateDigits(result.Formatted, slots.NumberingSystem);
            if (result.Parts is not null)
            {
                for (var i = 0; i < result.Parts.Count; i++)
                {
                    var part = result.Parts[i];
                    var translated = IntlUtilities.TranslateDigits(part.Value, slots.NumberingSystem);
                    if (!ReferenceEquals(translated, part.Value))
                    {
                        result.Parts[i] = part with { Value = translated };
                    }
                }
            }
        }

        return result;
    }

    private JsObject CreateNumberFormatResolvedOptions(JsObject nf)
    {
        var slots = GetSlots(nf);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string operation = "Intl.NumberFormat.prototype.resolvedOptions";

        // Properties in spec-defined order, only included when relevant
        CreateDataPropertyOrThrowJsValue(obj, "locale", slots.Locale, Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "numberingSystem", slots.NumberingSystem, Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "style", slots.Style, Realm, operation);

        // currency/currencyDisplay/currencySign only when style is "currency"
        if (string.Equals(slots.Style, "currency", StringComparison.Ordinal))
        {
            CreateDataPropertyOrThrowJsValue(obj, "currency",
                slots.Currency ?? string.Empty, Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "currencyDisplay", slots.CurrencyDisplay, Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "currencySign", slots.CurrencySign, Realm, operation);
        }

        // unit/unitDisplay only when style is "unit"
        if (string.Equals(slots.Style, "unit", StringComparison.Ordinal))
        {
            CreateDataPropertyOrThrowJsValue(obj, "unit",
                slots.Unit ?? string.Empty, Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "unitDisplay", slots.UnitDisplay, Realm, operation);
        }

        CreateDataPropertyOrThrowJsValue(obj, "minimumIntegerDigits", (double)slots.MinimumIntegerDigits, Realm,
            operation);

        // Digit properties depend on rounding type
        var roundingType = slots.RoundingType;
        var showFractionDigits = roundingType is "fractionDigits" or "morePrecision" or "lessPrecision";
        var showSignificantDigits = roundingType is "significantDigits" or "morePrecision" or "lessPrecision";

        if (showFractionDigits || roundingType is "compactRounding")
        {
            CreateDataPropertyOrThrowJsValue(obj, "minimumFractionDigits", (double)slots.MinimumFractionDigits, Realm,
                operation);
            CreateDataPropertyOrThrowJsValue(obj, "maximumFractionDigits", (double)slots.MaximumFractionDigits, Realm,
                operation);
        }

        if (showSignificantDigits)
        {
            CreateDataPropertyOrThrowJsValue(obj, "minimumSignificantDigits",
                (double)(slots.MinimumSignificantDigits ?? 1), Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "maximumSignificantDigits",
                (double)(slots.MaximumSignificantDigits ?? 21), Realm, operation);
        }

        // useGrouping
        if (string.Equals(slots.UseGrouping, "false", StringComparison.Ordinal))
        {
            CreateDataPropertyOrThrowJsValue(obj, "useGrouping", false, Realm, operation);
        }
        else
        {
            CreateDataPropertyOrThrowJsValue(obj, "useGrouping", slots.UseGrouping, Realm, operation);
        }

        CreateDataPropertyOrThrowJsValue(obj, "notation", slots.Notation, Realm, operation);

        // compactDisplay only when notation is "compact"
        if (string.Equals(slots.Notation, "compact", StringComparison.Ordinal))
        {
            CreateDataPropertyOrThrowJsValue(obj, "compactDisplay", slots.CompactDisplay ?? "short", Realm, operation);
        }

        CreateDataPropertyOrThrowJsValue(obj, "signDisplay", slots.SignDisplay, Realm, operation);

        // v3 properties
        CreateDataPropertyOrThrowJsValue(obj, "roundingIncrement", (double)slots.RoundingIncrement, Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "roundingMode", slots.RoundingMode, Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "roundingPriority", slots.RoundingPriority, Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "trailingZeroDisplay", slots.TrailingZeroDisplay, Realm, operation);

        return obj;
    }

    private IntlNumberFormatInternalSlots GetSlots(JsObject nf)
    {
        if (nf.TryGetProperty(SlotsKey, out var slotsValue) &&
            slotsValue.TryGetObject<IntlNumberFormatInternalSlots>(out var slots))
        {
            return slots;
        }

        throw ThrowTypeError("Intl.NumberFormat instance is missing internal slots", realm: Realm);
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
            new PropertyDescriptor { Value = (JsValue)1d, Writable = false, Enumerable = false, Configurable = true });
        function.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = (JsValue)string.Empty,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
    }
}
