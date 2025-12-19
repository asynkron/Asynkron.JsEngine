using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Number", ToStringTag = "Number")]
public sealed partial class NumberPrototype
{
    [JsHostMethod("toString", Length = 1d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var num = RequireNumberReceiver(thisValue, "Number.prototype.toString");
        var radixArg = args.GetArgument(0);
        var radixNumber = radixArg.IsUndefined ? 10d : JsOps.ToNumber(radixArg);
        if (double.IsNaN(radixNumber) || Math.Abs(radixNumber % 1) > double.Epsilon)
        {
            throw ThrowRangeError("radix must be an integer at least 2 and no greater than 36", realm: Realm);
        }

        var radix = (int)radixNumber;
        if (radix is < 2 or > 36)
        {
            throw ThrowRangeError("radix must be an integer at least 2 and no greater than 36", realm: Realm);
        }

        return NumberToString(num, radix);
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public JsValue ValueOf(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return RequireNumberReceiver(thisValue, "Number.prototype.valueOf");
    }

    [JsHostMethod("toFixed", Length = 1d)]
    public JsValue ToFixed(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var num = RequireNumberReceiver(thisValue, "Number.prototype.toFixed");
        var fractionDigits = args.Count > 0 ? (int)JsOps.ToNumber(args[0]) : 0;
        if (fractionDigits is < 0 or > 100)
        {
            throw ThrowRangeError("toFixed() digits argument must be between 0 and 100", realm: Realm);
        }

        if (double.IsNaN(num))
        {
            return "NaN";
        }

        if (double.IsInfinity(num))
        {
            return num > 0 ? "Infinity" : "-Infinity";
        }

        return num.ToString("F" + fractionDigits, CultureInfo.InvariantCulture);
    }

    [JsHostMethod("toExponential", Length = 1d)]
    public JsValue ToExponential(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var num = RequireNumberReceiver(thisValue, "Number.prototype.toExponential");
        if (double.IsNaN(num))
        {
            return "NaN";
        }

        if (double.IsInfinity(num))
        {
            return num > 0 ? "Infinity" : "-Infinity";
        }

        string result;
        if (args.Count <= 0 || !args[0].TryGetDouble(out var d))
        {
            result = num.ToString("e", CultureInfo.InvariantCulture);
        }
        else
        {
            var fractionDigits = (int)d;
            if (fractionDigits is < 0 or > 100)
            {
                throw ThrowRangeError("toExponential() digits argument must be between 0 and 100", realm: Realm);
            }

            result = num.ToString("e" + fractionDigits, CultureInfo.InvariantCulture);
        }

        return FormatExponentialForJs(result);
    }

    [JsHostMethod("toPrecision", Length = 1d)]
    public JsValue ToPrecision(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var num = RequireNumberReceiver(thisValue, "Number.prototype.toPrecision");
        if (args.Count == 0)
        {
            return num.ToString(CultureInfo.InvariantCulture);
        }

        if (double.IsNaN(num))
        {
            return "NaN";
        }

        if (double.IsInfinity(num))
        {
            return num > 0 ? "Infinity" : "-Infinity";
        }

        if (!args[0].TryGetDouble(out var d))
        {
            return num.ToString(CultureInfo.InvariantCulture);
        }

        var precision = (int)d;
        if (precision is < 1 or > 100)
        {
            throw ThrowRangeError("toPrecision() precision argument must be between 1 and 100", realm: Realm);
        }

        return num.ToString("G" + precision, CultureInfo.InvariantCulture);
    }

    [JsHostMethod("toLocaleString", Length = 0d)]
    public JsValue ToLocaleString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var num = RequireNumberReceiver(thisValue, "Number.prototype.toLocaleString");
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);

        if (TryFormatWithIntlNumberFormat(num, localesArg, optionsArg, Realm, out var formatted))
        {
            return JsValue.FromObjectUnsafe(formatted);
        }

        if (optionsArg.TryGetObject(out var options) && options is not null)
        {
            if (options.TryGetProperty("style", out var styleVal) && !styleVal.IsNullOrUndefined)
            {
                var style = JsOps.ToJsString(styleVal);
                if (string.Equals(style, "unit", StringComparison.OrdinalIgnoreCase) &&
                    options.TryGetProperty("unit", out var unitVal) &&
                    !unitVal.IsNullOrUndefined)
                {
                    return $"{num.ToString(CultureInfo.InvariantCulture)} {JsOps.ToJsString(unitVal)}";
                }
            }
        }

        return num.ToString(CultureInfo.InvariantCulture);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        // Set initial [[NumberData]] to +0 on the prototype
        if (Prototype is IJsPropertyAccessor accessor && !accessor.TryGetProperty("__value__", out _))
        {
            accessor.SetProperty("__value__", 0d);
        }

        Realm.NumberPrototype ??= Prototype as JsObject;
    }

    private double RequireNumberReceiver(JsValue receiver, string methodName)
    {
        // Check if it's a direct number
        if (receiver.TryGetDouble(out var num))
        {
            return num;
        }

        // Check if it's a Number object (with __value__ property)
        if (receiver.TryGetObject(out object? obj) && obj is not null)
        {
            if (obj is JsObject jsObj && jsObj.TryGetProperty("__value__", out var inner))
            {
                return JsOps.ToNumber(inner);
            }

            if (obj is IJsPropertyAccessor accessor && accessor.TryGetProperty("__value__", out var innerVal))
            {
                return JsOps.ToNumber(innerVal);
            }
        }

        throw ThrowTypeError($"{methodName} called on non-number object", realm: Realm);
    }

    private static string FormatExponentialForJs(string netExponential)
    {
        var eIndex = netExponential.IndexOf('e', StringComparison.Ordinal);
        if (eIndex < 0) return netExponential;
        var mantissa = netExponential[..(eIndex + 1)];
        var exponent = netExponential[(eIndex + 1)..];
        var sign = "";
        if (exponent.Length > 0 && (exponent[0] == '+' || exponent[0] == '-'))
        {
            sign = exponent[..1];
            exponent = exponent[1..];
        }

        exponent = exponent.TrimStart('0');
        if (exponent.Length == 0) exponent = "0";
        return mantissa + sign + exponent;
    }
}
