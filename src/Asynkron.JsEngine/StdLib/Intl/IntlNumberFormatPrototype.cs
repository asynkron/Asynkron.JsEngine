#region

using Asynkron.JsEngine.JsTypes;
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

    [JsHostMethod("formatToParts", Length = 0d)]
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

    [JsHostMethod("resolvedOptions", Length = 0d)]
    public JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        return new JsValue(CreateNumberFormatResolvedOptions(nf));
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
        object numericValue;
        try
        {
            numericValue = JsOps.ToNumericAsJsValue(in value, context);
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
        if (numericValue is JsBigInt bigInt)
        {
            return IntlNumberFormatter.FormatBigInteger(bigInt.Value, slots);
        }

        var number = (double)numericValue;
        return IntlNumberFormatter.FormatDouble(number, slots);
    }

    private JsObject CreateNumberFormatResolvedOptions(JsObject nf)
    {
        var slots = GetSlots(nf);
        var obj = new JsObject(Realm.ObjectPrototype);
        obj.SetProperty("locale", (JsValue)slots.Locale);
        obj.SetProperty("numberingSystem", (JsValue)slots.NumberingSystem);
        obj.SetProperty("style", (JsValue)slots.Style);
        obj.SetProperty("currency",
            slots.Currency is { Length: > 0 } currencyValue ? (JsValue)currencyValue : JsValue.Undefined);
        obj.SetProperty("currencyDisplay",
            string.Equals(slots.Style, "currency", StringComparison.Ordinal) ? (JsValue)slots.CurrencyDisplay : JsValue.Undefined);
        obj.SetProperty("currencySign",
            (JsValue)(string.Equals(slots.Style, "currency", StringComparison.Ordinal) ? slots.CurrencySign : "standard"));
        obj.SetProperty("minimumIntegerDigits", (double)slots.MinimumIntegerDigits);
        if (slots.UseSignificantDigits)
        {
            obj.SetProperty("minimumSignificantDigits", (double)(slots.MinimumSignificantDigits ?? 1));
            obj.SetProperty("maximumSignificantDigits", (double)(slots.MaximumSignificantDigits ?? 21));
            obj.SetProperty("minimumFractionDigits", JsValue.Undefined);
            obj.SetProperty("maximumFractionDigits", JsValue.Undefined);
        }
        else
        {
            obj.SetProperty("minimumSignificantDigits", JsValue.Undefined);
            obj.SetProperty("maximumSignificantDigits", JsValue.Undefined);
            obj.SetProperty("minimumFractionDigits", (double)slots.MinimumFractionDigits);
            obj.SetProperty("maximumFractionDigits", (double)slots.MaximumFractionDigits);
        }

        obj.SetProperty("useGrouping", (JsValue)(slots.UseGrouping ? "auto" : "never"));
        obj.SetProperty("notation", (JsValue)slots.Notation);
        obj.SetProperty("signDisplay", (JsValue)slots.SignDisplay);
        if (string.Equals(slots.Style, "unit", StringComparison.Ordinal))
        {
            obj.SetProperty("unit", slots.Unit is { Length: > 0 } unitValue ? (JsValue)unitValue : JsValue.Undefined);
            obj.SetProperty("unitDisplay", (JsValue)slots.UnitDisplay);
        }
        else
        {
            obj.SetProperty("unit", JsValue.Undefined);
            obj.SetProperty("unitDisplay", JsValue.Undefined);
        }

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
                Value = (JsValue)string.Empty, Writable = false, Enumerable = false, Configurable = true
            });
    }
}
