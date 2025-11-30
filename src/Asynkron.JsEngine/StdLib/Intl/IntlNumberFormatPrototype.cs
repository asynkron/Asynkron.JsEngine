using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.NumberFormat")]
public sealed partial class IntlNumberFormatPrototype : JsPrototype
{
    private const string NumberFormatBrand = "__numberFormat__";

    private static readonly string ToStringTagKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";

    public IntlNumberFormatPrototype(JsObject prototype, RealmState realm)
        : base(prototype, realm)
    {
        if (realm.ObjectPrototype is not null)
        {
            prototype.SetPrototype(realm.ObjectPrototype);
        }
    }

    internal static void InitializeInternalSlots(JsObject instance, RealmState realm)
    {
        instance.SetProperty(NumberFormatBrand, true);
        instance.SetProperty("__locale__", "en");
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
        ValidateNumberFormatReceiver(thisValue);
        return new HostFunction((_, formatArgs) =>
        {
            var value = formatArgs.Count > 0 ? formatArgs[0] : Symbol.Undefined;
            return FormatNumberValue(value);
        }, Realm)
        {
            IsConstructor = false
        };
    }

    [JsHostMethod("formatToParts", Length = 0d)]
    public object FormatToParts(object? thisValue, IReadOnlyList<object?> args)
    {
        ValidateNumberFormatReceiver(thisValue);
        var value = args.Count > 0 ? args[0] : Symbol.Undefined;
        var formatted = FormatNumberValue(value);
        var part = new JsObject();
        part.SetProperty("type", "literal");
        part.SetProperty("value", formatted);
        var parts = new JsArray(Realm);
        parts.Push(part);
        return parts;
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    public object ResolvedOptions(object? thisValue, IReadOnlyList<object?> _)
    {
        var nf = ValidateNumberFormatReceiver(thisValue);
        return CreateNumberFormatResolvedOptions(nf);
    }

    private JsObject ValidateNumberFormatReceiver(object? thisValue)
    {
        if (thisValue is JsObject obj && obj.TryGetProperty(NumberFormatBrand, out var marker) && marker is true)
        {
            return obj;
        }

        throw ThrowTypeError("Intl.NumberFormat method called on incompatible receiver", realm: Realm);
    }

    private string FormatNumberValue(object? value)
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

        if (double.IsNaN(number))
        {
            return "NaN";
        }

        if (number == 0d)
        {
            return "0";
        }

        return number.ToString(CultureInfo.InvariantCulture);
    }

    private JsObject CreateNumberFormatResolvedOptions(JsObject nf)
    {
        var obj = new JsObject();
        obj.SetPrototype(Realm.ObjectPrototype);
        obj.SetProperty("locale", nf.TryGetProperty("__locale__", out var loc) ? loc ?? "en" : "en");
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
        return obj;
    }

    protected override void ConfigurePrototype()
    {
        Prototype.DefineProperty(ToStringTagKey,
            new PropertyDescriptor
            {
                Value = "Intl.NumberFormat", Writable = false, Enumerable = false, Configurable = true
            });
    }
}
