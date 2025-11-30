using System.Globalization;
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

    internal static void InitializeInternalSlots(JsObject instance, RealmState realm)
    {
        instance.SetProperty(NumberFormatBrand, true);
        instance.SetProperty("__locale__", "en");
        instance.SetProperty("__numberingSystem__", "latn");
        instance.SetProperty("__style__", "decimal");
        instance.SetProperty("__currency__", Symbol.Undefined);
        instance.SetProperty("__unit__", Symbol.Undefined);
        instance.SetProperty("__roundingMode__", "halfExpand");
        instance.SetProperty("__roundingIncrement__", 1d);
        instance.SetProperty("__minimumIntegerDigits__", 1d);
        instance.SetProperty("__minimumFractionDigits__", 0d);
        instance.SetProperty("__maximumFractionDigits__", 3d);
        instance.SetProperty("__minimumSignificantDigits__", Symbol.Undefined);
        instance.SetProperty("__maximumSignificantDigits__", Symbol.Undefined);
    }

    [JsHostGetter("format", DisplayName = "get format")]
    public object GetFormat(object? thisValue)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        return new HostFunction((_, formatArgs) =>
        {
            var value = formatArgs.Count > 0 ? formatArgs[0] : Symbol.Undefined;
            return FormatNumberValue(nf, value);
        }, Realm, isConstructor: false);
    }

    [JsHostMethod("formatToParts", Length = 0d)]
    public JsArray FormatToParts(object? thisValue, IReadOnlyList<object?> args)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        var value = args.GetArgument(0);
        var formatted = FormatNumberValue(nf, value);
        var part = new JsObject();
        part.SetProperty("type", "literal");
        part.SetProperty("value", formatted);
        var parts = new JsArray(Realm);
        parts.Push(part);
        return parts;
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
        var context = Realm.CreateContext();
        double number;
        try
        {
            number = JsOps.ToNumber(value, context);
        }
        catch
        {
            throw ThrowTypeError("Intl.NumberFormat: value is not a number", context, Realm);
        }

        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var numeric = number switch
        {
            double.NaN => "NaN",
            0d => "0",
            _ => number.ToString(CultureInfo.InvariantCulture)
        };

        var style = nf.TryGetProperty("__style__", out var styleValue) && styleValue is string styleStr
            ? styleStr
            : "decimal";
        if (style == "currency" &&
            nf.TryGetProperty("__currency__", out var currencyValue) &&
            currencyValue is string currencyCode)
        {
            return $"{currencyCode} {numeric}";
        }

        if (style == "unit" &&
            nf.TryGetProperty("__unit__", out var unitValue) &&
            unitValue is string unitIdentifier)
        {
            return $"{numeric} {unitIdentifier}";
        }

        return numeric;
    }

    private JsObject CreateNumberFormatResolvedOptions(JsObject nf)
    {
        var obj = new JsObject(Realm.ObjectPrototype);
        obj.SetProperty("locale", nf.TryGetProperty("__locale__", out var loc) ? loc ?? "en" : "en");
        obj.SetProperty("numberingSystem",
            nf.TryGetProperty("__numberingSystem__", out var ns) ? ns ?? "latn" : "latn");
        obj.SetProperty("roundingMode",
            nf.TryGetProperty("__roundingMode__", out var rm) && rm is not null ? rm : "halfExpand");
        obj.SetProperty("roundingIncrement",
            nf.TryGetProperty("__roundingIncrement__", out var ri) && ri is not null ? ri : 1d);
        obj.SetProperty("minimumIntegerDigits",
            nf.TryGetProperty("__minimumIntegerDigits__", out var mid) && mid is not null ? mid : 1d);
        obj.SetProperty("minimumFractionDigits",
            nf.TryGetProperty("__minimumFractionDigits__", out var minfd) && minfd is not null ? minfd : 0d);
        obj.SetProperty("maximumFractionDigits",
            nf.TryGetProperty("__maximumFractionDigits__", out var maxfd) && maxfd is not null ? maxfd : 3d);
        obj.SetProperty("minimumSignificantDigits",
            nf.TryGetProperty("__minimumSignificantDigits__", out var minsig) ? minsig : Symbol.Undefined);
        obj.SetProperty("maximumSignificantDigits",
            nf.TryGetProperty("__maximumSignificantDigits__", out var maxsig) ? maxsig : Symbol.Undefined);
        var style = nf.TryGetProperty("__style__", out var styleValue) && styleValue is string styleStr
            ? styleStr
            : "decimal";
        obj.SetProperty("style", style);
        if (style == "currency" &&
            nf.TryGetProperty("__currency__", out var currencyValue) &&
            currencyValue is string currencyCode)
        {
            obj.SetProperty("currency", currencyCode);
        }
        if (style == "unit" &&
            nf.TryGetProperty("__unit__", out var unitValue) &&
            unitValue is string unitIdentifier)
        {
            obj.SetProperty("unit", unitIdentifier);
        }
        return obj;
    }

}
