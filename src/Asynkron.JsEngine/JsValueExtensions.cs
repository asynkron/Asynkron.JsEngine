using System.Globalization;
using System.Text;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

internal static class JsValueExtensions
{
    public static double ToNumber(this object? value)
    {
        return value switch
        {
            null => 0,
            Symbol sym when ReferenceEquals(sym, Symbols.Undefined) => double.NaN,
            JsBigInt => throw StandardLibrary.ThrowTypeError("Cannot convert a BigInt value to a number"),
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
            JsObject obj => obj.TryGetProperty("__value__", out var inner)
                ? ToNumber(inner)
                : double.NaN,
            IJsPropertyAccessor accessor => accessor.TryGetProperty("__value__", out var inner)
                ? ToNumber(inner)
                : double.NaN,
            _ => throw new InvalidOperationException($"Cannot convert value '{value}' to a number.")
        };
    }

    public static string ToJsString(this object? value)
    {
        return value switch
        {
            null => "null",
            Symbol sym when ReferenceEquals(sym, Symbols.Undefined) => "undefined",
            Symbol sym => sym.Name,
            bool b => b ? "true" : "false",
            JsBigInt bigInt => bigInt.ToString(),
            JsArray array => ArrayToString(array),
            IJsPropertyAccessor accessor => JsOps.ToPropertyName(accessor) ?? string.Empty,
            IJsCallable => "function() { [native code] }",
            string s => s,
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            uint ui => ui.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            ulong ul => ul.ToString(CultureInfo.InvariantCulture),
            short s16 => s16.ToString(CultureInfo.InvariantCulture),
            ushort us16 => us16.ToString(CultureInfo.InvariantCulture),
            byte b8 => b8.ToString(CultureInfo.InvariantCulture),
            sbyte sb8 => sb8.ToString(CultureInfo.InvariantCulture),
            TypedAstSymbol jsSymbol => jsSymbol.ToString(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    public static string ToJsStringForArray(this object? value)
    {
        if (value is null || value is Symbol sym && ReferenceEquals(sym, Symbols.Undefined))
        {
            return string.Empty;
        }

        return ToJsString(value);
    }

    private static double StringToNumber(string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return 0;
        }

        var trimmed = str.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return 0;
        }

        return double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : double.NaN;
    }

    private static double ArrayToNumber(JsArray arr)
    {
        return arr.Items.Count switch
        {
            0 => 0,
            1 => ToNumber(arr.Items[0]),
            _ => double.NaN
        };
    }

    private static string ArrayToString(JsArray array)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < array.Items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(array.Items[i].ToJsStringForArray());
        }

        return builder.ToString();
    }
}
