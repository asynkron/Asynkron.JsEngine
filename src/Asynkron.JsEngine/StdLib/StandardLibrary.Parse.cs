using System.Globalization;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateParseIntFunction()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            var str = args[0]?.ToString() ?? "";
            str = str.Trim();
            if (str == "")
            {
                return double.NaN;
            }

            var radix = args.Count > 1 && args[1] is double r ? (int)r : 10;
            if (radix is < 2 or > 36)
            {
                return double.NaN;
            }

            // Handle sign
            var sign = 1;
            if (str.StartsWith("-"))
            {
                sign = -1;
                str = str.Substring(1).TrimStart();
            }
            else if (str.StartsWith("+"))
            {
                str = str.Substring(1).TrimStart();
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

            return hasDigits ? result * sign : double.NaN;
        });
    }

    /// <summary>
    ///     Creates the global parseFloat function.
    /// </summary>
    public static HostFunction CreateParseFloatFunction()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            var str = args[0]?.ToString() ?? "";
            str = str.Trim();
            if (str == "")
            {
                return double.NaN;
            }

            // Try parsing the string as a double
            if (double.TryParse(str, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var result))
            {
                return result;
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
                return double.NaN;
            }

            var parsed = str.Substring(0, i);
            if (double.TryParse(parsed, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            return double.NaN;
        });
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
                return true;
            }

            var value = args[0];

            // Convert to number first (this is what JavaScript does)
            if (value is double d)
            {
                return double.IsNaN(d);
            }

            if (value is int or long or float or decimal)
            {
                return false;
            }

            if (value is string s)
            {
                if (double.TryParse(s, out var parsed))
                {
                    return double.IsNaN(parsed);
                }

                return true; // Can't parse, so NaN
            }

            return true; // Everything else becomes NaN
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
                return false;
            }

            var value = args[0];

            // Convert to number first (this is what JavaScript does)
            if (value is double d)
            {
                return !double.IsNaN(d) && !double.IsInfinity(d);
            }

            if (value is int or long or float or decimal)
            {
                return true;
            }

            if (value is string s)
            {
                if (double.TryParse(s, out var parsed))
                {
                    return !double.IsNaN(parsed) && !double.IsInfinity(parsed);
                }

                return false; // Can't parse, so NaN, so not finite
            }

            return false; // Everything else becomes NaN, which is not finite
        });
    }
}
