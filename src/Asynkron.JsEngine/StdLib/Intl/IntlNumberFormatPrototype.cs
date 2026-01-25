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
        if (numericValue.IsBigInt)
        {
            return IntlNumberFormatter.FormatBigInteger(numericValue.AsBigInt().Value, slots);
        }

        return IntlNumberFormatter.FormatDouble(numericValue.NumberValue, slots);
    }

    private JsObject CreateNumberFormatResolvedOptions(JsObject nf)
    {
        var slots = GetSlots(nf);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string operation = "Intl.NumberFormat.prototype.resolvedOptions";
        CreateDataPropertyOrThrowJsValue(obj, "locale", slots.Locale, Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "numberingSystem", slots.NumberingSystem, Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "style", slots.Style, Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "currency",
            slots.Currency is { Length: > 0 } currencyValue ? currencyValue : JsValue.Undefined,
            Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "currencyDisplay",
            string.Equals(slots.Style, "currency", StringComparison.Ordinal) ? slots.CurrencyDisplay : JsValue.Undefined,
            Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "currencySign",
            string.Equals(slots.Style, "currency", StringComparison.Ordinal) ? slots.CurrencySign : "standard",
            Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "minimumIntegerDigits", (double)slots.MinimumIntegerDigits, Realm, operation);
        if (slots.UseSignificantDigits)
        {
            CreateDataPropertyOrThrowJsValue(obj, "minimumSignificantDigits", (double)(slots.MinimumSignificantDigits ?? 1),
                Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "maximumSignificantDigits", (double)(slots.MaximumSignificantDigits ?? 21),
                Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "minimumFractionDigits", JsValue.Undefined, Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "maximumFractionDigits", JsValue.Undefined, Realm, operation);
        }
        else
        {
            CreateDataPropertyOrThrowJsValue(obj, "minimumSignificantDigits", JsValue.Undefined, Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "maximumSignificantDigits", JsValue.Undefined, Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "minimumFractionDigits", (double)slots.MinimumFractionDigits, Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "maximumFractionDigits", (double)slots.MaximumFractionDigits, Realm, operation);
        }

        CreateDataPropertyOrThrowJsValue(obj, "useGrouping", slots.UseGrouping ? "auto" : "never", Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "notation", slots.Notation, Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "signDisplay", slots.SignDisplay, Realm, operation);
        if (string.Equals(slots.Style, "unit", StringComparison.Ordinal))
        {
            CreateDataPropertyOrThrowJsValue(obj, "unit", slots.Unit is { Length: > 0 } unitValue ? unitValue : JsValue.Undefined,
                Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "unitDisplay", slots.UnitDisplay, Realm, operation);
        }
        else
        {
            CreateDataPropertyOrThrowJsValue(obj, "unit", JsValue.Undefined, Realm, operation);
            CreateDataPropertyOrThrowJsValue(obj, "unitDisplay", JsValue.Undefined, Realm, operation);
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
                Value = (JsValue)string.Empty,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
    }
}
