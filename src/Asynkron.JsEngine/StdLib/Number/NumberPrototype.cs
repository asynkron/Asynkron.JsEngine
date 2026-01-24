#region

using System.Globalization;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Number")]
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

        // Per spec: ToIntegerOrInfinity(fractionDigits), then check range
        var fractionDigitsArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var fractionDigitsNum = fractionDigitsArg.IsUndefined ? 0d : JsOps.ToNumber(fractionDigitsArg);

        // If fractionDigits is Infinity, throw RangeError
        if (double.IsInfinity(fractionDigitsNum))
        {
            throw ThrowRangeError("toFixed() digits argument must be between 0 and 100", realm: Realm);
        }

        // ToIntegerOrInfinity: NaN becomes 0, truncate toward zero
        var fractionDigits = double.IsNaN(fractionDigitsNum) ? 0 : (int)Math.Truncate(fractionDigitsNum);
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

        // For very large numbers (>= 10^21), return the same as ToString
        if (Math.Abs(num) >= 1e21)
        {
            return num.ToString(CultureInfo.InvariantCulture);
        }

        return num.ToString("F" + fractionDigits, CultureInfo.InvariantCulture);
    }

    [JsHostMethod("toExponential", Length = 1d)]
    public JsValue ToExponential(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var num = RequireNumberReceiver(thisValue, "Number.prototype.toExponential");
        return NumberHelper.FormatToExponentialCore(num, args, Realm);
    }

    [JsHostMethod("toPrecision", Length = 1d)]
    public JsValue ToPrecision(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var num = RequireNumberReceiver(thisValue, "Number.prototype.toPrecision");
        return NumberHelper.FormatToPrecisionCore(num, args, Realm);
    }

    [JsHostMethod("toLocaleString", Length = 0d)]
    public JsValue ToLocaleString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var num = RequireNumberReceiver(thisValue, "Number.prototype.toLocaleString");
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);

        if (TryFormatWithIntlNumberFormatJsValue(num, localesArg, optionsArg, Realm, out var formatted))
        {
            return formatted;
        }

        if (optionsArg.TryGetObject(out var options))
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
        if (Prototype is JsObject { RealmState: null } jsObj)
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

        // Check if it's a Number object (with __value__ property that is a number)
        if (receiver.TryGetObject(out object? obj))
        {
            if (obj is JsObject jsObj && jsObj.TryGetProperty("__value__", out var inner))
            {
                // Only accept if the __value__ is a number (Number wrapper objects)
                // Other wrapper types (String, Boolean) also use __value__ but should not be accepted
                if (inner.TryGetDouble(out var innerNum))
                {
                    return innerNum;
                }
            }

            if (obj is IJsPropertyAccessor accessor && accessor.TryGetProperty("__value__", out var innerVal))
            {
                // Only accept if the __value__ is a number
                if (innerVal.TryGetDouble(out var innerNum))
                {
                    return innerNum;
                }
            }
        }

        throw ThrowTypeError($"{methodName} called on non-number object", realm: Realm);
    }
}
