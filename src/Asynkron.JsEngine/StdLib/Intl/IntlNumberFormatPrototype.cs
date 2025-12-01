using System;
using System.Collections.Generic;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.NumberFormat", ToStringTag = "Intl.NumberFormat")]
public sealed partial class IntlNumberFormatPrototype
{
    private const string NumberFormatBrand = "__numberFormat__";
    private const string SlotsKey = "__numberFormatSlots__";

    internal static void InitializeInternalSlots(JsObject instance, IntlNumberFormatInternalSlots slots)
    {
        instance.SetProperty(NumberFormatBrand, true);
        instance.SetProperty(SlotsKey, slots);
    }

    [JsHostGetter("format", DisplayName = "get format")]
    public HostFunction GetFormat(object? thisValue)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        return CreateBoundFormatFunction(value => FormatNumberValue(nf, value));
    }

    [JsHostMethod("formatToParts", Length = 0d)]
    public JsArray FormatToParts(object? thisValue, IReadOnlyList<object?> args)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        var value = args.GetArgument(0);
        var result = FormatNumberResult(nf, value);
        var partsArray = new JsArray(Realm);
        var parts = result.Parts ?? new List<NumberFormatPart>
        {
            new("literal", result.Formatted)
        };
        foreach (var part in parts)
        {
            var entry = new JsObject(Realm.ObjectPrototype);
            entry.SetProperty("type", part.Type);
            entry.SetProperty("value", part.Value);
            partsArray.Push(entry);
        }

        return partsArray;
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    public JsObject ResolvedOptions(object? thisValue, IReadOnlyList<object?> _)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        return CreateNumberFormatResolvedOptions(nf);
    }

    private JsObject ValidateNumberFormatReceiver(object? thisValue)
    {
        return thisValue.EnsureBrand(NumberFormatBrand, Realm,
            "Intl.NumberFormat method called on incompatible receiver");
    }

    private string FormatNumberValue(JsObject nf, object? value)
    {
        return FormatNumberResult(nf, value).Formatted;
    }

    private IntlNumberFormatResult FormatNumberResult(JsObject nf, object? value)
    {
        var context = Realm.CreateContext();
        object numericValue;
        try
        {
            numericValue = JsOps.ToNumeric(value, context);
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
        obj.SetProperty("locale", slots.Locale);
        obj.SetProperty("numberingSystem", slots.NumberingSystem);
        obj.SetProperty("style", slots.Style);
        obj.SetProperty("currency", slots.Currency is { Length: > 0 } currencyValue ? currencyValue : Symbol.Undefined);
        obj.SetProperty("currencyDisplay",
            slots.Style == "currency" ? slots.CurrencyDisplay : Symbol.Undefined);
        obj.SetProperty("currencySign",
            slots.Style == "currency" ? slots.CurrencySign : "standard");
        obj.SetProperty("minimumIntegerDigits", (double)slots.MinimumIntegerDigits);
        if (slots.UseSignificantDigits)
        {
            obj.SetProperty("minimumSignificantDigits", (double)(slots.MinimumSignificantDigits ?? 1));
            obj.SetProperty("maximumSignificantDigits", (double)(slots.MaximumSignificantDigits ?? 21));
            obj.SetProperty("minimumFractionDigits", Symbol.Undefined);
            obj.SetProperty("maximumFractionDigits", Symbol.Undefined);
        }
        else
        {
            obj.SetProperty("minimumSignificantDigits", Symbol.Undefined);
            obj.SetProperty("maximumSignificantDigits", Symbol.Undefined);
            obj.SetProperty("minimumFractionDigits", (double)slots.MinimumFractionDigits);
            obj.SetProperty("maximumFractionDigits", (double)slots.MaximumFractionDigits);
        }
        obj.SetProperty("useGrouping", slots.UseGrouping ? "auto" : "never");
        obj.SetProperty("notation", slots.Notation);
        obj.SetProperty("signDisplay", slots.SignDisplay);
        if (slots.Style == "unit")
        {
            obj.SetProperty("unit", slots.Unit is { Length: > 0 } unitValue ? unitValue : Symbol.Undefined);
            obj.SetProperty("unitDisplay", slots.UnitDisplay);
        }
        else
        {
            obj.SetProperty("unit", Symbol.Undefined);
            obj.SetProperty("unitDisplay", Symbol.Undefined);
        }

        return obj;
    }

    private IntlNumberFormatInternalSlots GetSlots(JsObject nf)
    {
        if (nf.TryGetProperty(SlotsKey, out var slotsValue) && slotsValue is IntlNumberFormatInternalSlots slots)
        {
            return slots;
        }

        throw StandardLibrary.ThrowTypeError("Intl.NumberFormat instance is missing internal slots", realm: Realm);
    }
    private HostFunction CreateBoundFormatFunction(Func<object?, object?> formatter)
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
}
