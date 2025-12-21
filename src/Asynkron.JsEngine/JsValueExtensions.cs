using System.Globalization;
using System.Text;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine;

internal static class JsValueExtensions
{
    private static double StringToNumber(string str)
    {
        return NumericStringParser.ParseJsNumber(str);
    }

    private static double ArrayToNumber(JsArray arr)
    {
        return arr.Items.Count switch
        {
            0 => 0,
            1 => arr.Items[0].ToNumber(),
            _ => double.NaN
        };
    }

    private static string ArrayToString(JsArray array)
    {
        // Use the logical length and element lookup so holes become empty strings,
        // matching Array.prototype.join/ToString behaviour.
        var length = array.Length > int.MaxValue ? int.MaxValue : (int)array.Length;
        var builder = new StringBuilder(length * 2);
        for (var i = 0; i < length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var element = array.GetElement(i);
            builder.Append(element.ToJsStringForArray());
        }

        return builder.ToString();
    }

    extension(object? value)
    {
        public double ToNumber()
        {
            return value switch
            {
                null => 0,
                JsValue jsValue => jsValue.Kind switch
                {
                    JsValueKind.Undefined => double.NaN,
                    JsValueKind.Null => 0,
                    JsValueKind.Boolean => jsValue.NumberValue,
                    JsValueKind.Number => jsValue.NumberValue,
                    JsValueKind.String => StringToNumber(jsValue.ObjectValue as string ?? string.Empty),
                    JsValueKind.Symbol => throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a number"),
                    JsValueKind.BigInt => jsValue.ObjectValue is JsBigInt bi ? (double)bi.Value : double.NaN,
                    JsValueKind.Object => jsValue.ObjectValue.ToNumber(),
                    _ => double.NaN
                },
                Symbol sym when ReferenceEquals(sym, Symbol.Undefined) => double.NaN,
                IIsHtmlDda => double.NaN,
                JsBigInt bigInt => (double)bigInt.Value,
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
                Symbol sym when ReferenceEquals(sym, Symbol.Undefined) => double.NaN,
                Symbol => throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a number"),
                JsArray arr => ArrayToNumber(arr),
                JsObject obj => obj.TryGetProperty("__value__", out var inner)
                    ? inner.ToNumber()
                    : double.NaN,
                IJsPropertyAccessor accessor => accessor.TryGetProperty("__value__", out var inner)
                    ? inner.ToNumber()
                    : double.NaN,
                _ => throw new InvalidOperationException($"Cannot convert value '{value}' to a number.")
            };
        }

        public string ToJsString(EvaluationContext? context = null, RealmState? realm = null)
        {
            // Fast-path common primitives/wrappers before general object coercion.
            if (value is null)
            {
                return "null";
            }

            // Handle JsValue struct - unwrap based on kind to avoid boxing
            if (value is JsValue jsValue)
            {
                return jsValue.Kind switch
                {
                    JsValueKind.Undefined => "undefined",
                    JsValueKind.Null => "null",
                    JsValueKind.Boolean => jsValue.NumberValue != 0 ? "true" : "false",
                    JsValueKind.Number => JsOps.ToCanonicalNumberString(jsValue.NumberValue),
                    JsValueKind.String => jsValue.ObjectValue as string ?? string.Empty,
                    JsValueKind.Symbol => throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string", context, realm ?? context?.RealmState),
                    JsValueKind.BigInt => jsValue.ObjectValue is JsBigInt bi ? bi.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    JsValueKind.Object => jsValue.ObjectValue.ToJsString(context, realm),
                    _ => string.Empty
                };
            }

            if (value is Symbol sym && ReferenceEquals(sym, Symbol.Undefined))
            {
                return "undefined";
            }

            if (value is IIsHtmlDda)
            {
                return "undefined";
            }

            var realmState = realm ?? context?.RealmState;

            if (value is Symbol)
            {
                throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string", context, realmState);
            }

            if (value is TypedAstSymbol)
            {
                throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string", context, realmState);
            }

            if (value is bool b)
            {
                return b ? "true" : "false";
            }

            if (value is JsBigInt bigIntVal)
            {
                return bigIntVal.ToString();
            }

            if (value is JsArray arrayVal)
            {
                return ArrayToString(arrayVal);
            }

            if (value is IJsPropertyAccessor accessor)
            {
                var primitive = JsOps.ToPrimitive(accessor, ToPrimitiveHint.String, context);
                return primitive is IJsPropertyAccessor ? "[object Object]" : primitive.ToJsString(context, realmState);
            }

            return value switch
            {
                IJsCallable => "function() { [native code] }",
                string s => s,
                double d => JsOps.ToCanonicalNumberString(d),
                float f => JsOps.ToCanonicalNumberString(f),
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

        public string ToJsStringForArray(EvaluationContext? context = null, RealmState? realm = null)
        {
            if (value is null || (value is Symbol sym && ReferenceEquals(sym, Symbol.Undefined)))
            {
                return string.Empty;
            }

            // Handle JsValue struct - unwrap based on kind to avoid boxing
            if (value is JsValue jsValue)
            {
                // For array join, undefined and null become empty string
                if (jsValue.IsNullOrUndefined)
                {
                    return string.Empty;
                }
                // For objects, recurse with the underlying object
                if (jsValue.Kind == JsValueKind.Object)
                {
                    return jsValue.ObjectValue.ToJsStringForArray(context, realm);
                }
                // For primitives, use ToJsString which handles them directly
                return jsValue.Kind switch
                {
                    JsValueKind.Boolean => jsValue.NumberValue != 0 ? "true" : "false",
                    JsValueKind.Number => JsOps.ToCanonicalNumberString(jsValue.NumberValue),
                    JsValueKind.String => jsValue.ObjectValue as string ?? string.Empty,
                    JsValueKind.BigInt => jsValue.ObjectValue is JsBigInt bi ? bi.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    _ => string.Empty
                };
            }

            return value.ToJsString(context, realm);
        }
    }
}
