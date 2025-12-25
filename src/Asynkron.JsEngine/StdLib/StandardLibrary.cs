#region

using System.Globalization;
using System.Numerics;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
///     Provides standard JavaScript library objects and functions (Math, JSON, etc.)
/// </summary>
public static partial class StandardLibrary
{
    private static readonly BigInteger BigInt64Modulus = BigInteger.One << 64;
    private static readonly BigInteger BigInt64SignThreshold = BigInt64Modulus >> 1;

    internal static JsValue CreateTypeError(string message, EvaluationContext? context = null, RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.TypeErrorConstructor is not IJsCallable callable)
        {
            return CreateErrorFallback("TypeError", message, realm);
        }

        var result = callable.Invoke([new JsValue(message)], JsValue.Null);
        if (result.IsUndefined)
        {
            return CreateErrorFallback("TypeError", message, realm);
        }

        return result;
    }

    private static JsValue CreateRangeError(string message, EvaluationContext? context = null, RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.RangeErrorConstructor is not IJsCallable callable)
        {
            return CreateErrorFallback("RangeError", message, realm);
        }

        var result = callable.Invoke([new JsValue(message)], JsValue.Null);
        return result.IsUndefined ? CreateErrorFallback("RangeError", message, realm) : result;
    }

    internal static JsValue CreateReferenceError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.ReferenceErrorConstructor is not IJsCallable callable)
        {
            return CreateErrorFallback("ReferenceError", message, realm);
        }

        var result = callable.Invoke([new JsValue(message)], JsValue.Null);
        if (result.IsUndefined || result.IsNull)
        {
            return CreateErrorFallback("ReferenceError", message, realm);
        }

        return result;
    }

    internal static ThrowSignal ThrowTypeError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        return new ThrowSignal(CreateTypeError(message, context, realm));
    }

    internal static ThrowSignal ThrowRangeError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        return new ThrowSignal(CreateRangeError(message, context, realm));
    }

    internal static ThrowSignal ThrowReferenceError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        var errorValue = CreateReferenceError(message, context, realm);
        if (errorValue.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            var hasCtor = accessor.TryGetProperty("constructor", out _);
            if (!hasCtor && errorValue.TryGetObject<JsObject>(out var jsObj))
            {
                var realmState = realm ?? context?.RealmState;
                IJsCallable? ctor = realmState?.ReferenceErrorConstructor ??
                                    context?.RealmState.ReferenceErrorConstructor;
                if (ctor is null)
                {
                    if (realmState?.Engine?.GlobalObject.TryGetValue("ReferenceError", out var ctorValue) == true &&
                        ctorValue is JsValue ctorJs &&
                        ctorJs.TryGetObject<IJsCallable>(out var callable))
                    {
                        ctor = callable;
                    }
                }

                if (ctor is not null)
                {
                    jsObj.DefineProperty("constructor",
                        new PropertyDescriptor
                        {
                            Value = ctor, Writable = true, Enumerable = false, Configurable = true
                        });
                }
            }
        }

        return new ThrowSignal(errorValue);
    }

    internal static ThrowSignal ThrowSyntaxError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        return new ThrowSignal(CreateSyntaxError(message, context, realm));
    }

    internal static void DefineConstantProperty(
        IJsPropertyAccessor target,
        string name,
        object? value,
        bool configurable = false)
    {
        var descriptor = new PropertyDescriptor
        {
            Value = value,
            Writable = false,
            Enumerable = false,
            Configurable = configurable,
            HasValue = true,
            HasWritable = true,
            HasEnumerable = true,
            HasConfigurable = true
        };

        if (target is IPropertyDefinitionHost definable && definable.TryDefineProperty(name, descriptor))
        {
            return;
        }

        if (target is IJsObjectLike objectLike)
        {
            objectLike.DefineProperty(name, descriptor);
            return;
        }

        target.SetProperty(name, JsValue.FromObjectUnsafe(value));
    }

    internal static JsValue CreateSyntaxError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.SyntaxErrorConstructor is not IJsCallable callable)
        {
            return CreateErrorFallback("SyntaxError", message, realm);
        }

        var result = callable.Invoke([new JsValue(message)], JsValue.Null);
        return result.IsUndefined ? CreateErrorFallback("SyntaxError", message, realm) : result;
    }

    private static JsValue CreateErrorFallback(string name, string message, RealmState? realm)
    {
        var error = new JsObject { RealmState = realm };
        if (realm?.ErrorPrototype is not null)
        {
            error.SetPrototype(realm.ErrorPrototype);
        }

        error.SetProperty("name", (JsValue)name);
        error.SetProperty("message", (JsValue)message);
        return (JsValue)error;
    }

    internal static JsBigInt ToBigInt(JsValue value, EvaluationContext? context = null, RealmState? realmState = null)
    {
        realmState ??= context?.RealmState;
        var localContext = context ?? realmState?.CreateContext();

        while (true)
        {
            switch (value.Kind)
            {
                case JsValueKind.Undefined:
                    throw ThrowTypeError("Cannot convert undefined to a BigInt", localContext, realmState);
                case JsValueKind.Null:
                    throw ThrowTypeError("Cannot convert null to a BigInt", localContext, realmState);
                case JsValueKind.Boolean:
                    return value.NumberValue != 0 ? JsBigInt.One : JsBigInt.Zero;
                case JsValueKind.BigInt:
                    return value.ObjectValue as JsBigInt ??
                           throw ThrowTypeError("Invalid BigInt value", localContext, realmState);
                case JsValueKind.Number:
                    return ConvertNumberToBigInt(value.NumberValue, localContext, realmState);
                case JsValueKind.String:
                    return new JsBigInt(ParseBigIntString(value.AsString() ?? string.Empty, localContext, realmState));
                case JsValueKind.Symbol:
                    throw ThrowTypeError("Cannot convert Symbol to a BigInt", localContext, realmState);
                case JsValueKind.Object:
                {
                    if (value.TryGetObject<JsBigInt>(out var directBigInt))
                    {
                        return directBigInt;
                    }

                    if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
                    {
                        var primitive = JsOps.ToPrimitive(value, ToPrimitiveHint.Number, localContext);
                        if (localContext?.IsThrow == true)
                        {
                            throw new ThrowSignal(localContext.FlowValue);
                        }

                        value = primitive;
                        continue;
                    }

                    value = JsValue.FromObjectUnsafe(value.ObjectValue);
                    continue;
                }
                case JsValueKind.Unit:
                case JsValueKind.Uninitialized:
                default:
                    throw ThrowTypeError("Cannot convert value to a BigInt", localContext, realmState);
            }
        }
    }

    private static JsBigInt ConvertNumberToBigInt(double numberValue, EvaluationContext? context,
        RealmState? realmState)
    {
        if (double.IsNaN(numberValue) || double.IsInfinity(numberValue) ||
            Math.Floor(numberValue) != numberValue)
        {
            throw ThrowRangeError("Cannot convert a non-integer number to a BigInt", context, realmState);
        }

        return new JsBigInt(new BigInteger(numberValue));
    }

    internal static long ToBigInt64(BigInteger value)
    {
        var wrapped = value % BigInt64Modulus;
        if (wrapped.Sign < 0)
        {
            wrapped += BigInt64Modulus;
        }

        if (wrapped >= BigInt64SignThreshold)
        {
            wrapped -= BigInt64Modulus;
        }

        return (long)wrapped;
    }

    internal static ulong ToBigUint64(BigInteger value)
    {
        var wrapped = value % BigInt64Modulus;
        if (wrapped.Sign < 0)
        {
            wrapped += BigInt64Modulus;
        }

        return (ulong)wrapped;
    }

    private static BigInteger ParseBigIntString(string value, EvaluationContext? context = null,
        RealmState? realmState = null)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            return BigInteger.Zero;
        }

        if (text.EndsWith('n'))
        {
            throw ThrowSyntaxError("Invalid BigInt literal", context, realmState);
        }

        var sign = 1;
        if (text.StartsWith('+') || text.StartsWith('-'))
        {
            if (text[0] == '-')
            {
                sign = -1;
            }

            text = text[1..];
        }

        if (text.Length == 0)
        {
            return BigInteger.Zero;
        }

        var numberBase = 10;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            numberBase = 16;
            text = text[2..];
        }
        else if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            numberBase = 2;
            text = text[2..];
        }
        else if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
        {
            numberBase = 8;
            text = text[2..];
        }
        else if (text.StartsWith('0') && text.Length > 1 && char.IsDigit(text[1]))
        {
            throw ThrowSyntaxError("Invalid BigInt literal", context, realmState);
        }

        // A sign is only permitted with decimal strings.
        if (sign < 0 && numberBase != 10)
        {
            throw ThrowSyntaxError("Invalid BigInt literal", context, realmState);
        }

        // For decimal strings, reject any non-digit content.
        if (numberBase == 10)
        {
            foreach (var t in text)
            {
                if (t is < '0' or > '9')
                {
                    throw ThrowSyntaxError("Invalid BigInt literal", context, realmState);
                }
            }
        }

        if (text.Length == 0 || !TryParseBigIntWithBase(text, numberBase, sign, out var parsed))
        {
            throw ThrowSyntaxError("Invalid BigInt literal", context, realmState);
        }

        return parsed;
    }

    private static bool TryParseBigIntWithBase(string digits, int numberBase, int sign, out BigInteger result)
    {
        result = BigInteger.Zero;
        foreach (var ch in digits)
        {
            var digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'a' and <= 'z' => ch - 'a' + 10,
                >= 'A' and <= 'Z' => ch - 'A' + 10,
                _ => -1
            };

            if (digit < 0 || digit >= numberBase)
            {
                return false;
            }

            result = result * numberBase + digit;
        }

        if (sign < 0)
        {
            result = BigInteger.Negate(result);
        }

        return true;
    }

    /// <summary>
    ///     Converts a JavaScript value to its string representation, handling functions appropriately.
    ///     Exposed internally so prototype helpers can reuse the same semantics.
    /// </summary>
    internal static string JsValueToString(JsValue value, RealmState? realm = null)
    {
        return value.ToJsString(null, realm);
    }

    /// <summary>
    /// JsValue overload for TryFormatWithIntlNumberFormat. Avoids boxing/unboxing of JsValue arguments.
    /// Returns JsValue directly to avoid unnecessary boxing.
    /// </summary>
    internal static bool TryFormatWithIntlNumberFormatJsValue(
        double numericValue,
        JsValue localesArg,
        JsValue optionsArg,
        RealmState? realm,
        out JsValue formatted)
    {
        return TryFormatWithIntlNumberFormatJsValue(new JsValue(numericValue), localesArg, optionsArg, realm,
            out formatted);
    }

    /// <summary>
    /// JsValue overload for TryFormatWithIntlNumberFormat that accepts any JsValue as the numeric value.
    /// Supports BigInt, number, and other numeric types. Returns JsValue directly to avoid boxing.
    /// </summary>
    internal static bool TryFormatWithIntlNumberFormatJsValue(
        JsValue numericValue,
        JsValue localesArg,
        JsValue optionsArg,
        RealmState? realm,
        out JsValue formatted)
    {
        formatted = JsValue.Undefined;
        var constructor = ResolveIntlNumberFormatConstructor(realm);
        if (constructor is null)
        {
            return false;
        }

        var formatter = constructor.Invoke([localesArg, optionsArg], JsValue.Null);
        if (!formatter.TryGetObject<IJsPropertyAccessor>(out var accessor) ||
            !accessor.TryGetProperty("format", out var formatValue) ||
            !formatValue.TryGetObject<IJsCallable>(out var formatFn))
        {
            return false;
        }

        formatted = formatFn.Invoke([numericValue], formatter);
        return true;
    }

    private static IJsCallable? ResolveIntlNumberFormatConstructor(RealmState? realm)
    {
        var intl = realm?.Engine?.GlobalObject;
        if (intl is null)
        {
            return null;
        }

        if (!intl.TryGetProperty("Intl", out var intlValue) ||
            !intlValue.TryGetObject<IJsPropertyAccessor>(out var intlAccessor))
        {
            return null;
        }

        if (!intlAccessor.TryGetProperty("NumberFormat", out var ctorValue) ||
            !ctorValue.TryGetObject<IJsCallable>(out var ctor))
        {
            return null;
        }

        return ctor;
    }
}
