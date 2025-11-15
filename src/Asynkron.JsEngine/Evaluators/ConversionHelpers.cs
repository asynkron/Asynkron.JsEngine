using System.Globalization;

namespace Asynkron.JsEngine.Evaluators;

internal static class ConversionHelpers
{
    internal static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => false,
            bool b => b,
            double d => !double.IsNaN(d) && Math.Abs(d) > double.Epsilon,
            string s => s.Length > 0,
            _ => true
        };
    }

    internal static double ToNumber(this object? value)
    {
        return value switch
        {
            null => 0,
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => double.NaN,
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => ul,
            short s => s,
            ushort us => us,
            byte b => b,
            sbyte sb => sb,
            bool flag => flag ? 1 : 0,
            string str => StringToNumber(str),
            JsArray arr => ArrayToNumber(arr),
            JsObject => double.NaN, // Objects convert to NaN
            _ => throw new InvalidOperationException($"Cannot convert value '{value}' to a number.")
        };
    }

    internal static string ToString(object? value)
    {
        return value switch
        {
            null => "null",
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => "undefined",
            bool b => b ? "true" : "false",
            JsBigInt bigInt => bigInt.ToString(),
            JsArray arr => ArrayToString(arr),
            JsObject => "[object Object]",
            IJsCallable => "function() { [native code] }",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    // Helper method for converting values to strings in array context (join/toString)
    // where null and undefined become empty strings
    internal static string ToStringForArray(object? value)
    {
        // null and undefined convert to empty string in array toString/join
        if (value is null || (value is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined)))
        {
            return "";
        }

        return ToString(value);
    }

    internal static string GetTypeofString(object? value)
    {
        // JavaScript oddity: typeof null === "object" (historical bug)
        if (value is null)
        {
            return "object";
        }

        // Check for undefined symbol
        if (value is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined))
        {
            return "undefined";
        }

        // Check for JavaScript Symbol (primitive type)
        if (value is JsSymbol)
        {
            return "symbol";
        }

        // Check for BigInt
        if (value is JsBigInt)
        {
            return "bigint";
        }

        return value switch
        {
            bool => "boolean",
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte => "number",
            string => "string",
            JsFunction or HostFunction => "function",
            _ => "object"
        };
    }

    internal static int ToInt32(object? value)
    {
        var num = ToNumber(value);
        return JsNumericConversions.ToInt32(num);
    }

    internal static uint ToUInt32(object? value)
    {
        var num = ToNumber(value);
        return JsNumericConversions.ToUInt32(num);
    }

    internal static bool IsNumeric(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }

    internal static bool TryConvertToIndex(object? value, out int index)
    {
        switch (value)
        {
            case int i and >= 0:
                index = i;
                return true;
            case long l and >= 0 and <= int.MaxValue:
                index = (int)l;
                return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                var truncated = Math.Truncate(d);
                if (Math.Abs(d - truncated) < double.Epsilon && truncated is >= 0 and <= int.MaxValue)
                {
                    index = (int)truncated;
                    return true;
                }

                break;
            case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                               parsed >= 0:
                index = parsed;
                return true;
        }

        index = 0;
        return false;
    }

    internal static string? ToPropertyName(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            Symbol symbol => symbol.Name,
            JsSymbol jsSymbol => $"@@symbol:{jsSymbol.GetHashCode()}", // Special prefix for Symbol keys
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d when !double.IsNaN(d) && !double.IsInfinity(d) => d.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static double StringToNumber(string str)
    {
        // Empty string converts to 0
        if (string.IsNullOrEmpty(str))
        {
            return 0;
        }

        // Trim whitespace
        var trimmed = str.Trim();

        // Whitespace-only string converts to 0
        if (string.IsNullOrEmpty(trimmed))
        {
            return 0;
        }

        // Try to parse the trimmed string
        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        // Invalid number format converts to NaN
        return double.NaN;
    }

    private static double ArrayToNumber(JsArray arr)
    {
        return arr.Items.Count switch
        {
            // Empty array converts to 0
            0 => 0,
            // Single element array converts to the number representation of that element
            1 => ToNumber(arr.Items[0]),
            _ => double.NaN
        };

        // Multi-element array converts to NaN
    }

    private static string ArrayToString(JsArray arr)
    {
        // Convert each element to string and join with comma
        // Per ECMAScript spec: null and undefined are converted to empty strings
        var elements = arr.Items.Select(ToStringForArray).ToList();
        return string.Join(",", elements);
    }
}
