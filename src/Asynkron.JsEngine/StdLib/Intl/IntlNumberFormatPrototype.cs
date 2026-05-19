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

        var slots = GetSlots(nf);
        var xResult = FormatNumericForRange(x, slots);
        var yResult = FormatNumericForRange(y, slots);

        return (JsValue)FormatRangeResult(xResult, yResult, slots);
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

        var slots = GetSlots(nf);
        var xResult = FormatNumericForRange(x, slots);
        var yResult = FormatNumericForRange(y, slots);

        var partsArray = new JsArray(Realm);

        if (string.Equals(xResult.Formatted, yResult.Formatted, StringComparison.Ordinal))
        {
            AddRangePart(partsArray, "approximatelySign", "~", "shared");
            AddRangeParts(partsArray, xResult, "shared");
            return JsValue.FromJsArray(partsArray);
        }

        if (string.Equals(slots.Style, "currency", StringComparison.Ordinal))
        {
            if (TryAddCurrencyRangePartsWithSharedSuffix(partsArray, xResult, yResult, slots))
            {
                return JsValue.FromJsArray(partsArray);
            }

            if (string.Equals(slots.SignDisplay, "always", StringComparison.Ordinal) &&
                TryAddCurrencyRangePartsWithSharedPrefix(partsArray, xResult, yResult, slots))
            {
                return JsValue.FromJsArray(partsArray);
            }
        }

        AddRangeParts(partsArray, xResult, "startRange");
        AddRangePart(partsArray, "literal", GetRangeSeparator(slots, shareAffix: false), "shared");
        AddRangeParts(partsArray, yResult, "endRange");

        return JsValue.FromJsArray(partsArray);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    public JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        return new JsValue(CreateNumberFormatResolvedOptions(nf));
    }

    private IntlNumberFormatResult FormatNumericForRange(JsValue value, IntlNumberFormatInternalSlots slots)
    {
        if (value.IsString &&
            IntlNumberFormatter.TryFormatDecimalString(value.AsString(), slots, out var stringResult, out var isNaN))
        {
            if (!isNaN)
            {
                return stringResult;
            }
        }

        var numeric = ConvertToNumericForRange(value);
        if (double.IsNaN(numeric))
        {
            throw ThrowRangeError("start and end values must not be NaN", realm: Realm);
        }

        return IntlNumberFormatter.FormatDouble(numeric, slots);
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

    private static string FormatRangeResult(
        IntlNumberFormatResult xResult,
        IntlNumberFormatResult yResult,
        IntlNumberFormatInternalSlots slots)
    {
        if (string.Equals(xResult.Formatted, yResult.Formatted, StringComparison.Ordinal))
        {
            return $"~{xResult.Formatted}";
        }

        if (string.Equals(slots.Style, "currency", StringComparison.Ordinal))
        {
            if (TryFormatCurrencyRangeWithSharedSuffix(xResult.Formatted, yResult.Formatted, slots, out var suffixRange))
            {
                return suffixRange;
            }

            if (string.Equals(slots.SignDisplay, "always", StringComparison.Ordinal) &&
                TryFormatCurrencyRangeWithSharedPrefix(xResult.Formatted, yResult.Formatted, slots, out var prefixRange))
            {
                return prefixRange;
            }
        }

        var separator = GetRangeSeparator(slots, shareAffix: false);
        return $"{xResult.Formatted}{separator}{yResult.Formatted}";
    }

    private static bool TryFormatCurrencyRangeWithSharedPrefix(
        string x,
        string y,
        IntlNumberFormatInternalSlots slots,
        out string range)
    {
        range = string.Empty;
        var plus = slots.Culture.NumberFormat.PositiveSign;
        var xPrefixSign = string.Empty;
        if (string.Equals(slots.SignDisplay, "always", StringComparison.Ordinal) &&
            x.StartsWith(plus, StringComparison.Ordinal) &&
            y.StartsWith(plus, StringComparison.Ordinal))
        {
            xPrefixSign = plus;
            x = x[plus.Length..];
            y = y[plus.Length..];
        }

        var prefixLength = CommonPrefixLength(x, y);
        if (prefixLength == 0)
        {
            return false;
        }

        var prefix = x[..prefixLength];
        var xBody = PadRangeFractionDigits(x[prefixLength..], slots);
        var yBody = PadRangeFractionDigits(y[prefixLength..], slots);
        var separator = GetRangeSeparator(slots, shareAffix: true);
        range = $"{xPrefixSign}{prefix}{xBody}{separator}{yBody}";
        return true;
    }

    private static bool TryFormatCurrencyRangeWithSharedSuffix(
        string x,
        string y,
        IntlNumberFormatInternalSlots slots,
        out string range)
    {
        range = string.Empty;
        if (!HaveCompatibleRangeSigns(x, y, slots))
        {
            return false;
        }

        var suffixLength = CommonSuffixLength(x, y);
        suffixLength = TrimNumericRangeSuffix(x, suffixLength, slots);
        if (suffixLength == 0)
        {
            return false;
        }

        var xBody = x[..^suffixLength];
        var yBody = y[..^suffixLength];
        if (string.Equals(slots.SignDisplay, "always", StringComparison.Ordinal) &&
            yBody.StartsWith(slots.Culture.NumberFormat.PositiveSign, StringComparison.Ordinal))
        {
            yBody = yBody[slots.Culture.NumberFormat.PositiveSign.Length..];
        }

        xBody = PadRangeFractionDigits(xBody, slots);
        yBody = PadRangeFractionDigits(yBody, slots);
        var separator = GetRangeSeparator(slots, shareAffix: true);
        range = $"{xBody}{separator}{yBody}{y[^suffixLength..]}";
        return true;
    }

    private static int TrimNumericRangeSuffix(string value, int suffixLength, IntlNumberFormatInternalSlots slots)
    {
        while (suffixLength > 0)
        {
            var suffixStart = value.Length - suffixLength;
            if (!IsNumericRangeAffixCharacter(value[suffixStart], slots))
            {
                break;
            }

            suffixLength--;
        }

        return suffixLength;
    }

    private static bool IsNumericRangeAffixCharacter(char ch, IntlNumberFormatInternalSlots slots)
    {
        if (ch is >= '0' and <= '9')
        {
            return true;
        }

        var text = ch.ToString();
        return string.Equals(text, slots.Culture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal);
    }

    private static string PadRangeFractionDigits(string value, IntlNumberFormatInternalSlots slots)
    {
        if (slots.MinimumFractionDigits <= 0)
        {
            return value;
        }

        var separator = slots.Culture.NumberFormat.NumberDecimalSeparator;
        var separatorIndex = value.LastIndexOf(separator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return value + separator + new string('0', slots.MinimumFractionDigits);
        }

        var fractionLength = value.Length - separatorIndex - separator.Length;
        return fractionLength >= slots.MinimumFractionDigits
            ? value
            : value + new string('0', slots.MinimumFractionDigits - fractionLength);
    }

    private static string GetRangeSeparator(IntlNumberFormatInternalSlots slots, bool shareAffix)
    {
        if (string.Equals(slots.Locale, "pt-PT", StringComparison.OrdinalIgnoreCase))
        {
            return " - ";
        }

        if (shareAffix ||
            !string.Equals(slots.Style, "currency", StringComparison.Ordinal))
        {
            return "\u2013";
        }

        return " \u2013 ";
    }

    private static bool HaveCompatibleRangeSigns(
        string x,
        string y,
        IntlNumberFormatInternalSlots slots)
    {
        return GetRangeSignKind(x, slots) == GetRangeSignKind(y, slots);
    }

    private static int GetRangeSignKind(string value, IntlNumberFormatInternalSlots slots)
    {
        if (value.StartsWith(slots.Culture.NumberFormat.NegativeSign, StringComparison.Ordinal))
        {
            return -1;
        }

        if (value.StartsWith(slots.Culture.NumberFormat.PositiveSign, StringComparison.Ordinal))
        {
            return 1;
        }

        return 0;
    }

    private static int CommonPrefixLength(string x, string y)
    {
        var max = Math.Min(x.Length, y.Length);
        var length = 0;
        while (length < max && x[length] == y[length])
        {
            length++;
        }

        return length;
    }

    private static int CommonSuffixLength(string x, string y)
    {
        var max = Math.Min(x.Length, y.Length);
        var length = 0;
        while (length < max && x[^(length + 1)] == y[^(length + 1)])
        {
            length++;
        }

        return length;
    }

    private void AddRangeParts(JsArray array, IntlNumberFormatResult result, string source)
    {
        var parts = result.Parts ?? [new NumberFormatPart("literal", result.Formatted)];
        foreach (var part in parts)
        {
            AddRangePart(array, part.Type, part.Value, source);
        }
    }

    private bool TryAddCurrencyRangePartsWithSharedPrefix(
        JsArray array,
        IntlNumberFormatResult xResult,
        IntlNumberFormatResult yResult,
        IntlNumberFormatInternalSlots slots)
    {
        var x = xResult.Formatted;
        var y = yResult.Formatted;
        var plus = slots.Culture.NumberFormat.PositiveSign;
        var xPrefixSign = string.Empty;
        if (string.Equals(slots.SignDisplay, "always", StringComparison.Ordinal) &&
            x.StartsWith(plus, StringComparison.Ordinal) &&
            y.StartsWith(plus, StringComparison.Ordinal))
        {
            xPrefixSign = plus;
            x = x[plus.Length..];
            y = y[plus.Length..];
        }

        var prefixLength = CommonPrefixLength(x, y);
        if (prefixLength == 0)
        {
            return false;
        }

        if (xPrefixSign.Length > 0)
        {
            AddRangePart(array, "plusSign", xPrefixSign, "shared");
        }

        AddRangePartsSlice(array, xResult, xPrefixSign.Length, prefixLength, "shared");
        AddRangePartsSlice(array, xResult, xPrefixSign.Length + prefixLength,
            xResult.Formatted.Length - xPrefixSign.Length - prefixLength, "startRange");
        AddRangePart(array, "literal", GetRangeSeparator(slots, shareAffix: true), "shared");
        AddRangePartsSlice(array, yResult, xPrefixSign.Length + prefixLength,
            yResult.Formatted.Length - xPrefixSign.Length - prefixLength, "endRange");
        return true;
    }

    private bool TryAddCurrencyRangePartsWithSharedSuffix(
        JsArray array,
        IntlNumberFormatResult xResult,
        IntlNumberFormatResult yResult,
        IntlNumberFormatInternalSlots slots)
    {
        if (!HaveCompatibleRangeSigns(xResult.Formatted, yResult.Formatted, slots))
        {
            return false;
        }

        var suffixLength = CommonSuffixLength(xResult.Formatted, yResult.Formatted);
        suffixLength = TrimNumericRangeSuffix(xResult.Formatted, suffixLength, slots);
        if (suffixLength == 0)
        {
            return false;
        }

        var xBodyLength = xResult.Formatted.Length - suffixLength;
        var yBodyStart = 0;
        var yBodyLength = yResult.Formatted.Length - suffixLength;
        if (string.Equals(slots.SignDisplay, "always", StringComparison.Ordinal) &&
            yResult.Formatted.StartsWith(slots.Culture.NumberFormat.PositiveSign, StringComparison.Ordinal))
        {
            yBodyStart = slots.Culture.NumberFormat.PositiveSign.Length;
            yBodyLength -= yBodyStart;
        }

        AddRangePartsSlice(array, xResult, 0, xBodyLength, "startRange");
        AddRangePart(array, "literal", GetRangeSeparator(slots, shareAffix: true), "shared");
        AddRangePartsSlice(array, yResult, yBodyStart, yBodyLength, "endRange");
        AddRangePartsSlice(array, yResult, yResult.Formatted.Length - suffixLength, suffixLength, "shared");
        return true;
    }

    private void AddRangePartsSlice(
        JsArray array,
        IntlNumberFormatResult result,
        int start,
        int length,
        string source)
    {
        if (length <= 0)
        {
            return;
        }

        var end = start + length;
        var offset = 0;
        var parts = result.Parts ?? [new NumberFormatPart("literal", result.Formatted)];
        foreach (var part in parts)
        {
            var partStart = offset;
            var partEnd = offset + part.Value.Length;
            var sliceStart = Math.Max(start, partStart);
            var sliceEnd = Math.Min(end, partEnd);
            if (sliceStart < sliceEnd)
            {
                AddRangePart(array, part.Type, part.Value[(sliceStart - partStart)..(sliceEnd - partStart)], source);
            }

            offset = partEnd;
            if (offset >= end)
            {
                break;
            }
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

        var slots = GetSlots(nf);
        IntlNumberFormatResult result;
        if (numericValue.IsBigInt)
        {
            result = IntlNumberFormatter.FormatBigInteger(numericValue.AsBigInt().Value, slots);
        }
        else
        {
            result = IntlNumberFormatter.FormatDouble(numericValue.NumberValue, slots);
        }

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
