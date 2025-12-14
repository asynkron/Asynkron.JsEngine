using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateParseIntFunction()
    {
        var fn = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return JsValue.NaN;
            }

            var str = args[0].ToString() ?? "";
            str = str.Trim();
            if (str.Length == 0)
            {
                return JsValue.NaN;
            }

            var radix = args.Count > 1 && args[1].TryGetDouble(out var r) ? (int)r : 10;
            if (radix is < 2 or > 36)
            {
                return JsValue.NaN;
            }

            // Handle sign
            var sign = 1;
            if (str.StartsWith("-"))
            {
                sign = -1;
                str = str[1..].TrimStart();
            }
            else if (str.StartsWith("+"))
            {
                str = str[1..].TrimStart();
            }

            // Parse until we hit invalid character
            double result = 0;
            var hasDigits = false;
            foreach (var c in str)
            {
                int digit;
                if (char.IsDigit(c))
                {
                    digit = c - '0';
                }
                else if (char.IsLetter(c))
                {
                    var upper = char.ToUpperInvariant(c);
                    digit = upper - 'A' + 10;
                }
                else
                {
                    break; // Stop at first invalid character
                }

                if (digit >= radix)
                {
                    break;
                }

                result = result * radix + digit;
                hasDigits = true;
            }

            return hasDigits ? new JsValue(result * sign) : JsValue.NaN;
        }, isConstructor: false);
        fn.Properties.Delete("prototype");
        return fn;
    }

    /// <summary>
    ///     Creates the global parseFloat function.
    /// </summary>
    public static HostFunction CreateParseFloatFunction()
    {
        var fn = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return JsValue.NaN;
            }

            var str = args[0].ToString() ?? "";
            str = str.Trim();
            if (str.Length == 0)
            {
                return JsValue.NaN;
            }

            // Try parsing the string as a double
            if (double.TryParse(str, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var result))
            {
                return new JsValue(result);
            }

            // JavaScript parseFloat allows partial parsing - parse as much as possible
            var i = 0;
            var hasDigits = false;

            // Handle sign
            if (i < str.Length && (str[i] == '+' || str[i] == '-'))
            {
                i++;
            }

            // Parse digits before decimal point
            while (i < str.Length && char.IsDigit(str[i]))
            {
                hasDigits = true;
                i++;
            }

            // Parse decimal point and digits after
            if (i < str.Length && str[i] == '.')
            {
                i++;
                while (i < str.Length && char.IsDigit(str[i]))
                {
                    hasDigits = true;
                    i++;
                }
            }

            // Parse exponent
            if (i < str.Length && (str[i] == 'e' || str[i] == 'E'))
            {
                var j = i + 1;
                if (j < str.Length && (str[j] == '+' || str[j] == '-'))
                {
                    j++;
                }

                var hasExpDigits = false;
                while (j < str.Length && char.IsDigit(str[j]))
                {
                    hasExpDigits = true;
                    j++;
                }

                if (hasExpDigits)
                {
                    i = j;
                }
            }

            if (!hasDigits)
            {
                return JsValue.NaN;
            }

            var parsed = str[..i];
            if (double.TryParse(parsed, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out result))
            {
                return new JsValue(result);
            }

            return JsValue.NaN;
        }, isConstructor: false);
        fn.Properties.Delete("prototype");
        return fn;
    }

    /// <summary>
    ///     Creates the global isNaN function.
    /// </summary>
    public static HostFunction CreateIsNaNFunction()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return JsValue.True;
            }

            var value = args[0];

            // Per ECMAScript spec, isNaN must first convert the argument to Number using ToNumber
            var numericValue = JsOps.ToNumber(value);
            return new JsValue(double.IsNaN(numericValue));
        });
    }

    /// <summary>
    ///     Creates the global isFinite function.
    /// </summary>
    public static HostFunction CreateIsFiniteFunction()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return JsValue.False;
            }

            var value = args[0];

            // Convert to number first (this is what JavaScript does)
            if (value.TryGetDouble(out var d))
            {
                return new JsValue(!double.IsNaN(d) && !double.IsInfinity(d));
            }

            if (value.TryGetString(out var s))
            {
                if (double.TryParse(s, out var parsed))
                {
                    return new JsValue(!double.IsNaN(parsed) && !double.IsInfinity(parsed));
                }

                return JsValue.False; // Can't parse, so NaN, so not finite
            }

            // For other types, convert to number using ToNumber
            var numericValue = JsOps.ToNumber(value);
            return new JsValue(!double.IsNaN(numericValue) && !double.IsInfinity(numericValue));
        });
    }
}
